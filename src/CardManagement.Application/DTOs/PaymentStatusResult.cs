using CardManagement.Domain.Enums;

namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the result of a payment status inquiry.
/// </summary>
public record PaymentStatusResult(
    string TransactionReference,
    PaymentStatus Status,
    PaymentChannel Channel,
    DateTime LastStateChangeUtc,
    string? ProcessorReference,
    bool Found);
