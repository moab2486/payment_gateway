using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Iso8583;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class AuthorizationMessageValidatorTests
{
    private static AuthorizationMessageValidator CreateValidator()
    {
        return new AuthorizationMessageValidator(
            NullLogger<AuthorizationMessageValidator>.Instance);
    }

    /// <summary>
    /// Creates a valid authorization request message with all mandatory fields populated.
    /// </summary>
    private static Iso8583Message CreateValidAuthorizationRequest()
    {
        return new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "4532015112830366",    // PAN (16 digits)
                [3] = "000000",              // Processing Code
                [4] = "000000010000",        // Amount (100.00 in smallest unit)
                [11] = "123456",             // STAN
                [14] = "2512",               // Expiry Date (YYMM)
                [22] = "051",                // POS Entry Mode
                [25] = "00",                 // POS Condition Code
                [41] = "TERM0001",           // Terminal ID (8 chars)
                [49] = "566"                 // Currency Code (NGN)
            }
        };
    }

    #region Validate - Valid Messages

    [Fact]
    public void Validate_ValidAuthorizationRequest_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();

        var result = validator.Validate(message);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("0100", result.Value!.Mti);
    }

    [Fact]
    public void Validate_ValidRequest_WithAdditionalOptionalFields_ReturnsSuccess()
    {
        var validator = CreateValidator();
        var message = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "4532015112830366",
                [3] = "000000",
                [4] = "000000010000",
                [7] = "0101120000",          // Optional: Transmission date
                [11] = "123456",
                [12] = "120000",             // Optional: Local time
                [14] = "2512",
                [22] = "051",
                [25] = "00",
                [37] = "123456789012",       // Optional: Retrieval reference number
                [41] = "TERM0001",
                [42] = "MERCHANT000001 ",    // Optional: Card acceptor ID
                [49] = "566"
            }
        };

        var result = validator.Validate(message);

        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Validate - Missing Mandatory Fields

    [Fact]
    public void Validate_MissingPan_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(2);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 2", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingProcessingCode_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(3);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 3", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingAmount_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(4);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 4", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingStan_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(11);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 11", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingExpiryDate_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(14);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 14", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingPosEntryMode_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(22);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 22", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingPosConditionCode_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(25);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 25", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingTerminalId_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(41);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 41", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MissingCurrencyCode_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields);
        fields.Remove(49);
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 49", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_MultipleMissingFields_ReportsAllMissing()
    {
        var validator = CreateValidator();
        var message = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "4532015112830366",
                [11] = "123456"
                // Missing: 3, 4, 14, 22, 25, 41, 49
            }
        };

        var result = validator.Validate(message);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 3", result.ErrorMessage!);
        Assert.Contains("Field 4", result.ErrorMessage!);
        Assert.Contains("Field 14", result.ErrorMessage!);
        Assert.Contains("Field 22", result.ErrorMessage!);
        Assert.Contains("Field 25", result.ErrorMessage!);
        Assert.Contains("Field 41", result.ErrorMessage!);
        Assert.Contains("Field 49", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_EmptyStringField_TreatedAsMissing()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [2] = ""  // Empty PAN
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 2", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_WhitespaceOnlyField_TreatedAsMissing()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [41] = "        "  // Whitespace-only terminal ID
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 41", result.ErrorMessage!);
    }

    #endregion

    #region Validate - Structural Conformance Errors

    [Theory]
    [InlineData("123")]            // Too short
    [InlineData("12345678901234567890")] // Too long (20 digits)
    [InlineData("453201511283A366")] // Contains non-digit
    public void Validate_InvalidPanFormat_ReturnsFormatError(string invalidPan)
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [2] = invalidPan
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 2", result.ErrorMessage!);
    }

    [Theory]
    [InlineData("00000")]      // Too short (5 digits)
    [InlineData("0000000")]    // Too long (7 digits)
    [InlineData("00000A")]     // Non-digit
    public void Validate_InvalidProcessingCode_ReturnsFormatError(string invalidCode)
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [3] = invalidCode
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 3", result.ErrorMessage!);
    }

    [Theory]
    [InlineData("12345")]      // Too short (5 digits)
    [InlineData("1234567")]    // Too long (7 digits)
    [InlineData("12345A")]     // Non-digit
    public void Validate_InvalidStan_ReturnsFormatError(string invalidStan)
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [11] = invalidStan
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 11", result.ErrorMessage!);
    }

    [Theory]
    [InlineData("251")]        // Too short (3 digits)
    [InlineData("25123")]      // Too long (5 digits)
    [InlineData("25AA")]       // Non-digit
    public void Validate_InvalidExpiryDate_ReturnsFormatError(string invalidExpiry)
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [14] = invalidExpiry
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 14", result.ErrorMessage!);
    }

    [Fact]
    public void Validate_InvalidTerminalIdLength_ReturnsFormatError()
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [41] = "TERM01"  // Only 6 chars (should be 8)
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 41", result.ErrorMessage!);
    }

    [Theory]
    [InlineData("56")]         // Too short
    [InlineData("5666")]       // Too long
    public void Validate_InvalidCurrencyCodeLength_ReturnsFormatError(string invalidCurrency)
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest();
        var fields = new Dictionary<int, string>(message.Fields)
        {
            [49] = invalidCurrency
        };
        var invalidMessage = message with { Fields = fields };

        var result = validator.Validate(invalidMessage);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("Field 49", result.ErrorMessage!);
    }

    #endregion

    #region Validate - MTI Validation

    [Fact]
    public void Validate_NullMessage_ReturnsFormatError()
    {
        var validator = CreateValidator();

        var result = validator.Validate(null!);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Theory]
    [InlineData("0110")]   // Auth response (not request)
    [InlineData("0420")]   // Reversal request
    [InlineData("0800")]   // Network management
    [InlineData("0200")]   // Financial request (not auth)
    public void Validate_NonAuthorizationMti_ReturnsFormatError(string mti)
    {
        var validator = CreateValidator();
        var message = CreateValidAuthorizationRequest() with { Mti = mti };

        var result = validator.Validate(message);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("0100", result.ErrorMessage!);
    }

    #endregion

    #region ConstructDeclineResponse Tests

    [Fact]
    public void ConstructDeclineResponse_ReturnsResponseMti0110()
    {
        var validator = CreateValidator();
        var request = CreateValidAuthorizationRequest();

        var response = validator.ConstructDeclineResponse(request, "Validation failed");

        Assert.Equal("0110", response.Mti);
    }

    [Fact]
    public void ConstructDeclineResponse_SetsFormatErrorResponseCode()
    {
        var validator = CreateValidator();
        var request = CreateValidAuthorizationRequest();

        var response = validator.ConstructDeclineResponse(request, "Validation failed");

        Assert.Equal("30", response.Fields[39]);
    }

    [Fact]
    public void ConstructDeclineResponse_EchoesStan()
    {
        var validator = CreateValidator();
        var request = CreateValidAuthorizationRequest();

        var response = validator.ConstructDeclineResponse(request, "Validation failed");

        Assert.Equal("123456", response.Fields[11]);
    }

    [Fact]
    public void ConstructDeclineResponse_EchoesPan()
    {
        var validator = CreateValidator();
        var request = CreateValidAuthorizationRequest();

        var response = validator.ConstructDeclineResponse(request, "Validation failed");

        Assert.Equal("4532015112830366", response.Fields[2]);
    }

    [Fact]
    public void ConstructDeclineResponse_EchoesTerminalId()
    {
        var validator = CreateValidator();
        var request = CreateValidAuthorizationRequest();

        var response = validator.ConstructDeclineResponse(request, "Validation failed");

        Assert.Equal("TERM0001", response.Fields[41]);
    }

    [Fact]
    public void ConstructDeclineResponse_NullRequest_ReturnsMinimalResponse()
    {
        var validator = CreateValidator();

        var response = validator.ConstructDeclineResponse(null, "Message was null");

        Assert.Equal("0110", response.Mti);
        Assert.Equal("30", response.Fields[39]);
        Assert.Single(response.Fields); // Only response code
    }

    [Fact]
    public void ConstructDeclineResponse_PartialRequest_EchoesAvailableFields()
    {
        var validator = CreateValidator();
        var partialRequest = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [11] = "654321"  // Only STAN available
            }
        };

        var response = validator.ConstructDeclineResponse(partialRequest, "Missing fields");

        Assert.Equal("0110", response.Mti);
        Assert.Equal("30", response.Fields[39]);
        Assert.Equal("654321", response.Fields[11]);
        Assert.False(response.Fields.ContainsKey(2));  // Not present in request
        Assert.False(response.Fields.ContainsKey(41)); // Not present in request
    }

    #endregion

    #region Interswitch and CardFi Same Validation Rules

    [Fact]
    public void Validate_InterswitchStyleMessage_ValidatesSuccessfully()
    {
        // Interswitch uses the same MTI 0100 for authorization requests
        var validator = CreateValidator();
        var message = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "5060990000000000",    // Verve PAN
                [3] = "000000",
                [4] = "000000005000",
                [11] = "000001",
                [14] = "2612",
                [22] = "051",
                [25] = "00",
                [41] = "ISW00001",
                [49] = "566"                 // NGN
            }
        };

        var result = validator.Validate(message);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_CardFiStyleMessage_ValidatesSuccessfully()
    {
        // CardFi/Universal Processing uses the same MTI 0100 for authorization requests
        var validator = CreateValidator();
        var message = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "4111111111111111",    // Visa PAN
                [3] = "000000",
                [4] = "000000002500",
                [11] = "999999",
                [14] = "2703",
                [22] = "071",
                [25] = "08",
                [41] = "CFI00001",
                [49] = "840"                 // USD
            }
        };

        var result = validator.Validate(message);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_InterswitchMissingFields_SameErrorAsCardFi()
    {
        var validator = CreateValidator();

        // Missing field 49 (Currency Code) - same rule applies to both processors
        var interswitchMessage = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "5060990000000000",
                [3] = "000000",
                [4] = "000000005000",
                [11] = "000001",
                [14] = "2612",
                [22] = "051",
                [25] = "00",
                [41] = "ISW00001"
                // Missing [49]
            }
        };

        var cardFiMessage = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "4111111111111111",
                [3] = "000000",
                [4] = "000000002500",
                [11] = "999999",
                [14] = "2703",
                [22] = "071",
                [25] = "08",
                [41] = "CFI00001"
                // Missing [49]
            }
        };

        var interswitchResult = validator.Validate(interswitchMessage);
        var cardFiResult = validator.Validate(cardFiMessage);

        Assert.False(interswitchResult.IsSuccess);
        Assert.False(cardFiResult.IsSuccess);
        Assert.Equal("30", interswitchResult.ErrorCode);
        Assert.Equal("30", cardFiResult.ErrorCode);
        Assert.Contains("Field 49", interswitchResult.ErrorMessage!);
        Assert.Contains("Field 49", cardFiResult.ErrorMessage!);
    }

    #endregion
}
