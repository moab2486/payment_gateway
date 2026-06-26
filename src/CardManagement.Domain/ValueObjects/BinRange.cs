namespace CardManagement.Domain.ValueObjects;

/// <summary>
/// Bank Identification Number Range — the leading digits assigned by the card scheme
/// to identify the issuer. Used for routing transactions to the correct processor.
/// </summary>
public record BinRange
{
    public string Prefix { get; }
    public CardScheme Scheme { get; }
    public int PanLength { get; }

    public BinRange(string prefix, CardScheme scheme, int panLength)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("BIN prefix cannot be null or empty.", nameof(prefix));

        if (!prefix.All(char.IsDigit))
            throw new ArgumentException("BIN prefix must contain only digits.", nameof(prefix));

        if (prefix.Length < 1 || prefix.Length > 8)
            throw new ArgumentException(
                "BIN prefix must be between 1 and 8 digits.", nameof(prefix));

        if (panLength < 16 || panLength > 19)
            throw new ArgumentException(
                $"PAN length must be between 16 and 19. Got {panLength}.", nameof(panLength));

        if (prefix.Length >= panLength)
            throw new ArgumentException(
                "BIN prefix length must be less than PAN length.", nameof(prefix));

        Prefix = prefix;
        Scheme = scheme;
        PanLength = panLength;
    }

    /// <summary>
    /// Checks whether a given PAN starts with this BIN range's prefix
    /// and matches the expected PAN length.
    /// </summary>
    public bool Matches(string pan)
    {
        return pan.Length == PanLength && pan.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
