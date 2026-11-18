using HydroPulse.Api.Hubs;
using HydroPulse.Application.Interfaces;
using HydroPulse.Domain.Engineering;
using HydroPulse.Domain.Entities;
using Microsoft.AspNetCore.SignalR;

namespace HydroPulse.Api.Workers;

public class H2TelemetrySimulationWorker : BackgroundService
{
    /// <summary>Stable identifiers so history lines up across restarts and clients know which group to join.</summary>
    public static readonly Guid PlantId = Guid.Parse("7c1d3a52-5f0e-4d5b-9a0e-1b6e4f2a8c01");
    public static readonly Guid StackId = Guid.Parse("a3f4e0b8-2d61-4c7a-8e35-9d1f0c6b7a02");

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly ILogger<H2TelemetrySimulationWorker> _logger;

    public H2TelemetrySimulationWorker(
        IServiceScopeFactory scopeFactory,
        IHubContext<TelemetryHub> hubContext,
        ILogger<H2TelemetrySimulationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Green Hydrogen Telemetry Simulation Worker started (plant {PlantId}).", PlantId);

        var model = new ElectrolyzerModel(new ElectrolyzerParameters { RatedPowerKw = 1000.0 });
        var random = new Random();
        const double tariffUsdPerKwh = 0.06;
        double operatingHours = 0.0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Placeholder input: replace with a real solar/wind time series for the thesis.
                double powerKw = random.NextDouble() * 850 + 200;

                var state = model.Evaluate(powerKw, operatingHours);
                if (state.Mode != ElectrolyzerMode.Standby)
                    operatingHours += Interval.TotalHours;

                var record = new TelemetryRecord(
                    Id: Guid.NewGuid(),
                    StackId: StackId,
                    Timestamp: DateTime.UtcNow,
                    RenewablePowerInputKw: powerKw,
                    HydrogenProducedKgPerHour: state.HydrogenKgPerHour,
                    WaterConsumedLitersPerHour: state.WaterLitersPerHour,
                    ElectricityCostPerHour: state.PowerUsedKw * tariffUsdPerKwh,
                    OperationalStatus: Describe(state.Mode));

                using (var scope = _scopeFactory.CreateScope())
                {
                    var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                    await repository.AddRecordAsync(record, stoppingToken);
                }

                await _hubContext.Clients.Group(PlantId.ToString())
                    .SendAsync("ReceiveTelemetry", record, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad tick (e.g. DB briefly unavailable) must not take the whole host down.
                _logger.LogError(ex, "Telemetry tick failed; retrying after {Interval}.", Interval);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string Describe(ElectrolyzerMode mode) => mode switch
    {
        ElectrolyzerMode.Standby => "Standby / Insufficient Power",
        ElectrolyzerMode.PartLoad => "Active - Load Following",
        ElectrolyzerMode.Saturated => "Active - At Rated Capacity (Curtailing)",
        _ => mode.ToString()
    };
}
