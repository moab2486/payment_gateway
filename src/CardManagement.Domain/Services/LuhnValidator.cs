namespace CardManagement.Domain.Services;

/// <summary>
/// Domain service implementing the Luhn (mod-10) algorithm for PAN validation
/// and check digit computation.
/// </summary>
public static class LuhnValidator
{
    /// <summary>
    /// Validates whether a full PAN passes the Luhn algorithm check.
    /// </summary>
    /// <param name="pan">The complete PAN string (all digits including the check digit).</param>
    /// <returns>True if the PAN passes Luhn validation; false otherwise.</returns>
    public static bool IsValid(string pan)
    {
        if (string.IsNullOrEmpty(pan) || !AllDigits(pan))
            return false;

        int sum = 0;
        bool alternate = false;

        for (int i = pan.Length - 1; i >= 0; i--)
        {
            int digit = pan[i] - '0';

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

    /// <summary>
    /// Computes the Luhn check digit for a partial PAN (all digits except the last).
    /// The returned character, when appended to <paramref name="partialPan"/>,
    /// produces a full PAN that passes the Luhn check.
    /// </summary>
    /// <param name="partialPan">The PAN without its final check digit.</param>
    /// <returns>The check digit character ('0'-'9').</returns>
    /// <exception cref="ArgumentException">Thrown when the input is null, empty, or contains non-digit characters.</exception>
    public static char ComputeCheckDigit(string partialPan)
    {
        if (string.IsNullOrEmpty(partialPan))
            throw new ArgumentException("Partial PAN cannot be null or empty.", nameof(partialPan));

        if (!AllDigits(partialPan))
            throw new ArgumentException("Partial PAN must contain only digits.", nameof(partialPan));

        // Luhn algorithm: process from right to left of the partial PAN.
        // The check digit occupies the rightmost position of the full PAN,
        // so the rightmost digit of the partial PAN is in an "alternate" (doubled) position.
        int sum = 0;
        bool alternate = true;

        for (int i = partialPan.Length - 1; i >= 0; i--)
        {
            int digit = partialPan[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        int checkDigit = (10 - (sum % 10)) % 10;
        return (char)('0' + checkDigit);
    }

    private static bool AllDigits(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] < '0' || value[i] > '9')
                return false;
        }

        return true;
    }
}
