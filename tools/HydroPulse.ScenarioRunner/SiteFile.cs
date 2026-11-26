using System.Text.Json;
using HydroPulse.Domain.Engineering;

/// <summary>
/// Optional buyer sites. If data/sites.json exists, each entry adds a site to the scenarios (the built-in NASA
/// sites stay unless "includeBuiltIn" is false). Weather files are resolved relative to data/weather.
/// See data/sites.example.json. "format" is "nasa" (default) or "measured" (see MeasuredWeatherCsvParser).
/// </summary>
public sealed record SiteEntry
{
    public string Label { get; init; } = "";
    public string WeatherFile { get; init; } = "";
    public string Format { get; init; } = "nasa";
    public double ElevationM { get; init; }
    public double MeasuredHeightM { get; init; } = 80.0;
    public double ShearExponent { get; init; } = 0.14;
    public double PvKwp { get; init; }
    public double WindKw { get; init; }
    public string? DataNote { get; init; }
}

public sealed record SiteFileModel
{
    public bool IncludeBuiltIn { get; init; } = true;
    public List<SiteEntry> Sites { get; init; } = new();
}

public static class SiteFile
{
    public static SiteFileModel? Load(string root)
    {
        string path = Path.Combine(root, "data", "sites.json");
        if (!File.Exists(path)) return null;

        var model = JsonSerializer.Deserialize<SiteFileModel>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip })
            ?? throw new InvalidDataException("data/sites.json is empty.");

        foreach (var s in model.Sites)
        {
            if (string.IsNullOrWhiteSpace(s.Label)) throw new InvalidDataException("Every site in sites.json needs a label.");
            if (string.IsNullOrWhiteSpace(s.WeatherFile)) throw new InvalidDataException($"Site '{s.Label}' needs a weatherFile.");
            if (s.PvKwp <= 0 && s.WindKw <= 0) throw new InvalidDataException($"Site '{s.Label}' needs pvKwp and/or windKw above zero.");
            if (!s.Format.Equals("nasa", StringComparison.OrdinalIgnoreCase) && !s.Format.Equals("measured", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Site '{s.Label}': format must be \"nasa\" or \"measured\".");
        }
        return model;
    }

    public static WeatherSeries LoadWeather(string weatherDir, SiteEntry s)
    {
        string path = Path.IsPathRooted(s.WeatherFile) ? s.WeatherFile : Path.Combine(weatherDir, s.WeatherFile);
        if (!File.Exists(path)) throw new FileNotFoundException($"Weather file for '{s.Label}' not found: {path}");

        return s.Format.Equals("measured", StringComparison.OrdinalIgnoreCase)
            ? MeasuredWeatherCsvParser.Parse(s.Label, File.ReadLines(path), s.ElevationM, s.MeasuredHeightM, s.ShearExponent)
            : NasaPowerCsvParser.Parse(s.Label, File.ReadLines(path));
    }
}
