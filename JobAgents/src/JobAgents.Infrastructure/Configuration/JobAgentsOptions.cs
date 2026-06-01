namespace JobAgents.Infrastructure.Configuration;

/// <summary>Root options tree, bound from the "JobAgents" configuration section + user-secrets.</summary>
public sealed class JobAgentsOptions
{
    public const string SectionName = "JobAgents";

    public OpenAiOptions OpenAi { get; set; } = new();
    public TavilyOptions Tavily { get; set; } = new();
    public JoobleOptions Jooble { get; set; } = new();
    public JobHuntOptions JobHunt { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
}

public sealed class TavilyOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.tavily.com";
}

public sealed class JoobleOptions
{
    /// <summary>Optional. When empty, the Jooble job-board tool reports itself as unavailable.</summary>
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://jooble.org/api";
}

public sealed class JobHuntOptions
{
    public int MaxResults { get; set; } = 8;
    public int MaxFanOutConcurrency { get; set; } = 3;
    public int TopMatchesToExpand { get; set; } = 3;
}
