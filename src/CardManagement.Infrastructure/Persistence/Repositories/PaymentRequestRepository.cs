using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IPaymentRequestRepository.
/// </summary>
public class PaymentRequestRepository : IPaymentRequestRepository
{
    private readonly CardManagementDbContext _dbContext;

    public PaymentRequestRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task SaveAsync(PaymentRequest request, CancellationToken ct)
    {
        var existing = await _dbContext.PaymentRequests
            .FindAsync(new object[] { request.Id }, ct);

        if (existing is null)
        {
            _dbContext.PaymentRequests.Add(request);
        }
        else
        {
            _dbContext.Entry(existing).CurrentValues.SetValues(request);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<PaymentRequest?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
    {
        return await _dbContext.PaymentRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TransactionReference == transactionReference, ct);
    }

    public async Task<PaymentRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
    {
        return await _dbContext.PaymentRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);
    }
}
