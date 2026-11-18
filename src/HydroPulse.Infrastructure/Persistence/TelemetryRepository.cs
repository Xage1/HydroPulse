
using HydroPulse.Application.Interfaces;
using HydroPulse.Domain.Entities;
using HydroPulse.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace HydroPulse.Infrastructure.Persistence;

public class TelemetryRepository : ITelemetryRepository
{
    private readonly HydroPulseDbContext _context;

    public TelemetryRepository(HydroPulseDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(TelemetryRecord record)
    {
        await AddRecordAsync(record);
    }

    public async Task AddRecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default)
    {
        var entity = new TelemetryRecordEntity
        {
            Id = record.Id,
            StackId = record.StackId,
            Timestamp = record.Timestamp,
            RenewablePowerInputKw = record.RenewablePowerInputKw,
            HydrogenProducedKgPerHour = record.HydrogenProducedKgPerHour,
            WaterConsumedLitersPerHour = record.WaterConsumedLitersPerHour,
            ElectricityCostPerHour = record.ElectricityCostPerHour,
            OperationalStatus = record.OperationalStatus
        };

        await _context.TelemetryRecords.AddAsync(entity, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<TelemetryRecord>> GetLatestRecordsAsync(int limit)
    {
        var entities = await _context.TelemetryRecords
            .OrderByDescending(r => r.Timestamp)
            .Take(limit)
            .ToListAsync();

        return entities.Select(e => new TelemetryRecord(
            e.Id,
            e.StackId,
            e.Timestamp,
            e.RenewablePowerInputKw,
            e.HydrogenProducedKgPerHour,
            e.WaterConsumedLitersPerHour,
            e.ElectricityCostPerHour,
            e.OperationalStatus
        ));
    }
}

