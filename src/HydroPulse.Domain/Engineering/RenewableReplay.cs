namespace HydroPulse.Domain.Engineering;

/// <summary>
/// Turns an hourly weather series into a continuous renewable power signal using the same PV and wind
/// models as the scenario runner. Weather is interpolated linearly between hourly samples, so a plant
/// stepping through it every few seconds sees a smooth curve instead of hourly steps. The series wraps
/// around, so the year repeats for as long as the replay runs.
/// </summary>
public sealed class RenewableReplay
{
    private readonly IReadOnlyList<WeatherSample> _samples;
    private readonly PvParameters _pv;
    private readonly WindParameters _wind;
    private readonly double _elevationM;

    public RenewableReplay(WeatherSeries weather, PvParameters pv, WindParameters wind)
    {
        ArgumentNullException.ThrowIfNull(weather);
        ArgumentNullException.ThrowIfNull(pv);
        ArgumentNullException.ThrowIfNull(wind);
        if (weather.Samples.Count < 2)
            throw new ArgumentException("A replay needs at least two hourly weather samples.", nameof(weather));

        _samples = weather.Samples;
        _pv = pv;
        _wind = wind;
        _elevationM = weather.ElevationM;
    }

    /// <summary>Timestamp of the first weather sample.</summary>
    public DateTime FirstUtc => _samples[0].TimestampUtc;

    /// <summary>Number of hourly samples before the series wraps around.</summary>
    public int Hours => _samples.Count;

    /// <summary>Interpolated weather at <paramref name="utc"/>; times outside the series wrap around.</summary>
    public WeatherSample At(DateTime utc)
    {
        int n = _samples.Count;
        double h = (utc - FirstUtc).TotalHours % n;
        if (h < 0) h += n;
        if (h >= n) h -= n;                       // guards rounding right at the wrap point

        int i0 = (int)Math.Floor(h);
        double f = h - i0;
        var a = _samples[i0];
        var b = _samples[(i0 + 1) % n];

        double Lerp(double x, double y) => x + (y - x) * f;
        return new WeatherSample(
            utc,
            Lerp(a.IrradianceWhPerM2, b.IrradianceWhPerM2),
            Lerp(a.WindSpeed50mMs, b.WindSpeed50mMs),
            Lerp(a.WindSpeed10mMs, b.WindSpeed10mMs),
            Lerp(a.AirTemperatureC, b.AirTemperatureC));
    }

    /// <summary>Renewable plant output in kW at the given moment.</summary>
    public double PowerKw(DateTime utc)
    {
        var w = At(utc);
        double kw = PvModel.PowerKw(_pv, w.IrradianceWhPerM2, w.AirTemperatureC)
                  + WindModel.PowerKw(_wind, w.WindSpeed50mMs, w.WindSpeed10mMs, _elevationM, w.AirTemperatureC);
        return Math.Max(0.0, kw);
    }
}
