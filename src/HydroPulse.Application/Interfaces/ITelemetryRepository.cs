using HydroPulse.Domain.Entities;

namespace HydroPulse.Application.Interfaces;

public interface ITelemetryRepository
{
    Task AddRecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default);
    Task<IEnumerable<TelemetryRecord>> GetRecentRecordsAsync(Guid stackId, int limit = 100, CancellationToken cancellationToken = default);
}