namespace HydroPulse.Domain.Engineering;

/// <summary>Result of dispatching the electrolyzer and its compressor for one moment.</summary>
public readonly record struct DispatchResult(
    double StackKw,
    double H2KgPerHour,
    double WaterLitersPerHour,
    double CompressionKw,
    ElectrolyzerMode Mode)
{
    /// <summary>Everything the plant draws: stack plus compression.</summary>
    public double TotalKw => StackKw + CompressionKw;
}

/// <summary>
/// The single place that decides how much hydrogen a plant makes from the power it may use.
/// Both the scenario runner (hourly, for the LCOH charts) and the live telemetry worker call this,
/// so the two can never disagree.
/// Compression is a parasitic load: total demand = stack power * (1 + e_c / SEC).
/// </summary>
public static class PlantDispatch
{
    private const int FixedPointIterations = 5;

    /// <summary>
    /// Finds the stack power such that stack plus compression demand fits <paramref name="availableKw"/>.
    /// SEC depends weakly on load, so a short fixed-point iteration converges.
    /// </summary>
    /// <param name="compressionKwhPerKg">Compression energy per kg of hydrogen (see <see cref="CompressionModel"/>).</param>
    public static DispatchResult Run(ElectrolyzerModel model, double availableKw, double operatingHours, double compressionKwhPerKg)
    {
        if (availableKw <= 0) return default;

        var p = model.Parameters;
        double rated = p.RatedPowerKw;
        double minKw = rated * p.MinimumLoadFraction;

        double ps = Math.Min(availableKw, rated);
        double target = ps;
        for (int i = 0; i < FixedPointIterations; i++)
        {
            double sec = model.SpecificEnergy(ps / rated, operatingHours);
            target = availableKw / (1.0 + compressionKwhPerKg / sec);
            ps = Math.Min(rated, target);
        }

        if (ps < minKw) return default;

        var state = model.Evaluate(ps, operatingHours);
        return new DispatchResult(
            state.PowerUsedKw,
            state.HydrogenKgPerHour,
            state.WaterLitersPerHour,
            state.HydrogenKgPerHour * compressionKwhPerKg,
            target > rated ? ElectrolyzerMode.Saturated : ElectrolyzerMode.PartLoad);
    }
}
