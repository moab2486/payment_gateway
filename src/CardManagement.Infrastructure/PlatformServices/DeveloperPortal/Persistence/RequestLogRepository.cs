using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;

/// <summary>
/// EF Core implementation of IRequestLogRepository.
/// Provides persistence for API request/response logs with support for
/// paginated queries with filters and TTL-based purge of expired entries.
/// </summary>
public class RequestLogRepository : IRequestLogRepository
{
    private readonly CardManagementDbContext _dbContext;

    public RequestLogRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task CreateAsync(RequestLogEntry entry, CancellationToken ct)
    {
        _dbContext.DeveloperRequestLogs.Add(entry);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<RequestLogEntry>> QueryAsync(RequestLogQuery query, CancellationToken ct)
    {
        var dbQuery = _dbContext.DeveloperRequestLogs
            .AsNoTracking()
            .Where(e => e.DeveloperId == query.DeveloperId);

        if (!string.IsNullOrWhiteSpace(query.Endpoint))
        {
            dbQuery = dbQuery.Where(e => e.Endpoint.Contains(query.Endpoint));
        }

        if (query.StatusCode.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.ResponseStatus == query.StatusCode.Value);
        }

        if (query.FromUtc.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.TimestampUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.TimestampUtc <= query.ToUtc.Value);
        }

        var totalCount = await dbQuery.CountAsync(ct);

        var items = await dbQuery
            .OrderByDescending(e => e.TimestampUtc)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<RequestLogEntry>(items, totalCount, query.Page, query.PageSize);
    }

    public async Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - retention;

        await _dbContext.DeveloperRequestLogs
            .Where(e => e.TimestampUtc < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
