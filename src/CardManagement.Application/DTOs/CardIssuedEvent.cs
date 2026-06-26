namespace CardManagement.Application.DTOs;

/// <summary>
/// Domain event published when a new card is successfully issued.
/// </summary>
public record CardIssuedEvent
{
    /// <summary>Unique identifier of the issued card.</summary>
    public Guid CardId { get; init; }

    /// <summary>Card scheme (e.g., Visa, Mastercard).</summary>
    public string CardScheme { get; init; } = string.Empty;

    /// <summary>Account identifier the card belongs to.</summary>
    public Guid AccountId { get; init; }

    /// <summary>UTC timestamp when the card was issued.</summary>
    public DateTime IssuanceTimestamp { get; init; }

    /// <summary>Correlation identifier for end-to-end tracing.</summary>
    public string CorrelationId { get; init; } = string.Empty;
}
