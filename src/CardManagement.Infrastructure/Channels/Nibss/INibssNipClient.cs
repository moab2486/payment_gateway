namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Internal abstraction over NIBSS NIP HTTP API calls for testability.
/// </summary>
public interface INibssNipClient
{
    /// <summary>
    /// Submits a fund transfer request to NIBSS NIP.
    /// </summary>
    Task<NibssTransferResponse> SubmitTransferAsync(NibssTransferRequest request, CancellationToken ct);

    /// <summary>
    /// Queries the status of a previously submitted transfer.
    /// </summary>
    Task<NibssTransferResponse> QueryStatusAsync(string transactionReference, CancellationToken ct);
}

/// <summary>
/// Represents a transfer request to NIBSS NIP.
/// </summary>
public record NibssTransferRequest(
    string SessionId,
    string SenderName,
    string SenderAccount,
    string SenderBankCode,
    string RecipientName,
    string RecipientAccount,
    string RecipientBankCode,
    decimal Amount,
    string CurrencyCode,
    string Narration);

/// <summary>
/// Represents a response from NIBSS NIP.
/// </summary>
public record NibssTransferResponse(
    string ResponseCode,
    string? ResponseMessage,
    string? NibssReference);
