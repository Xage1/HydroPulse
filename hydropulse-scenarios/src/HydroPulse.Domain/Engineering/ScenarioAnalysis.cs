namespace HydroPulse.Domain.Engineering;

public sealed record SweepPoint(
    double Ratio,
    double? LcohUsdPerKg,
    double ElectrolyzerCapacityFactor,
    double SurplusShare,
    double HydrogenTonnes);

public sealed record SensitivityBar(
    string Parameter,
    string LowLabel,
    string HighLabel,
    double? LcohLow,
    double? LcohHigh);

public static class ScenarioAnalysis
{
    /// <summary>Scales PV and wind together (keeping their mix) so that renewable capacity = ratio x electrolyzer rating.</summary>
    public static ScenarioConfig ScaleRenewables(ScenarioConfig cfg, double ratio)
    {
        double total = cfg.Pv.CapacityKwp + cfg.Wind.CapacityKw;
        if (total <= 0) throw new ArgumentException("Configuration has no renewable capacity to scale.", nameof(cfg));

        double f = ratio * cfg.Electrolyzer.RatedPowerKw / total;
        return cfg with
        {
            Pv = cfg.Pv with { CapacityKwp = cfg.Pv.CapacityKwp * f },
            Wind = cfg.Wind with { CapacityKw = cfg.Wind.CapacityKw * f }
        };
    }

    public static IReadOnlyList<SweepPoint> SweepRatio(
        ScenarioConfig cfg, WeatherSeries weather, IEnumerable<double> ratios)
    {
        var points = new List<SweepPoint>();
        foreach (double ratio in ratios)
        {
            var r = ScenarioRunner.Run(ScaleRenewables(cfg, ratio), weather);
            double re = r.Year1.RenewableGenerationMwh;
            double surplus = r.Year1.CurtailedMwh + r.Year1.GridExportMwh;
            points.Add(new SweepPoint(
                ratio,
                r.Finance.LcohUsdPerKg,
                r.Year1.ElectrolyzerCapacityFactor,
                re > 0 ? surplus / re : 0.0,
                r.Year1.HydrogenTonnes));
        }
        return points;
    }

    /// <summary>The ratio with the lowest LCOH, or null if no point produced hydrogen.</summary>
    public static double? BestRatio(IReadOnlyList<SweepPoint> sweep) =>
        sweep.Where(p => p.LcohUsdPerKg.HasValue)
             .OrderBy(p => p.LcohUsdPerKg!.Value)
             .Select(p => (double?)p.Ratio)
             .FirstOrDefault();

    /// <summary>Scales the tariff and the maximum acceptable import price together, so dispatch behaviour is unchanged.</summary>
    private static ScenarioConfig ScaleTariff(ScenarioConfig cfg, double factor) =>
        cfg with
        {
            Grid = cfg.Grid with
            {
                ImportTariffUsdPerKwh = cfg.Grid.ImportTariffUsdPerKwh * factor,
                MaxImportPriceUsdPerKwh = cfg.Grid.MaxImportPriceUsdPerKwh * factor
            }
        };

    /// <summary>One-at-a-time sensitivity of LCOH to the main assumptions (for a tornado chart).</summary>
    public static IReadOnlyList<SensitivityBar> Sensitivity(ScenarioConfig cfg, WeatherSeries weather)
    {
        double? Lcoh(ScenarioConfig c) => ScenarioRunner.Run(c, weather).Finance.LcohUsdPerKg;
        var f = cfg.Finance;

        return new List<SensitivityBar>
        {
            new("Electrolyzer capex", "-20%", "+20%",
                Lcoh(cfg with { Finance = f with { ElectrolyzerCapexUsdPerKw = f.ElectrolyzerCapexUsdPerKw * 0.8 } }),
                Lcoh(cfg with { Finance = f with { ElectrolyzerCapexUsdPerKw = f.ElectrolyzerCapexUsdPerKw * 1.2 } })),

            new("Renewable capex", "-20%", "+20%",
                Lcoh(cfg with { Finance = f with { PvCapexUsdPerKwp = f.PvCapexUsdPerKwp * 0.8, WindCapexUsdPerKw = f.WindCapexUsdPerKw * 0.8 } }),
                Lcoh(cfg with { Finance = f with { PvCapexUsdPerKwp = f.PvCapexUsdPerKwp * 1.2, WindCapexUsdPerKw = f.WindCapexUsdPerKw * 1.2 } })),

            new("Discount rate", $"{f.DiscountRate - 0.02:P0}", $"{f.DiscountRate + 0.02:P0}",
                Lcoh(cfg with { Finance = f with { DiscountRate = f.DiscountRate - 0.02 } }),
                Lcoh(cfg with { Finance = f with { DiscountRate = f.DiscountRate + 0.02 } })),

            new("Stack efficiency (kWh/kg)", "-5%", "+5%",
                Lcoh(cfg with { Electrolyzer = cfg.Electrolyzer with { SpecificEnergyAtRatedKwhPerKg = cfg.Electrolyzer.SpecificEnergyAtRatedKwhPerKg * 0.95 } }),
                Lcoh(cfg with { Electrolyzer = cfg.Electrolyzer with { SpecificEnergyAtRatedKwhPerKg = cfg.Electrolyzer.SpecificEnergyAtRatedKwhPerKg * 1.05 } })),

            new("Grid tariff", "-25%", "+25%",
                Lcoh(ScaleTariff(cfg, 0.75)),
                Lcoh(ScaleTariff(cfg, 1.25)))
        };
    }
}
