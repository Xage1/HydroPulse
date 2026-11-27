using System.Globalization;
using System.Text.RegularExpressions;

namespace HydroPulse.Domain.Engineering;

/// <summary>One hourly weather record. Irradiance is the hourly mean (Wh/m² over one hour = W/m²).</summary>
public sealed record WeatherSample(
    DateTime TimestampUtc,
    double IrradianceWhPerM2,
    double WindSpeed50mMs,
    double WindSpeed10mMs,
    double AirTemperatureC);

public sealed record WeatherSeries(
    string Name,
    double ElevationM,
    IReadOnlyList<WeatherSample> Samples,
    int FilledMissingValues);

public static class WeatherScaling
{
    /// <summary>
    /// Multiplies the 50 m and 10 m wind speeds by a constant factor and leaves irradiance and temperature alone.
    /// Used to correct a reanalysis series that is known to be too low (or high) against a published site mean.
    /// The hourly pattern stays the reanalysis pattern, so this is a stop-gap, not a substitute for measured data.
    /// </summary>
    public static WeatherSeries WithWindScaled(this WeatherSeries series, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), "Wind scale factor must be a positive number.");

        var scaled = series.Samples
            .Select(x => x with { WindSpeed50mMs = x.WindSpeed50mMs * factor, WindSpeed10mMs = x.WindSpeed10mMs * factor })
            .ToList();
        return series with { Samples = scaled };
    }
}

/// <summary>
/// Parses NASA POWER hourly CSV exports (community RE) with columns
/// YEAR,MO,DY,HR,ALLSKY_SFC_SW_DWN,WS50M,WS10M,T2M. Missing values (-999) are carried forward from
/// the previous valid hour and counted in <see cref="WeatherSeries.FilledMissingValues"/>.
/// Customer-supplied measured data can be converted to the same layout.
/// </summary>
public static class NasaPowerCsvParser
{
    private static readonly Regex ElevationRegex =
        new(@"=\s*([0-9]+(?:\.[0-9]+)?)\s*meters", RegexOptions.Compiled);

    private static readonly string[] RequiredColumns =
        { "YEAR", "MO", "DY", "HR", "ALLSKY_SFC_SW_DWN", "WS50M", "WS10M", "T2M" };

    public static WeatherSeries Parse(string name, IEnumerable<string> lines)
    {
        double elevation = 0.0;
        int[]? col = null;
        var samples = new List<WeatherSample>(8800);
        int filled = 0;
        double lastGhi = 0, last50 = 0, last10 = 0, lastTemp = 0;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (col is null)
            {
                if (line.StartsWith("YEAR,MO,DY,HR", StringComparison.OrdinalIgnoreCase))
                {
                    var header = line.Split(',').Select(h => h.Trim()).ToArray();
                    var found = RequiredColumns
                        .Select(r => Array.FindIndex(header, h => h.Equals(r, StringComparison.OrdinalIgnoreCase)))
                        .ToArray();

                    var missing = RequiredColumns.Where((_, i) => found[i] < 0).ToList();
                    if (missing.Count > 0)
                        throw new FormatException("Weather file is missing column(s): " + string.Join(", ", missing));

                    col = found;
                }
                else if (line.Contains("Elevation", StringComparison.OrdinalIgnoreCase))
                {
                    var m = ElevationRegex.Match(line);
                    if (m.Success)
                        elevation = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                continue;
            }

            if (line.Length == 0) continue;
            var p = line.Split(',');

            var ts = new DateTime(
                int.Parse(p[col[0]], CultureInfo.InvariantCulture),
                int.Parse(p[col[1]], CultureInfo.InvariantCulture),
                int.Parse(p[col[2]], CultureInfo.InvariantCulture),
                int.Parse(p[col[3]], CultureInfo.InvariantCulture),
                0, 0, DateTimeKind.Utc);

            double ghi = Read(p, col[4], ref lastGhi, ref filled);
            double w50 = Read(p, col[5], ref last50, ref filled);
            double w10 = Read(p, col[6], ref last10, ref filled);
            double t = Read(p, col[7], ref lastTemp, ref filled);

            samples.Add(new WeatherSample(ts, Math.Max(0.0, ghi), Math.Max(0.0, w50), Math.Max(0.0, w10), t));
        }

        if (col is null) throw new FormatException("NASA POWER header row (YEAR,MO,DY,HR,...) not found.");
        if (samples.Count == 0) throw new FormatException("Weather file contains no data rows.");

        return new WeatherSeries(name, elevation, samples, filled);
    }

    private static double Read(string[] parts, int index, ref double last, ref int filled)
    {
        double v = double.Parse(parts[index], CultureInfo.InvariantCulture);
        if (v <= -998.5)
        {
            filled++;
            return last;
        }
        last = v;
        return v;
    }
}
