using CardManagement.Application.PlatformServices.AdminConsole.DTOs;
using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// EF Core implementation of IAdminReadModelQuery.
/// Queries denormalized read model tables with filtering, pagination, and staleness metadata.
/// </summary>
public sealed class AdminReadModelQueryService : IAdminReadModelQuery
{
    private readonly CardManagementDbContext _dbContext;
    private readonly ReadModelStalenessTracker _stalenessTracker;
    private readonly TimeSpan _stalenessThreshold;

    public AdminReadModelQueryService(
        CardManagementDbContext dbContext,
        ReadModelStalenessTracker stalenessTracker,
        IConfiguration configuration)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _stalenessTracker = stalenessTracker ?? throw new ArgumentNullException(nameof(stalenessTracker));

        var thresholdSeconds = configuration.GetValue("AdminConsole:ReadModelStalenessThresholdSeconds", 300);
        _stalenessThreshold = TimeSpan.FromSeconds(thresholdSeconds);
    }

    /// <inheritdoc/>
    public async Task<PagedResult<T>> QueryAsync<T>(ReadModelQuery query, CancellationToken ct) where T : class
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        if (typeof(T) == typeof(TransactionSummaryReadModel))
            return await QueryTransactionSummariesAsync<T>(query, ct);

        if (typeof(T) == typeof(ReconciliationStatusReadModel))
            return await QueryReconciliationStatusesAsync<T>(query, ct);

        if (typeof(T) == typeof(DisputeMetricsReadModel))
            return await QueryDisputeMetricsAsync<T>(query, ct);

        if (typeof(T) == typeof(ChannelHealthReadModel))
            return await QueryChannelHealthAsync<T>(query, ct);

        return new PagedResult<T>(new List<T>(), 0, query.Page, query.PageSize);
    }

    /// <inheritdoc/>
    public async Task<ReadModelMetadata> GetMetadataAsync(string readModelName, CancellationToken ct)
    {
        var (isStale, staleDuration) = await _stalenessTracker.CheckStalenessAsync(
            readModelName, _stalenessThreshold, ct);

        var lastProjected = await _stalenessTracker.GetLastProjectedAtAsync(readModelName, ct);

        return new ReadModelMetadata(
            ReadModelName: readModelName,
            LastProjectedAtUtc: lastProjected ?? DateTime.MinValue,
            IsStale: isStale,
            StaleDuration: staleDuration);
    }

    private async Task<PagedResult<T>> QueryTransactionSummariesAsync<T>(
        ReadModelQuery query, CancellationToken ct) where T : class
    {
        IQueryable<TransactionSummaryReadModel> queryable = _dbContext.AdminTransactionSummaries;

        if (query.Filters != null)
        {
            if (query.Filters.TryGetValue("channel", out var channel))
                queryable = queryable.Where(x => x.Channel == channel);
        }

        if (query.FromDate.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(query.FromDate.Value);
            queryable = queryable.Where(x => x.Date >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDate = DateOnly.FromDateTime(query.ToDate.Value);
            queryable = queryable.Where(x => x.Date <= toDate);
        }

        var totalCount = await queryable.CountAsync(ct);
        var skip = (query.Page - 1) * query.PageSize;

        var items = await queryable
            .OrderByDescending(x => x.Date)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items.Cast<T>().ToList(), totalCount, query.Page, query.PageSize);
    }

    private async Task<PagedResult<T>> QueryReconciliationStatusesAsync<T>(
        ReadModelQuery query, CancellationToken ct) where T : class
    {
        IQueryable<ReconciliationStatusReadModel> queryable = _dbContext.AdminReconciliationStatuses;

        if (query.Filters != null)
        {
            if (query.Filters.TryGetValue("processor", out var processor))
                queryable = queryable.Where(x => x.Processor == processor);
        }

        if (query.FromDate.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(query.FromDate.Value);
            queryable = queryable.Where(x => x.SettlementDate >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDate = DateOnly.FromDateTime(query.ToDate.Value);
            queryable = queryable.Where(x => x.SettlementDate <= toDate);
        }

        var totalCount = await queryable.CountAsync(ct);
        var skip = (query.Page - 1) * query.PageSize;

        var items = await queryable
            .OrderByDescending(x => x.SettlementDate)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items.Cast<T>().ToList(), totalCount, query.Page, query.PageSize);
    }

    private async Task<PagedResult<T>> QueryDisputeMetricsAsync<T>(
        ReadModelQuery query, CancellationToken ct) where T : class
    {
        IQueryable<DisputeMetricsReadModel> queryable = _dbContext.AdminDisputeMetrics;

        if (query.Filters != null)
        {
            if (query.Filters.TryGetValue("disputeType", out var disputeType))
                queryable = queryable.Where(x => x.DisputeType == disputeType);
        }

        if (query.FromDate.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(query.FromDate.Value);
            queryable = queryable.Where(x => x.Date >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDate = DateOnly.FromDateTime(query.ToDate.Value);
            queryable = queryable.Where(x => x.Date <= toDate);
        }

        var totalCount = await queryable.CountAsync(ct);
        var skip = (query.Page - 1) * query.PageSize;

        var items = await queryable
            .OrderByDescending(x => x.Date)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items.Cast<T>().ToList(), totalCount, query.Page, query.PageSize);
    }

    private async Task<PagedResult<T>> QueryChannelHealthAsync<T>(
        ReadModelQuery query, CancellationToken ct) where T : class
    {
        IQueryable<ChannelHealthReadModel> queryable = _dbContext.AdminChannelHealth;

        if (query.Filters != null)
        {
            if (query.Filters.TryGetValue("channel", out var channel))
                queryable = queryable.Where(x => x.ChannelName == channel);
        }

        if (query.FromDate.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(query.FromDate.Value);
            queryable = queryable.Where(x => x.Date >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDate = DateOnly.FromDateTime(query.ToDate.Value);
            queryable = queryable.Where(x => x.Date <= toDate);
        }

        var totalCount = await queryable.CountAsync(ct);
        var skip = (query.Page - 1) * query.PageSize;

        var items = await queryable
            .OrderByDescending(x => x.Date)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<T>(items.Cast<T>().ToList(), totalCount, query.Page, query.PageSize);
    }
}
