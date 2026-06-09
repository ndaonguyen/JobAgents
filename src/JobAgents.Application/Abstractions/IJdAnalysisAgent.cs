using JobAgents.Domain.Analysis;
using JobAgents.Domain.Runs;

namespace JobAgents.Application.Abstractions;

/// <summary>
/// Analyses a candidate's resume against a single pasted job description, returning a fit score,
/// matched strengths, gaps and tailoring advice. Standalone — not part of the job-hunt pipeline.
/// </summary>
public interface IJdAnalysisAgent
{
    Task<JdAnalysis> AnalyzeAsync(
        RunId runId, string resumeText, string jobDescription, string? modelOverride, CancellationToken ct);
}
