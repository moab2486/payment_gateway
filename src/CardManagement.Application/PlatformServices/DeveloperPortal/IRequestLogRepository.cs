using CardManagement.Domain.PlatformServices.DeveloperPortal;

namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Repository for persisting and querying API request/response logs.
/// Supports filtering, pagination, and TTL-based purge of expired entries.
/// </summary>
public interface IRequestLogRepository
{
    Task CreateAsync(RequestLogEntry entry, CancellationToken ct);
    Task<PagedResult<RequestLogEntry>> QueryAsync(RequestLogQuery query, CancellationToken ct);
    Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct);
}
