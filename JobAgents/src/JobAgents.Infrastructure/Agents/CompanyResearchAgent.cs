using JobAgents.Application.Abstractions;
using JobAgents.Domain.Agents;
using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Infrastructure.Agents;

public interface ICompanyResearchAgent
{
    Task<CompanyInsight> ResearchAsync(
        RunId runId, int index, string company, JobHuntConfig config, CancellationToken ct);
}

/// <summary>Researches a hiring company (culture, reputation, recent news) via web search.</summary>
public sealed class CompanyResearchAgent(IAgentRunner runner) : ICompanyResearchAgent
{
    private const string SystemPrompt =
        """
        You are a company-research agent. Use the Web.search_web tool to learn about the company a
        candidate may join. Return ONLY a JSON object:
        {
          "company": string,
          "summary": string,
          "highlights": string[],
          "recentNews": string[]
        }
        "highlights" are notable facts (size, products, culture signals). "recentNews" are recent
        developments. Ground everything in the search results; if little is found, say so in summary.
        """;

    private sealed record InsightDto(string? Company, string? Summary, List<string>? Highlights, List<string>? RecentNews);

    public async Task<CompanyInsight> ResearchAsync(
        RunId runId, int index, string company, JobHuntConfig config, CancellationToken ct)
    {
        var userPrompt = $"Research the company: {company}";

        var result = await runner.RunAsync(
            runId, AgentId.CompanyResearch(index), "Company Research",
            SystemPrompt, userPrompt, config.CompanyResearchModel, useTools: true, ct);

        var dto = AgentJson.TryParse<InsightDto>(result.Text);
        return new CompanyInsight(
            Company: dto?.Company ?? company,
            Summary: dto?.Summary ?? "(no company information found)",
            Highlights: dto?.Highlights ?? [],
            RecentNews: dto?.RecentNews ?? []);
    }
}
