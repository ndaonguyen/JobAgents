namespace JobAgents.Domain.Runs;

/// <summary>
/// Strongly-typed identifier for a single job-hunt run. Each run owns an isolated event stream.
/// </summary>
public readonly record struct RunId(string Value)
{
    public static RunId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
