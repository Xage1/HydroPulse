/// <summary>
/// Publishes the dashboard to the API's wwwroot so the live view can reach the SignalR hub from the same origin,
/// and keeps a local copy of the SignalR browser client so the live view also works on a network without internet.
/// </summary>
public static class LiveAssets
{
    const string SignalRUrl = "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js";

    public static async Task PublishAsync(string root, string html)
    {
        string apiDir = Path.Combine(root, "src", "HydroPulse.Api");
        if (!Directory.Exists(apiDir))
        {
            Console.WriteLine("API project not found under the repo root; skipped wwwroot copy.");
            return;
        }

        string wwwroot = Path.Combine(apiDir, "wwwroot");
        Directory.CreateDirectory(Path.Combine(wwwroot, "lib"));
        File.WriteAllText(Path.Combine(wwwroot, "index.html"), html);
        Console.WriteLine($"Wrote {Path.Combine(wwwroot, "index.html")}  <- served by the API at /");

        string lib = Path.Combine(wwwroot, "lib", "signalr.min.js");
        if (File.Exists(lib) && new FileInfo(lib).Length > 10_000) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            byte[] bytes = await http.GetByteArrayAsync(SignalRUrl);
            if (bytes.Length < 10_000) throw new InvalidOperationException("downloaded file is unexpectedly small");
            File.WriteAllBytes(lib, bytes);
            Console.WriteLine($"Saved the SignalR browser client to {lib}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not save the SignalR browser client ({ex.Message}). The live view will load it from the CDN instead.");
        }
    }
}
