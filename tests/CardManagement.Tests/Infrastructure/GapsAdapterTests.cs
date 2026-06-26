using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Channels.Nibss;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class GapsAdapterTests
{
    #region Helper Methods

    private static GapsOptions DefaultGapsOptions() => new()
    {
        BaseUrl = "https://gaps.nibss-plc.com.ng",
        ApiKey = "test-api-key",
        MaxBatchSize = 1000,
        StatusPollIntervalMs = 5000
    };

    private static string BuildBatchItemsJson(params (string recipientAccount, string bankCode, decimal amount, string narration, string itemReference)[] items)
    {
        var list = items.Select(i => new
        {
            recipientAccount = i.recipientAccount,
            bankCode = i.bankCode,
            amount = i.amount,
            narration = i.narration,
            itemReference = i.itemReference
        });
        return JsonSerializer.Serialize(list);
    }

    private static PaymentRequest CreateGapsPaymentRequest(string batchItemsJson)
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: $"TX-{Guid.NewGuid():N}",
            transactionType: PaymentTransactionType.BulkPayment,
            amount: new Money(100_000_00, "NGN"),
            sourceAccount: "058:1234567890",
            destinationAccount: batchItemsJson,
            channel: PaymentChannel.GAPS);
    }

    private static GapsAdapter CreateGapsAdapter(
        INibssGapsClient? client = null,
        ICircuitBreakerRegistry? circuitBreakerRegistry = null,
        GapsOptions? options = null)
    {
        return new GapsAdapter(
            client ?? new FakeNibssGapsClient(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            Options.Create(options ?? DefaultGapsOptions()),
            NullLogger<GapsAdapter>.Instance);
    }

    #endregion

    #region Valid Batch Submits Successfully

    [Fact]
    public async Task ProcessAsync_ValidBatch_ReturnsSuccessWithNibssReference()
    {
        var nibssRef = "NIBSS-GAPS-REF-001";
        var client = new FakeNibssGapsClient(
            submitResponse: new GapsBatchResponse("00", "Successful", nibssRef, null));

        var adapter = CreateGapsAdapter(client: client);

        var batchJson = BuildBatchItemsJson(
            ("0123456789", "058", 50_000_00m, "Salary payment", "ITEM-001"),
            ("9876543210", "044", 30_000_00m, "Vendor payment", "ITEM-002"));

        var request = CreateGapsPaymentRequest(batchJson);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nibssRef, result.ProcessorReference);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    #endregion

    #region Invalid Item Rejects Entire Batch With Per-Item Errors

    [Fact]
    public async Task ProcessAsync_ItemWithEmptyRecipientAccount_RejectsBatchWithValidationError()
    {
        var adapter = CreateGapsAdapter();

        var batchJson = BuildBatchItemsJson(
            ("0123456789", "058", 50_000_00m, "Valid payment", "ITEM-001"),
            ("", "044", 30_000_00m, "Invalid payment", "ITEM-002"));

        var request = CreateGapsPaymentRequest(batchJson);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("BATCH_VALIDATION_FAILED", result.ErrorCode);
        Assert.Contains("ITEM-002", result.ErrorMessage);
        Assert.Contains("RecipientAccount", result.ErrorMessage);
    }

    #endregion

    #region Batch Size Exceeded Is Rejected

    [Fact]
    public async Task ProcessAsync_BatchSizeExceedsMax_RejectsWithValidationError()
    {
        var options = new GapsOptions
        {
            BaseUrl = "https://gaps.nibss-plc.com.ng",
            ApiKey = "test-api-key",
            MaxBatchSize = 2,
            StatusPollIntervalMs = 5000
        };

        var adapter = CreateGapsAdapter(options: options);

        var batchJson = BuildBatchItemsJson(
            ("0123456789", "058", 10_000_00m, "Payment 1", "ITEM-001"),
            ("9876543210", "044", 20_000_00m, "Payment 2", "ITEM-002"),
            ("5555555555", "011", 30_000_00m, "Payment 3", "ITEM-003"));

        var request = CreateGapsPaymentRequest(batchJson);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("BATCH_VALIDATION_FAILED", result.ErrorCode);
        Assert.Contains("exceeds maximum", result.ErrorMessage);
    }

    #endregion

    #region Status Inquiry Returns Per-Item Status

    [Fact]
    public async Task QueryBatchStatusAsync_ReturnsPerItemStatuses()
    {
        var batchRef = "GAPS-BATCH-REF-001";
        var itemStatuses = new List<GapsBatchItemStatus>
        {
            new("ITEM-001", "successful", null),
            new("ITEM-002", "failed", "Insufficient funds"),
            new("ITEM-003", "pending", null)
        };

        var client = new FakeNibssGapsClient(
            statusResponse: new GapsBatchStatusResponse("00", batchRef, itemStatuses));

        var adapter = CreateGapsAdapter(client: client);

        var result = await adapter.QueryBatchStatusAsync(batchRef, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(batchRef, result.BatchReference);
        Assert.Equal(3, result.ItemStatuses.Count);
        Assert.Equal("successful", result.ItemStatuses[0].Status);
        Assert.Equal("ITEM-001", result.ItemStatuses[0].ItemReference);
        Assert.Equal("failed", result.ItemStatuses[1].Status);
        Assert.Equal("Insufficient funds", result.ItemStatuses[1].ErrorMessage);
        Assert.Equal("pending", result.ItemStatuses[2].Status);
    }

    #endregion

    #region Circuit Breaker Integration

    [Fact]
    public async Task ProcessAsync_CircuitBreakerOpen_ReturnsChannelUnavailableWithoutCallingClient()
    {
        var client = new FakeNibssGapsClient();
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);

        var adapter = CreateGapsAdapter(client: client, circuitBreakerRegistry: registry);

        var batchJson = BuildBatchItemsJson(
            ("0123456789", "058", 50_000_00m, "Payment", "ITEM-001"));

        var request = CreateGapsPaymentRequest(batchJson);

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("circuit breaker is open", result.ErrorMessage);
        Assert.False(client.SubmitBatchWasCalled);
    }

    #endregion

    #region Empty Batch Returns Validation Failure

    [Fact]
    public async Task ProcessAsync_EmptyBatch_ReturnsValidationFailure()
    {
        var adapter = CreateGapsAdapter();
        var request = CreateGapsPaymentRequest("[]");

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("BATCH_VALIDATION_FAILED", result.ErrorCode);
        Assert.Contains("no items", result.ErrorMessage);
    }

    #endregion

    #region ReverseAsync Returns NOT_SUPPORTED

    [Fact]
    public async Task ReverseAsync_ReturnsNotSupported()
    {
        var adapter = CreateGapsAdapter();

        var result = await adapter.ReverseAsync("TX-REF-001", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("NOT_SUPPORTED", result.ErrorCode);
        Assert.Contains("does not support", result.ErrorMessage);
    }

    #endregion

    #region Test Helpers

    private class FakeNibssGapsClient : INibssGapsClient
    {
        private readonly GapsBatchResponse _submitResponse;
        private readonly GapsBatchStatusResponse _statusResponse;

        public bool SubmitBatchWasCalled { get; private set; }

        public FakeNibssGapsClient(
            GapsBatchResponse? submitResponse = null,
            GapsBatchStatusResponse? statusResponse = null)
        {
            _submitResponse = submitResponse ?? new GapsBatchResponse("00", "Successful", "DEFAULT-NIBSS-REF", null);
            _statusResponse = statusResponse ?? new GapsBatchStatusResponse("00", "DEFAULT-BATCH-REF", Array.Empty<GapsBatchItemStatus>());
        }

        public Task<GapsBatchResponse> SubmitBatchAsync(GapsBatchRequest request, CancellationToken ct)
        {
            SubmitBatchWasCalled = true;
            return Task.FromResult(_submitResponse);
        }

        public Task<GapsBatchStatusResponse> QueryBatchStatusAsync(string batchReference, CancellationToken ct)
        {
            return Task.FromResult(_statusResponse);
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
                [PaymentChannel.GAPS] = Breaker.State
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
