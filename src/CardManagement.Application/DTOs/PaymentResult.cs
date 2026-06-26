using CardManagement.Domain.Enums;

namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the outcome of a payment initiation request.
/// </summary>
public record PaymentResult(
    string TransactionReference,
    PaymentStatus Status,
    bool Success,
    string? ErrorMessage,
    string? ProcessorReference);
