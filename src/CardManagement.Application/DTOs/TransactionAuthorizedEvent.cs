namespace CardManagement.Application.DTOs;

/// <summary>
/// Domain event published when a transaction is successfully authorized.
/// </summary>
public record TransactionAuthorizedEvent
{
    /// <summary>Unique identifier of the transaction.</summary>
    public Guid TransactionId { get; init; }

    /// <summary>Identifier of the card used for the transaction.</summary>
    public Guid CardId { get; init; }

    /// <summary>Transaction amount in minor units.</summary>
    public long Amount { get; init; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Type of processor that handled the transaction.</summary>
    public string ProcessorType { get; init; } = string.Empty;

    /// <summary>Authorization response code from the processor.</summary>
    public string ResponseCode { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the transaction was authorized.</summary>
    public DateTime AuthorizationTimestamp { get; init; }

    /// <summary>Correlation identifier for end-to-end tracing.</summary>
    public string CorrelationId { get; init; } = string.Empty;
}
