namespace CardManagement.Application.DTOs;

/// <summary>
/// DTO representing an account's current balance.
/// </summary>
public record AccountBalance
{
    /// <summary>
    /// The account identifier.
    /// </summary>
    public Guid AccountId { get; init; }

    /// <summary>
    /// Current balance in the smallest currency unit.
    /// </summary>
    public long Balance { get; init; }

    /// <summary>
    /// ISO 4217 currency code.
    /// </summary>
    public string Currency { get; init; } = string.Empty;
}
