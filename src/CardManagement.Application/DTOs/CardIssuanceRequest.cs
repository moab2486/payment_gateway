using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.DTOs;

/// <summary>
/// Request DTO for issuing a new virtual card.
/// </summary>
public record CardIssuanceRequest
{
    /// <summary>
    /// The card scheme under which to issue the card (Verve, Visa, Mastercard).
    /// </summary>
    public CardScheme CardScheme { get; init; }

    /// <summary>
    /// The BIN range to use for PAN generation.
    /// </summary>
    public BinRange BinRange { get; init; } = null!;

    /// <summary>
    /// The account ID to associate the card with.
    /// </summary>
    public Guid AccountId { get; init; }

    /// <summary>
    /// The validity period in months (1-60) for the card expiry date calculation.
    /// </summary>
    public int ValidityMonths { get; init; }
}
