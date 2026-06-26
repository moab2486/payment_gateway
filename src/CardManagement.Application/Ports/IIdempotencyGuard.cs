using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Guards against duplicate payment processing using idempotency keys.
/// </summary>
public interface IIdempotencyGuard
{
    Task<IdempotencyCheckResult> CheckAsync(string idempotencyKey, CancellationToken ct);
    Task StoreAsync(string idempotencyKey, Guid paymentRequestId, object response, CancellationToken ct);
    Task RegisterInProgressAsync(string idempotencyKey, Guid paymentRequestId, CancellationToken ct);
}
