namespace HydroPulse.Domain.Engineering;

public sealed record PlantSummary(
    double ElectrolyzerMw,
    double PvMwp,
    double WindMw,
    double RenewableToElectrolyzerRatio);

/// <summary>First-year physical results. Energy in MWh, hydrogen in tonnes.</summary>
public sealed record AnnualSummary(
    double RenewableGenerationMwh,
    double ElectrolyzerEnergyMwh,
    double CompressionEnergyMwh,
    double GridImportMwh,
    double GridExportMwh,
    double CurtailedMwh,
    double HydrogenTonnes,
    double WaterM3,
    double ElectrolyzerCapacityFactor,
    double FullLoadHours,
    double RenewableCapacityFactor,
    double SimultaneousRenewableShare,
    double MonthlyMatchedShare,
    double SpecificConsumptionKwhPerKg,
    double StandbyHours);

/// <summary>Contribution of each cost item to LCOH in USD/kg. The items sum to the LCOH.</summary>
public sealed record LcohBreakdown(
    double ElectrolyzerCapex,
    double RenewableCapex,
    double OtherCapex,
    double FixedOpex,
    double Water,
    double GridElectricity,
    double StackReplacement);

public sealed record FinancialSummary(
    double? LcohUsdPerKg,
    LcohBreakdown? Breakdown,
    double TotalCapexUsd,
    double LifetimeHydrogenTonnes,
    int StackReplacements);

public sealed record MonthlySummary(
    int Month,
    double RenewableMwh,
    double ElectrolyzerMwh,
    double GridImportMwh,
    double SurplusMwh,
    double HydrogenTonnes);

/// <summary>Hourly series for year one (kW, kg/h), rounded to one decimal to keep exports small.</summary>
public sealed record HourlySeries(
    double[] RenewableKw,
    double[] ElectrolyzerKw,
    double[] HydrogenKgPerHour,
    double[] GridImportKw);

public sealed record ScenarioResult(
    string Name,
    string Site,
    OperatingMode Mode,
    MatchingPolicy? Matching,
    PlantSummary Plant,
    AnnualSummary Year1,
    FinancialSummary Finance,
    IReadOnlyList<MonthlySummary> Monthly,
    HourlySeries Hourly);
