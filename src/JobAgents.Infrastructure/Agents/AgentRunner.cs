using System.Text;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using JobAgents.Domain.Runs;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace JobAgents.Infrastructure.Agents;

/// <summary>The text + usage produced by a single agent invocation.</summary>
public sealed record AgentResult(string Text, AgentUsage Usage);

/// <summary>
/// Runs one agent turn end to end. Extracted as an interface so the Coordinator and specialist
/// agents can be unit-tested without hitting a real model.
/// </summary>
public interface IAgentRunner
{
    Task<AgentResult> RunAsync(
        RunId runId,
        AgentId agentId,
        string role,
        string systemPrompt,
        string userPrompt,
        string? modelOverride,
        bool useTools,
        CancellationToken ct = default,
        bool jsonMode = false);
}

/// <summary>
/// Runs one agent turn: builds a kernel, streams the completion (publishing started/token/finished
/// events), lets the model auto-invoke tools, and extracts real token usage. Centralises the
/// event + usage plumbing so each specialist agent only owns its prompt and output parsing.
/// </summary>
public sealed class AgentRunner(
    IKernelFactory kernelFactory,
    IAgentEventBus bus,
    IUsageCalculator usageCalculator,
    AgentRunContext context,
    RunUsageAccumulator usageAccumulator)
    : IAgentRunner
{
    public async Task<AgentResult> RunAsync(
        RunId runId,
        AgentId agentId,
        string role,
        string systemPrompt,
        string userPrompt,
        string? modelOverride,
        bool useTools,
        CancellationToken ct = default,
        bool jsonMode = false)
    {
        // Attribute any tool calls fired during this turn to this (run, agent).
        context.Set(runId, agentId);

        var kernel = kernelFactory.Create(modelOverride, includePlugins: useTools);
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userPrompt);

        var effectiveModel = modelOverride ?? kernelFactory.DefaultModel;
        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = useTools ? FunctionChoiceBehavior.Auto() : FunctionChoiceBehavior.None(),
        };

        // OpenAI reasoning models (the o-series: o1/o3/o4-mini, …) reject any non-default temperature
        // with a 400 ("only the default (1) is supported"); only set our low temperature where the model
        // actually supports it. gpt-* and claude-* are fine.
        if (SupportsCustomTemperature(effectiveModel))
            settings.Temperature = 0.2;

        // Force a valid JSON object from models that support it (used for non-tool structured agents).
        // Anthropic's OpenAI-compatible endpoint doesn't honour response_format: json_object, so for
        // Claude we rely on the prompt's "return ONLY JSON" instruction + the tolerant AgentJson parser.
        if (jsonMode && !kernelFactory.IsAnthropicModel(modelOverride))
            settings.ResponseFormat = "json_object";

        await bus.PublishAsync(new AgentStartedEvent(runId, agentId, role, DateTime.UtcNow), ct);

        var builder = new StringBuilder();
        StreamingChatMessageContent? last = null;

        await foreach (var delta in chat.GetStreamingChatMessageContentsAsync(history, settings, kernel, ct))
        {
            if (!string.IsNullOrEmpty(delta.Content))
            {
                builder.Append(delta.Content);
                await bus.PublishAsync(new AgentTokenEvent(runId, agentId, delta.Content, DateTime.UtcNow), ct);
            }

            last = delta;
        }

        var (model, tokensIn, tokensOut) = UsageExtractor.Extract(last, modelOverride ?? kernelFactory.DefaultModel);
        var cost = usageCalculator.EstimateCostUsd(model, tokensIn, tokensOut);
        var text = builder.ToString();
        var usage = new AgentUsage(tokensIn, tokensOut, cost);

        // Record against the run so the Coordinator can report a true grand total in the System event.
        usageAccumulator.Add(runId, usage);

        await bus.PublishAsync(
            new AgentFinishedEvent(runId, agentId, text, tokensIn, tokensOut, cost, DateTime.UtcNow), ct);

        return new AgentResult(text, usage);
    }

    // Reasoning models match "o" + digit (o1, o3-mini, o4-mini); they only accept the default
    // temperature. Everything else (gpt-4o*, claude-*) accepts a custom value.
    private static bool SupportsCustomTemperature(string model) =>
        string.IsNullOrEmpty(model)
        || !(model.Length >= 2 && (model[0] is 'o' or 'O') && char.IsDigit(model[1]));
}
