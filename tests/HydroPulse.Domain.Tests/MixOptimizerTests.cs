using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class MixOptimizerTests
{
    private static readonly WeatherSeries Weather = TestWeather.Year();
    private static readonly double[] Ratios = { 1.0, 2.0, 3.0 };

    private static ScenarioConfig Plant(OperatingMode mode = OperatingMode.OffGrid) => new()
    {
        Name = "mix",
        Mode = mode,
        Matching = MatchingPolicy.Hourly,
        Electrolyzer = new ElectrolyzerParameters { RatedPowerKw = 1000 }
    };

    [Fact]
    public void OptimizeMix_ReturnsOnePointPerShare_InOrder()
    {
        var mix = ScenarioAnalysis.OptimizeMix(Plant(), Weather, new[] { 0.0, 0.5, 1.0 }, Ratios);

        Assert.Equal(3, mix.Count);
        Assert.Equal(new[] { 0.0, 0.5, 1.0 }, mix.Select(p => p.PvShare));
        Assert.All(mix, p => Assert.Contains(p.BestRatio, new double?[] { 1.0, 2.0, 3.0 }));
    }

    [Fact]
    public void OptimizeMix_EndPoints_MatchPureWindAndPureSolarSweeps()
    {
        var mix = ScenarioAnalysis.OptimizeMix(Plant(), Weather, new[] { 0.0, 1.0 }, Ratios);

        var windOnly = ScenarioAnalysis.SweepRatio(
            Plant() with { Wind = new WindParameters { CapacityKw = 1 } }, Weather, Ratios);
        var solarOnly = ScenarioAnalysis.SweepRatio(
            Plant() with { Pv = new PvParameters { CapacityKwp = 1 } }, Weather, Ratios);

        Assert.Equal(windOnly.Min(p => p.LcohUsdPerKg!.Value), mix[0].LcohUsdPerKg!.Value, 9);
        Assert.Equal(solarOnly.Min(p => p.LcohUsdPerKg!.Value), mix[1].LcohUsdPerKg!.Value, 9);
    }

    [Fact]
    public void BestPvShare_IsTheLowestCostPoint()
    {
        var mix = ScenarioAnalysis.OptimizeMix(Plant(), Weather, new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }, Ratios);
        double? best = ScenarioAnalysis.BestPvShare(mix);

        Assert.NotNull(best);
        double lowest = mix.Min(p => p.LcohUsdPerKg!.Value);
        Assert.Equal(lowest, mix.Single(p => p.PvShare == best).LcohUsdPerKg!.Value, 12);
    }

    [Fact]
    public void BestPvShare_IsNull_WhenNothingProducesHydrogen()
    {
        Assert.Null(ScenarioAnalysis.BestPvShare(new[] { new MixPoint(0.5, null, null, 0, 0) }));
    }

    [Fact]
    public void OptimizeMix_RejectsSharesOutsideZeroToOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScenarioAnalysis.OptimizeMix(Plant(), Weather, new[] { 1.5 }, Ratios));
    }

    [Fact]
    public void OptimizeMix_MixedSupplyIsNeverWorseThanTheWorseEndpoint()
    {
        // Solar and wind complement each other in the synthetic year, so a blend should beat at least one pure source.
        var mix = ScenarioAnalysis.OptimizeMix(Plant(), Weather, new[] { 0.0, 0.5, 1.0 }, Ratios);
        Assert.True(mix[1].LcohUsdPerKg <= Math.Max(mix[0].LcohUsdPerKg!.Value, mix[2].LcohUsdPerKg!.Value));
    }
}
