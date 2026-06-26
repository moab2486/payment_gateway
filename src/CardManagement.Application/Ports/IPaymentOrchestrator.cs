using CardManagement.Application.DTOs;
using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports;

/// <summary>
/// Orchestrates the full payment lifecycle: initiation, fraud check, routing, and status inquiry.
/// </summary>
public interface IPaymentOrchestrator
{
    Task<PaymentResult> InitiatePaymentAsync(PaymentRequest request, CancellationToken ct);
    Task<PaymentStatusResult> GetStatusAsync(string transactionReference, CancellationToken ct);
    Task<PaymentStatusResult> GetStatusByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);
}
