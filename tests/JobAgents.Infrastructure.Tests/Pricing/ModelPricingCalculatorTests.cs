using FluentAssertions;
using JobAgents.Infrastructure.Pricing;

namespace JobAgents.Infrastructure.Tests.Pricing;

public class ModelPricingCalculatorTests
{
    private readonly ModelPricingCalculator _calc = new();

    [Fact]
    public void Known_model_returns_a_positive_cost()
    {
        var cost = _calc.EstimateCostUsd("gpt-4o-mini", 1_000_000, 1_000_000);
        cost.Should().Be(0.15m + 0.60m);
    }

    [Fact]
    public void Dated_snapshot_resolves_to_base_model_via_prefix()
    {
        var dated = _calc.EstimateCostUsd("gpt-4o-mini-2024-07-18", 1_000_000, 0);
        var baseModel = _calc.EstimateCostUsd("gpt-4o-mini", 1_000_000, 0);
        dated.Should().Be(baseModel);
    }

    [Fact]
    public void Unknown_model_returns_null()
    {
        _calc.EstimateCostUsd("some-random-model", 1000, 1000).Should().BeNull();
        _calc.EstimateCostUsd("", 1000, 1000).Should().BeNull();
    }

    [Fact]
    public void Longer_prefix_wins_so_mini_is_not_priced_as_gpt_4o()
    {
        // gpt-4o-mini must NOT match the more expensive gpt-4o row.
        var mini = _calc.EstimateCostUsd("gpt-4o-mini", 1_000_000, 0);
        var full = _calc.EstimateCostUsd("gpt-4o", 1_000_000, 0);
        mini.Should().BeLessThan(full!.Value);
    }
}
