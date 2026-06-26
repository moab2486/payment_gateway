using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IDisputeRepository.
/// </summary>
public class DisputeRepository : IDisputeRepository
{
    private readonly CardManagementDbContext _dbContext;

    public DisputeRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SaveAsync(DisputeRecord dispute, CancellationToken ct)
    {
        _dbContext.DisputeRecords.Add(dispute);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DisputeRecord dispute, CancellationToken ct)
    {
        var existing = await _dbContext.DisputeRecords.FindAsync(new object[] { dispute.Id }, ct);
        if (existing is not null)
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(dispute);
        }
        else
        {
            _dbContext.DisputeRecords.Update(dispute);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<DisputeRecord?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await _dbContext.DisputeRecords.FindAsync(new object[] { id }, ct);
    }

    public async Task<DisputeRecord?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
    {
        return await _dbContext.DisputeRecords
            .FirstOrDefaultAsync(d => d.TransactionReference == transactionReference, ct);
    }
}
