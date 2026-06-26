using CardManagement.Domain.Entities;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Saga;

/// <summary>
/// PostgreSQL-backed repository for persisting saga state via EF Core.
/// </summary>
public sealed class SagaStateRepository : ISagaStateRepository
{
    private readonly CardManagementDbContext _dbContext;

    public SagaStateRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(SagaState state, CancellationToken ct)
    {
        _dbContext.SagaStates.Add(state);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(SagaState state, CancellationToken ct)
    {
        _dbContext.SagaStates.Update(state);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<SagaState?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _dbContext.SagaStates
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<SagaState?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
    {
        return await _dbContext.SagaStates
            .FirstOrDefaultAsync(s => s.TransactionReference == transactionReference, ct);
    }
}
