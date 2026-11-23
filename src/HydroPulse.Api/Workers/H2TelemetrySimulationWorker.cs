using HydroPulse.Api.Hubs;
using HydroPulse.Application.Interfaces;
using HydroPulse.Domain.Engineering;
using HydroPulse.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace HydroPulse.Api.Workers;

public class H2TelemetrySimulationWorker : BackgroundService
{
    /// <summary>Stable identifiers so history lines up across restarts and clients know which group to join.</summary>
    public static readonly Guid PlantId = Guid.Parse("7c1d3a52-5f0e-4d5b-9a0e-1b6e4f2a8c01");
    public static readonly Guid StackId = Guid.Parse("a3f4e0b8-2d61-4c7a-8e35-9d1f0c6b7a02");

    /// <summary>Rated electrolyzer power of the plant this worker simulates.</summary>
    public const double RatedPowerKw = 1000.0;

    /// <summary>Electricity price used to turn consumption into a cost figure.</summary>
    public const double TariffUsdPerKwh = 0.06;

    /// <summary>Compression energy per kg of hydrogen, from the same default compression train as the scenario runner.</summary>
    public static readonly double CompressionKwhPerKg = CompressionModel.EnergyKwhPerKg(
        new CompressionParameters().InletBar, new CompressionParameters().OutletBar, new CompressionParameters().Stages);

    /// <summary>Seconds between telemetry samples.</summary>
    public const int IntervalSeconds = 3;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(IntervalSeconds);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<TelemetryHub> _hubContext;
    private readonly IOptions<ReplayOptions> _replayOptions;
    private readonly ReplayState _replayState;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<H2TelemetrySimulationWorker> _logger;

    public H2TelemetrySimulationWorker(
        IServiceScopeFactory scopeFactory,
        IHubContext<TelemetryHub> hubContext,
        IOptions<ReplayOptions> replayOptions,
        ReplayState replayState,
        IHostEnvironment environment,
        ILogger<H2TelemetrySimulationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _replayOptions = replayOptions;
        _replayState = replayState;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Green Hydrogen Telemetry Simulation Worker started (plant {PlantId}).", PlantId);

        var model = new ElectrolyzerModel(new ElectrolyzerParameters { RatedPowerKw = RatedPowerKw });
        var random = new Random();
        double operatingHours = 0.0;

        // With a replay, renewable power follows a real weather year through the PV/wind models,
        // and simulated time runs faster than real time. Without one, power is random demo noise.
        var plan = ReplaySetup.TryCreate(_replayOptions.Value, _environment.ContentRootPath, _logger);
        DateTime simulatedUtc = plan?.StartUtc ?? DateTime.UtcNow;
        double speedUp = plan?.SpeedUp ?? 1.0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                double powerKw = plan is not null
                    ? plan.Replay.PowerKw(simulatedUtc)
                    : random.NextDouble() * 850 + 200;

                // The same dispatch the scenario runner uses, so live hydrogen includes the compressor's share of the power.
                var plant = PlantDispatch.Run(model, powerKw, operatingHours, CompressionKwhPerKg);
                if (plant.StackKw > 0)
                    operatingHours += Interval.TotalHours * speedUp;   // stack wear follows simulated time

                var record = new TelemetryRecord(
                    Id: Guid.NewGuid(),
                    StackId: StackId,
                    Timestamp: DateTime.UtcNow,
                    RenewablePowerInputKw: powerKw,
                    HydrogenProducedKgPerHour: plant.H2KgPerHour,
                    WaterConsumedLitersPerHour: plant.WaterLitersPerHour,
                    ElectricityCostPerHour: plant.TotalKw * TariffUsdPerKwh,
                    OperationalStatus: Describe(plant.Mode));

                using (var scope = _scopeFactory.CreateScope())
                {
                    var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
                    await repository.AddRecordAsync(record, stoppingToken);
                }

                var group = _hubContext.Clients.Group(PlantId.ToString());
                await group.SendAsync("ReceiveTelemetry", record, stoppingToken);

                if (plan is not null)
                {
                    var info = new ReplayInfo(plan.Site, plan.Source, plan.Ratio, plan.SpeedUp, simulatedUtc);
                    _replayState.Current = info;
                    await group.SendAsync("ReplayClock", info, stoppingToken);
                    simulatedUtc = simulatedUtc.AddSeconds(IntervalSeconds * speedUp);
                }
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
