using System.Diagnostics;
using System.Text.Json;
using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.Events;
using Microsoft.SemanticKernel;

namespace JobAgents.Infrastructure.Agents.Filters;

/// <summary>
/// Attached once to every kernel; fires for each function/tool invocation and publishes
/// <see cref="ToolCalledEvent"/> + <see cref="ToolResultEvent"/> to the bus, attributed to the
/// current (run, agent) pulled from <see cref="AgentRunContext"/>. No per-plugin wiring required.
/// </summary>
public sealed class EventPublishingFunctionFilter(IAgentEventBus bus, AgentRunContext context)
    : IFunctionInvocationFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext invocationContext,
        Func<FunctionInvocationContext, Task> next)
    {
        if (context.CurrentRun is not { } runId)
        {
            await next(invocationContext);
            return;
        }

        var agentId = context.CurrentAgent ?? AgentId.System;
        var toolName = $"{invocationContext.Function.PluginName}.{invocationContext.Function.Name}";
        var argsJson = SafeSerialize(invocationContext.Arguments);

        await bus.PublishAsync(new ToolCalledEvent(runId, agentId, toolName, argsJson, DateTime.UtcNow));

        var stopwatch = Stopwatch.StartNew();
        await next(invocationContext);
        stopwatch.Stop();

        var resultJson = SafeSerialize(invocationContext.Result?.GetValue<object>());
        await bus.PublishAsync(new ToolResultEvent(
            runId, agentId, toolName, resultJson, stopwatch.ElapsedMilliseconds, DateTime.UtcNow));
    }

    private static string SafeSerialize(object? value)
    {
        if (value is null)
            return "null";
        if (value is string s)
            return s;
        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch
        {
            return value.ToString() ?? string.Empty;
        }
    }
}
