namespace HydroPulse.Domain.Engineering;

/// <summary>Generic turbine parameters. Replace with the real turbine's power curve for a project.</summary>
public sealed record WindParameters
{
    /// <summary>Plant rating in kW. Zero disables wind.</summary>
    public double CapacityKw { get; init; }

    public double HubHeightM { get; init; } = 80.0;
    public double CutInMs { get; init; } = 3.0;
    public double RatedMs { get; init; } = 12.0;
    public double CutOutMs { get; init; } = 25.0;

    /// <summary>Wakes, availability, electrical and other losses.</summary>
    public double LossFactor { get; init; } = 0.85;

    /// <summary>Used when the 10 m / 50 m speeds cannot give a valid shear exponent.</summary>
    public double DefaultShearExponent { get; init; } = 0.14;
}

public static class WindModel
{
    public const double StandardAirDensityKgPerM3 = 1.225;

    /// <summary>Air density from site elevation (barometric formula) and temperature (ideal gas).</summary>
    public static double AirDensity(double elevationM, double tempC)
    {
        double pressurePa = 101325.0 * Math.Pow(1.0 - 2.25577e-5 * elevationM, 5.25588);
        return pressurePa / (287.05 * (tempC + 273.15));
    }

    /// <summary>
    /// Extrapolates the 50 m speed to hub height with a power law whose exponent is derived each hour
    /// from the 10 m and 50 m speeds: alpha = ln(v50/v10) / ln(5), clamped to [0.05, 0.40].
    /// </summary>
    public static double HubHeightSpeed(WindParameters p, double speed50m, double speed10m)
    {
        double alpha = (speed10m > 0.1 && speed50m > 0.1)
            ? Math.Log(speed50m / speed10m) / Math.Log(5.0)
            : p.DefaultShearExponent;
        alpha = Math.Clamp(alpha, 0.05, 0.40);
        return speed50m * Math.Pow(p.HubHeightM / 50.0, alpha);
    }

    /// <summary>
    /// Turbine output in kW. A cubic ramp between cut-in and rated speed; speed is density-normalised
    /// (v_eff = v * (rho / 1.225)^(1/3)) as in IEC 61400-12. Elevation from NASA POWER is a regional
    /// average, so treat the density correction as approximate.
    /// </summary>
    public static double PowerKw(WindParameters p, double speed50m, double speed10m, double elevationM, double tempC)
    {
        if (p.CapacityKw <= 0) return 0.0;

        double hub = HubHeightSpeed(p, speed50m, speed10m);
        double veff = hub * Math.Cbrt(AirDensity(elevationM, tempC) / StandardAirDensityKgPerM3);

        if (veff < p.CutInMs || veff >= p.CutOutMs) return 0.0;

        double fraction = veff >= p.RatedMs
            ? 1.0
            : (Math.Pow(veff, 3) - Math.Pow(p.CutInMs, 3)) / (Math.Pow(p.RatedMs, 3) - Math.Pow(p.CutInMs, 3));

        return p.CapacityKw * fraction * p.LossFactor;
    }
}
