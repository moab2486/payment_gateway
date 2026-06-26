using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents a request to raise a dispute against a payment transaction.
/// </summary>
public record DisputeRequest(
    string TransactionReference,
    string ReasonCode,
    Money Amount,
    string? Evidence);
