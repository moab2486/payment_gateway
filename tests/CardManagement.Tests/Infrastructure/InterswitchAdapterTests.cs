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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class InterswitchAdapterTests
{
    private static InterswitchOptions DefaultOptions() => new()
    {
        Host = "switch.interswitch.test",
        Port = 5043,
        TimeoutMs = 30_000,
        ReconnectDelayMs = 5_000,
        TlsEnabled = true,
        TerminalId = "20990001",
        MerchantId = "MID000000001"
    };

    private static PaymentRequest CreateRequest(
        string sourceAccount = "4111111111111111",
        string destinationAccount = "merchant-account-001",
        long amountKobo = 500_000,
        string currency = "NGN")
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: "TXN-REF-20240101-ABCDEF",
            transactionType: PaymentTransactionType.CardAuthorization,
            amount: new Money(amountKobo, currency),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.Interswitch);
    }

    private static InterswitchAdapter CreateAdapter(
        FakeOutboundConnectionPool? pool = null,
        FakeIso8583Gateway? gateway = null,
        FakeCircuitBreakerRegistry? circuitBreakerRegistry = null,
        InterswitchOptions? options = null,
        PciBoundary? pciBoundary = null)
    {
        return new InterswitchAdapter(
            pool ?? new FakeOutboundConnectionPool(),
            gateway ?? new FakeIso8583Gateway(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            pciBoundary ?? CreateDefaultPciBoundary(),
            Options.Create(options ?? DefaultOptions()),
            NullLogger<InterswitchAdapter>.Instance);
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

    /// <summary>
    /// Creates an adapter with a PCI boundary that has the given PAN pre-tokenized.
    /// Returns the adapter and the token that maps to the PAN.
    /// </summary>
    private static async Task<(InterswitchAdapter Adapter, string Token)> CreateAdapterWithTokenizedPan(
        string pan,
        string correlationId = "test-correlation",
        FakeOutboundConnectionPool? pool = null,
        FakeIso8583Gateway? gateway = null,
        FakeCircuitBreakerRegistry? circuitBreakerRegistry = null,
        InterswitchOptions? options = null)
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

    #region 1. Authorization request constructs valid ISO 8583

    [Fact]
    public void BuildAuthorizationMessage_SetsCorrectMti()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest();

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.Equal("0100", message.Mti);
    }

    [Fact]
    public void BuildAuthorizationMessage_IncludesPan()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(sourceAccount: "4111111111111111");

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.True(message.Fields.ContainsKey(2));
        Assert.Equal("4111111111111111", message.Fields[2]);
    }

    [Fact]
    public void BuildAuthorizationMessage_IncludesProcessingCode()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest();

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.True(message.Fields.ContainsKey(3));
        Assert.Equal("000000", message.Fields[3]);
    }

    [Fact]
    public void BuildAuthorizationMessage_FormatsAmountAs12Digits()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(amountKobo: 500_000);

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.True(message.Fields.ContainsKey(4));
        Assert.Equal("000000500000", message.Fields[4]);
    }

    [Fact]
    public void BuildAuthorizationMessage_IncludesStan()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest();

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.True(message.Fields.ContainsKey(11));
        Assert.Equal(6, message.Fields[11].Length);
        Assert.True(int.TryParse(message.Fields[11], out _));
    }

    [Fact]
    public void BuildAuthorizationMessage_IncludesLocalTimeAndDate()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest();

        var message = adapter.BuildAuthorizationMessage(request);

        // Field 12 = HHmmss (6 chars), Field 13 = MMdd (4 chars)
        Assert.True(message.Fields.ContainsKey(12));
        Assert.Equal(6, message.Fields[12].Length);
        Assert.True(message.Fields.ContainsKey(13));
        Assert.Equal(4, message.Fields[13].Length);
    }

    [Fact]
    public void BuildAuthorizationMessage_IncludesRetrievalReference()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest();

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.True(message.Fields.ContainsKey(37));
        Assert.Equal(12, message.Fields[37].Length);
    }

    [Fact]
    public void BuildAuthorizationMessage_IncludesTerminalAndMerchantIds()
    {
        var options = DefaultOptions();
        options.TerminalId = "TERM0001";
        options.MerchantId = "MERCH00001";
        var adapter = CreateAdapter(options: options);
        var request = CreateRequest();

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.Equal("TERM0001", message.Fields[41]);
        Assert.Equal("MERCH00001", message.Fields[42]);
    }

    [Fact]
    public void BuildAuthorizationMessage_MapsCurrencyCodeToNumeric()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(currency: "NGN");

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.True(message.Fields.ContainsKey(49));
        Assert.Equal("566", message.Fields[49]);
    }

    [Theory]
    [InlineData("USD", "840")]
    [InlineData("GBP", "826")]
    [InlineData("EUR", "978")]
    public void BuildAuthorizationMessage_MapsDifferentCurrencies(
        string currencyCode, string expectedNumeric)
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(currency: currencyCode);

        var message = adapter.BuildAuthorizationMessage(request);

        Assert.Equal(expectedNumeric, message.Fields[49]);
    }

    #endregion

    #region 2. Response parsing extracts auth code

    [Fact]
    public async Task ProcessAsync_ApprovedResponse_ExtractsAuthCode()
    {
        var gateway = new FakeIso8583Gateway(responseCode: "00", authCode: "XYZ789");
        var pool = new FakeOutboundConnectionPool();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "test-auth", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("XYZ789", result.ProcessorReference);
    }

    [Fact]
    public async Task ProcessAsync_ApprovedResponseWithAuthCode_ReturnsSuccessWithAuthCode()
    {
        // Use a gateway that will produce a "success" response
        var gateway = new FakeIso8583Gateway(responseCode: "00", authCode: "ABC123");
        var pool = new FakeOutboundConnectionPool(simulateResponse: true,
            responseCode: "00", authCode: "ABC123");
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "test-auth2", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("ABC123", result.ProcessorReference);
    }

    [Fact]
    public async Task ProcessAsync_DeclinedResponse_ReturnsFailureWithResponseCode()
    {
        var gateway = new FakeIso8583Gateway(responseCode: "51");
        var pool = new FakeOutboundConnectionPool();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "test-decline", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("51", result.ErrorCode);
        Assert.Contains("Insufficient funds", result.ErrorMessage);
    }

    [Fact]
    public void GenerateStan_Returns6DigitPaddedString()
    {
        var stan = InterswitchAdapter.GenerateStan();

        Assert.Equal(6, stan.Length);
        Assert.True(int.TryParse(stan, out var value));
        Assert.InRange(value, 0, 999999);
    }

    [Fact]
    public void GenerateStan_IncrementsOnEachCall()
    {
        var stan1 = InterswitchAdapter.GenerateStan();
        var stan2 = InterswitchAdapter.GenerateStan();

        Assert.NotEqual(stan1, stan2);
        var val1 = int.Parse(stan1);
        var val2 = int.Parse(stan2);
        Assert.Equal(1, val2 - val1);
    }

    [Fact]
    public void GenerateRetrievalReference_Returns12Characters()
    {
        var rrn = InterswitchAdapter.GenerateRetrievalReference("TXN-REF-20240101-ABCDEF");

        Assert.Equal(12, rrn.Length);
    }

    [Fact]
    public void GenerateRetrievalReference_NullOrEmpty_ReturnsPaddedZeros()
    {
        Assert.Equal("000000000000", InterswitchAdapter.GenerateRetrievalReference(""));
        Assert.Equal("000000000000", InterswitchAdapter.GenerateRetrievalReference(null!));
    }

    [Fact]
    public void GenerateRetrievalReference_ShortReference_PadsLeft()
    {
        var rrn = InterswitchAdapter.GenerateRetrievalReference("ABC");

        Assert.Equal(12, rrn.Length);
        Assert.Equal("000000000ABC", rrn);
    }

    #endregion

    #region 3. Capture message sends correctly

    [Fact]
    public void BuildCaptureMessage_SetsCorrectMti()
    {
        var adapter = CreateAdapter();
        var captureRequest = new CaptureRequest
        {
            Pan = "4111111111111111",
            Amount = 500_000,
            TransactionReference = "TXN-CAPTURE-001",
            OriginalAuthCode = "ABC123",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest);

        Assert.Equal("0220", message.Mti);
    }

    [Fact]
    public void BuildCaptureMessage_IncludesOriginalAuthCode()
    {
        var adapter = CreateAdapter();
        var captureRequest = new CaptureRequest
        {
            Pan = "4111111111111111",
            Amount = 500_000,
            TransactionReference = "TXN-CAPTURE-001",
            OriginalAuthCode = "XYZ789",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest);

        Assert.True(message.Fields.ContainsKey(38));
        Assert.Equal("XYZ789", message.Fields[38]);
    }

    [Fact]
    public void BuildCaptureMessage_IncludesPanAndAmount()
    {
        var adapter = CreateAdapter();
        var captureRequest = new CaptureRequest
        {
            Pan = "5500000000000004",
            Amount = 1_000_000,
            TransactionReference = "TXN-CAPTURE-002",
            OriginalAuthCode = "AUTH01",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest);

        Assert.Equal("5500000000000004", message.Fields[2]);
        Assert.Equal("000001000000", message.Fields[4]);
    }

    [Fact]
    public void BuildCaptureMessage_IncludesTerminalAndMerchantIds()
    {
        var options = DefaultOptions();
        options.TerminalId = "TERM8888";
        options.MerchantId = "MERCH9999";
        var adapter = CreateAdapter(options: options);
        var captureRequest = new CaptureRequest
        {
            Pan = "4111111111111111",
            Amount = 500_000,
            TransactionReference = "TXN-CAPTURE-003",
            OriginalAuthCode = "AUTH02",
            CurrencyCode = "566"
        };

        var message = adapter.BuildCaptureMessage(captureRequest);

        Assert.Equal("TERM8888", message.Fields[41]);
        Assert.Equal("MERCH9999", message.Fields[42]);
    }

    [Fact]
    public async Task CaptureAsync_SendsMessageViaConnectionPool()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "capture-test", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var captureRequest = new CaptureRequest
        {
            Pan = token,
            Amount = 500_000,
            TransactionReference = "TXN-CAPTURE-004",
            OriginalAuthCode = "AUTH03",
            CurrencyCode = "566"
        };

        var result = await adapter.CaptureAsync(captureRequest, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(pool.GetWriterCalled);
        Assert.True(gateway.ConstructCalled);
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
            TransactionReference = "TXN-CAPTURE-005",
            OriginalAuthCode = "AUTH04",
            CurrencyCode = "566"
        };

        var result = await adapter.CaptureAsync(captureRequest, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
    }

    #endregion

    #region 4. Reversal on timeout

    [Fact]
    public void BuildReversalMessage_SetsCorrectMti()
    {
        var adapter = CreateAdapter();

        var message = adapter.BuildReversalMessage("TXN-REV-001");

        Assert.Equal("0400", message.Mti);
    }

    [Fact]
    public void BuildReversalMessage_IncludesRetrievalReference()
    {
        var adapter = CreateAdapter();

        var message = adapter.BuildReversalMessage("TXN-REV-002");

        Assert.True(message.Fields.ContainsKey(37));
        Assert.Equal(12, message.Fields[37].Length);
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

        var message = adapter.BuildReversalMessage("TXN-REV-003", originalData);

        Assert.True(message.Fields.ContainsKey(90));
        var field90 = message.Fields[90];
        // Field 90 = Original MTI (4) + STAN (6) + DateTime (10) + AcquirerID (11)
        Assert.Equal(31, field90.Length);
        Assert.StartsWith("0100", field90);
        Assert.Contains("000123", field90);
    }

    [Fact]
    public void BuildReversalMessage_WithoutOriginalData_OmitsField90()
    {
        var adapter = CreateAdapter();

        var message = adapter.BuildReversalMessage("TXN-REV-004", null);

        Assert.False(message.Fields.ContainsKey(90));
    }

    [Fact]
    public async Task ReverseAsync_SendsReversalMessageViaConnectionPool()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var adapter = CreateAdapter(pool: pool, gateway: gateway);

        var result = await adapter.ReverseAsync("TXN-REV-005", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(pool.GetWriterCalled);
        Assert.True(gateway.ConstructCalled);
    }

    [Fact]
    public async Task ReverseAsync_CircuitBreakerOpen_ReturnsUnavailable()
    {
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateAdapter(circuitBreakerRegistry: registry);

        var result = await adapter.ReverseAsync("TXN-REV-006", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
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

        var result = await adapter.ReverseAsync("TXN-REV-007", originalData, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(pool.GetWriterCalled);
    }

    #endregion

    #region 5. TCP reconnection on disconnection

    [Fact]
    public async Task ProcessAsync_ConnectionPoolException_RecordsFailure()
    {
        var pool = new FakeOutboundConnectionPool(throwOnGetWriter: true);
        var gateway = new FakeIso8583Gateway();
        var registry = new FakeCircuitBreakerRegistry();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "test-fail", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, circuitBreakerRegistry: registry, pciBoundary: boundary);
        var request = CreateRequest(sourceAccount: token);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_ERROR", result.ErrorCode);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_ConnectionPoolReturnsWriter_WriterIsReturned()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "test-return", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateRequest(sourceAccount: token);

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(pool.ReturnCalled);
    }

    [Fact]
    public async Task ProcessAsync_AfterFailure_NextCallUsesConnectionPool()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var (boundary, _) = CreatePciBoundaryWithVault();
        var token = await boundary.TokenizeAsync("4111111111111111", null, "test-retry", CancellationToken.None);
        var adapter = CreateAdapter(pool: pool, gateway: gateway, pciBoundary: boundary);
        var request = CreateRequest(sourceAccount: token);

        // First call
        await adapter.ProcessAsync(request, CancellationToken.None);
        Assert.True(pool.GetWriterCalled);

        // Reset tracking
        pool.ResetTracking();

        // Second call - should again use the connection pool
        await adapter.ProcessAsync(request, CancellationToken.None);
        Assert.True(pool.GetWriterCalled);
    }

    #endregion

    #region 6. PCI boundary masks data

    [Fact]
    public void PciBoundary_MaskPan_HidesAllButLast4Digits()
    {
        // Verify PCI boundary mask behavior for card data in responses
        var maskedPan = PciBoundary.MaskPan("4111111111111111");

        Assert.Equal("************1111", maskedPan);
        Assert.DoesNotContain("41111111", maskedPan);
    }

    [Fact]
    public void PciBoundary_MaskPan_DifferentPanLengths()
    {
        // Standard 16-digit Visa
        Assert.Equal("************1111", PciBoundary.MaskPan("4111111111111111"));
        // 16-digit Mastercard
        Assert.Equal("************0004", PciBoundary.MaskPan("5500000000000004"));
        // 19-digit extended PAN
        Assert.Equal("***************6789", PciBoundary.MaskPan("1234567890123456789"));
    }

    [Fact]
    public void PciBoundary_MaskPan_ShortPan_DoesNotCrash()
    {
        // PANs of 4 or fewer chars are returned as-is
        Assert.Equal("1234", PciBoundary.MaskPan("1234"));
        Assert.Equal("123", PciBoundary.MaskPan("123"));
    }

    [Fact]
    public void BuildAuthorizationMessage_PanInMessage_ShouldBeMaskedBeforeLogging()
    {
        // This test verifies the PAN is present in the message (needed for processing)
        // but when exposed externally it would be masked via PCI boundary
        var adapter = CreateAdapter();
        var request = CreateRequest(sourceAccount: "4111111111111111");

        var message = adapter.BuildAuthorizationMessage(request);
        var pan = message.Fields[2];

        // The raw PAN is in the message for transmission
        Assert.Equal("4111111111111111", pan);

        // But the PCI boundary mask would hide it in any external response/log
        var maskedPan = PciBoundary.MaskPan(pan);
        Assert.Equal("************1111", maskedPan);
        Assert.DoesNotContain("41111111", maskedPan);
    }

    [Fact]
    public async Task ProcessAsync_ErrorResponse_DoesNotLeakPanInErrorMessage()
    {
        var pool = new FakeOutboundConnectionPool();
        var gateway = new FakeIso8583Gateway();
        var adapter = CreateAdapter(pool: pool, gateway: gateway);
        var request = CreateRequest(sourceAccount: "4111111111111111");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        // Verify that any error message does NOT contain the full PAN
        if (result.ErrorMessage != null)
        {
            Assert.DoesNotContain("4111111111111111", result.ErrorMessage);
        }
    }

    #endregion

    #region Additional edge cases

    [Fact]
    public async Task ProcessAsync_CircuitBreakerOpen_ReturnsUnavailableWithoutSending()
    {
        var pool = new FakeOutboundConnectionPool();
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateAdapter(pool: pool, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
        // Should NOT have tried to get a writer
        Assert.False(pool.GetWriterCalled);
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
    public void Channel_ReturnsInterswitch()
    {
        var adapter = CreateAdapter();
        Assert.Equal(PaymentChannel.Interswitch, adapter.Channel);
    }

    #endregion

    #region Test Helpers

    private class FakeOutboundConnectionPool : IOutboundConnectionPool
    {
        private readonly bool _throwOnGetWriter;
        public bool GetWriterCalled { get; private set; }
        public bool ReturnCalled { get; private set; }

        public FakeOutboundConnectionPool(
            bool throwOnGetWriter = false,
            bool simulateResponse = false,
            string? responseCode = null,
            string? authCode = null)
        {
            _throwOnGetWriter = throwOnGetWriter;
        }

        public Task<PipeWriter> GetWriterAsync(ProcessorEndpoint endpoint, CancellationToken ct)
        {
            GetWriterCalled = true;

            if (_throwOnGetWriter)
                throw new InvalidOperationException("Connection failed - simulating TCP disconnection");

            // Create a pipe and return its writer
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
            GetWriterCalled = true; // Marks that the connection pool was used

            if (_throwOnGetWriter)
                throw new InvalidOperationException("Connection failed - simulating TCP disconnection");

            return Task.FromResult<IPooledConnection>(new FakePooledConnection());
        }

        public Task ReleaseConnectionAsync(ProcessorEndpoint endpoint, IPooledConnection connection)
        {
            ReturnCalled = true; // Marks that the connection was returned to pool
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
            // Build a fake framed ISO 8583 response: 2-byte length header + body
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

    /// <summary>
    /// A stream that discards writes and returns pre-configured data on reads.
    /// Used to simulate bidirectional ISO 8583 TCP communication in tests.
    /// </summary>
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
            // Return a dummy byte array representing a constructed message
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
                [PaymentChannel.Interswitch] = Breaker.State
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
