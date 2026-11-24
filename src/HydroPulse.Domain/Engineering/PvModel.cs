namespace HydroPulse.Domain.Engineering;

/// <summary>Placeholder defaults: replace with datasheet / project values and cite them.</summary>
public sealed record PvParameters
{
    /// <summary>DC nameplate capacity in kWp. Zero disables solar.</summary>
    public double CapacityKwp { get; init; }

    /// <summary>Inverter, wiring, soiling and mismatch losses (everything except temperature).</summary>
    public double SystemEfficiencyFactor { get; init; } = 0.86;

    /// <summary>Power temperature coefficient per °C (typical crystalline silicon is about -0.35 %/°C).</summary>
    public double TempCoefficientPerC { get; init; } = -0.0035;

    /// <summary>Nominal operating cell temperature in °C.</summary>
    public double NoctC { get; init; } = 45.0;

    public double DegradationPerYear { get; init; } = 0.005;
}

/// <summary>
/// Simple PV model: P = kWp * (G/1000) * efficiency * (1 + gamma * (Tcell - 25)), with
/// Tcell = Tair + (NOCT - 20)/800 * G. Uses global HORIZONTAL irradiance as a proxy for plane-of-array
/// irradiance (no tilt/tracking model), so it slightly underestimates tilted or tracked arrays.
/// </summary>
public static class PvModel
{
    public static double PowerKw(PvParameters p, double irradianceWPerM2, double airTempC)
    {
        if (p.CapacityKwp <= 0 || irradianceWPerM2 <= 0) return 0.0;

        double cellTemp = airTempC + (p.NoctC - 20.0) / 800.0 * irradianceWPerM2;
        double tempFactor = 1.0 + p.TempCoefficientPerC * (cellTemp - 25.0);
        double power = p.CapacityKwp * (irradianceWPerM2 / 1000.0) * p.SystemEfficiencyFactor * tempFactor;

        // AC output cannot exceed the plant rating (DC/AC ratio of 1 assumed).
        return Math.Clamp(power, 0.0, p.CapacityKwp);
    }
}
