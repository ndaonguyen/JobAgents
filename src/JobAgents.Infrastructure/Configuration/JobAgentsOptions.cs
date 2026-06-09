namespace JobAgents.Infrastructure.Configuration;

/// <summary>Root options tree, bound from the "JobAgents" configuration section + user-secrets.</summary>
public sealed class JobAgentsOptions
{
    public const string SectionName = "JobAgents";

    public OpenAiOptions OpenAi { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
    public TavilyOptions Tavily { get; set; } = new();
    public JobHuntOptions JobHunt { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
}

/// <summary>
/// Anthropic (Claude) is reached through its OpenAI-compatible Chat Completions endpoint, so the
/// existing OpenAI connector is reused — only the base URL + key differ. Any agent whose model id
/// starts with <c>claude</c> is routed here; the resume matcher defaults to <see cref="Model"/>.
/// </summary>
public sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/";
    public string Model { get; set; } = "claude-haiku-4-5";
}

public sealed class TavilyOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.tavily.com";
}

public sealed class JobHuntOptions
{
    public int MaxResults { get; set; } = 8;
    public int MaxFanOutConcurrency { get; set; } = 3;
    public int TopMatchesToExpand { get; set; } = 3;
}
