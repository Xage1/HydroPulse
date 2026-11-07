namespace HydroPulse.Infrastructure.Entities;

public class TelemetryRecordEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StackId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double RenewablePowerInputKw { get; set; }
    public double HydrogenProducedKgPerHour { get; set; }
    public double WaterConsumedLitersPerHour { get; set; }
    public double ElectricityCostPerHour { get; set; }
    public string OperationalStatus { get; set; } = string.Empty;
}