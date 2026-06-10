namespace JobAgents.Application.Abstractions;

/// <summary>
/// Turns text into embedding vectors for semantic retrieval. When no provider is configured
/// (<see cref="IsEnabled"/> is false) or a request fails, <see cref="EmbedAsync"/> returns an empty
/// list so callers degrade to keyword matching instead of failing the run.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>True when an embedding provider/key is configured. False ⇒ callers should use keyword fallback.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Embeds each input. Returns one vector per input, in input order, or an empty list when the
    /// service is disabled or the request fails. Never throws for transport/provider errors.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default);
}
