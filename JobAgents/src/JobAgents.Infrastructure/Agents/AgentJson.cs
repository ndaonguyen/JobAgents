using System.Text.Json;

namespace JobAgents.Infrastructure.Agents;

/// <summary>
/// Helpers for parsing the JSON that agents are prompted to emit. Tolerates models wrapping their
/// output in Markdown code fences or adding prose around the JSON object/array.
/// </summary>
internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Deserializes the first JSON object/array found in <paramref name="text"/>, or null.</summary>
    public static T? TryParse<T>(string text) where T : class
    {
        var json = ExtractJson(text);
        if (json is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Find the outermost JSON object or array.
        var firstObj = text.IndexOf('{');
        var firstArr = text.IndexOf('[');
        var start = (firstObj, firstArr) switch
        {
            (< 0, < 0) => -1,
            (< 0, _) => firstArr,
            (_, < 0) => firstObj,
            _ => Math.Min(firstObj, firstArr),
        };
        if (start < 0)
            return null;

        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var last = text.LastIndexOf(close);
        if (last <= start)
            return null;

        return text[start..(last + 1)];
    }
}
