namespace CardManagement.Application.DTOs;

/// <summary>
/// Domain event published when a transaction is successfully reversed.
/// </summary>
public record TransactionReversedEvent
{
    /// <summary>Identifier of the original transaction being reversed.</summary>
    public Guid OriginalTransactionId { get; init; }

    /// <summary>Identifier of the reversal transaction.</summary>
    public Guid ReversalTransactionId { get; init; }

    /// <summary>Reversal amount in minor units.</summary>
    public long Amount { get; init; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the reversal occurred.</summary>
    public DateTime ReversalTimestamp { get; init; }

    /// <summary>Correlation identifier for end-to-end tracing.</summary>
    public string CorrelationId { get; init; } = string.Empty;
}
