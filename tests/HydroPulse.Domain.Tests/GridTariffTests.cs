using HydroPulse.Domain.Engineering;
using Xunit;

namespace HydroPulse.Domain.Tests;

public class GridTariffTests
{
    private static readonly WeatherSeries Weather = TestWeather.Year();

    private static GridParameters TimeOfUse(double peak, double off, double max) => new()
    {
        ImportTariffUsdPerKwh = peak,
        OffPeakImportTariffUsdPerKwh = off,
        MaxImportPriceUsdPerKwh = max
    };

    [Fact]
    public void TariffAt_IsFlat_WhenNoOffPeakPriceIsSet()
    {
        var g = new GridParameters { ImportTariffUsdPerKwh = 0.12 };
        Assert.Equal(0.12, g.TariffAt(new DateTime(2024, 5, 1, 3, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(0.12, g.TariffAt(new DateTime(2024, 5, 1, 15, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(18, false)] // 21:00 local
    [InlineData(19, true)]  // 22:00 local, off-peak starts
    [InlineData(23, true)]  // 02:00 local
    [InlineData(2, true)]   // 05:00 local
    [InlineData(3, false)]  // 06:00 local, off-peak ends
    [InlineData(10, false)]
    public void TariffAt_OffPeakWindow_WrapsMidnight_InLocalTime(int utcHour, bool expectOffPeak)
    {
        var g = TimeOfUse(0.20, 0.10, 1.0); // UTC+3, off-peak 22:00 to 06:00 local
        double price = g.TariffAt(new DateTime(2024, 5, 1, utcHour, 0, 0, DateTimeKind.Utc));
        Assert.Equal(expectOffPeak ? 0.10 : 0.20, price);
    }

    [Fact]
    public void NoMatching_ImportsOnlyInHoursWhereTheTariffIsAcceptable()
    {
        // Peak price is above the limit, off-peak is below it: import may happen only in off-peak hours.
        var cfg = TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.None) with
        {
            Grid = TimeOfUse(0.30, 0.08, 0.10)
        };
        var r = ScenarioRunner.Run(cfg, Weather);

        Assert.True(r.Year1.GridImportMwh > 0);
        for (int i = 0; i < r.Hourly.GridImportKw.Length; i++)
        {
            int utcHour = i % 24;
            bool offPeak = utcHour >= 19 || utcHour < 3;
            if (!offPeak) Assert.Equal(0.0, r.Hourly.GridImportKw[i]);
        }
    }

    [Fact]
    public void DemandCharge_RaisesGridCost_WhenGridPowerIsImported()
    {
        var baseCfg = TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.None);
        var without = ScenarioRunner.Run(baseCfg, Weather);
        var with = ScenarioRunner.Run(
            baseCfg with { Grid = baseCfg.Grid with { DemandChargeUsdPerKvaMonth = 8.0 } }, Weather);

        Assert.True(without.Year1.GridImportMwh > 0);
        Assert.True(with.Finance.Breakdown!.GridElectricity > without.Finance.Breakdown!.GridElectricity);
        Assert.True(with.Finance.LcohUsdPerKg > without.Finance.LcohUsdPerKg);
    }

    [Fact]
    public void DemandCharge_DoesNotApply_WhenNothingIsImported()
    {
        var baseCfg = TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.Hourly);
        var without = ScenarioRunner.Run(baseCfg, Weather);
        var with = ScenarioRunner.Run(
            baseCfg with { Grid = baseCfg.Grid with { DemandChargeUsdPerKvaMonth = 8.0 } }, Weather);

        Assert.Equal(without.Finance.LcohUsdPerKg!.Value, with.Finance.LcohUsdPerKg!.Value, 9);
    }

    [Fact]
    public void FlatTariff_DefaultsLeaveDispatchUnchanged()
    {
        // With no off-peak price and no demand charge, behaviour matches a plain flat-tariff run.
        var cfg = TestWeather.WindPlant(OperatingMode.GridConnected, MatchingPolicy.None);
        var explicitFlat = cfg with { Grid = cfg.Grid with { OffPeakImportTariffUsdPerKwh = null, DemandChargeUsdPerKvaMonth = 0 } };
        var a = ScenarioRunner.Run(cfg, Weather);
        var b = ScenarioRunner.Run(explicitFlat, Weather);
        Assert.Equal(a.Year1.GridImportMwh, b.Year1.GridImportMwh);
        Assert.Equal(a.Finance.LcohUsdPerKg!.Value, b.Finance.LcohUsdPerKg!.Value, 9);
    }
}
