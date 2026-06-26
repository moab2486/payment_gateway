using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository for persisting and retrieving PaymentRequest entities.
/// </summary>
public interface IPaymentRequestRepository
{
    Task SaveAsync(PaymentRequest request, CancellationToken ct);
    Task<PaymentRequest?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct);
    Task<PaymentRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);
}
