using System.Text.Json;
using System.Text.Json.Serialization;
using HydroPulse.Domain.Engineering;

// Usage: dotnet run --project tools\HydroPulse.ScenarioRunner -c Release [repoRoot]
// Reads data/weather/{nairobi,turkana}-2024.csv and writes out/results.json and out/dashboard.html,
// and copies the dashboard into the API's wwwroot so the API serves it (with the live view) at /.

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

var siteList = new List<(string Label, WeatherSeries Weather, PvParameters Pv, WindParameters Wind)>
{
    ("Nairobi solar",       nairobi, new PvParameters { CapacityKwp = 10_000 }, new WindParameters()),
    ("Lake Turkana wind",   turkana, new PvParameters(), new WindParameters { CapacityKw = 10_000 }),
    ("Lake Turkana hybrid", turkana, new PvParameters { CapacityKwp = 5_000 }, new WindParameters { CapacityKw = 5_000 }),
};

// Optional buyer sites from data/sites.json (measured or NASA weather).
var siteFile = SiteFile.Load(root);
var customNotes = new List<string>();
var measuredLabels = new HashSet<string>();
var customDataNotes = new Dictionary<string, string>();
if (siteFile is not null)
{
    if (!siteFile.IncludeBuiltIn) siteList.Clear();
    foreach (var entry in siteFile.Sites)
    {
        var w = SiteFile.LoadWeather(weatherDir, entry);
        Console.WriteLine($"Loaded {entry.Label} ({entry.Format}): {w.Samples.Count} hours, {w.FilledMissingValues} missing values filled");
        siteList.Add((entry.Label, w, new PvParameters { CapacityKwp = entry.PvKwp }, new WindParameters { CapacityKw = entry.WindKw }));
        if (entry.Format.Equals("measured", StringComparison.OrdinalIgnoreCase)) measuredLabels.Add(entry.Label);
        if (!string.IsNullOrWhiteSpace(entry.DataNote)) customDataNotes[entry.Label] = entry.DataNote!;
        customNotes.Add($"{entry.Label}: {(entry.Format.Equals("measured", StringComparison.OrdinalIgnoreCase) ? "customer measured data" : "NASA POWER")}"
            + (string.IsNullOrWhiteSpace(entry.DataNote) ? "" : $" ({entry.DataNote})"));
    }
}
var sites = siteList.ToArray();

// Per-site weather data quality, shown on the dashboard next to any scenario that uses wind.
const string TurkanaWindNote =
    "A 310 MW wind plant at Lake Turkana expects about 1,250 GWh a year, roughly a 46% capacity factor by our arithmetic, " +
    "far above what this data gives, so the wind speeds here are probably too low.";
var siteData = sites.ToDictionary(
    st => st.Label,
    st => new
    {
        kind = measuredLabels.Contains(st.Label) ? "measured" : "reanalysis",
        meanWind50mMs = Math.Round(st.Weather.Samples.Average(x => x.WindSpeed50mMs), 2),
        note = customDataNotes.TryGetValue(st.Label, out var dn) ? dn
             : (!measuredLabels.Contains(st.Label) && st.Label.Contains("Turkana", StringComparison.OrdinalIgnoreCase) ? TurkanaWindNote : null)
    });

var modes = new (string Key, string Label, OperatingMode Mode, MatchingPolicy Matching)[]
{
    ("offgrid", "Off-grid (onsite)",                OperatingMode.OffGrid,       MatchingPolicy.Hourly),
    ("none",    "Grid-connected, no matching",      OperatingMode.GridConnected, MatchingPolicy.None),
    ("monthly", "Grid-connected, monthly matching", OperatingMode.GridConnected, MatchingPolicy.Monthly),
    ("hourly",  "Grid-connected, hourly matching",  OperatingMode.GridConnected, MatchingPolicy.Hourly),
};

// Renewable capacity as a multiple of electrolyzer rating: 0.5-4.0 in steps of 0.25, then 4.5-8.0 in steps of 0.5.
var ratios = Enumerable.Range(2, 15).Select(i => i * 0.25)
    .Concat(Enumerable.Range(9, 8).Select(i => i * 0.5))
    .ToArray();
var scenarios = new List<object>();

// Solar/wind split search for hybrid sites: PV share 0-100% in 10% steps, capacity ratio 0.5-8x in steps of 0.5.
var pvShares = Enumerable.Range(0, 11).Select(i => i / 10.0).ToArray();
var mixRatios = Enumerable.Range(1, 16).Select(i => i * 0.5).ToArray();

Console.WriteLine();
Console.WriteLine($"{"Scenario",-58} {"Ratio",5} {"LCOH $/kg",10} {"El. CF",7} {"RE CF",6} {"H2 t/yr",8} {"Sim.RE",7} {"Mon.RE",7}");

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

        // Sites with both solar and wind: find the best solar/wind split first, then size around it.
        IReadOnlyList<MixPoint>? mix = null;
        double? bestPvShare = null;
        if (site.Pv.CapacityKwp > 0 && site.Wind.CapacityKw > 0)
        {
            double totalKw = site.Pv.CapacityKwp + site.Wind.CapacityKw;
            mix = ScenarioAnalysis.OptimizeMix(baseCfg, site.Weather, pvShares, mixRatios);
            bestPvShare = ScenarioAnalysis.BestPvShare(mix) ?? 0.5;
            baseCfg = baseCfg with
            {
                Pv = baseCfg.Pv with { CapacityKwp = bestPvShare.Value * totalKw },
                Wind = baseCfg.Wind with { CapacityKw = (1.0 - bestPvShare.Value) * totalKw }
            };
        }

        var sweep = ScenarioAnalysis.SweepRatio(baseCfg, site.Weather, ratios);
        double best = ScenarioAnalysis.BestRatio(sweep) ?? 1.5;

        var cfg = ScenarioAnalysis.ScaleRenewables(baseCfg, best);
        var result = ScenarioRunner.Run(cfg, site.Weather);
        var sensitivity = ScenarioAnalysis.Sensitivity(cfg, site.Weather);

        scenarios.Add(new
        {
            id = $"{site.Label}|{mode.Key}",
            site = site.Label,
            modeKey = mode.Key,
            mode = mode.Label,
            bestRatio = best,
            bestPvShare,
            mix,
            result,
            sweep,
            sensitivity
        });

        var y = result.Year1;
        string edge = best >= ratios[^1] ? " <- at sweep limit" : "";
        Console.WriteLine(
            $"{label,-58} {best,5:F2} {(result.Finance.LcohUsdPerKg?.ToString("F2") ?? "n/a"),10} " +
            $"{y.ElectrolyzerCapacityFactor,7:P0} {y.RenewableCapacityFactor,6:P0} {y.HydrogenTonnes,8:F0} " +
            $"{y.SimultaneousRenewableShare,7:P0} {y.MonthlyMatchedShare,7:P0}{edge}" +
            (bestPvShare is { } share ? $"  [PV {share:P0} / wind {1 - share:P0}]" : ""));
    }
}

var fx = await FxRates.LoadAsync(Path.Combine(root, "data", "fx-cache.json"));

var output = new
{
    generatedUtc = DateTime.UtcNow,
    weatherSource = "NASA POWER hourly (CERES SYN1deg irradiance, MERRA-2 wind/temperature), year 2024, UTC"
        + (customNotes.Count > 0 ? ". Buyer sites: " + string.Join("; ", customNotes) : ""),
    note = "All technical and financial defaults are placeholders; replace with cited or customer values.",
    assumptions = new ScenarioConfig { Electrolyzer = electrolyzer },
    fx,
    siteData,
    scenarios
};

var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
};

string json = JsonSerializer.Serialize(output, options);
string jsonPath = Path.Combine(outDir, "results.json");
File.WriteAllText(jsonPath, json);
Console.WriteLine();
Console.WriteLine($"Wrote {jsonPath} ({json.Length / 1024} KB)");

// Embed the results into the standalone dashboard page.
string templatePath = Path.Combine(AppContext.BaseDirectory, "dashboard.template.html");
if (File.Exists(templatePath))
{
    string html = File.ReadAllText(templatePath).Replace("__RESULTS_JSON__", json.Replace("</", "<\\/"));
    string htmlPath = Path.Combine(outDir, "dashboard.html");
    File.WriteAllText(htmlPath, html);
    Console.WriteLine($"Wrote {htmlPath}  <- open this in a browser");
    await LiveAssets.PublishAsync(root, html);
}
else
{
    Console.WriteLine("dashboard.template.html not found next to the runner; skipped dashboard.html.");
}
