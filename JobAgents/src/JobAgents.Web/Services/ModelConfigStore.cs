using System.Text.Json;

namespace JobAgents.Web.Services;

/// <summary>
/// The user's chosen model per agent, reused across sessions. A null field means "use the app default"
/// (the configured Anthropic/OpenAI model), so an empty config behaves exactly as before.
/// </summary>
public sealed record AgentModelConfig(
    string? CoordinatorModel = null,
    string? SearchModel = null,
    string? ResumeMatchModel = null,
    string? CompanyResearchModel = null,
    string? SalaryAnalysisModel = null,
    string? InterviewPrepModel = null,
    DateTime UpdatedAtUtc = default);

/// <summary>A selectable model: the id passed to the agents, plus a friendly label for the dropdown.</summary>
public sealed record ModelOption(string Id, string Label);

/// <summary>
/// The models offered in the picker. Only models priced in ModelPricingCalculator are listed, so cost
/// tracking keeps working; the empty id is the "use default" sentinel. Claude ids beginning "claude"
/// route to Anthropic automatically; everything else routes to OpenAI.
/// </summary>
public static class ModelCatalog
{
    public static readonly IReadOnlyList<ModelOption> Options =
    [
        new("", "Default"),
        new("claude-haiku-4-5", "Claude Haiku 4.5"),
        new("claude-sonnet-4", "Claude Sonnet 4"),
        new("claude-opus-4", "Claude Opus 4"),
        new("claude-3-5-haiku", "Claude 3.5 Haiku"),
        new("gpt-4o-mini", "GPT-4o mini"),
        new("gpt-4o", "GPT-4o"),
        new("gpt-4.1-mini", "GPT-4.1 mini"),
        new("gpt-4.1", "GPT-4.1"),
        new("o4-mini", "o4-mini"),
    ];
}

/// <summary>
/// Persists the per-agent model selection as <c>results/model-config.json</c>. Mirrors
/// <see cref="ProfileStore"/>: best-effort, single-config, file-backed so the choice survives restarts.
/// </summary>
public sealed class ModelConfigStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _file = Path.Combine(directory, "model-config.json");
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task SaveAsync(AgentModelConfig config, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(config with { UpdatedAtUtc = DateTime.UtcNow }, JsonOptions);

        await _lock.WaitAsync(ct);
        try
        {
            await File.WriteAllTextAsync(_file, json, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AgentModelConfig?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_file))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_file, ct);
            return JsonSerializer.Deserialize<AgentModelConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
