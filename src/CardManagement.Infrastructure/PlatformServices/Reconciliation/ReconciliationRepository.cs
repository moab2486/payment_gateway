using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation;

/// <summary>
/// EF Core implementation of IReconciliationRepository.
/// Provides persistence operations for reconciliation batches, settlement line items,
/// exceptions, and adjustments.
/// </summary>
public class ReconciliationRepository : IReconciliationRepository
{
    private readonly CardManagementDbContext _dbContext;

    public ReconciliationRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<ReconciliationBatch> CreateBatchAsync(ReconciliationBatch batch, CancellationToken ct)
    {
        _dbContext.ReconciliationBatches.Add(batch);
        await _dbContext.SaveChangesAsync(ct);
        return batch;
    }

    public async Task<ReconciliationBatch?> GetBatchAsync(Guid batchId, CancellationToken ct)
    {
        return await _dbContext.ReconciliationBatches
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);
    }

    public async Task UpdateBatchAsync(ReconciliationBatch batch, CancellationToken ct)
    {
        _dbContext.ReconciliationBatches.Update(batch);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReconciliationBatch>> ListBatchesAsync(int limit, int offset, CancellationToken ct)
    {
        return await _dbContext.ReconciliationBatches
            .AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<bool> FileHashExistsAsync(string fileHash, CancellationToken ct)
    {
        return await _dbContext.ReconciliationBatches
            .AsNoTracking()
            .AnyAsync(b => b.FileHash == fileHash, ct);
    }

    public async Task AddSettlementLineItemsAsync(IEnumerable<SettlementLineItem> items, CancellationToken ct)
    {
        _dbContext.SettlementLineItems.AddRange(items);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AddExceptionsAsync(IEnumerable<ReconciliationException> exceptions, CancellationToken ct)
    {
        _dbContext.ReconciliationExceptions.AddRange(exceptions);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ReconciliationException>> ListExceptionsByBatchAsync(Guid batchId, int limit, int offset, CancellationToken ct)
    {
        return await _dbContext.ReconciliationExceptions
            .AsNoTracking()
            .Where(e => e.BatchId == batchId)
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<ReconciliationException?> GetExceptionAsync(Guid exceptionId, CancellationToken ct)
    {
        return await _dbContext.ReconciliationExceptions
            .FirstOrDefaultAsync(e => e.Id == exceptionId, ct);
    }

    public async Task UpdateExceptionAsync(ReconciliationException exception, CancellationToken ct)
    {
        _dbContext.ReconciliationExceptions.Update(exception);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task AddAdjustmentAsync(Adjustment adjustment, CancellationToken ct)
    {
        _dbContext.Adjustments.Add(adjustment);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Adjustment>> ListAdjustmentsAsync(Guid? exceptionId, int limit, int offset, CancellationToken ct)
    {
        var query = _dbContext.Adjustments.AsNoTracking();

        if (exceptionId.HasValue)
            query = query.Where(a => a.ExceptionId == exceptionId.Value);

        return await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }
}
