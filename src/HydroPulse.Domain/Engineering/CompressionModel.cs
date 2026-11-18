namespace HydroPulse.Domain.Engineering;

/// <summary>
/// Ideal-gas multistage compression with equal pressure ratio per stage and intercooling
/// back to the inlet temperature:
///   w = n * (g/(g-1)) * (R/M) * T1 * ((p2/p1)^((g-1)/(g*n)) - 1) / eta
/// Real hydrogen deviates from ideal-gas behaviour at high pressure (compressibility), so treat
/// results above ~350 bar as a lower bound and state this limitation in the thesis.
/// </summary>
public static class CompressionModel
{
    private const double UniversalGasConstant = 8.314462618;   // J/(mol K)
    private const double H2MolarMassKgPerMol = 2.01588e-3;     // kg/mol
    private const double JoulesPerKwh = 3.6e6;

    public static double EnergyKwhPerKg(
        double inletBar,
        double outletBar,
        int stages,
        double inletTemperatureK = 300.0,
        double heatCapacityRatio = 1.41,
        double isentropicEfficiency = 0.75)
    {
        if (inletBar <= 0) throw new ArgumentOutOfRangeException(nameof(inletBar));
        if (outletBar < inletBar) throw new ArgumentOutOfRangeException(nameof(outletBar), "Outlet pressure must be >= inlet pressure.");
        if (stages < 1) throw new ArgumentOutOfRangeException(nameof(stages));
        if (isentropicEfficiency is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(isentropicEfficiency));
        if (heatCapacityRatio <= 1) throw new ArgumentOutOfRangeException(nameof(heatCapacityRatio));

        if (outletBar == inletBar) return 0.0;

        double specificGasConstant = UniversalGasConstant / H2MolarMassKgPerMol; // J/(kg K)
        double stageRatio = Math.Pow(outletBar / inletBar, 1.0 / stages);
        double exponent = (heatCapacityRatio - 1.0) / heatCapacityRatio;

        double joulesPerKg = stages
            * (heatCapacityRatio / (heatCapacityRatio - 1.0))
            * specificGasConstant * inletTemperatureK
            * (Math.Pow(stageRatio, exponent) - 1.0)
            / isentropicEfficiency;

        return joulesPerKg / JoulesPerKwh;
    }
}
