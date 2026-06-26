namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Maps NIBSS NIP response codes to internal standardized error codes and messages.
/// </summary>
public static class NibssResponseCodes
{
    /// <summary>
    /// NIBSS response code indicating a successful transaction.
    /// </summary>
    public const string Success = "00";

    private static readonly Dictionary<string, (string ErrorCode, string Message)> ResponseCodeMap = new()
    {
        ["00"] = ("SUCCESS", "Transaction successful"),
        ["01"] = ("UNKNOWN_STATUS", "Status unknown, please check later"),
        ["03"] = ("INVALID_SENDER", "Invalid sender"),
        ["05"] = ("DO_NOT_HONOUR", "Do not honour"),
        ["06"] = ("SYSTEM_ERROR", "System error at NIBSS"),
        ["07"] = ("INVALID_ACCOUNT", "Invalid account"),
        ["09"] = ("IN_PROGRESS", "Request processing in progress"),
        ["12"] = ("INVALID_TRANSACTION", "Invalid transaction"),
        ["13"] = ("INVALID_AMOUNT", "Invalid amount"),
        ["14"] = ("INVALID_CARD_NUMBER", "Invalid card number"),
        ["16"] = ("UNKNOWN_BANK", "Unknown bank code"),
        ["17"] = ("CUSTOMER_CANCELLATION", "Customer cancellation"),
        ["25"] = ("ACCOUNT_NOT_FOUND", "Unable to locate record"),
        ["26"] = ("DUPLICATE_REFERENCE", "Duplicate record"),
        ["30"] = ("FORMAT_ERROR", "Format error"),
        ["34"] = ("SUSPECTED_FRAUD", "Suspected fraud"),
        ["35"] = ("CONTACT_SENDING_BANK", "Contact sending bank"),
        ["51"] = ("INSUFFICIENT_FUNDS", "No sufficient funds"),
        ["52"] = ("NO_SAVINGS_ACCOUNT", "No savings account"),
        ["53"] = ("NO_CURRENT_ACCOUNT", "No current account"),
        ["57"] = ("TRANSACTION_NOT_PERMITTED", "Transaction not permitted to sender"),
        ["58"] = ("TRANSACTION_NOT_PERMITTED_TERMINAL", "Transaction not permitted on terminal"),
        ["61"] = ("TRANSFER_LIMIT_EXCEEDED", "Transfer limit exceeded"),
        ["63"] = ("SECURITY_VIOLATION", "Security violation"),
        ["65"] = ("EXCEEDS_WITHDRAWAL_FREQUENCY", "Exceeds withdrawal frequency"),
        ["68"] = ("TIMEOUT_WAITING_RESPONSE", "Response received too late"),
        ["69"] = ("UNSUCCESSFUL", "Unsuccessful"),
        ["91"] = ("BENEFICIARY_BANK_NOT_AVAILABLE", "Beneficiary bank not available"),
        ["92"] = ("ROUTING_ERROR", "Routing error"),
        ["94"] = ("DUPLICATE_TRANSACTION", "Duplicate transaction"),
        ["96"] = ("SYSTEM_MALFUNCTION", "System malfunction"),
    };

    /// <summary>
    /// Maps a NIBSS response code to an internal standardized error code and message.
    /// </summary>
    public static (string ErrorCode, string Message) Map(string? responseCode)
    {
        if (string.IsNullOrEmpty(responseCode))
            return ("UNKNOWN_ERROR", "No response code received from NIBSS");

        if (ResponseCodeMap.TryGetValue(responseCode, out var mapped))
            return mapped;

        return ("UNMAPPED_CODE", $"Unmapped NIBSS response code: {responseCode}");
    }

    /// <summary>
    /// Determines if the NIBSS response code indicates success.
    /// </summary>
    public static bool IsSuccess(string? responseCode) => responseCode == Success;
}
