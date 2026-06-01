using Microsoft.SemanticKernel;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// Pulls token usage and the real model name out of an OpenAI streaming response's metadata.
/// Token usage is only present when the connector emits a usage chunk (OpenAI's
/// <c>stream_options.include_usage</c>); when it is absent we return zeros so the cost surfaces as
/// "unknown" rather than a wrong number. Reflection keeps us decoupled from the OpenAI SDK's
/// concrete usage type, which has shifted across versions.
/// </summary>
internal static class UsageExtractor
{
    public static (string Model, int TokensIn, int TokensOut) Extract(
        StreamingChatMessageContent? content, string fallbackModel)
    {
        var model = content?.ModelId is { Length: > 0 } m ? m : fallbackModel;

        if (content?.Metadata is null || !content.Metadata.TryGetValue("Usage", out var usageObj) || usageObj is null)
            return (model, 0, 0);

        var tokensIn = ReadInt(usageObj, "InputTokenCount", "PromptTokens", "InputTokens");
        var tokensOut = ReadInt(usageObj, "OutputTokenCount", "CompletionTokens", "OutputTokens");
        return (model, tokensIn, tokensOut);
    }

    private static int ReadInt(object source, params string[] propertyNames)
    {
        var type = source.GetType();
        foreach (var name in propertyNames)
        {
            var prop = type.GetProperty(name);
            if (prop?.GetValue(source) is { } value && TryToInt(value, out var result))
                return result;
        }

        return 0;
    }

    private static bool TryToInt(object value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }
}
