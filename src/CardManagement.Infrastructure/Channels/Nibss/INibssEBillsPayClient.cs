namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Internal abstraction over NIBSS e-BillsPay HTTP API calls for testability.
/// </summary>
public interface INibssEBillsPayClient
{
    /// <summary>
    /// Submits a bill payment instruction to NIBSS e-BillsPay.
    /// </summary>
    Task<EBillsPayResponse> SubmitPaymentAsync(EBillsPayRequest request, CancellationToken ct);
}

/// <summary>
/// Information about a registered biller in the NIBSS biller directory.
/// </summary>
public record BillerInfo(
    string BillerCode,
    string BillerName,
    decimal? MinAmount,
    decimal? MaxAmount,
    bool AcceptsCustomerRef);

/// <summary>
/// Represents a bill payment request to NIBSS e-BillsPay.
/// </summary>
public record EBillsPayRequest(
    string BillerCode,
    string CustomerReference,
    decimal Amount,
    string PayerName,
    string PayerAccount,
    string TransactionReference);

/// <summary>
/// Represents a response from NIBSS e-BillsPay.
/// </summary>
public record EBillsPayResponse(
    string ResponseCode,
    string? ResponseMessage,
    string? ConfirmationReference);
