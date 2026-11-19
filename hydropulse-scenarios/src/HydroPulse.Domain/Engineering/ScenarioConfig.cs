namespace HydroPulse.Domain.Engineering;

public enum OperatingMode
{
    /// <summary>Islanded: the electrolyzer runs only on on-site renewable generation.</summary>
    OffGrid,

    /// <summary>Connected to the grid; <see cref="ScenarioConfig.Matching"/> decides how grid power may be used.</summary>
    GridConnected
}

/// <summary>
/// How grid-connected operation is matched to renewable generation.
/// None: import freely whenever the tariff is acceptable (hydrogen is not certifiable as renewable).
/// Monthly: grid import is limited to surplus renewable energy banked earlier in the same calendar month
///          (the EU RFNBO rule until 31 Dec 2029).
/// Hourly: consumption may not exceed the same hour's renewable generation (the EU rule from 1 Jan 2030),
///         so the grid never supplies energy for the electrolyzer.
/// Rules are under EU review; keep the policy a setting, not hard-coded behaviour.
/// </summary>
public enum MatchingPolicy
{
    None,
    Monthly,
    Hourly
}

/// <summary>All defaults are PLACEHOLDERS to be replaced with cited or customer values.</summary>
public sealed record FinancialParameters
{
    public double DiscountRate { get; init; } = 0.08;
    public int ProjectLifetimeYears { get; init; } = 20;
    public double ElectrolyzerCapexUsdPerKw { get; init; } = 1500.0;
    public double PvCapexUsdPerKwp { get; init; } = 800.0;
    public double WindCapexUsdPerKw { get; init; } = 1500.0;

    /// <summary>Compression, storage, water treatment, civil works as a fraction of electrolyzer + renewable capex.</summary>
    public double OtherCapexFraction { get; init; } = 0.10;

    public double OpexFractionOfCapexPerYear { get; init; } = 0.025;
    public double StackLifetimeHours { get; init; } = 80_000.0;

    /// <summary>Stack replacement cost as a fraction of electrolyzer capex.</summary>
    public double StackReplacementFraction { get; init; } = 0.30;

    public double WaterCostUsdPerM3 { get; init; } = 2.0;
}

public sealed record GridParameters
{
    public double ImportTariffUsdPerKwh { get; init; } = 0.12;

    /// <summary>With MatchingPolicy.None, grid power is imported only while the tariff is at or below this price.</summary>
    public double MaxImportPriceUsdPerKwh { get; init; } = 0.12;

    public double ExportPriceUsdPerKwh { get; init; } = 0.0;

    /// <summary>Network charge on banked renewable energy drawn back from the grid (Monthly policy).</summary>
    public double WheelingFeeUsdPerKwh { get; init; } = 0.02;

    public double MaxImportKw { get; init; } = 1_000_000_000.0;
}

public sealed record CompressionParameters
{
    public double InletBar { get; init; } = 30.0;
    public double OutletBar { get; init; } = 350.0;
    public int Stages { get; init; } = 3;
}

public sealed record ScenarioConfig
{
    public string Name { get; init; } = "Scenario";
    public OperatingMode Mode { get; init; } = OperatingMode.OffGrid;
    public MatchingPolicy Matching { get; init; } = MatchingPolicy.Hourly;
    public ElectrolyzerParameters Electrolyzer { get; init; } = new();
    public PvParameters Pv { get; init; } = new();
    public WindParameters Wind { get; init; } = new();
    public CompressionParameters Compression { get; init; } = new();
    public GridParameters Grid { get; init; } = new();
    public FinancialParameters Finance { get; init; } = new();
}
