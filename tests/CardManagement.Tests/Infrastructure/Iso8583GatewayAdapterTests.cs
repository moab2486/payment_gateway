using System.Text;
using CardManagement.Infrastructure.Iso8583;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetCore8583;
using NetCore8583.Parse;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for Iso8583GatewayAdapter verifying parse, construct,
/// and frame size validation behaviors.
/// </summary>
public class Iso8583GatewayAdapterTests
{
    private readonly Iso8583GatewayAdapter _adapter;

    public Iso8583GatewayAdapterTests()
    {
        var logger = NullLogger<Iso8583GatewayAdapter>.Instance;
        _adapter = new Iso8583GatewayAdapter(logger);
    }

    [Fact]
    public void IsValidFrameSize_WithinLimit_ReturnsTrue()
    {
        Assert.True(_adapter.IsValidFrameSize(0));
        Assert.True(_adapter.IsValidFrameSize(100));
        Assert.True(_adapter.IsValidFrameSize(9999));
    }

    [Fact]
    public void IsValidFrameSize_ExceedsLimit_ReturnsFalse()
    {
        Assert.False(_adapter.IsValidFrameSize(10000));
        Assert.False(_adapter.IsValidFrameSize(50000));
    }

    [Fact]
    public void IsValidFrameSize_Negative_ReturnsFalse()
    {
        Assert.False(_adapter.IsValidFrameSize(-1));
    }

    [Fact]
    public void Parse_OversizedMessage_ReturnsFailureWithCode30()
    {
        // Arrange - message exceeding 9999 bytes
        var oversizedData = new byte[10000];
        Array.Fill(oversizedData, (byte)0x41);

        // Act
        var result = _adapter.Parse(oversizedData);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public void Parse_MalformedMessage_ReturnsFailureWithCode96()
    {
        // Arrange - random garbage bytes that are not valid ISO 8583
        var malformedData = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB, 0xFA };

        // Act
        var result = _adapter.Parse(malformedData);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("96", result.ErrorCode);
    }

    [Fact]
    public void Parse_EmptyMessage_ReturnsFailureWithCode96()
    {
        // Act
        var result = _adapter.Parse(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("96", result.ErrorCode);
    }

    [Fact]
    public void Construct_ValidMessage_ReturnsSuccessWithBytes()
    {
        // Arrange
        var message = new Application.DTOs.Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = "4111111111111111",
                [3] = "000000",
                [11] = "123456",
                [41] = "TERM0001",
                [49] = "566"
            }
        };

        // Act
        var result = _adapter.Construct(message);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.True(result.Value.Length > 0);
        Assert.True(result.Value.Length <= 9999);
    }

    [Fact]
    public void Construct_InvalidMti_ReturnsFailure()
    {
        // Arrange
        var message = new Application.DTOs.Iso8583Message
        {
            Mti = "ZZZZ", // Not valid hex
            Fields = new Dictionary<int, string>
            {
                [3] = "000000"
            }
        };

        // Act
        var result = _adapter.Construct(message);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public void Construct_ThenParse_RoundTrip_PreservesFields()
    {
        // Round-trip through the adapter (which has the full parse maps)
        var originalMessage = new Application.DTOs.Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string>
            {
                [11] = "123456",
                [70] = "001"
            }
        };

        var constructResult = _adapter.Construct(originalMessage);
        Assert.True(constructResult.IsSuccess, $"Construct failed: {constructResult.ErrorMessage}");

        var parseResult = _adapter.Parse(constructResult.Value!);
        Assert.True(parseResult.IsSuccess, $"Parse failed: {parseResult.ErrorMessage}");

        var parsed = parseResult.Value!;
        Assert.Equal("0800", parsed.Mti);
        Assert.Equal("123456", parsed.Fields[11]);
        Assert.Equal("001", parsed.Fields[70]);
    }

    [Fact]
    public void Construct_NetworkManagementMessage_Succeeds()
    {
        // Arrange - network management message (0800)
        var message = new Application.DTOs.Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string>
            {
                [11] = "000001",
                [70] = "001"  // Sign-on
            }
        };

        // Act
        var result = _adapter.Construct(message);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public void Parse_ValidConstructedMessage_ExtractsMti()
    {
        // Arrange - construct a valid network mgmt response
        var message = new Application.DTOs.Iso8583Message
        {
            Mti = "0810",
            Fields = new Dictionary<int, string>
            {
                [11] = "654321",
                [39] = "00",
                [70] = "001"
            }
        };

        var constructResult = _adapter.Construct(message);
        Assert.True(constructResult.IsSuccess, $"Construct failed: {constructResult.ErrorMessage}");

        // Act
        var parseResult = _adapter.Parse(constructResult.Value!);

        // Assert
        Assert.True(parseResult.IsSuccess, $"Parse failed: {parseResult.ErrorMessage}");
        Assert.Equal("0810", parseResult.Value!.Mti);
    }
}
