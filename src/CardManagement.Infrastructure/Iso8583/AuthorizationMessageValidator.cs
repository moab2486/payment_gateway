using CardManagement.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Iso8583;

/// <summary>
/// Validates ISO 8583 authorization request messages (MTI 0100) for structural conformance
/// and mandatory field presence. Returns a decline response with response code "30" (format error)
/// when validation fails. Applies the same validation rules for both Interswitch (Verve) and
/// CardFi/Universal Processing (Visa/Mastercard) messages.
/// </summary>
public sealed class AuthorizationMessageValidator
{
    /// <summary>
    /// MTI for authorization request messages.
    /// </summary>
    public const string MtiAuthorizationRequest = "0100";

    /// <summary>
    /// MTI for authorization response messages.
    /// </summary>
    public const string MtiAuthorizationResponse = "0110";

    /// <summary>
    /// ISO 8583 response code for format error (used when mandatory fields are missing or invalid).
    /// </summary>
    public const string ResponseCodeFormatError = "30";

    /// <summary>
    /// ISO 8583 field number for the response code.
    /// </summary>
    private const int FieldResponseCode = 39;

    /// <summary>
    /// Mandatory fields for an authorization request (MTI 0100).
    /// Both Interswitch and CardFi/Universal Processing require the same set of mandatory fields.
    /// </summary>
    private static readonly IReadOnlyList<MandatoryFieldDefinition> MandatoryFields = new List<MandatoryFieldDefinition>
    {
        new(2, "PAN (Primary Account Number)"),
        new(3, "Processing Code"),
        new(4, "Amount"),
        new(11, "System Trace Audit Number (STAN)"),
        new(14, "Expiry Date"),
        new(22, "POS Entry Mode"),
        new(25, "POS Condition Code"),
        new(41, "Terminal ID"),
        new(49, "Currency Code")
    };

    private readonly ILogger<AuthorizationMessageValidator> _logger;

    public AuthorizationMessageValidator(ILogger<AuthorizationMessageValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates an authorization request message for structural conformance and mandatory field presence.
    /// </summary>
    /// <param name="message">The ISO 8583 message to validate.</param>
    /// <returns>
    /// A successful result if the message is valid, or a failure result containing a decline response
    /// message with response code "30" (format error) if validation fails.
    /// </returns>
    public Result<Iso8583Message> Validate(Iso8583Message message)
    {
        if (message is null)
        {
            _logger.LogWarning("Authorization validation failed: message is null");
            return Result<Iso8583Message>.Failure(
                "Authorization request message cannot be null.",
                ResponseCodeFormatError);
        }

        // Validate MTI is an authorization request
        if (!IsAuthorizationRequest(message.Mti))
        {
            _logger.LogWarning(
                "Authorization validation failed: unexpected MTI {Mti}, expected {Expected}",
                message.Mti, MtiAuthorizationRequest);
            return Result<Iso8583Message>.Failure(
                $"Expected MTI {MtiAuthorizationRequest} for authorization request but received {message.Mti}.",
                ResponseCodeFormatError);
        }

        // Validate mandatory field presence
        var missingFields = GetMissingMandatoryFields(message);
        if (missingFields.Count > 0)
        {
            var missingFieldNames = string.Join(", ", missingFields.Select(f => $"Field {f.FieldNumber} ({f.FieldName})"));

            _logger.LogWarning(
                "Authorization validation failed: missing mandatory fields [{MissingFields}]",
                missingFieldNames);

            return Result<Iso8583Message>.Failure(
                $"Authorization request is missing mandatory fields: {missingFieldNames}.",
                ResponseCodeFormatError);
        }

        // Validate field value structural conformance
        var structuralErrors = ValidateFieldStructure(message);
        if (structuralErrors.Count > 0)
        {
            var errorDetails = string.Join("; ", structuralErrors);

            _logger.LogWarning(
                "Authorization validation failed: structural errors [{Errors}]",
                errorDetails);

            return Result<Iso8583Message>.Failure(
                $"Authorization request has structural errors: {errorDetails}.",
                ResponseCodeFormatError);
        }

        _logger.LogDebug("Authorization request validated successfully (STAN: {Stan})",
            message.Fields.TryGetValue(11, out var stan) ? stan : "unknown");

        return Result<Iso8583Message>.Success(message);
    }

    /// <summary>
    /// Constructs a decline response message with response code "30" (format error)
    /// for an invalid authorization request.
    /// </summary>
    /// <param name="request">The original request message (may be partially populated).</param>
    /// <param name="errorMessage">Description of the validation failure.</param>
    /// <returns>An ISO 8583 authorization response (MTI 0110) with response code "30".</returns>
    public Iso8583Message ConstructDeclineResponse(Iso8583Message? request, string errorMessage)
    {
        var responseFields = new Dictionary<int, string>();

        // Echo back available fields from the request
        if (request?.Fields != null)
        {
            // Echo STAN if present
            if (request.Fields.TryGetValue(11, out var stan))
                responseFields[11] = stan;

            // Echo PAN if present
            if (request.Fields.TryGetValue(2, out var pan))
                responseFields[2] = pan;

            // Echo processing code if present
            if (request.Fields.TryGetValue(3, out var procCode))
                responseFields[3] = procCode;

            // Echo terminal ID if present
            if (request.Fields.TryGetValue(41, out var terminalId))
                responseFields[41] = terminalId;
        }

        // Set format error response code
        responseFields[FieldResponseCode] = ResponseCodeFormatError;

        return new Iso8583Message
        {
            Mti = MtiAuthorizationResponse,
            Fields = responseFields
        };
    }

    /// <summary>
    /// Determines whether the given MTI represents an authorization request.
    /// Both Interswitch and CardFi/Universal Processing use MTI 0100 for authorization requests.
    /// </summary>
    private static bool IsAuthorizationRequest(string? mti)
    {
        return string.Equals(mti, MtiAuthorizationRequest, StringComparison.Ordinal);
    }

    /// <summary>
    /// Identifies mandatory fields that are missing from the message.
    /// </summary>
    private static List<MandatoryFieldDefinition> GetMissingMandatoryFields(Iso8583Message message)
    {
        var missing = new List<MandatoryFieldDefinition>();

        foreach (var field in MandatoryFields)
        {
            if (!message.Fields.TryGetValue(field.FieldNumber, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                missing.Add(field);
            }
        }

        return missing;
    }

    /// <summary>
    /// Validates structural conformance of mandatory field values.
    /// </summary>
    private static List<string> ValidateFieldStructure(Iso8583Message message)
    {
        var errors = new List<string>();

        // Field 2 - PAN: must be 13-19 digits
        if (message.Fields.TryGetValue(2, out var pan))
        {
            if (pan.Length < 13 || pan.Length > 19 || !pan.All(char.IsDigit))
            {
                errors.Add("Field 2 (PAN) must be 13-19 digits");
            }
        }

        // Field 3 - Processing Code: must be exactly 6 digits
        if (message.Fields.TryGetValue(3, out var procCode))
        {
            if (procCode.Length != 6 || !procCode.All(char.IsDigit))
            {
                errors.Add("Field 3 (Processing Code) must be exactly 6 digits");
            }
        }

        // Field 4 - Amount: must be numeric (up to 12 digits)
        if (message.Fields.TryGetValue(4, out var amount))
        {
            if (string.IsNullOrEmpty(amount) || !amount.All(char.IsDigit) || amount.Length > 12)
            {
                errors.Add("Field 4 (Amount) must be numeric up to 12 digits");
            }
        }

        // Field 11 - STAN: must be exactly 6 digits
        if (message.Fields.TryGetValue(11, out var stan))
        {
            if (stan.Length != 6 || !stan.All(char.IsDigit))
            {
                errors.Add("Field 11 (STAN) must be exactly 6 digits");
            }
        }

        // Field 14 - Expiry Date: must be 4 digits (YYMM format)
        if (message.Fields.TryGetValue(14, out var expiry))
        {
            if (expiry.Length != 4 || !expiry.All(char.IsDigit))
            {
                errors.Add("Field 14 (Expiry Date) must be 4 digits in YYMM format");
            }
        }

        // Field 22 - POS Entry Mode: must be 3 digits
        if (message.Fields.TryGetValue(22, out var posEntry))
        {
            if (posEntry.Length != 3 || !posEntry.All(char.IsDigit))
            {
                errors.Add("Field 22 (POS Entry Mode) must be 3 digits");
            }
        }

        // Field 25 - POS Condition Code: must be 2 digits
        if (message.Fields.TryGetValue(25, out var posCondition))
        {
            if (posCondition.Length != 2 || !posCondition.All(char.IsDigit))
            {
                errors.Add("Field 25 (POS Condition Code) must be 2 digits");
            }
        }

        // Field 41 - Terminal ID: must be exactly 8 characters
        if (message.Fields.TryGetValue(41, out var terminalId))
        {
            if (terminalId.Length != 8)
            {
                errors.Add("Field 41 (Terminal ID) must be exactly 8 characters");
            }
        }

        // Field 49 - Currency Code: must be exactly 3 characters
        if (message.Fields.TryGetValue(49, out var currency))
        {
            if (currency.Length != 3)
            {
                errors.Add("Field 49 (Currency Code) must be exactly 3 characters");
            }
        }

        return errors;
    }

    /// <summary>
    /// Represents a mandatory field definition with its number and human-readable name.
    /// </summary>
    private sealed record MandatoryFieldDefinition(int FieldNumber, string FieldName);
}
