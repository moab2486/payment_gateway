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

public class MCashAdapterTests
{
    private static MCashOptions DefaultOptions(int sessionTimeoutMs = 60_000) => new()
    {
        BaseUrl = "https://mcash.nibss-plc.com.ng",
        ApiKey = "test-mcash-api-key",
        SessionTimeoutMs = sessionTimeoutMs
    };

    private static PaymentRequest CreateRequest(
        string sourceAccount = "08012345678|ENCRYPTED_PIN_BLOCK",
        string destinationAccount = "08098765432",
        long amountKobo = 50_000_00)
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: Guid.NewGuid().ToString(),
            transactionType: PaymentTransactionType.USSDPayment,
            amount: new Money(amountKobo, "NGN"),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.MCash);
    }

    private static MCashAdapter CreateAdapter(
        INibssMCashClient? client = null,
        ICircuitBreakerRegistry? circuitBreakerRegistry = null,
        MCashOptions? options = null)
    {
        return new MCashAdapter(
            client ?? new FakeNibssMCashClient(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            Options.Create(options ?? DefaultOptions()),
            NullLogger<MCashAdapter>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulPayment_ReturnsConfirmation()
    {
        var nibssRef = "MCASH-REF-12345";
        var client = new FakeNibssMCashClient(
            new MCashResponse("00", "Successful", nibssRef));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nibssRef, result.ProcessorReference);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_SessionTimeout_TriggersStatusInquiry()
    {
        var nibssRef = "MCASH-REF-TIMEOUT-INQUIRY";
        var client = new TimeoutThenSuccessMCashClient(
            statusResponse: new MCashResponse("00", "Successful", nibssRef));

        var adapter = CreateAdapter(
            client: client,
            options: DefaultOptions(sessionTimeoutMs: 50));
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nibssRef, result.ProcessorReference);
        Assert.True(client.StatusInquiryCalled, "Status inquiry should have been called after session timeout");
    }

    [Fact]
    public async Task ProcessAsync_SessionTimeout_StatusInquiryReturnsFailure()
    {
        var client = new TimeoutThenSuccessMCashClient(
            statusResponse: new MCashResponse("68", "Response received too late", null));

        var adapter = CreateAdapter(
            client: client,
            options: DefaultOptions(sessionTimeoutMs: 50));
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("TIMEOUT_WAITING_RESPONSE", result.ErrorCode);
        Assert.True(client.StatusInquiryCalled);
    }

    [Fact]
    public async Task ProcessAsync_PinBlockIsIncludedInRequest()
    {
        var pinBlock = "ENCRYPTED_PIN_BLOCK_DATA";
        var client = new CapturingMCashClient(new MCashResponse("00", "Successful", "REF-123"));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest(sourceAccount: $"08012345678|{pinBlock}");

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.NotNull(client.CapturedRequest);
        Assert.Equal(pinBlock, client.CapturedRequest.PinBlock);
        Assert.Equal("08012345678", client.CapturedRequest.SourceAccount);
    }

    [Fact]
    public async Task ProcessAsync_NoPinBlock_SubmitsEmptyPin()
    {
        var client = new CapturingMCashClient(new MCashResponse("00", "Successful", "REF-123"));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest(sourceAccount: "08012345678");

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.NotNull(client.CapturedRequest);
        Assert.Equal(string.Empty, client.CapturedRequest.PinBlock);
        Assert.Equal("08012345678", client.CapturedRequest.SourceAccount);
    }

    [Fact]
    public async Task ProcessAsync_MCashSpecificResponseCode_MappedCorrectly()
    {
        var client = new FakeNibssMCashClient(
            new MCashResponse("A5", "Invalid PIN entered", null));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INVALID_PIN", result.ErrorCode);
        Assert.Equal("Invalid PIN entered", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_CommonNibssResponseCode_MappedViaFallback()
    {
        var client = new FakeNibssMCashClient(
            new MCashResponse("51", "No sufficient funds", null));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_FUNDS", result.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_SessionExpiredCode_MappedCorrectly()
    {
        var client = new FakeNibssMCashClient(
            new MCashResponse("A3", "USSD session expired", null));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SESSION_EXPIRED", result.ErrorCode);
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
        var client = new FakeNibssMCashClient(
            new MCashResponse("00", "Successful", "REF-123"));
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
        var client = new FakeNibssMCashClient(
            new MCashResponse("51", "No sufficient funds", null));
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(0, registry.Breaker.SuccessCount);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_ExceptionDuringPayment_RecordsFailureOnCircuitBreaker()
    {
        var client = new ThrowingMCashClient();
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_ERROR", result.ErrorCode);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_GeneratesSessionIdInNFormat()
    {
        var client = new CapturingMCashClient(new MCashResponse("00", "Successful", "REF-123"));
        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.NotNull(client.CapturedRequest);
        // "N" format: 32 hex digits, no hyphens
        Assert.Equal(32, client.CapturedRequest.SessionId.Length);
        Assert.DoesNotContain("-", client.CapturedRequest.SessionId);
        Assert.True(client.CapturedRequest.SessionId.All(c => "0123456789abcdef".Contains(c)));
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
    public void Channel_ReturnsMCash()
    {
        var adapter = CreateAdapter();
        Assert.Equal(PaymentChannel.MCash, adapter.Channel);
    }

    [Fact]
    public void GenerateSessionId_ReturnsGuidInNFormat()
    {
        var sessionId = MCashAdapter.GenerateSessionId();

        Assert.Equal(32, sessionId.Length);
        Assert.DoesNotContain("-", sessionId);
        Assert.True(sessionId.All(c => "0123456789abcdef".Contains(c)));
    }

    [Theory]
    [InlineData("08012345678|PIN123", "08012345678", "PIN123")]
    [InlineData("08012345678", "08012345678", "")]
    [InlineData("|PIN_ONLY", "", "PIN_ONLY")]
    [InlineData("", "", "")]
    public void ParseSourceWithPin_ParsesCorrectly(string input, string expectedSource, string expectedPin)
    {
        var (source, pin) = MCashAdapter.ParseSourceWithPin(input);

        Assert.Equal(expectedSource, source);
        Assert.Equal(expectedPin, pin);
    }

    [Fact]
    public void MCashResponseCodes_Map_NullResponseCode_ReturnsUnknownError()
    {
        var (errorCode, _) = MCashResponseCodes.Map(null);
        Assert.Equal("UNKNOWN_ERROR", errorCode);
    }

    [Fact]
    public void MCashResponseCodes_Map_EmptyResponseCode_ReturnsUnknownError()
    {
        var (errorCode, _) = MCashResponseCodes.Map("");
        Assert.Equal("UNKNOWN_ERROR", errorCode);
    }

    [Theory]
    [InlineData("A1", "INVALID_PHONE_NUMBER")]
    [InlineData("A2", "PIN_ATTEMPTS_EXCEEDED")]
    [InlineData("A3", "SESSION_EXPIRED")]
    [InlineData("A4", "USER_ABORT")]
    [InlineData("A5", "INVALID_PIN")]
    [InlineData("A6", "ACCOUNT_BLOCKED")]
    [InlineData("A7", "SERVICE_NOT_SUBSCRIBED")]
    public void MCashResponseCodes_Map_MCashSpecificCodes_MappedCorrectly(string code, string expectedErrorCode)
    {
        var (errorCode, _) = MCashResponseCodes.Map(code);
        Assert.Equal(expectedErrorCode, errorCode);
    }

    [Theory]
    [InlineData("51", "INSUFFICIENT_FUNDS")]
    [InlineData("91", "BENEFICIARY_BANK_NOT_AVAILABLE")]
    [InlineData("96", "SYSTEM_MALFUNCTION")]
    public void MCashResponseCodes_Map_CommonNibssCodes_FallsBackCorrectly(string code, string expectedErrorCode)
    {
        var (errorCode, _) = MCashResponseCodes.Map(code);
        Assert.Equal(expectedErrorCode, errorCode);
    }

    [Fact]
    public void MCashResponseCodes_IsSuccess_ReturnsTrue_ForSuccessCode()
    {
        Assert.True(MCashResponseCodes.IsSuccess("00"));
    }

    [Fact]
    public void MCashResponseCodes_IsSuccess_ReturnsFalse_ForNonSuccessCode()
    {
        Assert.False(MCashResponseCodes.IsSuccess("51"));
        Assert.False(MCashResponseCodes.IsSuccess("A5"));
        Assert.False(MCashResponseCodes.IsSuccess(null));
    }

    #region Test Helpers

    private class FakeNibssMCashClient : INibssMCashClient
    {
        private readonly MCashResponse _response;

        public FakeNibssMCashClient(MCashResponse? response = null)
        {
            _response = response ?? new MCashResponse("00", "Successful", "DEFAULT-MCASH-REF");
        }

        public Task<MCashResponse> SubmitPaymentAsync(MCashRequest request, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }

        public Task<MCashResponse> QueryStatusAsync(string sessionId, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }
    }

    private class CapturingMCashClient : INibssMCashClient
    {
        private readonly MCashResponse _response;
        public MCashRequest? CapturedRequest { get; private set; }

        public CapturingMCashClient(MCashResponse response)
        {
            _response = response;
        }

        public Task<MCashResponse> SubmitPaymentAsync(MCashRequest request, CancellationToken ct)
        {
            CapturedRequest = request;
            return Task.FromResult(_response);
        }

        public Task<MCashResponse> QueryStatusAsync(string sessionId, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }
    }

    private class TimeoutThenSuccessMCashClient : INibssMCashClient
    {
        private readonly MCashResponse _statusResponse;
        public bool StatusInquiryCalled { get; private set; }

        public TimeoutThenSuccessMCashClient(MCashResponse statusResponse)
        {
            _statusResponse = statusResponse;
        }

        public async Task<MCashResponse> SubmitPaymentAsync(MCashRequest request, CancellationToken ct)
        {
            // Simulate a long-running USSD session that exceeds the timeout
            await Task.Delay(5_000, ct);
            return new MCashResponse("00", "Should not reach here", null);
        }

        public Task<MCashResponse> QueryStatusAsync(string sessionId, CancellationToken ct)
        {
            StatusInquiryCalled = true;
            return Task.FromResult(_statusResponse);
        }
    }

    private class ThrowingMCashClient : INibssMCashClient
    {
        public Task<MCashResponse> SubmitPaymentAsync(MCashRequest request, CancellationToken ct)
        {
            throw new HttpRequestException("Connection refused");
        }

        public Task<MCashResponse> QueryStatusAsync(string sessionId, CancellationToken ct)
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
                [PaymentChannel.MCash] = Breaker.State
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
