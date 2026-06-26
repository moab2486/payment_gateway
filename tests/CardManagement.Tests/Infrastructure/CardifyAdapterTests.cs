using System.IO.Pipelines;
using System.Security.Cryptography;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Channels.CardSwitch;
using CardManagement.Infrastructure.Resilience;
using CardManagement.Infrastructure.Security;
using CardScheme = CardManagement.Infrastructure.Channels.CardSwitch.CardScheme;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class CardifyAdapterTests
{
    private static CardifyOptions DefaultOptions() => new()
    {
        Host = "switch.cardify.test",
        Port = 6043,
        TimeoutMs = 30_000,
        ReconnectDelayMs = 5_000,
        TlsEnabled = true,
        TerminalId = "CARD0001",
        MerchantId = "CMID00000001"
    };

    private static PaymentRequest CreateVisaRequest(
        string sourceAccount = "4111111111111111",
        string destinationAccount = "merchant-account-001",
        long amountKobo = 500_000,
        string currency = "NGN")
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: "TXN-REF-VISA-20240101-ABCDEF",
            transactionType: PaymentTransactionType.CardAuthorization,
            amount: new Money(amountKobo, currency),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.Cardify);
    }

    private static PaymentRequest CreateMastercardRequest(
        string sourceAccount = "5500000000000004",
        string destinationAccount = "merchant-account-001",
        long amountKobo = 750_000,
        string currency = "NGN")
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: "TXN-REF-MC-20240101-GHIJKL",
            transactionType: PaymentTransactionType.CardAuthorization,
            amount: new Money(amountKobo, currency),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.Cardify);
    }

    private static CardifyAdapter CreateAdapter(
        FakeOutboundConnectionPool? pool = null,
        FakeIso8583Gateway? gateway = null,
        FakeCircuitBreakerRegistry? circuitBreakerRegistry = null,
        CardifyOptions? options = null,
        PciBoundary? pciBoundary = null)
    {
        return new CardifyAdapter(
            pool ?? new FakeOutboundConnectionPool(),
            gateway ?? new FakeIso8583Gateway(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            pciBoundary ?? CreateDefaultPciBoundary(),
            Options.Create(options ?? DefaultOptions()),
            NullLogger<CardifyAdapter>.Instance);
    }

    private static PciBoundary CreateDefaultPciBoundary()
    {
        var (boundary, _) = CreatePciBoundaryWithVault();
        return boundary;
    }

    private static (PciBoundary Boundary, TokenVault Vault) CreatePciBoundaryWithVault()
    {
        var encryptionKey = new byte[32];
        Random.Shared.NextBytes(encryptionKey);
        var pciOptions = Options.Create(new PciSecurityOptions
        {
            EncryptionKey = Convert.ToBase64String(encryptionKey),
            AllowedServices = new List<string> { "InterswitchAdapter", "CardifyAdapter" }
        });
        var tokenVault = new TokenVault();
        var allowlist = new ServiceAllowlist(pciOptions);
        var auditStore = new InMemoryAuditStore();
        var boundary = new PciBoundary(tokenVault, allowlist, auditStore, pciOptions);
        return (boundary, tokenVault);
    }

    private static async Task<(CardifyAdapter Adapter, string Token)> CreateAdapterWithTokenizedPan(
        string pan,
        string correlationId = "test-correlation",
        FakeOutboundConnectionPool? pool = null,
        FakeIso8583Gateway? gateway = null,
        FakeCircuitBreakerRegistry? circuitBreakerRegistry = null,
        CardifyOptions? options = null)
    {
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync(pan, null, correlationId, CancellationToken.None);
        var adapter = CreateAdapter(
            pool: pool,
            gateway: gateway,
            circuitBreakerRegistry: circuitBreakerRegistry,
            options: options,
            pciBoundary: boundary);
        return (adapter, token);
    }

    #region 1. Visa authorization request format

    [Fact]
    public void BuildAuthorizationMessage_Visa_SetsCorrectMti()
    {
        var adapter = CreateAdapter();
        var request = CreateVisaRequest();

        var message = adapter.BuildAuthorizationMessage(request, "4111111111111111", CardScheme.Visa);

        Assert.Equal("0100", message.Mti);
    }

    [Fact]
    public void BuildAuthorizationMessage_Visa_UsesVisaProcessingCode()
    {
        var adapter = CreateAdapter();
        var request = CreateVisaRequest();

        var message = adapter.BuildAuthorizationMessage(request, "4111111111111111", CardScheme.Visa);

        Assert.True(message.Fields.ContainsKey(3));
        Assert.Equal("003000", message.Fields[3]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Visa_SetsVisaNetworkId()
    {
        var adapter = CreateAdapter();
        var request = CreateVisaRequest();

        var message = adapter.BuildAuthorizationMessage(request, "4111111111111111", CardScheme.Visa);

        Assert.True(message.Fields.ContainsKey(24));
        Assert.Equal("0002", message.Fields[24]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Visa_SetsPosEntryMode051()
    {
        var adapter = CreateAdapter();
        var request = CreateVisaRequest();

        var message = adapter.BuildAuthorizationMessage(request, "4111111111111111", CardScheme.Visa);

        Assert.True(message.Fields.ContainsKey(22));
        Assert.Equal("051", message.Fields[22]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Visa_IncludesPanAndAmount()
    {
        var adapter = CreateAdapter();
        var request = CreateVisaRequest(amountKobo: 1_000_000);

        var message = adapter.BuildAuthorizationMessage(request, "4111111111111111", CardScheme.Visa);

        Assert.Equal("4111111111111111", message.Fields[2]);
        Assert.Equal("000001000000", message.Fields[4]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Visa_IncludesTerminalAndMerchantIds()
    {
        var options = DefaultOptions();
        options.TerminalId = "VTERM001";
        options.MerchantId = "VMERCH0001";
        var adapter = CreateAdapter(options: options);
        var request = CreateVisaRequest();

        var message = adapter.BuildAuthorizationMessage(request, "4111111111111111", CardScheme.Visa);

        Assert.Equal("VTERM001", message.Fields[41]);
        Assert.Equal("VMERCH0001", message.Fields[42]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Visa_IncludesCurrencyCode()
    {
        var adapter = CreateAdapter();
        var request = CreateVisaRequest(currency: "USD");

        var message = adapter.BuildAuthorizationMessage(request, "4111111111111111", CardScheme.Visa);

        Assert.Equal("840", message.Fields[49]);
    }

    #endregion

    #region 2. Mastercard authorization request format

    [Fact]
    public void BuildAuthorizationMessage_Mastercard_SetsCorrectMti()
    {
        var adapter = CreateAdapter();
        var request = CreateMastercardRequest();

        var message = adapter.BuildAuthorizationMessage(request, "5500000000000004", CardScheme.Mastercard);

        Assert.Equal("0100", message.Mti);
    }

    [Fact]
    public void BuildAuthorizationMessage_Mastercard_UsesMastercardProcessingCode()
    {
        var adapter = CreateAdapter();
        var request = CreateMastercardRequest();

        var message = adapter.BuildAuthorizationMessage(request, "5500000000000004", CardScheme.Mastercard);

        Assert.True(message.Fields.ContainsKey(3));
        Assert.Equal("000000", message.Fields[3]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Mastercard_SetsMastercardNetworkId()
    {
        var adapter = CreateAdapter();
        var request = CreateMastercardRequest();

        var message = adapter.BuildAuthorizationMessage(request, "5500000000000004", CardScheme.Mastercard);

        Assert.True(message.Fields.ContainsKey(24));
        Assert.Equal("0005", message.Fields[24]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Mastercard_SetsPosEntryMode071()
    {
        var adapter = CreateAdapter();
        var request = CreateMastercardRequest();

        var message = adapter.BuildAuthorizationMessage(request, "5500000000000004", CardScheme.Mastercard);

        Assert.True(message.Fields.ContainsKey(22));
        Assert.Equal("071", message.Fields[22]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Mastercard_IncludesPanAndAmount()
    {
        var adapter = CreateAdapter();
        var request = CreateMastercardRequest(amountKobo: 2_500_000);

        var message = adapter.BuildAuthorizationMessage(request, "5500000000000004", CardScheme.Mastercard);

        Assert.Equal("5500000000000004", message.Fields[2]);
        Assert.Equal("000002500000", message.Fields[4]);
    }

    [Fact]
    public void BuildAuthorizationMessage_Mastercard_2xxxRange_DetectsCorrectly()
    {
        var adapter = CreateAdapter();
        var request = CreateMastercardRequest(sourceAccount: "2221000000000009");

        var message = adapter.BuildAuthorizationMessage(request, "2221000000000009", CardScheme.Mastercard);

        Assert.Equal("000000", message.Fields[3]);
        Assert.Equal("0005", message.Fields[24]);
    }

    #endregion

    #region 3. Response parsing

    [Fact]
    public async Task ProcessAsync_ApprovedResponse_ExtractsAuthCode()
    {
        var gateway = new FakeIso8583Gateway(responseCode: "00", authCode: "VIS789");
        var pool = new FakeOutboundConnectionPool();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "visa-auth", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("VIS789", result.ProcessorReference);
    }

    [Fact]
    public async Task ProcessAsync_DeclinedResponse_ReturnsErrorCode()
    {
        var gateway = new FakeIso8583Gateway(responseCode: "51");
        var pool = new FakeOutboundConnectionPool();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("5500000000000004", null, "mc-decline", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateMastercardRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("51", result.ErrorCode);
        Assert.Contains("Insufficient funds", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_ExpiredCard_ReturnsCorrectErrorMessage()
    {
        var gateway = new FakeIso8583Gateway(responseCode: "54");
        var pool = new FakeOutboundConnectionPool();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "expired-test", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("54", result.ErrorCode);
        Assert.Contains("Expired card", result.ErrorMessage);
    }

    #endregion

    #region 4. Capture and reversal

    [Fact]
    public void BuildCaptureMessage_SetsMti0220()
    {
        var adapter = CreateAdapter();
        var captureRequest = new CaptureRequest
        {
            Pan = "4111111111111111",
            Amount = 500_000,
            TransactionReference = "TXN-CAP-001",
            OriginalAuthCode = "ABC123",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest, "4111111111111111", CardScheme.Visa);

        Assert.Equal("0220", message.Mti);
    }

    [Fact]
    public void BuildCaptureMessage_Visa_UsesVisaProcessingCode()
    {
        var adapter = CreateAdapter();
        var captureRequest = new CaptureRequest
        {
            Pan = "4111111111111111",
            Amount = 500_000,
            TransactionReference = "TXN-CAP-002",
            OriginalAuthCode = "AUTH01",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest, "4111111111111111", CardScheme.Visa);

        Assert.Equal("003000", message.Fields[3]);
        Assert.Equal("0002", message.Fields[24]);
    }

    [Fact]
    public void BuildCaptureMessage_Mastercard_UsesMastercardProcessingCode()
    {
        var adapter = CreateAdapter();
        var captureRequest = new CaptureRequest
        {
            Pan = "5500000000000004",
            Amount = 750_000,
            TransactionReference = "TXN-CAP-003",
            OriginalAuthCode = "MC0001",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest, "5500000000000004", CardScheme.Mastercard);

        Assert.Equal("000000", message.Fields[3]);
        Assert.Equal("0005", message.Fields[24]);
    }

    [Fact]
    public void BuildCaptureMessage_IncludesOriginalAuthCode()
    {
        var adapter = CreateAdapter();
        var captureRequest = new CaptureRequest
        {
            Pan = "4111111111111111",
            Amount = 500_000,
            TransactionReference = "TXN-CAP-004",
            OriginalAuthCode = "XYZ789",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest, "4111111111111111", CardScheme.Visa);

        Assert.True(message.Fields.ContainsKey(38));
        Assert.Equal("XYZ789", message.Fields[38]);
    }

    [Fact]
    public void BuildReversalMessage_SetsMti0400()
    {
        var adapter = CreateAdapter();

        var message = adapter.BuildReversalMessage("TXN-REV-001");

        Assert.Equal("0400", message.Mti);
    }

    [Fact]
    public void BuildReversalMessage_WithOriginalData_IncludesField90()
    {
        var adapter = CreateAdapter();
        var originalData = new OriginalAuthData
        {
            OriginalMti = "0100",
            OriginalStan = "000123",
            OriginalDateTime = "0115143022",
            AcquiringInstitutionId = "12345678901"
        };

        var message = adapter.BuildReversalMessage("TXN-REV-002", originalData);

        Assert.True(message.Fields.ContainsKey(90));
        var field90 = message.Fields[90];
        Assert.Equal(31, field90.Length);
        Assert.StartsWith("0100", field90);
        Assert.Contains("000123", field90);
    }

    [Fact]
    public void BuildReversalMessage_WithoutOriginalData_OmitsField90()
    {
        var adapter = CreateAdapter();

        var message = adapter.BuildReversalMessage("TXN-REV-003", null);

        Assert.False(message.Fields.ContainsKey(90));
    }

    [Fact]
    public async Task CaptureAsync_SendsMessageViaConnectionPool()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "cap-test", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var captureRequest = new CaptureRequest
        {
            Pan = token,
            Amount = 500_000,
            TransactionReference = "TXN-CAP-005",
            OriginalAuthCode = "AUTH05",
            CurrencyCode = "566"
        };

        var result = await adapter.CaptureAsync(captureRequest, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(pool.GetWriterCalled);
        Assert.True(gateway.ConstructCalled);
    }

    [Fact]
    public async Task ReverseAsync_SendsReversalViaConnectionPool()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var adapter = CreateAdapter(pool: pool, gateway: gateway);

        var result = await adapter.ReverseAsync("TXN-REV-004", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(pool.GetWriterCalled);
        Assert.True(gateway.ConstructCalled);
    }

    [Fact]
    public async Task ReverseAsync_WithOriginalData_SendsSuccessfully()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var adapter = CreateAdapter(pool: pool, gateway: gateway);
        var originalData = new OriginalAuthData
        {
            OriginalMti = "0100",
            OriginalStan = "000456",
            OriginalDateTime = "0215101530",
            AcquiringInstitutionId = "98765432100"
        };

        var result = await adapter.ReverseAsync("TXN-REV-005", originalData, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(pool.GetWriterCalled);
    }

    #endregion

    #region 5. TCP reconnection

    [Fact]
    public async Task ProcessAsync_ConnectionPoolException_RecordsFailure()
    {
        var pool = new FakeOutboundConnectionPool(throwOnAcquire: true);
        var gateway = new FakeIso8583Gateway();
        var registry = new FakeCircuitBreakerRegistry();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "tcp-fail", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, circuitBreakerRegistry: registry, pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_ERROR", result.ErrorCode);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_ConnectionPoolReturnsConnection_ConnectionIsReturned()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "tcp-return", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: token);

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(pool.ReturnCalled);
    }

    [Fact]
    public async Task ProcessAsync_AfterFailure_NextCallUsesConnectionPool()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "tcp-retry", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: token);

        await adapter.ProcessAsync(request, CancellationToken.None);
        Assert.True(pool.GetWriterCalled);

        pool.ResetTracking();

        await adapter.ProcessAsync(request, CancellationToken.None);
        Assert.True(pool.GetWriterCalled);
    }

    #endregion

    #region 6. PCI boundary integration

    [Fact]
    public async Task ProcessAsync_DetokenizesBeforeMessageConstruction()
    {
        var gateway = new FakeIso8583Gateway(responseCode: "00", authCode: "PCI001");
        var pool = new FakeOutboundConnectionPool();
        var (boundary, _) = CreatePciBoundaryWithVault();
        // Tokenize a Visa PAN
        var token = await boundary.TokenizeAsync("4222222222222222", null, "pci-test", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        // Should succeed - PCI boundary detokenized correctly
        Assert.True(result.Success);
        Assert.Equal("PCI001", result.ProcessorReference);
    }

    [Fact]
    public async Task ProcessAsync_InvalidToken_ReturnsInvalidTokenError()
    {
        var gateway = new FakeIso8583Gateway();
        var pool = new FakeOutboundConnectionPool();
        var (boundary, _) = CreatePciBoundaryWithVault();
        // Use a token that was never registered
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: "nonexistent-token-value");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INVALID_TOKEN", result.ErrorCode);
    }

    [Fact]
    public void PciBoundary_MaskPan_ShowsOnlyLast4Digits()
    {
        var maskedVisa = PciBoundary.MaskPan("4111111111111111");
        var maskedMc = PciBoundary.MaskPan("5500000000000004");

        Assert.Equal("************1111", maskedVisa);
        Assert.Equal("************0004", maskedMc);
        Assert.DoesNotContain("41111111", maskedVisa);
        Assert.DoesNotContain("55000000", maskedMc);
    }

    [Fact]
    public async Task ProcessAsync_UnauthorizedService_ReturnsPciAccessDenied()
    {
        var encryptionKey = new byte[32];
        Random.Shared.NextBytes(encryptionKey);
        // Create PCI boundary that does NOT allow CardifyAdapter
        var pciOptions = Options.Create(new PciSecurityOptions
        {
            EncryptionKey = Convert.ToBase64String(encryptionKey),
            AllowedServices = new List<string> { "OtherServiceOnly" }
        });
        var tokenVault = new TokenVault();
        var allowlist = new ServiceAllowlist(pciOptions);
        var auditStore = new InMemoryAuditStore();
        var boundary = new PciBoundary(tokenVault, allowlist, auditStore, pciOptions);

        // Tokenize a PAN (tokenization doesn't require allowlist)
        var token = await boundary.TokenizeAsync("4111111111111111", null, "unauth-test", CancellationToken.None);

        var adapter = CreateAdapter(pciBoundary: boundary);
        var request = CreateVisaRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("PCI_ACCESS_DENIED", result.ErrorCode);
    }

    #endregion

    #region 7. Circuit breaker integration

    [Fact]
    public async Task ProcessAsync_CircuitBreakerOpen_ReturnsUnavailableWithoutSending()
    {
        var pool = new FakeOutboundConnectionPool();
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateAdapter(pool: pool, circuitBreakerRegistry: registry);
        var request = CreateVisaRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
        Assert.False(pool.GetWriterCalled);
    }

    [Fact]
    public async Task CaptureAsync_CircuitBreakerOpen_ReturnsUnavailable()
    {
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateAdapter(circuitBreakerRegistry: registry);
        var captureRequest = new CaptureRequest
        {
            Pan = "4111111111111111",
            Amount = 500_000,
            TransactionReference = "TXN-CB-CAP-001",
            OriginalAuthCode = "AUTH99",
            CurrencyCode = "566"
        };

        var result = await adapter.CaptureAsync(captureRequest, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task ReverseAsync_CircuitBreakerOpen_ReturnsUnavailable()
    {
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateAdapter(circuitBreakerRegistry: registry);

        var result = await adapter.ReverseAsync("TXN-CB-REV-001", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task CheckHealthAsync_CircuitBreakerClosed_ReturnsHealthy()
    {
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Closed);
        var adapter = CreateAdapter(circuitBreakerRegistry: registry);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        Assert.True(health.IsHealthy);
        Assert.Contains("Closed", health.Details);
    }

    [Fact]
    public async Task CheckHealthAsync_CircuitBreakerOpen_ReturnsUnhealthy()
    {
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateAdapter(circuitBreakerRegistry: registry);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        Assert.False(health.IsHealthy);
        Assert.Contains("Open", health.Details);
    }

    [Fact]
    public void Channel_ReturnsCardify()
    {
        var adapter = CreateAdapter();
        Assert.Equal(PaymentChannel.Cardify, adapter.Channel);
    }

    #endregion

    #region Card Scheme Detection

    [Theory]
    [InlineData("4111111111111111", CardScheme.Visa)]
    [InlineData("4000000000000002", CardScheme.Visa)]
    [InlineData("5100000000000000", CardScheme.Mastercard)]
    [InlineData("5500000000000004", CardScheme.Mastercard)]
    [InlineData("2221000000000009", CardScheme.Mastercard)]
    [InlineData("2720000000000005", CardScheme.Mastercard)]
    [InlineData("6011000000000000", CardScheme.Unknown)]
    public void DetectCardScheme_ReturnsCorrectScheme(string pan, CardScheme expected)
    {
        var result = CardifyAdapter.DetectCardScheme(pan);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Test Helpers

    private class FakeOutboundConnectionPool : IOutboundConnectionPool
    {
        private readonly bool _throwOnAcquire;
        public bool GetWriterCalled { get; private set; }
        public bool ReturnCalled { get; private set; }

        public FakeOutboundConnectionPool(bool throwOnAcquire = false)
        {
            _throwOnAcquire = throwOnAcquire;
        }

        public Task<PipeWriter> GetWriterAsync(ProcessorEndpoint endpoint, CancellationToken ct)
        {
            GetWriterCalled = true;
            if (_throwOnAcquire)
                throw new InvalidOperationException("Connection failed - simulating TCP disconnection");
            var pipe = new Pipe();
            return Task.FromResult(pipe.Writer);
        }

        public Task ReturnAsync(ProcessorEndpoint endpoint, PipeWriter writer)
        {
            ReturnCalled = true;
            return Task.CompletedTask;
        }

        public Task<IPooledConnection> AcquireConnectionAsync(ProcessorEndpoint endpoint, bool useTls, CancellationToken ct)
        {
            GetWriterCalled = true;
            if (_throwOnAcquire)
                throw new InvalidOperationException("Connection failed - simulating TCP disconnection");
            return Task.FromResult<IPooledConnection>(new FakePooledConnection());
        }

        public Task ReleaseConnectionAsync(ProcessorEndpoint endpoint, IPooledConnection connection)
        {
            ReturnCalled = true;
            return Task.CompletedTask;
        }

        public void ResetTracking()
        {
            GetWriterCalled = false;
            ReturnCalled = false;
        }
    }

    private class FakePooledConnection : IPooledConnection
    {
        private readonly FakeResponseStream _stream;

        public FakePooledConnection()
        {
            var responseBody = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var framedResponse = new byte[2 + responseBody.Length];
            framedResponse[0] = (byte)(responseBody.Length >> 8);
            framedResponse[1] = (byte)(responseBody.Length & 0xFF);
            Buffer.BlockCopy(responseBody, 0, framedResponse, 2, responseBody.Length);
            _stream = new FakeResponseStream(framedResponse);
        }

        public Stream Stream => _stream;
        public bool IsConnected => true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class FakeResponseStream : MemoryStream
    {
        private readonly byte[] _responseData;
        private int _readPosition;

        public FakeResponseStream(byte[] responseData) : base()
        {
            _responseData = responseData;
            _readPosition = 0;
        }

        public override void Write(byte[] buffer, int offset, int count) { }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.CompletedTask;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => ValueTask.CompletedTask;
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var available = _responseData.Length - _readPosition;
            if (available <= 0) return 0;
            var toRead = Math.Min(count, available);
            Buffer.BlockCopy(_responseData, _readPosition, buffer, offset, toRead);
            _readPosition += toRead;
            return toRead;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var available = _responseData.Length - _readPosition;
            if (available <= 0) return ValueTask.FromResult(0);
            var toRead = Math.Min(buffer.Length, available);
            _responseData.AsSpan(_readPosition, toRead).CopyTo(buffer.Span);
            _readPosition += toRead;
            return ValueTask.FromResult(toRead);
        }

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
    }

    private class FakeIso8583Gateway : IIso8583Gateway
    {
        private readonly string _responseCode;
        private readonly string _authCode;
        public bool ConstructCalled { get; private set; }
        public bool ParseCalled { get; private set; }

        public FakeIso8583Gateway(string? responseCode = null, string? authCode = null)
        {
            _responseCode = responseCode ?? "00";
            _authCode = authCode ?? "AUTH01";
        }

        public Result<Iso8583Message> Parse(ReadOnlySpan<byte> rawMessage)
        {
            ParseCalled = true;
            return Result<Iso8583Message>.Success(new Iso8583Message
            {
                Mti = "0110",
                Fields = new Dictionary<int, string>
                {
                    [39] = _responseCode,
                    [38] = _authCode
                }
            });
        }

        public Result<byte[]> Construct(Iso8583Message message)
        {
            ConstructCalled = true;
            return Result<byte[]>.Success(new byte[] { 0x01, 0x02, 0x03, 0x04 });
        }

        public bool IsValidFrameSize(int messageLength)
        {
            return messageLength >= 0 && messageLength <= 9999;
        }
    }

    private class FakeCircuitBreakerRegistry : ICircuitBreakerRegistry
    {
        public FakeCircuitBreaker Breaker { get; }

        public FakeCircuitBreakerRegistry(CircuitBreakerState initialState = CircuitBreakerState.Closed)
        {
            Breaker = new FakeCircuitBreaker(initialState);
        }

        public ICircuitBreaker GetBreaker(PaymentChannel channel) => Breaker;

        public IReadOnlyDictionary<PaymentChannel, CircuitBreakerState> GetAllStates()
        {
            return new Dictionary<PaymentChannel, CircuitBreakerState>
            {
                [PaymentChannel.Cardify] = Breaker.State
            };
        }
    }

    private class FakeCircuitBreaker : ICircuitBreaker
    {
        public CircuitBreakerState State { get; private set; }
        public int SuccessCount { get; private set; }
        public int FailureCount { get; private set; }

        public FakeCircuitBreaker(CircuitBreakerState initialState = CircuitBreakerState.Closed)
        {
            State = initialState;
        }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            if (State == CircuitBreakerState.Open)
                throw new CircuitBreakerOpenException("Circuit breaker is open");
            return action(ct);
        }

        public void RecordSuccess() => SuccessCount++;
        public void RecordFailure() => FailureCount++;
    }

    private class InMemoryAuditStore : IAuditStore
    {
        private readonly List<AuditEntry> _entries = new();

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            var results = _entries.Where(e => e.TransactionReference == transactionReference).ToList();
            return Task.FromResult<IReadOnlyList<AuditEntry>>(results);
        }
    }

    #endregion
}
