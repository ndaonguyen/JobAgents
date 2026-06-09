namespace JobAgents.Application.Abstractions;

/// <summary>
/// Converts token counts for a given model into a USD cost. Returns <c>null</c> for unknown models
/// so the UI can show "—" rather than a misleading $0.00.
/// </summary>
public interface IUsageCalculator
{
    decimal? EstimateCostUsd(string model, int tokensIn, int tokensOut);
}
