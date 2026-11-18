
using Microsoft.AspNetCore.Mvc;
using HydroPulse.Application.Interfaces;

namespace HydroPulse.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly ITelemetryRepository _repository;
    private readonly ILogger<TelemetryController> _logger;

    public TelemetryController(ITelemetryRepository repository, ILogger<TelemetryController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the latest telemetry records.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLatest([FromQuery] int limit = 50)
    {
        var records = await _repository.GetLatestRecordsAsync(limit);
        return Ok(records);
    }
}

