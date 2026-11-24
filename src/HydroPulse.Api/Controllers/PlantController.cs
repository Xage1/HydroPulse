using HydroPulse.Api.Workers;
using Microsoft.AspNetCore.Mvc;

namespace HydroPulse.Api.Controllers;

/// <summary>Describes the plant behind the live telemetry stream so the dashboard can label it correctly.</summary>
[ApiController]
[Route("api/[controller]")]
public class PlantController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ReplayState _replay;

    public PlantController(IConfiguration configuration, ReplayState replay)
    {
        _configuration = configuration;
        _replay = replay;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        plantId = H2TelemetrySimulationWorker.PlantId,
        stackId = H2TelemetrySimulationWorker.StackId,
        name = _configuration["Telemetry:PlantName"] ?? "Demo plant",
        ratedPowerKw = H2TelemetrySimulationWorker.RatedPowerKw,
        intervalSeconds = H2TelemetrySimulationWorker.IntervalSeconds,
        tariffUsdPerKwh = H2TelemetrySimulationWorker.TariffUsdPerKwh,
        compressionKwhPerKg = H2TelemetrySimulationWorker.CompressionKwhPerKg,
        // "Simulated" until a real SCADA/PLC source replaces the simulation worker. The dashboard shows this label.
        source = _configuration["Telemetry:Source"] ?? "Simulated",
        hub = "/hubs/telemetry",
        // Present while the simulation replays a weather year; null when it runs on random demo power.
        replay = _replay.Current
    });
}
