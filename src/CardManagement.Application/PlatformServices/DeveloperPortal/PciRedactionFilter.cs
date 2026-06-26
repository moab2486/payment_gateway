using System.Text.RegularExpressions;

namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Filters and masks PCI-sensitive data from request/response bodies before storage.
/// Masks card numbers (13-19 digit PANs), CVV/CVC values, and PIN values.
/// </summary>
public static class PciRedactionFilter
{
    private const string MaskedSensitiveValue = "***";

    // Matches 13-19 consecutive digits (potential card numbers / PANs)
    // Looks for digit sequences in JSON string values
    private static readonly Regex PanPattern = new(
        @"(?<="")(\d{13,19})(?="")",
        RegexOptions.Compiled);

    // Matches CVV/CVC/security code fields and their values
    private static readonly Regex CvvFieldPattern = new(
        @"""(cvv|cvc|cvv2|cvc2|securityCode|security_code)"":\s*""(\d{3,4})""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Matches PIN fields and their values
    private static readonly Regex PinFieldPattern = new(
        @"""(pin|pinBlock|pin_block)"":\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Redacts PCI-sensitive data from the given body string.
    /// Card numbers are masked showing only the last 4 digits.
    /// CVV and PIN field values are replaced with '***'.
    /// </summary>
    /// <param name="body">The request or response body to redact.</param>
    /// <returns>The redacted body with sensitive values masked.</returns>
    public static string Redact(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return body ?? string.Empty;

        var result = body;

        // Mask CVV fields first (before PAN, since CVV is 3-4 digits which overlaps)
        result = CvvFieldPattern.Replace(result, match =>
        {
            var fieldName = match.Groups[1].Value;
            return $"\"{fieldName}\":\"{MaskedSensitiveValue}\"";
        });

        // Mask PIN fields
        result = PinFieldPattern.Replace(result, match =>
        {
            var fieldName = match.Groups[1].Value;
            return $"\"{fieldName}\":\"{MaskedSensitiveValue}\"";
        });

        // Mask card numbers (13-19 digits) - replace with masked version showing last 4
        result = PanPattern.Replace(result, match =>
        {
            var pan = match.Value;
            return MaskPan(pan);
        });

        return result;
    }

    /// <summary>
    /// Masks a PAN showing only the last 4 digits, replacing all others with asterisks.
    /// </summary>
    private static string MaskPan(string pan)
    {
        if (string.IsNullOrWhiteSpace(pan) || pan.Length <= 4)
            return pan;

        return new string('*', pan.Length - 4) + pan[^4..];
    }
}
