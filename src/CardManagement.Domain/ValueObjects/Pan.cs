namespace CardManagement.Domain.ValueObjects;

/// <summary>
/// Primary Account Number (PAN) — the 16-19 digit number on a payment card.
/// Validates Luhn check digit and length constraints on construction.
/// </summary>
public record Pan
{
    public string Value { get; }

    public Pan(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("PAN cannot be null or empty.", nameof(value));

        if (!value.All(char.IsDigit))
            throw new ArgumentException("PAN must contain only digits.", nameof(value));

        if (value.Length < 16 || value.Length > 19)
            throw new ArgumentException(
                $"PAN must be between 16 and 19 digits. Got {value.Length} digits.",
                nameof(value));

        if (!PassesLuhnCheck(value))
            throw new ArgumentException("PAN does not pass Luhn validation.", nameof(value));

        Value = value;
    }

    /// <summary>
    /// Validates a PAN string against the Luhn algorithm.
    /// </summary>
    internal static bool PassesLuhnCheck(string number)
    {
        int sum = 0;
        bool alternate = false;

        for (int i = number.Length - 1; i >= 0; i--)
        {
            int digit = number[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    public override string ToString() => Value;
}
