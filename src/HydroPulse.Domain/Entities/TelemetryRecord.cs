namespace HydroPulse.Domain.Entities;

public record TelemetryRecord(
    Guid Id,
    Guid StackId,
    DateTime Timestamp,
    double RenewablePowerInputKw,
    double HydrogenProducedKgPerHour,
    double WaterConsumedLitersPerHour,
    double ElectricityCostPerHour,
    string OperationalStatus
);