using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

internal static class TestWeather
{
    /// <summary>A non-leap year of synthetic hours: diurnal sun, and wind swinging between 2 and 12 m/s (37 h period).</summary>
    public static WeatherSeries Year()
    {
        var list = new List<WeatherSample>(8760);
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 8760; i++)
        {
            var ts = start.AddHours(i);
            double ghi = ts.Hour is >= 6 and <= 18 ? 900.0 * Math.Sin(Math.PI * (ts.Hour - 6) / 12.0) : 0.0;
            double v50 = 7.0 + 5.0 * Math.Sin(2 * Math.PI * i / 37.0);
            list.Add(new WeatherSample(ts, ghi, v50, v50 * 0.75, 25.0));
        }
        return new WeatherSeries("Synthetic", 0.0, list, 0);
    }

    public static ScenarioConfig WindPlant(OperatingMode mode, MatchingPolicy matching = MatchingPolicy.Hourly) => new()
    {
        Name = "test",
        Mode = mode,
        Matching = matching,
        Electrolyzer = new ElectrolyzerParameters { RatedPowerKw = 1000 },
        Wind = new WindParameters { CapacityKw = 1500 }
    };
}

public class ScenarioRunnerTests
{
    private static readonly WeatherSeries Weather = TestWeather.Year();

    [Fact]
    public void OffGrid_ImportsNothing_AndBalancesEnergy()
    {
        var r = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.OffGrid), Weather);
        var y = r.Year1;

        Assert.Equal(0.0, y.GridImportMwh);
        Assert.Equal(y.RenewableGenerationMwh,
            y.ElectrolyzerEnergyMwh + y.CompressionEnergyMwh + y.CurtailedMwh,
            precision: 3);
        Assert.Equal(1.0, y.SimultaneousRenewableShare, 6);
        Assert.True(y.HydrogenTonnes > 0);
        Assert.InRange(y.ElectrolyzerCapacityFactor, 0.01, 0.99);
        Assert.InRange(y.StandbyHours, 1, 8759);
    }

    [Fact]
    public void OffGrid_SpecificConsumption_IncludesCompression()
    {
        var y = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.OffGrid), Weather).Year1;
        Assert.InRange(y.SpecificConsumptionKwhPerKg, 50.0, 56.0);
        Assert.True(y.CompressionEnergyMwh > 0);
    }

    [Fact]
    public void HourlyMatching_NeverImports_AndProducesSameAsOffGrid()
    {
        var off = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.OffGrid), Weather).Year1;
        var hourly = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.Hourly), Weather).Year1;

        Assert.Equal(0.0, hourly.GridImportMwh);
        Assert.Equal(1.0, hourly.SimultaneousRenewableShare, 6);
        Assert.Equal(off.HydrogenTonnes, hourly.HydrogenTonnes, 6);
        Assert.True(hourly.GridExportMwh > 0);   // surplus is exported, not curtailed
    }

    [Fact]
    public void MonthlyMatching_UsesBankedEnergy_AndStaysMonthlyCompliant()
    {
        var hourly = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.Hourly), Weather).Year1;
        var monthly = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.Monthly), Weather).Year1;

        Assert.True(monthly.GridImportMwh > 0);
        Assert.True(monthly.HydrogenTonnes > hourly.HydrogenTonnes);
        Assert.True(monthly.MonthlyMatchedShare >= 0.9999);
        Assert.True(monthly.SimultaneousRenewableShare < 1.0);
        Assert.True(monthly.GridImportMwh <= monthly.GridExportMwh + 1e-6);
    }

    [Fact]
    public void NoMatching_ImportsGridPower_AndRaisesUtilisation()
    {
        var off = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.OffGrid), Weather).Year1;
        var open = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.None), Weather).Year1;

        Assert.True(open.GridImportMwh > 0);
        Assert.True(open.ElectrolyzerCapacityFactor > off.ElectrolyzerCapacityFactor);
        Assert.True(open.SimultaneousRenewableShare < 1.0);
    }

    [Fact]
    public void NoMatching_WhenTariffAboveLimit_BehavesLikeRenewableOnly()
    {
        var cfg = TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.None) with
        {
            Grid = new GridParameters { ImportTariffUsdPerKwh = 0.30, MaxImportPriceUsdPerKwh = 0.10 }
        };
        Assert.Equal(0.0, ScenarioRunner.Run(cfg, Weather).Year1.GridImportMwh);
    }

    [Theory]
    [InlineData(OperatingMode.OffGrid, MatchingPolicy.Hourly)]
    [InlineData(OperatingMode.GridConnected, MatchingPolicy.None)]
    [InlineData(OperatingMode.GridConnected, MatchingPolicy.Monthly)]
    public void LcohBreakdown_SumsToLcoh(OperatingMode mode, MatchingPolicy matching)
    {
        var f = ScenarioRunner.Run(TestWeather.WindPlant(mode, matching), Weather).Finance;

        Assert.NotNull(f.LcohUsdPerKg);
        Assert.NotNull(f.Breakdown);
        var b = f.Breakdown!;
        double sum = b.ElectrolyzerCapex + b.RenewableCapex + b.OtherCapex + b.FixedOpex
                     + b.Water + b.GridElectricity + b.StackReplacement;

        Assert.Equal(f.LcohUsdPerKg!.Value, sum, 6);
        Assert.True(f.LcohUsdPerKg > 0);
    }

    [Fact]
    public void Run_WithoutRenewableCapacity_Throws()
    {
        var cfg = new ScenarioConfig { Electrolyzer = new ElectrolyzerParameters { RatedPowerKw = 1000 } };
        Assert.Throws<ArgumentException>(() => ScenarioRunner.Run(cfg, Weather));
    }

    [Fact]
    public void Run_ReturnsTwelveMonthsAndFullHourlySeries()
    {
        var r = ScenarioRunner.Run(TestWeather.WindPlant(OperatingMode.OffGrid), Weather);

        Assert.Equal(12, r.Monthly.Count);
        Assert.Equal(8760, r.Hourly.RenewableKw.Length);
        Assert.Equal(8760, r.Hourly.HydrogenKgPerHour.Length);
        Assert.Equal(r.Year1.HydrogenTonnes, r.Monthly.Sum(m => m.HydrogenTonnes), 3);
    }
}

public class ScenarioAnalysisTests
{
    private static readonly WeatherSeries Weather = TestWeather.Year();

    [Fact]
    public void ScaleRenewables_SetsCapacityToRatioTimesElectrolyzer()
    {
        var cfg = TestWeather.WindPlant(OperatingMode.OffGrid) with
        {
            Pv = new PvParameters { CapacityKwp = 500 },
            Wind = new WindParameters { CapacityKw = 1500 }
        };

        var scaled = ScenarioAnalysis.ScaleRenewables(cfg, 2.0);

        Assert.Equal(2000.0, scaled.Pv.CapacityKwp + scaled.Wind.CapacityKw, 6);
        Assert.Equal(0.25, scaled.Pv.CapacityKwp / (scaled.Pv.CapacityKwp + scaled.Wind.CapacityKw), 6);
    }

    [Fact]
    public void SweepRatio_ReturnsOnePointPerRatio_AndBestRatioIsOneOfThem()
    {
        var sweep = ScenarioAnalysis.SweepRatio(TestWeather.WindPlant(OperatingMode.OffGrid), Weather, new[] { 0.5, 1.0, 2.0 });

        Assert.Equal(3, sweep.Count);
        Assert.Contains(ScenarioAnalysis.BestRatio(sweep), new double?[] { 0.5, 1.0, 2.0 });
    }

    [Fact]
    public void Sensitivity_HigherCapex_RaisesLcoh()
    {
        var bars = ScenarioAnalysis.Sensitivity(TestWeather.WindPlant(OperatingMode.OffGrid), Weather);
        var capex = bars.Single(b => b.Parameter == "Electrolyzer capex");

        Assert.True(capex.LcohHigh > capex.LcohLow);
    }
}
