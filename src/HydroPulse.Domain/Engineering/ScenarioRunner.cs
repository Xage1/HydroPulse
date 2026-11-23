namespace HydroPulse.Domain.Engineering;

/// <summary>
/// Replays one weather year through the plant for every project year (weather repeats, equipment degrades),
/// then computes physical results (year one) and a discounted LCOH with a cost breakdown. Pure and I/O-free.
///
/// Per hour, renewable generation r is computed, then the electrolyzer is dispatched with the power it may use:
///   OffGrid                 : r
///   GridConnected / None    : r + MaxImportKw if the tariff is acceptable, else r
///   GridConnected / Monthly : r + energy banked from earlier surplus in the same month
///   GridConnected / Hourly  : r
/// Compression is a parasitic load: total demand = stack power * (1 + e_c / SEC).
/// </summary>
public static class ScenarioRunner
{
    public static ScenarioResult Run(ScenarioConfig cfg, WeatherSeries weather)
    {
        if (cfg.Electrolyzer.RatedPowerKw <= 0)
            throw new ArgumentException("Electrolyzer rated power must be positive.", nameof(cfg));
        if (cfg.Pv.CapacityKwp <= 0 && cfg.Wind.CapacityKw <= 0)
            throw new ArgumentException("At least one renewable source needs capacity.", nameof(cfg));
        if (cfg.Finance.ProjectLifetimeYears < 1)
            throw new ArgumentException("Project lifetime must be at least one year.", nameof(cfg));
        if (weather.Samples.Count == 0)
            throw new ArgumentException("Weather series is empty.", nameof(weather));

        var fin = cfg.Finance;
        var model = new ElectrolyzerModel(cfg.Electrolyzer);
        double ec = CompressionModel.EnergyKwhPerKg(
            cfg.Compression.InletBar, cfg.Compression.OutletBar, cfg.Compression.Stages);

        int n = weather.Samples.Count;
        var pvBase = new double[n];
        var wind = new double[n];
        for (int i = 0; i < n; i++)
        {
            var s = weather.Samples[i];
            pvBase[i] = PvModel.PowerKw(cfg.Pv, s.IrradianceWhPerM2, s.AirTemperatureC);
            wind[i] = WindModel.PowerKw(cfg.Wind, s.WindSpeed50mMs, s.WindSpeed10mMs, weather.ElevationM, s.AirTemperatureC);
        }

        double capexEl = cfg.Electrolyzer.RatedPowerKw * fin.ElectrolyzerCapexUsdPerKw;
        double capexRe = cfg.Pv.CapacityKwp * fin.PvCapexUsdPerKwp + cfg.Wind.CapacityKw * fin.WindCapexUsdPerKw;
        double capexOther = fin.OtherCapexFraction * (capexEl + capexRe);
        double totalCapex = capexEl + capexRe + capexOther;

        var yearly = new List<YearlyProjection>();
        double dFixedOpex = 0, dWater = 0, dGrid = 0, dRepl = 0;
        double lifetimeH2Kg = 0;
        double opHours = 0;
        int replacements = 0;
        YearResult? first = null;

        for (int year = 1; year <= fin.ProjectLifetimeYears; year++)
        {
            double pvFactor = Math.Pow(1.0 - cfg.Pv.DegradationPerYear, year - 1);
            var yr = SimulateYear(cfg, weather, model, ec, pvBase, wind, pvFactor, ref opHours, collectSeries: year == 1);
            first ??= yr;

            double replacement = 0;
            if (opHours >= fin.StackLifetimeHours)
            {
                replacement = fin.StackReplacementFraction * capexEl;
                opHours = 0;
                replacements++;
            }

            double fixedOpex = fin.OpexFractionOfCapexPerYear * totalCapex;
            double waterCost = yr.WaterLiters / 1000.0 * fin.WaterCostUsdPerM3;

            yearly.Add(new YearlyProjection(fixedOpex + waterCost, yr.GridCostUsd, replacement, yr.HydrogenKg));
            lifetimeH2Kg += yr.HydrogenKg;

            double factor = Math.Pow(1.0 + fin.DiscountRate, year);
            dFixedOpex += fixedOpex / factor;
            dWater += waterCost / factor;
            dGrid += yr.GridCostUsd / factor;
            dRepl += replacement / factor;
        }

        double? lcoh = null;
        LcohBreakdown? breakdown = null;
        try
        {
            var result = LcohCalculator.Calculate(totalCapex, fin.DiscountRate, yearly);
            double d = result.DiscountedHydrogenKg;
            lcoh = result.LcohUsdPerKg;
            breakdown = new LcohBreakdown(
                ElectrolyzerCapex: capexEl / d,
                RenewableCapex: capexRe / d,
                OtherCapex: capexOther / d,
                FixedOpex: dFixedOpex / d,
                Water: dWater / d,
                GridElectricity: dGrid / d,
                StackReplacement: dRepl / d);
        }
        catch (InvalidOperationException)
        {
            // No hydrogen produced in any year: LCOH is undefined.
        }

        var y1 = first!;
        double rated = cfg.Electrolyzer.RatedPowerKw;
        double reCapacityKw = cfg.Pv.CapacityKwp + cfg.Wind.CapacityKw;
        double consumedKwh = y1.StackKwh + y1.CompressionKwh;

        double matchedMonthly = 0;
        for (int m = 0; m < 12; m++) matchedMonthly += Math.Min(y1.MonthConsumedKwh[m], y1.MonthRenewableKwh[m]);

        var annual = new AnnualSummary(
            RenewableGenerationMwh: y1.RenewableKwh / 1000.0,
            ElectrolyzerEnergyMwh: y1.StackKwh / 1000.0,
            CompressionEnergyMwh: y1.CompressionKwh / 1000.0,
            GridImportMwh: y1.ImportKwh / 1000.0,
            GridExportMwh: y1.ExportKwh / 1000.0,
            CurtailedMwh: y1.CurtailedKwh / 1000.0,
            HydrogenTonnes: y1.HydrogenKg / 1000.0,
            WaterM3: y1.WaterLiters / 1000.0,
            ElectrolyzerCapacityFactor: SafeDiv(y1.StackKwh, rated * n),
            FullLoadHours: SafeDiv(y1.StackKwh, rated),
            RenewableCapacityFactor: SafeDiv(y1.RenewableKwh, reCapacityKw * n),
            SimultaneousRenewableShare: SafeDiv(y1.MatchedHourlyKwh, consumedKwh),
            MonthlyMatchedShare: SafeDiv(matchedMonthly, consumedKwh),
            SpecificConsumptionKwhPerKg: SafeDiv(consumedKwh, y1.HydrogenKg),
            StandbyHours: y1.StandbyHours);

        var monthly = Enumerable.Range(0, 12).Select(m => new MonthlySummary(
            Month: m + 1,
            RenewableMwh: y1.MonthRenewableKwh[m] / 1000.0,
            ElectrolyzerMwh: y1.MonthConsumedKwh[m] / 1000.0,
            GridImportMwh: y1.MonthImportKwh[m] / 1000.0,
            SurplusMwh: y1.MonthSurplusKwh[m] / 1000.0,
            HydrogenTonnes: y1.MonthH2Kg[m] / 1000.0)).ToList();

        return new ScenarioResult(
            Name: cfg.Name,
            Site: weather.Name,
            Mode: cfg.Mode,
            Matching: cfg.Mode == OperatingMode.GridConnected ? cfg.Matching : null,
            Plant: new PlantSummary(rated / 1000.0, cfg.Pv.CapacityKwp / 1000.0, cfg.Wind.CapacityKw / 1000.0, SafeDiv(reCapacityKw, rated)),
            Year1: annual,
            Finance: new FinancialSummary(lcoh, breakdown, totalCapex, lifetimeH2Kg / 1000.0, replacements),
            Monthly: monthly,
            Hourly: new HourlySeries(y1.SeriesRenewable!, y1.SeriesConsumed!, y1.SeriesH2!, y1.SeriesImport!));
    }

    private static YearResult SimulateYear(
        ScenarioConfig cfg, WeatherSeries weather, ElectrolyzerModel model, double ec,
        double[] pvBase, double[] wind, double pvFactor, ref double opHours, bool collectSeries)
    {
        var grid = cfg.Grid;
        int n = weather.Samples.Count;
        var yr = new YearResult();
        if (collectSeries)
        {
            yr.SeriesRenewable = new double[n];
            yr.SeriesConsumed = new double[n];
            yr.SeriesH2 = new double[n];
            yr.SeriesImport = new double[n];
        }

        bool gridConnected = cfg.Mode == OperatingMode.GridConnected;
        bool tariffAcceptable = grid.ImportTariffUsdPerKwh <= grid.MaxImportPriceUsdPerKwh;
        double bank = 0.0;
        int prevMonth = weather.Samples[0].TimestampUtc.Month;

        for (int i = 0; i < n; i++)
        {
            int month = weather.Samples[i].TimestampUtc.Month;
            if (month != prevMonth)
            {
                FlushBank(ref bank, yr, grid);
                prevMonth = month;
            }

            double r = wind[i] + pvBase[i] * pvFactor;

            double allowed;
            if (!gridConnected) allowed = r;
            else switch (cfg.Matching)
            {
                case MatchingPolicy.None:
                    allowed = r + (tariffAcceptable ? grid.MaxImportKw : 0.0);
                    break;
                case MatchingPolicy.Monthly:
                    allowed = r + Math.Min(bank, grid.MaxImportKw);
                    break;
                default: // Hourly
                    allowed = r;
                    break;
            }

            var d = PlantDispatch.Run(model, allowed, opHours, ec);
            double c = d.StackKw + d.CompressionKw;

            double import = 0.0;
            double surplus = Math.Max(0.0, r - c);
            if (gridConnected && cfg.Matching != MatchingPolicy.Hourly)
            {
                import = Math.Max(0.0, c - r);
                import = Math.Min(import, allowed - r);
            }

            if (d.StackKw > 0) { opHours += 1.0; yr.RunHours += 1.0; }
            else yr.StandbyHours += 1.0;

            yr.RenewableKwh += r;
            yr.StackKwh += d.StackKw;
            yr.CompressionKwh += d.CompressionKw;
            yr.HydrogenKg += d.H2KgPerHour;
            yr.WaterLiters += d.WaterLitersPerHour;
            yr.MatchedHourlyKwh += Math.Min(c, r);
            yr.ImportKwh += import;

            if (!gridConnected)
            {
                yr.CurtailedKwh += surplus;
            }
            else
            {
                yr.ExportKwh += surplus;
                switch (cfg.Matching)
                {
                    case MatchingPolicy.None:
                        yr.GridCostUsd += import * grid.ImportTariffUsdPerKwh - surplus * grid.ExportPriceUsdPerKwh;
                        break;
                    case MatchingPolicy.Monthly:
                        bank = Math.Max(0.0, bank - import + surplus);
                        yr.GridCostUsd += import * grid.WheelingFeeUsdPerKwh;
                        break;
                    default:
                        yr.GridCostUsd -= surplus * grid.ExportPriceUsdPerKwh;
                        break;
                }
            }

            int mi = month - 1;
            yr.MonthRenewableKwh[mi] += r;
            yr.MonthConsumedKwh[mi] += c;
            yr.MonthImportKwh[mi] += import;
            yr.MonthSurplusKwh[mi] += surplus;
            yr.MonthH2Kg[mi] += d.H2KgPerHour;

            if (collectSeries)
            {
                yr.SeriesRenewable![i] = Math.Round(r, 1);
                yr.SeriesConsumed![i] = Math.Round(c, 1);
                yr.SeriesH2![i] = Math.Round(d.H2KgPerHour, 2);
                yr.SeriesImport![i] = Math.Round(import, 1);
            }
        }

        FlushBank(ref bank, yr, grid);
        return yr;
    }

    /// <summary>Unused banked credit at month end is exported and paid at the export price.</summary>
    private static void FlushBank(ref double bank, YearResult yr, GridParameters grid)
    {
        if (bank > 0) yr.GridCostUsd -= bank * grid.ExportPriceUsdPerKwh;
        bank = 0.0;
    }

    private static double SafeDiv(double a, double b) => b > 0 ? a / b : 0.0;

    private sealed class YearResult
    {
        public double HydrogenKg, WaterLiters, StackKwh, CompressionKwh, RenewableKwh;
        public double ImportKwh, ExportKwh, CurtailedKwh, GridCostUsd, MatchedHourlyKwh;
        public double RunHours, StandbyHours;
        public readonly double[] MonthRenewableKwh = new double[12];
        public readonly double[] MonthConsumedKwh = new double[12];
        public readonly double[] MonthImportKwh = new double[12];
        public readonly double[] MonthSurplusKwh = new double[12];
        public readonly double[] MonthH2Kg = new double[12];
        public double[]? SeriesRenewable, SeriesConsumed, SeriesH2, SeriesImport;
    }
}
