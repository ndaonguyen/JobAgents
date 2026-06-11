using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using JobAgents.Application.Abstractions;
using JobAgents.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobAgents.Infrastructure.Sourcing;

/// <summary>
/// <see cref="IEmbeddingService"/> backed by OpenAI's embeddings endpoint, reached with the same key as
/// the chat connector. Reuses the named "openai" <see cref="HttpClient"/>. Disabled (returns empty) when
/// no key is set, and swallows transport errors so a failed embed degrades to keyword retrieval rather
/// than breaking a job-hunt run.
/// </summary>
public sealed class OpenAiEmbeddingService(
    IHttpClientFactory httpFactory,
    IOptions<JobAgentsOptions> options,
    ILogger<OpenAiEmbeddingService> logger) : IEmbeddingService
{
    // Real OpenAI — not the Anthropic-compatible endpoint. Anthropic has no embeddings API.
    private const string Endpoint = "https://api.openai.com/v1/embeddings";

    private readonly OpenAiOptions _openAi = options.Value.OpenAi;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_openAi.ApiKey);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        if (!IsEnabled || inputs.Count == 0)
            return [];

        try
        {
            var http = httpFactory.CreateClient("openai");
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(new EmbeddingRequest(_openAi.EmbeddingModel, inputs)),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAi.ApiKey);

            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var body = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>(ct);
            if (body?.Data is not { Count: > 0 } data)
                return [];

            // Provider may reorder; re-sort by the echoed index to keep input alignment.
            return data.OrderBy(d => d.Index).Select(d => d.Embedding).ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding request failed; falling back to keyword retrieval.");
            return [];
        }
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] List<EmbeddingDatum>? Data);

    private sealed record EmbeddingDatum(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding);
}
