using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class NetworkManagementHandlerTests
{
    private static NetworkManagementHandler CreateHandler(
        ICardProcessorRouter? router = null)
    {
        var logger = NullLogger<NetworkManagementHandler>.Instance;
        return new NetworkManagementHandler(
            router ?? new FakeCardProcessorRouter(),
            logger);
    }

    #region HandleNetworkManagementAsync Tests

    [Fact]
    public async Task HandleNetworkManagementAsync_SignOn_ReturnsApprovedResponse()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        };

        var result = await handler.HandleNetworkManagementAsync(request, ProcessorType.Interswitch);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("0810", result.Value!.Mti);
        Assert.Equal("00", result.Value.Fields[39]);
    }

    [Fact]
    public async Task HandleNetworkManagementAsync_SignOn_SetsProcessorSignedOn()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        };

        await handler.HandleNetworkManagementAsync(request, ProcessorType.Interswitch);

        Assert.True(router.IsSignedOn(ProcessorType.Interswitch));
    }

    [Fact]
    public async Task HandleNetworkManagementAsync_EchoTest_ReturnsApprovedResponse()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "301" }
        };

        var result = await handler.HandleNetworkManagementAsync(request, ProcessorType.CardFi);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("0810", result.Value!.Mti);
        Assert.Equal("00", result.Value.Fields[39]);
    }

    [Fact]
    public async Task HandleNetworkManagementAsync_KeyExchange_ReturnsApprovedResponse()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "161" }
        };

        var result = await handler.HandleNetworkManagementAsync(request, ProcessorType.Interswitch);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("0810", result.Value!.Mti);
        Assert.Equal("00", result.Value.Fields[39]);
    }

    [Fact]
    public async Task HandleNetworkManagementAsync_EchoesRequestFieldsInResponse()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string>
            {
                [7] = "0101120000",   // Transmission date/time
                [11] = "123456",      // STAN
                [70] = "001"          // Function code
            }
        };

        var result = await handler.HandleNetworkManagementAsync(request, ProcessorType.Interswitch);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal("0101120000", response.Fields[7]);
        Assert.Equal("123456", response.Fields[11]);
        Assert.Equal("001", response.Fields[70]);
    }

    [Fact]
    public async Task HandleNetworkManagementAsync_NonNetworkMti_ReturnsFailure()
    {
        var handler = CreateHandler();

        var request = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>()
        };

        var result = await handler.HandleNetworkManagementAsync(request, ProcessorType.Interswitch);

        Assert.False(result.IsSuccess);
        Assert.Contains("0800", result.ErrorMessage!);
        Assert.Equal("UNSUPPORTED_MTI", result.ErrorCode);
    }

    [Fact]
    public async Task HandleNetworkManagementAsync_NullMessage_ReturnsFailure()
    {
        var handler = CreateHandler();

        var result = await handler.HandleNetworkManagementAsync(null!, ProcessorType.Interswitch);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_MESSAGE", result.ErrorCode);
    }

    [Fact]
    public async Task HandleNetworkManagementAsync_CardFiSignOn_ReturnsApprovedResponse()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        };

        var result = await handler.HandleNetworkManagementAsync(request, ProcessorType.CardFi);

        Assert.True(result.IsSuccess);
        Assert.Equal("0810", result.Value!.Mti);
        Assert.Equal("00", result.Value.Fields[39]);
        Assert.True(router.IsSignedOn(ProcessorType.CardFi));
    }

    #endregion

    #region ValidateFinancialMessageAllowed Tests

    [Fact]
    public void ValidateFinancialMessageAllowed_NotSignedOn_AuthRequest_ReturnsFailure()
    {
        var router = new FakeCardProcessorRouter(); // Not signed on by default
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>()
        };

        var result = handler.ValidateFinancialMessageAllowed(request, ProcessorType.Interswitch);

        Assert.False(result.IsSuccess);
        Assert.Equal("SIGN_ON_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public void ValidateFinancialMessageAllowed_NotSignedOn_ReversalRequest_ReturnsFailure()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>()
        };

        var result = handler.ValidateFinancialMessageAllowed(request, ProcessorType.CardFi);

        Assert.False(result.IsSuccess);
        Assert.Equal("SIGN_ON_REQUIRED", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateFinancialMessageAllowed_SignedOn_AuthRequest_ReturnsSuccess()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        // Perform sign-on first
        var signOnMsg = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        };
        await handler.HandleNetworkManagementAsync(signOnMsg, ProcessorType.Interswitch);

        // Now try a financial message
        var request = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>()
        };

        var result = handler.ValidateFinancialMessageAllowed(request, ProcessorType.Interswitch);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ValidateFinancialMessageAllowed_SignedOn_ReversalRequest_ReturnsSuccess()
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        // Perform sign-on first
        var signOnMsg = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        };
        await handler.HandleNetworkManagementAsync(signOnMsg, ProcessorType.CardFi);

        var request = new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>()
        };

        var result = handler.ValidateFinancialMessageAllowed(request, ProcessorType.CardFi);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateFinancialMessageAllowed_NetworkManagementMessage_AlwaysAllowed()
    {
        var router = new FakeCardProcessorRouter(); // Not signed on
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        };

        // Network management messages should not be blocked even when not signed on
        var result = handler.ValidateFinancialMessageAllowed(request, ProcessorType.Interswitch);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateFinancialMessageAllowed_NullMessage_ReturnsFailure()
    {
        var handler = CreateHandler();

        var result = handler.ValidateFinancialMessageAllowed(null!, ProcessorType.Interswitch);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_MESSAGE", result.ErrorCode);
    }

    [Fact]
    public void ValidateFinancialMessageAllowed_OneProcessorSignedOn_OtherStillBlocked()
    {
        var router = new FakeCardProcessorRouter();
        router.ForceSignOn(ProcessorType.Interswitch);
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>()
        };

        // Interswitch is signed on — allowed
        var interswitchResult = handler.ValidateFinancialMessageAllowed(request, ProcessorType.Interswitch);
        Assert.True(interswitchResult.IsSuccess);

        // CardFi is NOT signed on — blocked
        var cardFiResult = handler.ValidateFinancialMessageAllowed(request, ProcessorType.CardFi);
        Assert.False(cardFiResult.IsSuccess);
        Assert.Equal("SIGN_ON_REQUIRED", cardFiResult.ErrorCode);
    }

    [Theory]
    [InlineData("0110")] // Authorization response
    [InlineData("0430")] // Reversal response
    public void ValidateFinancialMessageAllowed_ResponseMtis_BlockedWhenNotSignedOn(string mti)
    {
        var router = new FakeCardProcessorRouter();
        var handler = CreateHandler(router);

        var request = new Iso8583Message
        {
            Mti = mti,
            Fields = new Dictionary<int, string>()
        };

        var result = handler.ValidateFinancialMessageAllowed(request, ProcessorType.Interswitch);

        Assert.False(result.IsSuccess);
        Assert.Equal("SIGN_ON_REQUIRED", result.ErrorCode);
    }

    #endregion

    #region Fake Implementations

    /// <summary>
    /// Fake ICardProcessorRouter for unit testing the NetworkManagementHandler in isolation.
    /// </summary>
    private class FakeCardProcessorRouter : ICardProcessorRouter
    {
        private readonly Dictionary<ProcessorType, bool> _signOnState = new()
        {
            [ProcessorType.Interswitch] = false,
            [ProcessorType.CardFi] = false
        };

        public ProcessorType ResolveProcessor(string pan)
        {
            // Simple stub: not needed for network management tests
            throw new NotImplementedException();
        }

        public bool IsSignedOn(ProcessorType processor)
        {
            return _signOnState.TryGetValue(processor, out var signedOn) && signedOn;
        }

        public Task<Result> ProcessNetworkManagement(Iso8583Message message, ProcessorType processor)
        {
            var functionCode = message.Fields.TryGetValue(70, out var code) ? code : null;

            switch (functionCode)
            {
                case "001": // Sign-on
                    _signOnState[processor] = true;
                    return Task.FromResult(Result.Success());
                case "301": // Echo test
                    return Task.FromResult(Result.Success());
                case "161": // Key exchange
                    return Task.FromResult(Result.Success());
                default:
                    _signOnState[processor] = true;
                    return Task.FromResult(Result.Success());
            }
        }

        /// <summary>
        /// Helper to force sign-on state for testing.
        /// </summary>
        public void ForceSignOn(ProcessorType processor)
        {
            _signOnState[processor] = true;
        }
    }

    #endregion
}
