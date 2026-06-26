namespace CardManagement.Domain.ValueObjects;

/// <summary>
/// Represents a monetary amount in the smallest currency unit (e.g., kobo, cents).
/// Ensures amounts are non-negative and currency codes follow ISO 4217.
/// </summary>
public record Money
{
    public long Amount { get; }
    public string CurrencyCode { get; }

    public Money(long amount, string currencyCode)
    {
        if (amount < 0)
            throw new ArgumentException(
                "Amount must be non-negative (expressed in smallest currency unit).",
                nameof(amount));

        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException(
                "Currency code cannot be null or empty.", nameof(currencyCode));

        if (currencyCode.Length != 3 || !currencyCode.All(char.IsLetter))
            throw new ArgumentException(
                "Currency code must be a 3-letter ISO 4217 code.", nameof(currencyCode));

        CurrencyCode = currencyCode.ToUpperInvariant();
        Amount = amount;
    }

    public override string ToString() => $"{Amount} {CurrencyCode}";
}
