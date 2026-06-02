using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

public interface ISalaryAnalysisAgent
{
    Task<SalaryEstimate> AnalyzeAsync(
        RunId runId, int index, JobPosting posting, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct);
}

/// <summary>Estimates a salary range for a posting using web search for market data.</summary>
public sealed class SalaryAnalysisAgent(IAgentRunner runner) : ISalaryAnalysisAgent
{
    private const string SystemPrompt =
        """
        You are a salary-analysis agent. Use the Web.search_web tool to find market salary data for
        the given role, location and seniority. Use AT MOST 2 search_web calls (maxResults: 10) — stop
        once you have a usable range; do not keep searching for a more precise figure. Return ONLY a
        JSON object:
        {
          "low": number or null,
          "median": number or null,
          "high": number or null,
          "currency": string,
          "basis": string
        }
        Amounts are annual gross. "basis" briefly cites where the range came from. Use null for any
        figure you cannot ground in data rather than guessing.
        """;

    private sealed record EstimateDto(decimal? Low, decimal? Median, decimal? High, string? Currency, string? Basis);

    public async Task<SalaryEstimate> AnalyzeAsync(
        RunId runId, int index, JobPosting posting, SearchCriteria criteria, JobHuntConfig config, CancellationToken ct)
    {
        var userPrompt =
            $"""
            Estimate the salary range for:
            Role: {posting.Title}
            Company: {posting.Company}
            Location: {posting.Location}
            Seniority: {criteria.Seniority}
            """;

        var result = await runner.RunAsync(
            runId, AgentId.SalaryAnalysis(index), "Salary Analysis",
            SystemPrompt, userPrompt, config.SalaryAnalysisModel, useTools: true, ct);

        var dto = AgentJson.TryParse<EstimateDto>(result.Text);
        return new SalaryEstimate(
            Low: dto?.Low,
            Median: dto?.Median,
            High: dto?.High,
            Currency: string.IsNullOrWhiteSpace(dto?.Currency) ? "USD" : dto!.Currency!,
            Basis: dto?.Basis ?? "(no market data found)");
    }
}
