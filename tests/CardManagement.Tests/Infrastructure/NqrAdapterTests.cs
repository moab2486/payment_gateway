using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Channels.Nibss;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class NqrAdapterTests
{
    private static NqrOptions DefaultOptions(int expiryMinutes = 15) => new()
    {
        BaseUrl = "https://nqr.nibss-plc.com.ng",
        ApiKey = "test-api-key",
        DynamicQrExpiryMinutes = expiryMinutes
    };

    private static PaymentRequest CreateRequest(
        string sourceAccount = "058:1234567890",
        string destinationAccount = "MERCHANT-001",
        long amountKobo = 500_000,
        string? transactionReference = null)
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: transactionReference ?? Guid.NewGuid().ToString(),
            transactionType: PaymentTransactionType.QRPayment,
            amount: new Money(amountKobo, "NGN"),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.NQR);
    }

    private static PaymentRequest CreateStaticQrRequest(
        string destinationAccount = "MERCHANT-001",
        string? transactionReference = null)
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: transactionReference ?? Guid.NewGuid().ToString(),
            transactionType: PaymentTransactionType.QRPayment,
            amount: new Money(0, "NGN"),
            sourceAccount: "058:1234567890",
            destinationAccount: destinationAccount,
            channel: PaymentChannel.NQR);
    }

    private static NqrAdapter CreateAdapter(
        INibssNqrClient? client = null,
        ICircuitBreakerRegistry? circuitBreakerRegistry = null,
        NqrOptions? options = null,
        ITimeProvider? timeProvider = null)
    {
        return new NqrAdapter(
            client ?? new FakeNibssNqrClient(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            Options.Create(options ?? DefaultOptions()),
            NullLogger<NqrAdapter>.Instance,
            timeProvider);
    }

    #region QR Generation Tests

    [Fact]
    public async Task ProcessAsync_DynamicQr_GeneratesPayloadWithRequiredFields()
    {
        var qrRef = "NQR-REF-12345";
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", qrRef));
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);

        var adapter = CreateAdapter(client: client, timeProvider: timeProvider);
        var request = CreateRequest(amountKobo: 500_000, destinationAccount: "MERCHANT-001");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(qrRef, result.ProcessorReference);

        // Verify the registration request sent to NIBSS
        var sentRequest = client.LastRequest;
        Assert.NotNull(sentRequest);
        Assert.Equal("MERCHANT-001", sentRequest!.MerchantId);
        Assert.Equal(5000m, sentRequest.Amount); // 500_000 kobo = 5000 naira
        Assert.Equal(request.TransactionReference, sentRequest.TransactionReference);
        Assert.Equal("Dynamic", sentRequest.QrType);
        Assert.NotNull(sentRequest.ExpiresAtUtc);
    }

    [Fact]
    public async Task ProcessAsync_StaticQr_GeneratesPayloadWithoutExpiry()
    {
        var qrRef = "NQR-STATIC-REF";
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", qrRef));

        var adapter = CreateAdapter(client: client);
        var request = CreateStaticQrRequest(destinationAccount: "MERCHANT-002");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(qrRef, result.ProcessorReference);

        var sentRequest = client.LastRequest;
        Assert.NotNull(sentRequest);
        Assert.Equal("MERCHANT-002", sentRequest!.MerchantId);
        Assert.Equal(0m, sentRequest.Amount);
        Assert.Equal("Static", sentRequest.QrType);
        Assert.Null(sentRequest.ExpiresAtUtc);
    }

    [Fact]
    public void GenerateQrPayload_DynamicQr_IncludesMerchantIdAmountAndReference()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var adapter = CreateAdapter(timeProvider: timeProvider, options: DefaultOptions(expiryMinutes: 30));

        var request = CreateRequest(
            destinationAccount: "MERCHANT-XYZ",
            amountKobo: 1_000_000);

        var payload = adapter.GenerateQrPayload(request);

        Assert.Equal("MERCHANT-XYZ", payload.MerchantId);
        Assert.Equal(10_000m, payload.Amount); // 1_000_000 kobo = 10_000 naira
        Assert.Equal(request.TransactionReference, payload.TransactionReference);
        Assert.Equal("Dynamic", payload.QrType);
        Assert.Equal(fixedTime.AddMinutes(30), payload.ExpiresAtUtc);
    }

    [Fact]
    public void GenerateQrPayload_StaticQr_HasNoExpiry()
    {
        var adapter = CreateAdapter();
        var request = CreateStaticQrRequest(destinationAccount: "MERCHANT-ABC");

        var payload = adapter.GenerateQrPayload(request);

        Assert.Equal("MERCHANT-ABC", payload.MerchantId);
        Assert.Equal(0m, payload.Amount);
        Assert.Equal(request.TransactionReference, payload.TransactionReference);
        Assert.Equal("Static", payload.QrType);
        Assert.Null(payload.ExpiresAtUtc);
    }

    #endregion

    #region Static vs Dynamic Differentiation

    [Fact]
    public async Task ProcessAsync_AmountGreaterThanZero_CreatesDynamicQr()
    {
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "REF-DYN"));
        var adapter = CreateAdapter(client: client);

        var request = CreateRequest(amountKobo: 100); // > 0 = dynamic

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.Equal("Dynamic", client.LastRequest!.QrType);
    }

    [Fact]
    public async Task ProcessAsync_AmountZero_CreatesStaticQr()
    {
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "REF-STATIC"));
        var adapter = CreateAdapter(client: client);

        var request = CreateStaticQrRequest(); // amount = 0 = static

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.Equal("Static", client.LastRequest!.QrType);
    }

    #endregion

    #region Dynamic QR Expiry Tests

    [Fact]
    public async Task IsDynamicQrExpired_BeforeExpiry_ReturnsFalse()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "REF-1"));
        var adapter = CreateAdapter(
            client: client,
            timeProvider: timeProvider,
            options: DefaultOptions(expiryMinutes: 15));

        var txRef = "TX-EXPIRY-TEST-1";
        var request = CreateRequest(amountKobo: 500_000, transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        // Still within expiry window (10 minutes later)
        timeProvider.SetTime(fixedTime.AddMinutes(10));

        Assert.False(adapter.IsDynamicQrExpired(txRef));
    }

    [Fact]
    public async Task IsDynamicQrExpired_AfterExpiry_ReturnsTrue()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "REF-2"));
        var adapter = CreateAdapter(
            client: client,
            timeProvider: timeProvider,
            options: DefaultOptions(expiryMinutes: 15));

        var txRef = "TX-EXPIRY-TEST-2";
        var request = CreateRequest(amountKobo: 500_000, transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        // After expiry window (20 minutes later)
        timeProvider.SetTime(fixedTime.AddMinutes(20));

        Assert.True(adapter.IsDynamicQrExpired(txRef));
    }

    [Fact]
    public async Task IsDynamicQrExpired_StaticQr_ReturnsFalse()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "REF-3"));
        var adapter = CreateAdapter(client: client, timeProvider: timeProvider);

        var txRef = "TX-STATIC-NOEXPIRY";
        var request = CreateStaticQrRequest(transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        // Even after a long time, static QR doesn't expire
        timeProvider.SetTime(fixedTime.AddDays(365));

        Assert.False(adapter.IsDynamicQrExpired(txRef));
    }

    #endregion

    #region Notification Matching Tests

    [Fact]
    public async Task MatchPaymentNotification_ValidNotification_ReturnsSuccess()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "QR-REF"));
        var adapter = CreateAdapter(client: client, timeProvider: timeProvider);

        var txRef = "TX-NOTIFICATION-MATCH";
        var request = CreateRequest(amountKobo: 500_000, transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        var notification = new NqrPaymentNotification(
            TransactionReference: txRef,
            Amount: 5000m, // Matches: 500_000 kobo = 5000 naira
            PayerAccount: "058:9876543210",
            NibssReference: "NIBSS-PAY-REF-001");

        var result = adapter.MatchPaymentNotification(notification);

        Assert.True(result.Success);
        Assert.Equal("NIBSS-PAY-REF-001", result.ProcessorReference);
    }

    [Fact]
    public async Task MatchPaymentNotification_AmountMismatch_ReturnsError()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "QR-REF"));
        var adapter = CreateAdapter(client: client, timeProvider: timeProvider);

        var txRef = "TX-AMOUNT-MISMATCH";
        var request = CreateRequest(amountKobo: 500_000, transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        var notification = new NqrPaymentNotification(
            TransactionReference: txRef,
            Amount: 3000m, // Mismatch: expected 5000
            PayerAccount: "058:9876543210",
            NibssReference: "NIBSS-PAY-REF-002");

        var result = adapter.MatchPaymentNotification(notification);

        Assert.False(result.Success);
        Assert.Equal("AMOUNT_MISMATCH", result.ErrorCode);
        Assert.Contains("5000", result.ErrorMessage);
        Assert.Contains("3000", result.ErrorMessage);
    }

    [Fact]
    public void MatchPaymentNotification_UnknownReference_ReturnsNotFound()
    {
        var adapter = CreateAdapter();

        var notification = new NqrPaymentNotification(
            TransactionReference: "UNKNOWN-TX-REF",
            Amount: 1000m,
            PayerAccount: "058:9876543210",
            NibssReference: "NIBSS-REF-UNKNOWN");

        var result = adapter.MatchPaymentNotification(notification);

        Assert.False(result.Success);
        Assert.Equal("TRANSACTION_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task MatchPaymentNotification_ExpiredDynamicQr_ReturnsExpired()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "QR-REF"));
        var adapter = CreateAdapter(
            client: client,
            timeProvider: timeProvider,
            options: DefaultOptions(expiryMinutes: 15));

        var txRef = "TX-EXPIRED-NOTIFICATION";
        var request = CreateRequest(amountKobo: 500_000, transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        // Advance time past expiry
        timeProvider.SetTime(fixedTime.AddMinutes(20));

        var notification = new NqrPaymentNotification(
            TransactionReference: txRef,
            Amount: 5000m,
            PayerAccount: "058:9876543210",
            NibssReference: "NIBSS-REF-EXPIRED");

        var result = adapter.MatchPaymentNotification(notification);

        Assert.False(result.Success);
        Assert.Equal("QR_EXPIRED", result.ErrorCode);
    }

    [Fact]
    public async Task MatchPaymentNotification_DynamicQr_RemovedAfterMatch()
    {
        var fixedTime = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "QR-REF"));
        var adapter = CreateAdapter(client: client, timeProvider: timeProvider);

        var txRef = "TX-SINGLE-USE";
        var request = CreateRequest(amountKobo: 500_000, transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        var notification = new NqrPaymentNotification(
            TransactionReference: txRef,
            Amount: 5000m,
            PayerAccount: "058:9876543210",
            NibssReference: "NIBSS-FIRST");

        // First match succeeds
        var result1 = adapter.MatchPaymentNotification(notification);
        Assert.True(result1.Success);

        // Second match fails (dynamic QR is single-use)
        var result2 = adapter.MatchPaymentNotification(notification);
        Assert.False(result2.Success);
        Assert.Equal("TRANSACTION_NOT_FOUND", result2.ErrorCode);
    }

    [Fact]
    public async Task MatchPaymentNotification_StaticQr_RemainsAfterMatch()
    {
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "QR-STATIC"));
        var adapter = CreateAdapter(client: client);

        var txRef = "TX-STATIC-REUSABLE";
        var request = CreateStaticQrRequest(transactionReference: txRef);
        await adapter.ProcessAsync(request, CancellationToken.None);

        var notification = new NqrPaymentNotification(
            TransactionReference: txRef,
            Amount: 1000m, // Static QR accepts any amount
            PayerAccount: "058:9876543210",
            NibssReference: "NIBSS-STATIC-1");

        // First match succeeds
        var result1 = adapter.MatchPaymentNotification(notification);
        Assert.True(result1.Success);

        // Second match also succeeds (static QR is reusable)
        var notification2 = new NqrPaymentNotification(
            TransactionReference: txRef,
            Amount: 2000m,
            PayerAccount: "044:1111111111",
            NibssReference: "NIBSS-STATIC-2");

        var result2 = adapter.MatchPaymentNotification(notification2);
        Assert.True(result2.Success);
    }

    #endregion

    #region Circuit Breaker Integration

    [Fact]
    public async Task ProcessAsync_CircuitBreakerOpen_ReturnsUnavailable()
    {
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateAdapter(circuitBreakerRegistry: registry);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("circuit breaker is open", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_Success_RecordsSuccessOnCircuitBreaker()
    {
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("00", "Successful", "REF-123"));
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(1, registry.Breaker.SuccessCount);
        Assert.Equal(0, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_Failure_RecordsFailureOnCircuitBreaker()
    {
        var client = new FakeNibssNqrClient(
            new NqrRegistrationResponse("96", "System malfunction", null));
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(0, registry.Breaker.SuccessCount);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    #endregion

    #region Other Tests

    [Fact]
    public void Channel_ReturnsNQR()
    {
        var adapter = CreateAdapter();
        Assert.Equal(PaymentChannel.NQR, adapter.Channel);
    }

    [Fact]
    public async Task ReverseAsync_ReturnsNotSupported()
    {
        var adapter = CreateAdapter();

        var result = await adapter.ReverseAsync("some-ref", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NOT_SUPPORTED", result.ErrorCode);
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
    public async Task ProcessAsync_ExceptionDuringRegistration_RecordsFailure()
    {
        var client = new ThrowingNibssNqrClient();
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_ERROR", result.ErrorCode);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    #endregion

    #region Test Helpers

    private class FakeNibssNqrClient : INibssNqrClient
    {
        private readonly NqrRegistrationResponse _response;
        public NqrRegistrationRequest? LastRequest { get; private set; }

        public FakeNibssNqrClient(NqrRegistrationResponse? response = null)
        {
            _response = response ?? new NqrRegistrationResponse("00", "Successful", "DEFAULT-QR-REF");
        }

        public Task<NqrRegistrationResponse> RegisterQrAsync(NqrRegistrationRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    private class ThrowingNibssNqrClient : INibssNqrClient
    {
        public Task<NqrRegistrationResponse> RegisterQrAsync(NqrRegistrationRequest request, CancellationToken ct)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    private class FakeTimeProvider : ITimeProvider
    {
        private DateTime _utcNow;

        public FakeTimeProvider(DateTime utcNow)
        {
            _utcNow = utcNow;
        }

        public DateTime UtcNow => _utcNow;

        public void SetTime(DateTime utcNow)
        {
            _utcNow = utcNow;
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
                [PaymentChannel.NQR] = Breaker.State
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

    #endregion
}
