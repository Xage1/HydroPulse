using System.Globalization;

namespace HydroPulse.Domain.Engineering;

/// <summary>
/// Parses customer-supplied MEASURED hourly weather (met mast, SCADA or a resource assessment) into the same
/// <see cref="WeatherSeries"/> the NASA POWER parser produces, so every model and the dashboard work unchanged.
///
/// CSV layout (header names are case-insensitive, extra columns are ignored, lines starting with # are skipped):
///   timestamp_utc   required  ISO 8601, e.g. 2024-01-01T00:00:00Z. Strictly hourly, ascending, no gaps.
///   wind_speed_ms   required  hourly mean wind speed at the measurement height
///   ghi_w_m2        optional  global horizontal irradiance, hourly mean (0 when the column is absent)
///   temp_c          optional  air temperature (25 when the column is absent)
/// Blank, NaN or -999 cells are filled from the previous hour and counted.
///
/// The wind model needs speeds at 50 m and 10 m. The measured speed is converted with a power-law shear
/// exponent: v(z) = v_meas * (z / h_meas)^alpha. The model then recovers alpha from the two speeds and
/// extrapolates to hub height, so the hub-height speed equals v_meas * (hub / h_meas)^alpha exactly.
/// </summary>
public static class MeasuredWeatherCsvParser
{
    public static WeatherSeries Parse(
        string name,
        IEnumerable<string> lines,
        double elevationM,
        double measuredHeightM = 80.0,
        double shearExponent = 0.14)
    {
        if (measuredHeightM <= 0) throw new ArgumentOutOfRangeException(nameof(measuredHeightM));

        double alpha = Math.Clamp(shearExponent, 0.05, 0.40);
        double to50 = Math.Pow(50.0 / measuredHeightM, alpha);
        double to10 = Math.Pow(10.0 / measuredHeightM, alpha);

        int iTime = -1, iWind = -1, iGhi = -1, iTemp = -1;
        bool haveHeader = false;
        var samples = new List<WeatherSample>(8800);
        int filled = 0;
        double lastWind = 0.0, lastGhi = 0.0, lastTemp = 25.0;
        int lineNo = 0;

        foreach (var raw in lines)
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var p = line.Split(',');

            if (!haveHeader)
            {
                var header = p.Select(h => h.Trim()).ToArray();
                iTime = IndexOf(header, "timestamp_utc");
                iWind = IndexOf(header, "wind_speed_ms");
                iGhi = IndexOf(header, "ghi_w_m2");
                iTemp = IndexOf(header, "temp_c");
                if (iTime < 0 || iWind < 0)
                    throw new FormatException("Measured weather file needs the columns timestamp_utc and wind_speed_ms.");
                haveHeader = true;
                continue;
            }

            if (!DateTime.TryParse(Cell(p, iTime), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                throw new FormatException($"Line {lineNo}: cannot read timestamp '{Cell(p, iTime)}'.");

            if (samples.Count > 0 && ts != samples[^1].TimestampUtc.AddHours(1))
                throw new FormatException(
                    $"Line {lineNo}: timestamps must be hourly and ascending without gaps (expected {samples[^1].TimestampUtc.AddHours(1):u}, found {ts:u}).");

            double wind = Math.Max(0.0, Read(p, iWind, ref lastWind, ref filled));
            double ghi = iGhi >= 0 ? Math.Max(0.0, Read(p, iGhi, ref lastGhi, ref filled)) : 0.0;
            double temp = iTemp >= 0 ? Read(p, iTemp, ref lastTemp, ref filled) : 25.0;

            samples.Add(new WeatherSample(
                DateTime.SpecifyKind(ts, DateTimeKind.Utc), ghi, wind * to50, wind * to10, temp));
        }

        if (!haveHeader) throw new FormatException("Measured weather file has no header row.");
        if (samples.Count < 24) throw new FormatException("Measured weather file needs at least 24 hourly rows.");

        return new WeatherSeries(name, elevationM, samples, filled);
    }

    private static int IndexOf(string[] header, string column) =>
        Array.FindIndex(header, h => h.Equals(column, StringComparison.OrdinalIgnoreCase));

    private static string Cell(string[] parts, int index) => index < parts.Length ? parts[index].Trim() : "";

    private static double Read(string[] parts, int index, ref double last, ref int filled)
    {
        string cell = Cell(parts, index);
        if (double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            && !double.IsNaN(v) && v > -998.5)
        {
            last = v;
            return v;
        }
        filled++;
        return last;
    }
}
