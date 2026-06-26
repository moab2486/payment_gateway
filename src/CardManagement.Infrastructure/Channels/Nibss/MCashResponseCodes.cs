namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Maps NIBSS mCash/USSD response codes to internal standardized error codes.
/// Reuses common NIBSS codes from <see cref="NibssResponseCodes"/> and adds mCash-specific codes.
/// </summary>
public static class MCashResponseCodes
{
    /// <summary>
    /// NIBSS mCash response code indicating a successful transaction.
    /// </summary>
    public const string Success = "00";

    /// <summary>
    /// mCash-specific response codes that extend the common NIBSS set.
    /// </summary>
    private static readonly Dictionary<string, (string ErrorCode, string Message)> MCashSpecificCodes = new()
    {
        ["A1"] = ("INVALID_PHONE_NUMBER", "Invalid source phone number"),
        ["A2"] = ("PIN_ATTEMPTS_EXCEEDED", "Maximum PIN attempts exceeded"),
        ["A3"] = ("SESSION_EXPIRED", "USSD session expired"),
        ["A4"] = ("USER_ABORT", "User aborted USSD session"),
        ["A5"] = ("INVALID_PIN", "Invalid PIN entered"),
        ["A6"] = ("ACCOUNT_BLOCKED", "Account blocked due to PIN failures"),
        ["A7"] = ("SERVICE_NOT_SUBSCRIBED", "Mobile money service not subscribed"),
    };

    /// <summary>
    /// Maps a NIBSS mCash response code to an internal standardized error code and message.
    /// Checks mCash-specific codes first, then falls back to common NIBSS codes.
    /// </summary>
    public static (string ErrorCode, string Message) Map(string? responseCode)
    {
        if (string.IsNullOrEmpty(responseCode))
            return ("UNKNOWN_ERROR", "No response code received from NIBSS mCash");

        // Check mCash-specific codes first
        if (MCashSpecificCodes.TryGetValue(responseCode, out var mcashMapped))
            return mcashMapped;

        // Fall back to common NIBSS response codes
        return NibssResponseCodes.Map(responseCode);
    }

    /// <summary>
    /// Determines if the mCash response code indicates success.
    /// </summary>
    public static bool IsSuccess(string? responseCode) => responseCode == Success;
}
