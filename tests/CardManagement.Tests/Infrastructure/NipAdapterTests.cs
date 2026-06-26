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

public class NipAdapterTests
{
    private static NipOptions DefaultOptions(int timeoutMs = 30_000, int statusInquiryDelayMs = 0) => new()
    {
        BaseUrl = "https://nip.nibss-plc.com.ng",
        ApiKey = "test-api-key",
        TimeoutMs = timeoutMs,
        StatusInquiryDelayMs = statusInquiryDelayMs
    };

    private static PaymentRequest CreateRequest(
        string sourceAccount = "058:1234567890",
        string destinationAccount = "044:0987654321",
        long amountKobo = 100_000_00)
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: Guid.NewGuid().ToString(),
            transactionType: PaymentTransactionType.InterbankTransfer,
            amount: new Money(amountKobo, "NGN"),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.NIP);
    }

    private static NipAdapter CreateAdapter(
        INibssNipClient? client = null,
        ICircuitBreakerRegistry? circuitBreakerRegistry = null,
        NipOptions? options = null)
    {
        return new NipAdapter(
            client ?? new FakeNibssNipClient(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            Options.Create(options ?? DefaultOptions()),
            NullLogger<NipAdapter>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulTransfer_ReturnsNibssReference()
    {
        var nibssRef = "NIP-REF-12345";
        var client = new FakeNibssNipClient(
            new NibssTransferResponse("00", "Successful", nibssRef));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nibssRef, result.ProcessorReference);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_FailedTransfer_ReturnsReasonCodeAndDescription()
    {
        var client = new FakeNibssNipClient(
            new NibssTransferResponse("51", "No sufficient funds", null));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_FUNDS", result.ErrorCode);
        Assert.Equal("No sufficient funds", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_Timeout_TriggersStatusInquiry()
    {
        var nibssRef = "NIP-REF-TIMEOUT-INQUIRY";
        var client = new TimeoutThenSuccessNibssNipClient(
            statusResponse: new NibssTransferResponse("00", "Successful", nibssRef));

        var adapter = CreateAdapter(
            client: client,
            options: DefaultOptions(timeoutMs: 50, statusInquiryDelayMs: 0));
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nibssRef, result.ProcessorReference);
        Assert.True(client.StatusInquiryCalled, "Status inquiry should have been called after timeout");
    }

    [Fact]
    public async Task ProcessAsync_Timeout_StatusInquiryReturnsFailure()
    {
        var client = new TimeoutThenSuccessNibssNipClient(
            statusResponse: new NibssTransferResponse("91", "Beneficiary bank not available", null));

        var adapter = CreateAdapter(
            client: client,
            options: DefaultOptions(timeoutMs: 50, statusInquiryDelayMs: 0));
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("BENEFICIARY_BANK_NOT_AVAILABLE", result.ErrorCode);
        Assert.True(client.StatusInquiryCalled);
    }

    [Fact]
    public async Task ProcessAsync_InvalidBankCode_IsRejected()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(destinationAccount: "XYZ:0987654321");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("Invalid bank code", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_InvalidAccountNumber_IsRejected()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(destinationAccount: "044:12345"); // Too short

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("Invalid account number", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_MissingDestinationAccount_IsRejected()
    {
        var adapter = CreateAdapter();
        // Create a request with an invalid format (no colon separator)
        var request = CreateRequest(destinationAccount: "0987654321");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("BankCode:AccountNumber", result.ErrorMessage);
    }

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
        var client = new FakeNibssNipClient(
            new NibssTransferResponse("00", "Successful", "REF-123"));
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
        var client = new FakeNibssNipClient(
            new NibssTransferResponse("51", "No sufficient funds", null));
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(0, registry.Breaker.SuccessCount);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_ExceptionDuringTransfer_RecordsFailureOnCircuitBreaker()
    {
        var client = new ThrowingNibssNipClient();
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_ERROR", result.ErrorCode);
        Assert.Equal(1, registry.Breaker.FailureCount);
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
    public async Task ReverseAsync_ReturnsNotSupported()
    {
        var adapter = CreateAdapter();

        var result = await adapter.ReverseAsync("some-ref", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NOT_SUPPORTED", result.ErrorCode);
    }

    [Fact]
    public void ValidateDestination_ValidBankCodeAndAccount_ReturnsNull()
    {
        var result = NipAdapter.ValidateDestination("044:0123456789");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("AB:0123456789", "Invalid bank code")]
    [InlineData("0044:0123456789", "Invalid bank code")]
    [InlineData("04:0123456789", "Invalid bank code")]
    [InlineData("044:012345", "Invalid account number")]
    [InlineData("044:01234567890", "Invalid account number")]
    [InlineData("044:012345678A", "Invalid account number")]
    public void ValidateDestination_InvalidInputs_ReturnsError(string destination, string expectedSubstring)
    {
        var result = NipAdapter.ValidateDestination(destination);
        Assert.NotNull(result);
        Assert.Contains(expectedSubstring, result);
    }

    [Fact]
    public async Task ProcessAsync_UnmappedResponseCode_ReturnsUnmappedError()
    {
        var client = new FakeNibssNipClient(
            new NibssTransferResponse("99", "Some unknown error", null));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("UNMAPPED_CODE", result.ErrorCode);
        Assert.Contains("99", result.ErrorMessage);
    }

    [Fact]
    public void Channel_ReturnsNIP()
    {
        var adapter = CreateAdapter();
        Assert.Equal(PaymentChannel.NIP, adapter.Channel);
    }

    #region Test Helpers

    private class FakeNibssNipClient : INibssNipClient
    {
        private readonly NibssTransferResponse _response;

        public FakeNibssNipClient(NibssTransferResponse? response = null)
        {
            _response = response ?? new NibssTransferResponse("00", "Successful", "DEFAULT-REF");
        }

        public Task<NibssTransferResponse> SubmitTransferAsync(NibssTransferRequest request, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }

        public Task<NibssTransferResponse> QueryStatusAsync(string transactionReference, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }
    }

    private class TimeoutThenSuccessNibssNipClient : INibssNipClient
    {
        private readonly NibssTransferResponse _statusResponse;
        public bool StatusInquiryCalled { get; private set; }

        public TimeoutThenSuccessNibssNipClient(NibssTransferResponse statusResponse)
        {
            _statusResponse = statusResponse;
        }

        public async Task<NibssTransferResponse> SubmitTransferAsync(NibssTransferRequest request, CancellationToken ct)
        {
            // Simulate a long-running request that will exceed the timeout
            await Task.Delay(5_000, ct);
            return new NibssTransferResponse("00", "Should not reach here", null);
        }

        public Task<NibssTransferResponse> QueryStatusAsync(string transactionReference, CancellationToken ct)
        {
            StatusInquiryCalled = true;
            return Task.FromResult(_statusResponse);
        }
    }

    private class ThrowingNibssNipClient : INibssNipClient
    {
        public Task<NibssTransferResponse> SubmitTransferAsync(NibssTransferRequest request, CancellationToken ct)
        {
            throw new HttpRequestException("Connection refused");
        }

        public Task<NibssTransferResponse> QueryStatusAsync(string transactionReference, CancellationToken ct)
        {
            throw new HttpRequestException("Connection refused");
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
                [PaymentChannel.NIP] = Breaker.State
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
