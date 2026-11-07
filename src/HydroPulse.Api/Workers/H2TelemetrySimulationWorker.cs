using HydroPulse.Api.Hubs;
using HydroPulse.Application.Interfaces;
using HydroPulse.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HydroPulse.Api.Workers;

public class H2TelemetrySimulationWorker : BackgroundService
{
    readonly IServiceScopeFactory _scopeFactory;
    readonly IHubContext<TelemetryHub> _hubContext;
    readonly ILogger<H2TelemetrySimulationWorker> _logger;
    readonly Guid _plantId = Guid.NewGuid();

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
        _logger.LogInformation("Green Hydrogen Telemetry Simulation Worker started.");

        // Initialize a 1 MW PEM Electrolyzer stack model
        var stack = new ElectrolyzerStack("PEM-Megawatt-100", 1000.0, 50.0);
        var random = new Random();

        while (!stoppingToken.IsCancellationRequested)
        {
            // Simulate fluctuating renewable solar/wind power between 200 kW and 1050 kW
            double simulatedPowerKw = random.NextDouble() * 850 + 200;
            double simulatedTariff = 0.06; // $0.06 / kWh

            var operationResult = stack.EvaluateOperation(simulatedPowerKw, simulatedTariff);

            var telemetryRecord = new TelemetryRecord(
                Id: Guid.NewGuid(),
                StackId: stack.Id,
                Timestamp: DateTime.UtcNow,
                RenewablePowerInputKw: simulatedPowerKw,
                HydrogenProducedKgPerHour: operationResult.HydrogenProducedKgPerHour,
                WaterConsumedLitersPerHour: operationResult.WaterConsumedLitersPerHour,
                ElectricityCostPerHour: operationResult.ElectricityCostPerHour,
                OperationalStatus: operationResult.Status
            );

            // Save to database & broadcast via WebSockets in a scoped context
            using (var scope = _scopeFactory.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                await repository.AddRecordAsync(telemetryRecord, stoppingToken);
            }

            await _hubContext.Clients.All.SendAsync("ReceiveTelemetry", telemetryRecord, cancellationToken: stoppingToken);

            // Emit telemetry every 3 seconds
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }
}