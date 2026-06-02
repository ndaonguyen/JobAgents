using JobAgents.Domain.JobHunt;
using JobAgents.Domain.Runs;

namespace JobAgents.Domain.Agents;

/// <summary>
/// The input to a job-hunt run: the candidate's resume text and their free-form preferences
/// (target roles, locations, seniority, remote, salary expectations, etc.). When <see cref="Criteria"/>
/// is supplied (e.g. the user reviewed/edited the parsed criteria), the Coordinator uses it directly
/// instead of re-parsing the resume + preferences.
/// </summary>
public sealed record AgentRunRequest(
    RunId RunId, string ResumeText, string Preferences, SearchCriteria? Criteria = null);
