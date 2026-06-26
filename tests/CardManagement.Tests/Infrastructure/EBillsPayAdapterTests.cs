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

public class EBillsPayAdapterTests
{
    private static readonly Dictionary<string, BillerInfo> DefaultBillerDirectory = new()
    {
        ["POWER001"] = new BillerInfo("POWER001", "Power Distribution Co.", 500m, 500_000m, true),
        ["WATER001"] = new BillerInfo("WATER001", "Water Corporation", 1_000m, 100_000m, true),
        ["TAX001"] = new BillerInfo("TAX001", "Federal Tax Authority", null, null, true),
        ["NOREF001"] = new BillerInfo("NOREF001", "No Ref Biller", 100m, 50_000m, false),
    };

    private static EBillsPayOptions DefaultOptions() => new()
    {
        BaseUrl = "https://ebillspay.nibss-plc.com.ng",
        ApiKey = "test-api-key",
        BillerDirectoryCacheTtlMinutes = 60
    };

    private static PaymentRequest CreateRequest(
        string sourceAccount = "058:1234567890",
        string destinationAccount = "POWER001:METER12345",
        long amountKobo = 50_000_00) // 50,000 NGN
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: Guid.NewGuid().ToString(),
            transactionType: PaymentTransactionType.BillPayment,
            amount: new Money(amountKobo, "NGN"),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.EBillsPay);
    }

    private static EBillsPayAdapter CreateAdapter(
        INibssEBillsPayClient? client = null,
        ICircuitBreakerRegistry? circuitBreakerRegistry = null,
        EBillsPayOptions? options = null,
        IReadOnlyDictionary<string, BillerInfo>? billerDirectory = null)
    {
        return new EBillsPayAdapter(
            client ?? new FakeEBillsPayClient(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            Options.Create(options ?? DefaultOptions()),
            NullLogger<EBillsPayAdapter>.Instance,
            billerDirectory ?? DefaultBillerDirectory);
    }

    [Fact]
    public async Task ProcessAsync_ValidBillPayment_ReturnsConfirmationReference()
    {
        var confirmationRef = "EBILLS-CONF-12345";
        var client = new FakeEBillsPayClient(
            new EBillsPayResponse("00", "Successful", confirmationRef));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(confirmationRef, result.ProcessorReference);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_InvalidBillerCode_IsRejected()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(destinationAccount: "UNKNOWN999:METER12345");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("Invalid biller code", result.ErrorMessage);
        Assert.Contains("UNKNOWN999", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_InvalidCustomerReference_IsRejected()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(destinationAccount: "POWER001:");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("Customer reference cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_AmountBelowMinimum_IsRejected()
    {
        var adapter = CreateAdapter();
        // POWER001 min is 500 NGN, provide 100 NGN (10000 kobo)
        var request = CreateRequest(amountKobo: 100_00, destinationAccount: "POWER001:METER12345");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("below the minimum", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_AmountAboveMaximum_IsRejected()
    {
        var adapter = CreateAdapter();
        // POWER001 max is 500,000 NGN, provide 600,000 NGN (60000000 kobo)
        var request = CreateRequest(amountKobo: 600_000_00, destinationAccount: "POWER001:METER12345");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("above the maximum", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_BillerWithNoLimits_AcceptsAnyAmount()
    {
        var confirmationRef = "EBILLS-TAX-REF";
        var client = new FakeEBillsPayClient(
            new EBillsPayResponse("00", "Successful", confirmationRef));

        var adapter = CreateAdapter(client: client);
        // TAX001 has no min/max limits
        var request = CreateRequest(amountKobo: 1_000_000_00, destinationAccount: "TAX001:TAXID123");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(confirmationRef, result.ProcessorReference);
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
    public async Task ProcessAsync_NibssFailureResponse_ReturnsError()
    {
        var client = new FakeEBillsPayClient(
            new EBillsPayResponse("96", "System malfunction", null));

        var adapter = CreateAdapter(client: client);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_MALFUNCTION", result.ErrorCode);
    }

    [Fact]
    public async Task ProcessAsync_Success_RecordsSuccessOnCircuitBreaker()
    {
        var client = new FakeEBillsPayClient(
            new EBillsPayResponse("00", "Successful", "REF-123"));
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
        var client = new FakeEBillsPayClient(
            new EBillsPayResponse("96", "System malfunction", null));
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.Equal(0, registry.Breaker.SuccessCount);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_ExceptionDuringSubmission_RecordsFailureOnCircuitBreaker()
    {
        var client = new ThrowingEBillsPayClient();
        var registry = new FakeCircuitBreakerRegistry();
        var adapter = CreateAdapter(client: client, circuitBreakerRegistry: registry);
        var request = CreateRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("SYSTEM_ERROR", result.ErrorCode);
        Assert.Equal(1, registry.Breaker.FailureCount);
    }

    [Fact]
    public async Task ProcessAsync_MissingDestinationAccount_IsRejected()
    {
        var adapter = CreateAdapter();
        var request = CreateRequest(destinationAccount: "INVALID_FORMAT");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Contains("BillerCode:CustomerReference", result.ErrorMessage);
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
    public void Channel_ReturnsEBillsPay()
    {
        var adapter = CreateAdapter();
        Assert.Equal(PaymentChannel.EBillsPay, adapter.Channel);
    }

    [Fact]
    public void ParseDestination_ValidFormat_ReturnsParsedValues()
    {
        var (billerCode, customerRef, error) = EBillsPayAdapter.ParseDestination("POWER001:METER12345");

        Assert.Equal("POWER001", billerCode);
        Assert.Equal("METER12345", customerRef);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("", "Destination account is required")]
    [InlineData("NOBILLER", "BillerCode:CustomerReference")]
    [InlineData(":METER12345", "Biller code cannot be empty")]
    [InlineData("POWER001:", "Customer reference cannot be empty")]
    public void ParseDestination_InvalidInputs_ReturnsError(string destination, string expectedSubstring)
    {
        var (_, _, error) = EBillsPayAdapter.ParseDestination(destination);

        Assert.NotNull(error);
        Assert.Contains(expectedSubstring, error);
    }

    [Fact]
    public void ValidateAmount_WithinLimits_ReturnsNull()
    {
        var biller = new BillerInfo("TEST", "Test Biller", 100m, 10_000m, true);

        var result = EBillsPayAdapter.ValidateAmount(5_000m, biller);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateAmount_BelowMinimum_ReturnsError()
    {
        var biller = new BillerInfo("TEST", "Test Biller", 100m, 10_000m, true);

        var result = EBillsPayAdapter.ValidateAmount(50m, biller);

        Assert.NotNull(result);
        Assert.Contains("below the minimum", result);
    }

    [Fact]
    public void ValidateAmount_AboveMaximum_ReturnsError()
    {
        var biller = new BillerInfo("TEST", "Test Biller", 100m, 10_000m, true);

        var result = EBillsPayAdapter.ValidateAmount(20_000m, biller);

        Assert.NotNull(result);
        Assert.Contains("above the maximum", result);
    }

    [Fact]
    public void ValidateAmount_NoLimits_ReturnsNull()
    {
        var biller = new BillerInfo("TEST", "Test Biller", null, null, true);

        var result = EBillsPayAdapter.ValidateAmount(1_000_000m, biller);

        Assert.Null(result);
    }

    #region Test Helpers

    private class FakeEBillsPayClient : INibssEBillsPayClient
    {
        private readonly EBillsPayResponse _response;

        public FakeEBillsPayClient(EBillsPayResponse? response = null)
        {
            _response = response ?? new EBillsPayResponse("00", "Successful", "DEFAULT-CONF-REF");
        }

        public Task<EBillsPayResponse> SubmitPaymentAsync(EBillsPayRequest request, CancellationToken ct)
        {
            return Task.FromResult(_response);
        }
    }

    private class ThrowingEBillsPayClient : INibssEBillsPayClient
    {
        public Task<EBillsPayResponse> SubmitPaymentAsync(EBillsPayRequest request, CancellationToken ct)
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
                [PaymentChannel.EBillsPay] = Breaker.State
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
