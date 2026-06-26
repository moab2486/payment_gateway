using CardManagement.Application.DTOs;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class CardProcessorRouterTests
{
    private static readonly string BinRangesJson = @"[
        {""prefix"": ""506199"", ""scheme"": ""Verve"", ""panLength"": 19},
        {""prefix"": ""650002"", ""scheme"": ""Verve"", ""panLength"": 19},
        {""prefix"": ""4"", ""scheme"": ""Visa"", ""panLength"": 16},
        {""prefix"": ""51"", ""scheme"": ""Mastercard"", ""panLength"": 16},
        {""prefix"": ""52"", ""scheme"": ""Mastercard"", ""panLength"": 16},
        {""prefix"": ""53"", ""scheme"": ""Mastercard"", ""panLength"": 16}
    ]";

    private static CardProcessorRouter CreateRouter(
        string? binRangesJson = null,
        IProcessorSessionRepository? sessionRepository = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CARD__BIN_RANGES"] = binRangesJson ?? BinRangesJson
            })
            .Build();

        return new CardProcessorRouter(config, sessionRepository ?? new InMemoryProcessorSessionRepository());
    }

    [Fact]
    public void ResolveProcessor_VerveBin_ReturnsInterswitch()
    {
        var router = CreateRouter();

        // 506199 prefix, 19 digits total
        var result = router.ResolveProcessor("5061990012345678901");

        Assert.Equal(ProcessorType.Interswitch, result);
    }

    [Fact]
    public void ResolveProcessor_SecondVerveBin_ReturnsInterswitch()
    {
        var router = CreateRouter();

        // 650002 prefix, 19 digits total
        var result = router.ResolveProcessor("6500021234567890123");

        Assert.Equal(ProcessorType.Interswitch, result);
    }

    [Fact]
    public void ResolveProcessor_VisaBin_ReturnsCardFi()
    {
        var router = CreateRouter();

        // Visa: starts with 4, 16 digits
        var result = router.ResolveProcessor("4111111111111111");

        Assert.Equal(ProcessorType.CardFi, result);
    }

    [Fact]
    public void ResolveProcessor_MastercardBin51_ReturnsCardFi()
    {
        var router = CreateRouter();

        // Mastercard: starts with 51, 16 digits
        var result = router.ResolveProcessor("5100123456789012");

        Assert.Equal(ProcessorType.CardFi, result);
    }

    [Fact]
    public void ResolveProcessor_MastercardBin52_ReturnsCardFi()
    {
        var router = CreateRouter();

        // Mastercard: starts with 52, 16 digits
        var result = router.ResolveProcessor("5212345678901234");

        Assert.Equal(ProcessorType.CardFi, result);
    }

    [Fact]
    public void ResolveProcessor_MastercardBin53_ReturnsCardFi()
    {
        var router = CreateRouter();

        // Mastercard: starts with 53, 16 digits
        var result = router.ResolveProcessor("5312345678901234");

        Assert.Equal(ProcessorType.CardFi, result);
    }

    [Fact]
    public void ResolveProcessor_UnrecognizedBin_ThrowsInvalidOperationException()
    {
        var router = CreateRouter();

        // BIN 9999 not in configuration
        var ex = Assert.Throws<InvalidOperationException>(
            () => router.ResolveProcessor("9999123456789012"));

        Assert.Contains("does not match any configured BIN range", ex.Message);
    }

    [Fact]
    public void ResolveProcessor_NullPan_ThrowsInvalidOperationException()
    {
        var router = CreateRouter();

        Assert.Throws<InvalidOperationException>(
            () => router.ResolveProcessor(null!));
    }

    [Fact]
    public void ResolveProcessor_EmptyPan_ThrowsInvalidOperationException()
    {
        var router = CreateRouter();

        Assert.Throws<InvalidOperationException>(
            () => router.ResolveProcessor(""));
    }

    [Fact]
    public void ResolveProcessor_WrongLength_ThrowsInvalidOperationException()
    {
        var router = CreateRouter();

        // Visa prefix "4" expects 16 digits, but providing 19
        var ex = Assert.Throws<InvalidOperationException>(
            () => router.ResolveProcessor("4111111111111111111"));

        Assert.Contains("does not match any configured BIN range", ex.Message);
    }

    [Fact]
    public void ResolveProcessor_NoBinRangesConfigured_ThrowsInvalidOperationException()
    {
        var router = CreateRouter(binRangesJson: "[]");

        Assert.Throws<InvalidOperationException>(
            () => router.ResolveProcessor("4111111111111111"));
    }

    [Fact]
    public void IsSignedOn_InitialState_ReturnsFalse()
    {
        var router = CreateRouter();

        Assert.False(router.IsSignedOn(ProcessorType.Interswitch));
        Assert.False(router.IsSignedOn(ProcessorType.CardFi));
    }

    [Fact]
    public async Task ProcessNetworkManagement_SignOn_SetsSignedOnTrue()
    {
        var sessionRepo = new InMemoryProcessorSessionRepository();
        var router = CreateRouter(sessionRepository: sessionRepo);

        var signOnMessage = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "001" }
        };

        var result = await router.ProcessNetworkManagement(signOnMessage, ProcessorType.Interswitch);

        Assert.True(result.IsSuccess);
        Assert.True(router.IsSignedOn(ProcessorType.Interswitch));
        Assert.False(router.IsSignedOn(ProcessorType.CardFi)); // Other processor unchanged
    }

    [Fact]
    public async Task ProcessNetworkManagement_EchoTest_Succeeds()
    {
        var router = CreateRouter();

        var echoMessage = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "301" }
        };

        var result = await router.ProcessNetworkManagement(echoMessage, ProcessorType.CardFi);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ProcessNetworkManagement_KeyExchange_Succeeds()
    {
        var router = CreateRouter();

        var keyExchangeMessage = new Iso8583Message
        {
            Mti = "0800",
            Fields = new Dictionary<int, string> { [70] = "161" }
        };

        var result = await router.ProcessNetworkManagement(keyExchangeMessage, ProcessorType.Interswitch);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ProcessNetworkManagement_NonNetworkMti_ReturnsFailure()
    {
        var router = CreateRouter();

        var authMessage = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>()
        };

        var result = await router.ProcessNetworkManagement(authMessage, ProcessorType.Interswitch);

        Assert.False(result.IsSuccess);
        Assert.Contains("Unsupported network management MTI", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessNetworkManagement_NullMessage_ReturnsFailure()
    {
        var router = CreateRouter();

        var result = await router.ProcessNetworkManagement(null!, ProcessorType.Interswitch);

        Assert.False(result.IsSuccess);
    }

    /// <summary>
    /// In-memory implementation of IProcessorSessionRepository for testing purposes.
    /// </summary>
    private class InMemoryProcessorSessionRepository : IProcessorSessionRepository
    {
        private readonly Dictionary<ProcessorType, ProcessorSession> _sessions = new();

        public Task<ProcessorSession?> GetByTypeAsync(ProcessorType processorType, CancellationToken cancellationToken = default)
        {
            _sessions.TryGetValue(processorType, out var session);
            return Task.FromResult(session);
        }

        public Task UpdateAsync(ProcessorSession session, CancellationToken cancellationToken = default)
        {
            _sessions[session.ProcessorType] = session;
            return Task.CompletedTask;
        }
    }
}
