using CardManagement.Domain.Enums;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Internal abstraction over NIBSS Direct Debit API calls for testability.
/// </summary>
public interface INibssDirectDebitClient
{
    /// <summary>
    /// Submits a mandate creation request to NIBSS Direct Debit.
    /// </summary>
    Task<DirectDebitResponse> CreateMandateAsync(DirectDebitMandateRequest request, CancellationToken ct);

    /// <summary>
    /// Activates an existing mandate at NIBSS Direct Debit.
    /// </summary>
    Task<DirectDebitResponse> ActivateMandateAsync(string mandateReference, CancellationToken ct);

    /// <summary>
    /// Submits a scheduled debit for an active mandate at NIBSS Direct Debit.
    /// </summary>
    Task<DirectDebitResponse> SubmitDebitAsync(DirectDebitSubmitRequest request, CancellationToken ct);

    /// <summary>
    /// Cancels an existing mandate at NIBSS Direct Debit.
    /// </summary>
    Task<DirectDebitResponse> CancelMandateAsync(string mandateReference, CancellationToken ct);
}

/// <summary>
/// Represents a mandate creation request to NIBSS Direct Debit.
/// </summary>
/// <param name="MandateReference">Unique mandate reference for tracking.</param>
/// <param name="DebtorAccount">Account to be debited.</param>
/// <param name="CreditorAccount">Account to receive the funds.</param>
/// <param name="Amount">Mandate amount in minor currency units (kobo).</param>
/// <param name="CurrencyCode">ISO 4217 currency code.</param>
/// <param name="Frequency">Debit frequency (Daily, Weekly, Monthly, etc.).</param>
/// <param name="StartDate">Mandate effective start date.</param>
/// <param name="EndDate">Mandate expiry date.</param>
/// <param name="TransactionReference">Unique transaction reference for the originating payment request.</param>
public record DirectDebitMandateRequest(
    string MandateReference,
    string DebtorAccount,
    string CreditorAccount,
    decimal Amount,
    string CurrencyCode,
    MandateFrequency Frequency,
    DateTime StartDate,
    DateTime EndDate,
    string TransactionReference);

/// <summary>
/// Represents a scheduled debit submission request to NIBSS Direct Debit.
/// </summary>
/// <param name="MandateReference">The reference of the active mandate to debit against.</param>
/// <param name="Amount">The debit amount in minor currency units (kobo).</param>
/// <param name="CurrencyCode">ISO 4217 currency code.</param>
/// <param name="TransactionReference">Unique transaction reference for this debit.</param>
public record DirectDebitSubmitRequest(
    string MandateReference,
    decimal Amount,
    string CurrencyCode,
    string TransactionReference);

/// <summary>
/// Represents a response from NIBSS Direct Debit.
/// </summary>
public record DirectDebitResponse(
    string ResponseCode,
    string? ResponseMessage,
    string? NibssReference);
