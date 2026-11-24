using System.Globalization;
using HydroPulse.Domain.Engineering;

namespace HydroPulse.Api.Workers;

/// <summary>
/// Settings under "Telemetry:Replay". With the defaults, the simulated plant follows the Nairobi solar
/// weather year, one weather hour per real minute. Set Enabled to false for the old random demo power.
/// </summary>
public sealed class ReplayOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>"Nairobi" or "Turkana".</summary>
    public string Site { get; set; } = "Nairobi";

    /// <summary>"Solar", "Wind" or "Hybrid" (half and half).</summary>
    public string Source { get; set; } = "Solar";

    /// <summary>Renewable capacity as a multiple of the electrolyzer rating.</summary>
    public double RenewableToElectrolyzerRatio { get; set; } = 2.0;

    /// <summary>Weather seconds that pass per real second. 60 means one weather hour per real minute.</summary>
    public double SpeedUp { get; set; } = 60.0;

    /// <summary>Optional day to start from, for example "2024-03-15". Only the month, day and hour are used.</summary>
    public string? StartDate { get; set; }

    /// <summary>Optional path to a weather CSV, to replay measured site data instead of the bundled NASA file.</summary>
    public string? WeatherFile { get; set; }
}

/// <summary>What the replay is doing right now; sent to the dashboard so it can show the simulated clock.</summary>
public sealed record ReplayInfo(string Site, string Source, double RenewableToElectrolyzerRatio, double SpeedUp, DateTime SimulatedUtc);

/// <summary>Holds the latest replay clock so the plant endpoint can report it.</summary>
public sealed class ReplayState
{
    private volatile ReplayInfo? _current;

    public ReplayInfo? Current
    {
        get => _current;
        set => _current = value;
    }
}

public sealed record ReplayPlan(RenewableReplay Replay, DateTime StartUtc, string Site, string Source, double Ratio, double SpeedUp);

public static class ReplaySetup
{
    /// <summary>Builds the replay from the settings, or returns null (and logs why) so the caller can fall back.</summary>
    public static ReplayPlan? TryCreate(ReplayOptions o, string contentRoot, ILogger logger)
    {
        if (!o.Enabled)
        {
            logger.LogInformation("Weather replay is disabled; using random demo power.");
            return null;
        }

        try
        {
            string site = o.Site.Trim();
            string file = site.Contains("turkana", StringComparison.OrdinalIgnoreCase) ? "turkana-2024.csv"
                        : site.Contains("nairobi", StringComparison.OrdinalIgnoreCase) ? "nairobi-2024.csv"
                        : throw new ArgumentException($"Unknown replay site '{o.Site}'. Use Nairobi or Turkana.");
            string siteLabel = file.StartsWith("turkana") ? "Lake Turkana" : "Nairobi";

            string path = Resolve(o.WeatherFile, file, contentRoot)
                ?? throw new FileNotFoundException($"Weather file '{o.WeatherFile ?? file}' was not found near {contentRoot}.");

            var weather = NasaPowerCsvParser.Parse(siteLabel, File.ReadLines(path));

            double ratio = Math.Max(0.1, o.RenewableToElectrolyzerRatio);
            double renewableKw = ratio * H2TelemetrySimulationWorker.RatedPowerKw;
            string source = o.Source.Trim().ToLowerInvariant();
            (PvParameters pv, WindParameters wind, string label) = source switch
            {
                "solar" => (new PvParameters { CapacityKwp = renewableKw }, new WindParameters(), "solar"),
                "wind" => (new PvParameters(), new WindParameters { CapacityKw = renewableKw }, "wind"),
                "hybrid" => (new PvParameters { CapacityKwp = renewableKw / 2 }, new WindParameters { CapacityKw = renewableKw / 2 }, "hybrid"),
                _ => throw new ArgumentException($"Unknown replay source '{o.Source}'. Use Solar, Wind or Hybrid.")
            };

            double speed = Math.Clamp(o.SpeedUp, 1.0, 3600.0);

            var first = weather.Samples[0].TimestampUtc;
            var start = first;
            if (!string.IsNullOrWhiteSpace(o.StartDate) &&
                DateTime.TryParse(o.StartDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            {
                int day = Math.Min(d.Day, DateTime.DaysInMonth(first.Year, d.Month));   // the weather year decides the leap day
                start = new DateTime(first.Year, d.Month, day, d.Hour, d.Minute, 0, DateTimeKind.Utc);
            }
            else if (!string.IsNullOrWhiteSpace(o.StartDate))
            {
                logger.LogWarning("Could not read StartDate '{StartDate}'; starting from the first weather hour.", o.StartDate);
            }

            logger.LogInformation(
                "Weather replay: {Site} {Source}, {Ratio}x the electrolyzer rating, {Speed}x speed, starting {Start:u}, from {Path}.",
                siteLabel, label, ratio, speed, start, path);

            return new ReplayPlan(new RenewableReplay(weather, pv, wind), start, siteLabel, label, ratio, speed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Weather replay is unavailable; using random demo power instead.");
            return null;
        }
    }

    /// <summary>Finds the weather file: an explicit path first, then data/weather in the content root or any parent folder.</summary>
    private static string? Resolve(string? explicitPath, string fileName, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string[] tries =
            {
                explicitPath,
                Path.Combine(contentRoot, explicitPath),
                Path.Combine(Directory.GetCurrentDirectory(), explicitPath)
            };
            return tries.FirstOrDefault(File.Exists);
        }

        var dir = new DirectoryInfo(contentRoot);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "data", "weather", fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
