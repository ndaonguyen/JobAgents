using JobAgents.Domain.Runs;

namespace JobAgents.Domain.Agents;

/// <summary>
/// The input to a job-hunt run: the candidate's resume text and their free-form preferences
/// (target roles, locations, seniority, remote, salary expectations, etc.).
/// </summary>
public sealed record AgentRunRequest(RunId RunId, string ResumeText, string Preferences);
