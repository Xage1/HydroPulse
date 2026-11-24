
using HydroPulse.Domain.Entities;

namespace HydroPulse.Application.Interfaces;

public interface ITelemetryRepository
{
    Task AddAsync(TelemetryRecord record);
    Task AddRecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default);
    Task<IEnumerable<TelemetryRecord>> GetLatestRecordsAsync(int limit);
}

