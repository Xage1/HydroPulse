namespace HydroPulse.Domain.Engineering;

/// <summary>
/// Physical parameters of an electrolyzer system. All defaults are PLACEHOLDERS to be replaced
/// with values from a cited source or vendor datasheet for the thesis.
/// </summary>
public sealed record ElectrolyzerParameters
{
    public double RatedPowerKw { get; init; } = 1000.0;

    /// <summary>Specific energy consumption at rated load, new stack (kWh per kg H2).</summary>
    public double SpecificEnergyAtRatedKwhPerKg { get; init; } = 50.0;

    /// <summary>
    /// Fractional efficiency gain towards zero load. SEC(l) = SEC_rated * (1 - gain * (1 - l)).
    /// 0.08 means SEC is 8% lower as load approaches 0. Replace with a measured polarization curve.
    /// </summary>
    public double PartLoadEfficiencyGain { get; init; } = 0.08;

    /// <summary>Minimum stable load as a fraction of rated power (turndown).</summary>
    public double MinimumLoadFraction { get; init; } = 0.10;

    /// <summary>Fractional SEC increase per year of operation (linear in operating hours).</summary>
    public double DegradationPerYear { get; init; } = 0.01;

    /// <summary>Water per kg H2. Stoichiometric minimum is ~8.94 L; purification losses add more.</summary>
    public double WaterLitersPerKg { get; init; } = 9.5;
}

public enum ElectrolyzerMode
{
    Standby,
    PartLoad,
    /// <summary>Incoming power exceeds rated power; the excess is curtailed.</summary>
    Saturated
}

public sealed record ElectrolyzerState(
    ElectrolyzerMode Mode,
    double PowerUsedKw,
    double CurtailedKw,
    double SpecificEnergyKwhPerKg,
    double HydrogenKgPerHour,
    double WaterLitersPerHour);

/// <summary>Pure, I/O-free steady-state electrolyzer model.</summary>
public sealed class ElectrolyzerModel
{
    private const double HoursPerYear = 8760.0;

    public ElectrolyzerParameters Parameters { get; }

    public ElectrolyzerModel(ElectrolyzerParameters parameters)
    {
        if (parameters.RatedPowerKw <= 0) throw new ArgumentOutOfRangeException(nameof(parameters), "Rated power must be positive.");
        if (parameters.SpecificEnergyAtRatedKwhPerKg <= 0) throw new ArgumentOutOfRangeException(nameof(parameters), "SEC must be positive.");
        if (parameters.MinimumLoadFraction is < 0 or >= 1) throw new ArgumentOutOfRangeException(nameof(parameters), "Minimum load must be in [0, 1).");
        Parameters = parameters;
    }

    /// <summary>Specific energy consumption (kWh/kg) at a load fraction after the given operating hours.</summary>
    public double SpecificEnergy(double loadFraction, double operatingHours)
    {
        double l = Math.Clamp(loadFraction, 0.0, 1.0);
        double partLoad = 1.0 - Parameters.PartLoadEfficiencyGain * (1.0 - l);
        double degradation = 1.0 + Parameters.DegradationPerYear * (operatingHours / HoursPerYear);
        return Parameters.SpecificEnergyAtRatedKwhPerKg * partLoad * degradation;
    }

    public ElectrolyzerState Evaluate(double availablePowerKw, double operatingHours = 0.0)
    {
        double rated = Parameters.RatedPowerKw;
        double minPower = rated * Parameters.MinimumLoadFraction;

        if (availablePowerKw < minPower)
            return new ElectrolyzerState(ElectrolyzerMode.Standby, 0, 0, 0, 0, 0);

        double used = Math.Min(availablePowerKw, rated);
        double sec = SpecificEnergy(used / rated, operatingHours);
        double h2 = used / sec;

        return new ElectrolyzerState(
            Mode: availablePowerKw > rated ? ElectrolyzerMode.Saturated : ElectrolyzerMode.PartLoad,
            PowerUsedKw: used,
            CurtailedKw: Math.Max(0, availablePowerKw - rated),
            SpecificEnergyKwhPerKg: sec,
            HydrogenKgPerHour: h2,
            WaterLitersPerHour: h2 * Parameters.WaterLitersPerKg);
    }
}
