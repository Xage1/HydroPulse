using System.Text.Json;
using System.Text.Json.Serialization;
using HydroPulse.Domain.Engineering;

// Usage: dotnet run --project tools\HydroPulse.ScenarioRunner [repoRoot]
// Reads data/weather/{nairobi,turkana}-2024.csv and writes out/results.json.

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
string weatherDir = Path.Combine(root, "data", "weather");
string outDir = Path.Combine(root, "out");
Directory.CreateDirectory(outDir);

WeatherSeries Load(string name, string file)
{
    string path = Path.Combine(weatherDir, file);
    if (!File.Exists(path))
        throw new FileNotFoundException($"Weather file not found: {path}. Run from the repo root or pass it as the first argument.");
    var series = NasaPowerCsvParser.Parse(name, File.ReadLines(path));
    Console.WriteLine($"Loaded {name}: {series.Samples.Count} hours, elevation {series.ElevationM:F0} m, {series.FilledMissingValues} missing values filled");
    return series;
}

var nairobi = Load("Nairobi", "nairobi-2024.csv");
var turkana = Load("Lake Turkana", "turkana-2024.csv");

// Demo plant: 10 MW electrolyzer. Renewable capacity is sized by the sweep below.
var electrolyzer = new ElectrolyzerParameters { RatedPowerKw = 10_000 };

var sites = new (string Label, WeatherSeries Weather, PvParameters Pv, WindParameters Wind)[]
{
    ("Nairobi solar",       nairobi, new PvParameters { CapacityKwp = 10_000 }, new WindParameters()),
    ("Lake Turkana wind",   turkana, new PvParameters(), new WindParameters { CapacityKw = 10_000 }),
    ("Lake Turkana hybrid", turkana, new PvParameters { CapacityKwp = 5_000 }, new WindParameters { CapacityKw = 5_000 }),
};

var modes = new (string Label, OperatingMode Mode, MatchingPolicy Matching)[]
{
    ("Off-grid (onsite)",                  OperatingMode.OffGrid,       MatchingPolicy.Hourly),
    ("Grid-connected, no matching",        OperatingMode.GridConnected, MatchingPolicy.None),
    ("Grid-connected, monthly matching",   OperatingMode.GridConnected, MatchingPolicy.Monthly),
    ("Grid-connected, hourly matching",    OperatingMode.GridConnected, MatchingPolicy.Hourly),
};

var ratios = Enumerable.Range(2, 15).Select(i => i * 0.25).ToArray(); // 0.5 .. 4.0
var scenarios = new List<object>();

Console.WriteLine();
Console.WriteLine($"{"Scenario",-58} {"Ratio",5} {"LCOH $/kg",10} {"El. CF",7} {"H2 t/yr",8} {"Sim.RE",7} {"Mon.RE",7}");

foreach (var site in sites)
{
    foreach (var mode in modes)
    {
        string label = $"{site.Label} | {mode.Label}";
        var baseCfg = new ScenarioConfig
        {
            Name = label,
            Mode = mode.Mode,
            Matching = mode.Matching,
            Electrolyzer = electrolyzer,
            Pv = site.Pv,
            Wind = site.Wind
        };

        var sweep = ScenarioAnalysis.SweepRatio(baseCfg, site.Weather, ratios);
        double best = ScenarioAnalysis.BestRatio(sweep) ?? 1.5;

        var cfg = ScenarioAnalysis.ScaleRenewables(baseCfg, best);
        var result = ScenarioRunner.Run(cfg, site.Weather);
        var sensitivity = ScenarioAnalysis.Sensitivity(cfg, site.Weather);

        string id = label.ToLowerInvariant().Replace(" | ", "-").Replace(", ", "-").Replace(' ', '-');
        scenarios.Add(new
        {
            id,
            site = site.Label,
            mode = mode.Label,
            bestRatio = best,
            result,
            sweep,
            sensitivity
        });

        var y = result.Year1;
        Console.WriteLine(
            $"{label,-58} {best,5:F2} {(result.Finance.LcohUsdPerKg?.ToString("F2") ?? "n/a"),10} " +
            $"{y.ElectrolyzerCapacityFactor,7:P0} {y.HydrogenTonnes,8:F0} {y.SimultaneousRenewableShare,7:P0} {y.MonthlyMatchedShare,7:P0}");
    }
}

var output = new
{
    generatedUtc = DateTime.UtcNow,
    weatherSource = "NASA POWER hourly (CERES SYN1deg irradiance, MERRA-2 wind/temperature), year 2024, UTC",
    note = "All technical and financial defaults are placeholders; replace with cited or customer values.",
    assumptions = new ScenarioConfig { Electrolyzer = electrolyzer },
    scenarios
};

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
};

string json = JsonSerializer.Serialize(output, options);
string outPath = Path.Combine(outDir, "results.json");
File.WriteAllText(outPath, json);
Console.WriteLine();
Console.WriteLine($"Wrote {outPath} ({json.Length / 1024} KB)");
