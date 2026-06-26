namespace CardManagement.Application.DTOs;

/// <summary>
/// DTO representing an issued virtual card (with masked PAN for security).
/// </summary>
public record VirtualCard
{
    /// <summary>
    /// Unique identifier for the card.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Masked PAN for display purposes (e.g., "****1234").
    /// </summary>
    public string PanMasked { get; init; } = string.Empty;

    /// <summary>
    /// Card expiry date in MM/YY format.
    /// </summary>
    public string ExpiryDate { get; init; } = string.Empty;

    /// <summary>
    /// Current card status (Active, Blocked, etc.).
    /// </summary>
    public string Status { get; init; } = string.Empty;
}
