namespace JobAgents.Application.Abstractions;

/// <summary>Token counts plus an optional USD cost for a single agent invocation.</summary>
public sealed record AgentUsage(int TokensIn, int TokensOut, decimal? EstimatedCostUsd)
{
    public static AgentUsage Zero { get; } = new(0, 0, 0m);

    /// <summary>Sums two usages; cost is null if either side is unknown.</summary>
    public AgentUsage Add(AgentUsage other) => new(
        TokensIn + other.TokensIn,
        TokensOut + other.TokensOut,
        EstimatedCostUsd is { } a && other.EstimatedCostUsd is { } b ? a + b : null);
}
