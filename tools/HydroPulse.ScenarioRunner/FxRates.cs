using System.Text.Json;

/// <summary>
/// US-dollar exchange rates for the dashboard's currency picker. The model itself always runs in USD;
/// these rates are only used to display values in the buyer's currency.
/// Source: ExchangeRate-API open access endpoint (no key, refreshed daily, attribution required).
/// </summary>
public sealed record FxSnapshot(
    string Base,
    Dictionary<string, double> Rates,
    string AsOf,
    string Status,       // "live" (fetched this run) or "cached" (last saved copy)
    string Source,
    string SourceUrl);

public static class FxRates
{
    const string Endpoint = "https://open.er-api.com/v6/latest/USD";
    const string Credit = "ExchangeRate-API (open access)";
    const string CreditUrl = "https://www.exchangerate-api.com";

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    /// <summary>Fetches fresh rates; on failure falls back to the last saved copy; returns null if neither exists.</summary>
    public static async Task<FxSnapshot?> LoadAsync(string cachePath)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            string text = await http.GetStringAsync(Endpoint);
            using var doc = JsonDocument.Parse(text);
            var el = doc.RootElement;
            if (el.GetProperty("result").GetString() != "success")
                throw new InvalidOperationException("rate provider returned a non-success result");

            var rates = new Dictionary<string, double>();
            foreach (var p in el.GetProperty("rates").EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.GetDouble() > 0)
                    rates[p.Name] = p.Value.GetDouble();

            string asOf = el.TryGetProperty("time_last_update_utc", out var t) ? (t.GetString() ?? "") : "";
            if (DateTimeOffset.TryParse(asOf, out var when)) asOf = when.UtcDateTime.ToString("yyyy-MM-dd");

            var snap = new FxSnapshot("USD", rates, asOf, "live", Credit, CreditUrl);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(snap, Json));
            Console.WriteLine($"Exchange rates: {rates.Count} currencies, updated {asOf} (live).");
            return snap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exchange rates: could not fetch live rates ({ex.GetType().Name}: {ex.Message}).");
        }

        try
        {
            if (File.Exists(cachePath))
            {
                var cached = JsonSerializer.Deserialize<FxSnapshot>(File.ReadAllText(cachePath), Json);
                if (cached is { Rates.Count: > 0 })
                {
                    Console.WriteLine($"Exchange rates: using saved copy from {cached.AsOf}.");
                    return cached with { Status = "cached" };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exchange rates: saved copy unreadable ({ex.Message}).");
        }

        Console.WriteLine("Exchange rates: none available. The dashboard will offer USD and a custom rate only.");
        return null;
    }
}
