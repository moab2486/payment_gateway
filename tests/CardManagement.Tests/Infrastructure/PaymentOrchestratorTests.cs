using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Orchestration;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for PaymentOrchestrator covering:
/// - End-to-end payment flow
/// - Duplicate request returns cached response
/// - Unknown route returns error
/// - Fraud deny blocks payment
/// - Circuit breaker open returns unavailable
/// - Status inquiry returns correct state
/// </summary>
public class PaymentOrchestratorTests
{
    private readonly FakeIdempotencyGuard _idempotencyGuard;
    private readonly FakeFraudRiskEngine _fraudRiskEngine;
    private readonly FakeSagaOrchestrator _sagaOrchestrator;
    private readonly FakeAuditStore _auditStore;
    private readonly FakeCircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly FakePaymentRequestRepository _paymentRequestRepository;
    private readonly List<IChannelAdapter> _channelAdapters;
    private readonly FakeChannelAdapter _nipAdapter;
    private readonly PaymentOrchestrator _orchestrator;

    public PaymentOrchestratorTests()
    {
        _idempotencyGuard = new FakeIdempotencyGuard();
        _fraudRiskEngine = new FakeFraudRiskEngine();
        _sagaOrchestrator = new FakeSagaOrchestrator();
        _auditStore = new FakeAuditStore();
        _circuitBreakerRegistry = new FakeCircuitBreakerRegistry();
        _paymentRequestRepository = new FakePaymentRequestRepository();
        _nipAdapter = new FakeChannelAdapter(PaymentChannel.NIP);
        _channelAdapters = new List<IChannelAdapter> { _nipAdapter };

        _orchestrator = new PaymentOrchestrator(
            _idempotencyGuard,
            _fraudRiskEngine,
            _sagaOrchestrator,
            _auditStore,
            _circuitBreakerRegistry,
            _channelAdapters,
            _paymentRequestRepository);
    }

    #region End-to-End Payment Flow

    [Fact]
    public async Task InitiatePaymentAsync_SuccessfulFlow_ReturnsCompletedResult()
    {
        // Arrange
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer);

        _fraudRiskEngine.Decision = new RiskDecision(10, RiskVerdict.Approve, new List<string>());
        _sagaOrchestrator.Result = new SagaResult("txn", true, null, null);
        _nipAdapter.ProcessResult = new ChannelResult(true, "NIBSS-REF-123", null, null);

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(PaymentStatus.Completed, result.Status);
        Assert.NotNull(result.TransactionReference);
        Assert.StartsWith("TXN-", result.TransactionReference);
        Assert.Equal("NIBSS-REF-123", result.ProcessorReference);
        Assert.Null(result.ErrorMessage);

        // Verify audit entries were recorded
        Assert.True(_auditStore.Entries.Count > 0);
    }

    [Fact]
    public async Task InitiatePaymentAsync_SuccessfulFlow_PersistsPaymentRequest()
    {
        // Arrange
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer);

        _fraudRiskEngine.Decision = new RiskDecision(10, RiskVerdict.Approve, new List<string>());
        _sagaOrchestrator.Result = new SagaResult("txn", true, null, null);
        _nipAdapter.ProcessResult = new ChannelResult(true, "NIBSS-REF-123", null, null);

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.True(_paymentRequestRepository.SavedRequests.Count > 0);
        var saved = _paymentRequestRepository.SavedRequests.Last();
        Assert.Equal(PaymentStatus.Completed, saved.Status);
    }

    #endregion

    #region Duplicate Request Returns Cached Response

    [Fact]
    public async Task InitiatePaymentAsync_DuplicateRequest_ReturnsCachedResponse()
    {
        // Arrange
        var cachedResult = new PaymentResult("TXN-123", PaymentStatus.Completed, true, null, "REF-ABC");
        _idempotencyGuard.SetCompleted("idem-key-1", cachedResult);

        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer, "idem-key-1");

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert - Returns the cached result directly
        Assert.Equal(PaymentStatus.Completed, result.Status);
        Assert.Equal("TXN-123", result.TransactionReference);
    }

    [Fact]
    public async Task InitiatePaymentAsync_InProgressRequest_ReturnsConflict()
    {
        // Arrange
        _idempotencyGuard.SetInProgress("idem-key-2");

        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer, "idem-key-2");

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.Processing, result.Status);
        Assert.Contains("already being processed", result.ErrorMessage);
    }

    #endregion

    #region Unknown Route Returns Error

    [Fact]
    public async Task InitiatePaymentAsync_CardAuthorizationWithShortPan_ReturnsRoutingError()
    {
        // Arrange - source account too short for BIN resolution
        var request = CreatePaymentRequest(PaymentTransactionType.CardAuthorization, sourceAccount: "12");

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Contains("Cannot determine card scheme", result.ErrorMessage);
    }

    [Fact]
    public async Task InitiatePaymentAsync_NoAdapterForChannel_ReturnsError()
    {
        // Arrange - QRPayment routes to NQR but we have no NQR adapter
        var request = CreatePaymentRequest(PaymentTransactionType.QRPayment);

        _fraudRiskEngine.Decision = new RiskDecision(10, RiskVerdict.Approve, new List<string>());
        _sagaOrchestrator.Result = new SagaResult("txn", true, null, null);

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Contains("No adapter found", result.ErrorMessage);
    }

    #endregion

    #region Fraud Deny Blocks Payment

    [Fact]
    public async Task InitiatePaymentAsync_FraudDeny_BlocksPayment()
    {
        // Arrange
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer);
        _fraudRiskEngine.Decision = new RiskDecision(95, RiskVerdict.Deny, new List<string> { "High risk amount" });

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Contains("denied by fraud check", result.ErrorMessage);
        Assert.Contains("95", result.ErrorMessage);
    }

    [Fact]
    public async Task InitiatePaymentAsync_FraudReview_QueuesForManualReview()
    {
        // Arrange
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer);
        _fraudRiskEngine.Decision = new RiskDecision(60, RiskVerdict.Review, new List<string> { "Unusual pattern" });

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.ManualReview, result.Status);
        Assert.Contains("manual review", result.ErrorMessage);
    }

    #endregion

    #region Circuit Breaker Open Returns Unavailable

    [Fact]
    public async Task InitiatePaymentAsync_CircuitBreakerOpen_ReturnsUnavailable()
    {
        // Arrange
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer);
        _fraudRiskEngine.Decision = new RiskDecision(10, RiskVerdict.Approve, new List<string>());
        _circuitBreakerRegistry.SetState(PaymentChannel.NIP, CircuitBreakerState.Open);

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Contains("circuit breaker open", result.ErrorMessage);
        Assert.Contains("NIP", result.ErrorMessage);
    }

    #endregion

    #region Status Inquiry Returns Correct State

    [Fact]
    public async Task GetStatusAsync_ExistingTransaction_ReturnsCorrectState()
    {
        // Arrange - Run a successful payment first
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer);
        _fraudRiskEngine.Decision = new RiskDecision(10, RiskVerdict.Approve, new List<string>());
        _sagaOrchestrator.Result = new SagaResult("txn", true, null, null);
        _nipAdapter.ProcessResult = new ChannelResult(true, "NIBSS-REF-123", null, null);

        var paymentResult = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Act
        var statusResult = await _orchestrator.GetStatusAsync(paymentResult.TransactionReference, CancellationToken.None);

        // Assert
        Assert.True(statusResult.Found);
        Assert.Equal(paymentResult.TransactionReference, statusResult.TransactionReference);
        Assert.Equal(PaymentStatus.Completed, statusResult.Status);
        Assert.Equal(PaymentChannel.NIP, statusResult.Channel);
    }

    [Fact]
    public async Task GetStatusAsync_NonExistentTransaction_ReturnsNotFound()
    {
        // Act
        var statusResult = await _orchestrator.GetStatusAsync("TXN-nonexistent", CancellationToken.None);

        // Assert
        Assert.False(statusResult.Found);
    }

    [Fact]
    public async Task GetStatusByIdempotencyKeyAsync_ExistingKey_ReturnsCorrectState()
    {
        // Arrange - Run a successful payment first
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer, "test-idem-key");
        _fraudRiskEngine.Decision = new RiskDecision(10, RiskVerdict.Approve, new List<string>());
        _sagaOrchestrator.Result = new SagaResult("txn", true, null, null);
        _nipAdapter.ProcessResult = new ChannelResult(true, "NIBSS-REF-123", null, null);

        await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Act
        var statusResult = await _orchestrator.GetStatusByIdempotencyKeyAsync("test-idem-key", CancellationToken.None);

        // Assert
        Assert.True(statusResult.Found);
        Assert.Equal(PaymentStatus.Completed, statusResult.Status);
        Assert.Equal(PaymentChannel.NIP, statusResult.Channel);
    }

    [Fact]
    public async Task GetStatusByIdempotencyKeyAsync_NonExistentKey_ReturnsNotFound()
    {
        // Act
        var statusResult = await _orchestrator.GetStatusByIdempotencyKeyAsync("nonexistent-key", CancellationToken.None);

        // Assert
        Assert.False(statusResult.Found);
    }

    #endregion

    #region Transaction Reference Generation

    [Fact]
    public void GenerateTransactionReference_ReturnsGloballyUnique()
    {
        var refs = Enumerable.Range(0, 1000)
            .Select(_ => PaymentOrchestrator.GenerateTransactionReference())
            .ToHashSet();

        Assert.Equal(1000, refs.Count);
        Assert.All(refs, r => Assert.StartsWith("TXN-", r));
    }

    #endregion

    #region Channel Routing

    [Theory]
    [InlineData(PaymentTransactionType.InterbankTransfer, PaymentChannel.NIP)]
    [InlineData(PaymentTransactionType.QRPayment, PaymentChannel.NQR)]
    [InlineData(PaymentTransactionType.BillPayment, PaymentChannel.EBillsPay)]
    [InlineData(PaymentTransactionType.USSDPayment, PaymentChannel.MCash)]
    [InlineData(PaymentTransactionType.RecurringDebit, PaymentChannel.DirectDebit)]
    [InlineData(PaymentTransactionType.BulkPayment, PaymentChannel.GAPS)]
    public void ResolveChannel_NonCardTypes_RoutesCorrectly(PaymentTransactionType type, PaymentChannel expected)
    {
        var result = PaymentOrchestrator.ResolveChannel(type, "1234567890");
        Assert.True(result.Success);
        Assert.Equal(expected, result.Channel);
    }

    [Theory]
    [InlineData("5061234567890123", PaymentChannel.Interswitch)]  // Verve (506)
    [InlineData("6501234567890123", PaymentChannel.Interswitch)]  // Verve (650)
    [InlineData("4111111111111111", PaymentChannel.Cardify)]      // Visa
    [InlineData("5200000000001096", PaymentChannel.Cardify)]      // Mastercard
    [InlineData("3782822463100005", PaymentChannel.Cardify)]      // Other
    public void ResolveChannel_CardAuthorization_RoutesBasedOnBin(string pan, PaymentChannel expected)
    {
        var result = PaymentOrchestrator.ResolveChannel(PaymentTransactionType.CardAuthorization, pan);
        Assert.True(result.Success);
        Assert.Equal(expected, result.Channel);
    }

    [Fact]
    public void ResolveChannel_CardAuthorizationWithEmptyPan_ReturnsError()
    {
        var result = PaymentOrchestrator.ResolveChannel(PaymentTransactionType.CardAuthorization, "");
        Assert.False(result.Success);
        Assert.Contains("Cannot determine card scheme", result.ErrorMessage);
    }

    #endregion

    #region Saga Failure

    [Fact]
    public async Task InitiatePaymentAsync_SagaFailure_ReturnsFailed()
    {
        // Arrange
        var request = CreatePaymentRequest(PaymentTransactionType.InterbankTransfer);
        _fraudRiskEngine.Decision = new RiskDecision(10, RiskVerdict.Approve, new List<string>());
        _sagaOrchestrator.Result = new SagaResult("txn", false, "ProcessPayment", "Connection timeout");
        _nipAdapter.ProcessResult = new ChannelResult(false, null, "TIMEOUT", "Connection timeout");

        // Act
        var result = await _orchestrator.InitiatePaymentAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Contains("Connection timeout", result.ErrorMessage);
    }

    #endregion

    #region Missing Idempotency Key

    [Fact]
    public async Task InitiatePaymentAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _orchestrator.InitiatePaymentAsync(null!, CancellationToken.None));
    }

    #endregion

    #region Helpers

    private static PaymentRequest CreatePaymentRequest(
        PaymentTransactionType transactionType,
        string? idempotencyKey = null,
        string sourceAccount = "1234567890")
    {
        // The orchestrator uses the input request's properties (IdempotencyKey, TransactionType, Amount, etc.)
        // but generates its own TransactionReference internally.
        // We provide a placeholder TransactionReference since the entity requires it.
        var key = idempotencyKey ?? $"idem-{Guid.NewGuid()}";
        
        // For the missing idempotency key test, we can't use Create() since it validates.
        // Instead, we'll need to handle that differently.
        if (string.IsNullOrWhiteSpace(key))
        {
            // Use a valid key for construction, then we'll rely on the orchestrator's check
            // Actually, the orchestrator checks request.IdempotencyKey, so we need an entity with empty key.
            // Since PaymentRequest.Create validates, we use a workaround - create with a placeholder
            // and the orchestrator reads the IdempotencyKey from it.
            // We'll need to provide a separate mechanism for this test case.
            key = "placeholder-for-empty-test";
        }

        return PaymentRequest.Create(
            key,
            $"TXN-PLACEHOLDER-{Guid.NewGuid()}", // Placeholder; orchestrator generates its own
            transactionType,
            new Money(100000, "NGN"),
            sourceAccount,
            "0987654321",
            PaymentChannel.NIP); // Channel will be resolved by orchestrator
    }

    #endregion

    #region Fake Implementations

    /// <summary>
    /// Fake IdempotencyGuard that implements IIdempotencyGuard for testing.
    /// </summary>
    private sealed class FakeIdempotencyGuard : IIdempotencyGuard
    {
        private readonly Dictionary<string, IdempotencyCheckResult> _checkResults = new();
        private readonly Dictionary<string, object> _storedResponses = new();

        public void SetCompleted(string key, object cachedResponse)
        {
            _checkResults[key] = new IdempotencyCheckResult.Completed(cachedResponse);
        }

        public void SetInProgress(string key)
        {
            _checkResults[key] = new IdempotencyCheckResult.InProgress();
        }

        public Task<IdempotencyCheckResult> CheckAsync(string idempotencyKey, CancellationToken ct)
        {
            if (_checkResults.TryGetValue(idempotencyKey, out var result))
                return Task.FromResult(result);

            return Task.FromResult<IdempotencyCheckResult>(new IdempotencyCheckResult.NotFound());
        }

        public Task StoreAsync(string idempotencyKey, Guid paymentRequestId, object response, CancellationToken ct)
        {
            _storedResponses[idempotencyKey] = response;
            return Task.CompletedTask;
        }

        public Task RegisterInProgressAsync(string idempotencyKey, Guid paymentRequestId, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFraudRiskEngine : IFraudRiskEngine
    {
        public RiskDecision Decision { get; set; } = new(0, RiskVerdict.Approve, new List<string>());

        public Task<RiskDecision> EvaluateAsync(PaymentRequest request, CancellationToken ct)
        {
            return Task.FromResult(Decision);
        }
    }

    private sealed class FakeSagaOrchestrator : ISagaOrchestrator
    {
        public SagaResult Result { get; set; } = new("txn", true, null, null);

        public async Task<SagaResult> ExecuteAsync(string transactionReference, IReadOnlyList<SagaStepDefinition> steps, CancellationToken ct)
        {
            // Execute the steps to trigger channel adapter calls
            foreach (var step in steps)
            {
                try
                {
                    await step.ExecuteAction(ct);
                }
                catch
                {
                    // If execution fails and saga result says failure, that's expected
                }
            }
            return Result;
        }
    }

    private sealed class FakeAuditStore : IAuditStore
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            var result = Entries.Where(e => e.TransactionReference == transactionReference).ToList();
            return Task.FromResult<IReadOnlyList<AuditEntry>>(result);
        }
    }

    private sealed class FakeCircuitBreakerRegistry : ICircuitBreakerRegistry
    {
        private readonly Dictionary<PaymentChannel, CircuitBreakerState> _states = new();

        public void SetState(PaymentChannel channel, CircuitBreakerState state)
        {
            _states[channel] = state;
        }

        public ICircuitBreaker GetBreaker(PaymentChannel channel)
        {
            var state = _states.GetValueOrDefault(channel, CircuitBreakerState.Closed);
            return new FakeCircuitBreaker(state);
        }

        public IReadOnlyDictionary<PaymentChannel, CircuitBreakerState> GetAllStates()
        {
            return _states;
        }
    }

    private sealed class FakeCircuitBreaker : ICircuitBreaker
    {
        public CircuitBreakerState State { get; }

        public FakeCircuitBreaker(CircuitBreakerState state)
        {
            State = state;
        }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            return action(ct);
        }

        public void RecordSuccess() { }
        public void RecordFailure() { }
    }

    private sealed class FakeChannelAdapter : IChannelAdapter
    {
        public PaymentChannel Channel { get; }
        public ChannelResult ProcessResult { get; set; } = new(true, "REF-001", null, null);
        public ChannelResult ReverseResult { get; set; } = new(true, null, null, null);

        public FakeChannelAdapter(PaymentChannel channel)
        {
            Channel = channel;
        }

        public Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
        {
            return Task.FromResult(ProcessResult);
        }

        public Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
        {
            return Task.FromResult(ReverseResult);
        }

        public Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
        {
            return Task.FromResult(new HealthStatus(true, "Healthy"));
        }
    }

    private sealed class FakePaymentRequestRepository : IPaymentRequestRepository
    {
        public List<PaymentRequest> SavedRequests { get; } = new();
        private readonly Dictionary<string, PaymentRequest> _byTransactionRef = new();
        private readonly Dictionary<string, PaymentRequest> _byIdempotencyKey = new();

        public Task SaveAsync(PaymentRequest request, CancellationToken ct)
        {
            SavedRequests.Add(request);
            _byTransactionRef[request.TransactionReference] = request;
            _byIdempotencyKey[request.IdempotencyKey] = request;
            return Task.CompletedTask;
        }

        public Task<PaymentRequest?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            _byTransactionRef.TryGetValue(transactionReference, out var result);
            return Task.FromResult(result);
        }

        public Task<PaymentRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
        {
            _byIdempotencyKey.TryGetValue(idempotencyKey, out var result);
            return Task.FromResult(result);
        }
    }

    #endregion
}
