using JobAgents.Application.Abstractions;

namespace JobAgents.Infrastructure.Pricing;

/// <summary>
/// Per-model USD pricing (per 1M tokens). Returns <c>null</c> for models not in the table so the UI
/// can show "—" instead of a misleading $0.00. Matching is prefix-based so dated snapshots
/// (e.g. <c>gpt-4o-mini-2024-07-18</c>) resolve to their base model.
/// </summary>
public sealed class ModelPricingCalculator : IUsageCalculator
{
    private sealed record Price(decimal InputPerMillion, decimal OutputPerMillion);

    // Ordered longest-prefix-first so specific snapshots win over base names.
    private static readonly (string Prefix, Price Price)[] Table =
    [
        ("gpt-4o-mini", new Price(0.15m, 0.60m)),
        ("gpt-4o",      new Price(2.50m, 10.00m)),
        ("gpt-4.1-mini", new Price(0.40m, 1.60m)),
        ("gpt-4.1",     new Price(2.00m, 8.00m)),
        ("o4-mini",     new Price(1.10m, 4.40m)),
        // Anthropic (Claude), per 1M tokens.
        ("claude-haiku-4-5",  new Price(1.00m, 5.00m)),
        ("claude-sonnet-4",   new Price(3.00m, 15.00m)),
        ("claude-opus-4",     new Price(15.00m, 75.00m)),
        ("claude-haiku",      new Price(1.00m, 5.00m)),
    ];

    public decimal? EstimateCostUsd(string model, int tokensIn, int tokensOut)
    {
        if (string.IsNullOrWhiteSpace(model))
            return null;

        var match = Table
            .Where(t => model.StartsWith(t.Prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Prefix.Length)
            .Select(t => (Price?)t.Price)
            .FirstOrDefault();

        if (match is not { } price)
            return null;

        return (tokensIn / 1_000_000m * price.InputPerMillion)
             + (tokensOut / 1_000_000m * price.OutputPerMillion);
    }
}
