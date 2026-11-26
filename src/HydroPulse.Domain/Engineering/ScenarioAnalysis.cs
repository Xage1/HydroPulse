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

/// <summary>One solar/wind split: the cheapest renewable-to-electrolyzer ratio for that split and what it costs.</summary>
public sealed record MixPoint(
    double PvShare,
    double? BestRatio,
    double? LcohUsdPerKg,
    double ElectrolyzerCapacityFactor,
    double HydrogenTonnes);

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

    /// <summary>
    /// Finds the cheapest split of renewable capacity between PV and wind. For every PV share (0 = all wind,
    /// 1 = all solar) the ratio sweep runs and the best ratio is kept, so each point is the cheapest plant
    /// with that split. The configuration's own PV/wind sizes are ignored; only the weather and the other
    /// parameters are used.
    /// </summary>
    public static IReadOnlyList<MixPoint> OptimizeMix(
        ScenarioConfig cfg, WeatherSeries weather, IEnumerable<double> pvShares, IReadOnlyList<double> ratios)
    {
        var points = new List<MixPoint>();
        foreach (double share in pvShares)
        {
            if (share < 0.0 || share > 1.0) throw new ArgumentOutOfRangeException(nameof(pvShares), "PV shares must be between 0 and 1.");

            // Capacities sum to 1, so ScaleRenewables turns the ratio directly into total renewable kW.
            var split = cfg with
            {
                Pv = cfg.Pv with { CapacityKwp = share },
                Wind = cfg.Wind with { CapacityKw = 1.0 - share }
            };
            var sweep = SweepRatio(split, weather, ratios);
            double? best = BestRatio(sweep);
            var at = best is null ? null : sweep.First(p => p.Ratio == best);
            points.Add(new MixPoint(share, best, at?.LcohUsdPerKg, at?.ElectrolyzerCapacityFactor ?? 0.0, at?.HydrogenTonnes ?? 0.0));
        }
        return points;
    }

    /// <summary>The PV share with the lowest LCOH, or null if no split produced hydrogen.</summary>
    public static double? BestPvShare(IReadOnlyList<MixPoint> mix) =>
        mix.Where(p => p.LcohUsdPerKg.HasValue)
           .OrderBy(p => p.LcohUsdPerKg!.Value)
           .Select(p => (double?)p.PvShare)
           .FirstOrDefault();

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
                OffPeakImportTariffUsdPerKwh = cfg.Grid.OffPeakImportTariffUsdPerKwh * factor,
                DemandChargeUsdPerKvaMonth = cfg.Grid.DemandChargeUsdPerKvaMonth * factor,
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
            // Low/high are the cited reference range (docs/SOURCES.md): IEA installed cost USD 1,000-2,500/kW.
            new("Electrolyzer capex", "$1,000/kW", "$2,500/kW",
                Lcoh(cfg with { Finance = f with { ElectrolyzerCapexUsdPerKw = 1000.0 } }),
                Lcoh(cfg with { Finance = f with { ElectrolyzerCapexUsdPerKw = 2500.0 } })),

            new("Renewable capex", "-20%", "+20%",
                Lcoh(cfg with { Finance = f with { PvCapexUsdPerKwp = f.PvCapexUsdPerKwp * 0.8, WindCapexUsdPerKw = f.WindCapexUsdPerKw * 0.8 } }),
                Lcoh(cfg with { Finance = f with { PvCapexUsdPerKwp = f.PvCapexUsdPerKwp * 1.2, WindCapexUsdPerKw = f.WindCapexUsdPerKw * 1.2 } })),

            // IEA: 8.5-9% for Kenyan utility solar; IRENA assumes 12% for African wind.
            new("Discount rate", "8%", "12%",
                Lcoh(cfg with { Finance = f with { DiscountRate = 0.08 } }),
                Lcoh(cfg with { Finance = f with { DiscountRate = 0.12 } })),

            new("Stack efficiency (kWh/kg)", "-5%", "+5%",
                Lcoh(cfg with { Electrolyzer = cfg.Electrolyzer with { SpecificEnergyAtRatedKwhPerKg = cfg.Electrolyzer.SpecificEnergyAtRatedKwhPerKg * 0.95 } }),
                Lcoh(cfg with { Electrolyzer = cfg.Electrolyzer with { SpecificEnergyAtRatedKwhPerKg = cfg.Electrolyzer.SpecificEnergyAtRatedKwhPerKg * 1.05 } })),

            new("Grid tariff", "-25%", "+25%",
                Lcoh(ScaleTariff(cfg, 0.75)),
                Lcoh(ScaleTariff(cfg, 1.25)))
        };
    }
}
