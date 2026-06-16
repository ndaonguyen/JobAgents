using JobAgents.Domain.JobHunt;
using JobAgents.Web.Services;

namespace JobAgents.Api;

/// <summary>Parse-only request: resume + preferences → <see cref="SearchCriteria"/> for review.</summary>
public sealed record AnalyzeRequest(string Resume, string Preferences);

/// <summary>
/// A full job-hunt request from the React client. The server derives the preferences block, run title,
/// included domains and the <c>JobHuntConfig</c> from <see cref="Inputs"/> (mirroring the Blazor Home
/// page), so the client only sends the structured form state plus the resume.
/// </summary>
public sealed record HuntRequest(
    string Resume,
    SearchInputs Inputs,
    int SearchBoost = 0);

/// <summary>Resume vs a single pasted job description (standalone JD analyzer).</summary>
public sealed record JdAnalyzeRequest(string ResumeText, string JobDescription);

/// <summary>On-demand research for one already-matched posting.</summary>
public sealed record ExpandRequest(JobMatch Match, SearchCriteria? Criteria);

/// <summary>Save the candidate's reusable CV text.</summary>
public sealed record SaveProfileRequest(string ResumeText);

/// <summary>Create / update an improvement idea.</summary>
public sealed record IdeaUpsertRequest(string Title, string Description);

/// <summary>Set an idea's workflow status.</summary>
public sealed record IdeaStatusRequest(string Status);

/// <summary>Rename a saved run.</summary>
public sealed record RenameRequest(string Title);

/// <summary>Pin / unpin a saved run.</summary>
public sealed record PinRequest(bool Pinned);

/// <summary>A human score for one match, attributed to a finished run.</summary>
public sealed record FeedbackRequest(
    string RunId,
    JobPosting Posting,
    SearchCriteria Criteria,
    int AgentScore,
    IReadOnlyList<string> AgentMatchedSkills,
    string Resume,
    int HumanScore,
    string? Note);
