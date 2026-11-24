using Microsoft.AspNetCore.Mvc;
using HydroPulse.Application.Interfaces;

namespace HydroPulse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private const int MaxLimit = 2000;

    private readonly ITelemetryRepository _repository;
    private readonly ILogger<TelemetryController> _logger;

    public TelemetryController(ITelemetryRepository repository, ILogger<TelemetryController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the latest telemetry records, newest first. The limit is capped at 2000.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatest([FromQuery] int limit = 50)
    {
        var records = await _repository.GetLatestRecordsAsync(Math.Clamp(limit, 1, MaxLimit));
        return Ok(records);
    }
}
