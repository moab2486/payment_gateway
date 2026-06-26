using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.DTOs;

/// <summary>
/// DTO representing a transaction record with trace number, status, and response code.
/// </summary>
public record TransactionRecordDto
{
    /// <summary>
    /// Unique identifier for the transaction.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// System Trace Audit Number — unique identifier for end-to-end traceability.
    /// </summary>
    public string SystemTraceAuditNumber { get; init; } = string.Empty;

    /// <summary>
    /// Message type indicator (e.g., "0100", "0420").
    /// </summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>
    /// ISO 8583 response code (e.g., "00" approved, "51" insufficient funds).
    /// </summary>
    public string ResponseCode { get; init; } = string.Empty;

    /// <summary>
    /// Transaction amount in smallest currency unit.
    /// </summary>
    public long Amount { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>
    /// The processor type that handled this transaction.
    /// </summary>
    public ProcessorType ProcessorType { get; init; }

    /// <summary>
    /// Current transaction status (Pending, Approved, Declined, Reversed).
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
