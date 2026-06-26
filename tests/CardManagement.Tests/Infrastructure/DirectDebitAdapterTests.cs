using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
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

public class DirectDebitAdapterTests
{
    #region DirectDebitAdapter Tests

    private static DirectDebitOptions DefaultDirectDebitOptions() => new()
    {
        BaseUrl = "https://directdebit.nibss-plc.com.ng",
        ApiKey = "test-api-key",
        MaxRetries = 3,
        RetryIntervalHours = 24
    };

    private static PaymentRequest CreateDirectDebitPaymentRequest(
        string transactionReference = "MREF001|Monthly|2024-01-01|2025-01-01",
        string sourceAccount = "058:1234567890",
        string destinationAccount = "044:0987654321",
        long amountKobo = 50_000_00)
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: transactionReference,
            transactionType: PaymentTransactionType.RecurringDebit,
            amount: new Money(amountKobo, "NGN"),
            sourceAccount: sourceAccount,
            destinationAccount: destinationAccount,
            channel: PaymentChannel.DirectDebit);
    }

    private static DirectDebitAdapter CreateDirectDebitAdapter(
        INibssDirectDebitClient? client = null,
        ICircuitBreakerRegistry? circuitBreakerRegistry = null,
        DirectDebitOptions? options = null)
    {
        return new DirectDebitAdapter(
            client ?? new FakeNibssDirectDebitClient(),
            circuitBreakerRegistry ?? new FakeCircuitBreakerRegistry(),
            Options.Create(options ?? DefaultDirectDebitOptions()),
            NullLogger<DirectDebitAdapter>.Instance);
    }

    [Fact]
    public async Task ProcessAsync_SuccessfulMandateCreation_ReturnsChannelResultWithNibssReference()
    {
        var nibssRef = "NIBSS-DD-REF-001";
        var client = new FakeNibssDirectDebitClient(
            createResponse: new DirectDebitResponse("00", "Successful", nibssRef));

        var adapter = CreateDirectDebitAdapter(client: client);
        var request = CreateDirectDebitPaymentRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(nibssRef, result.ProcessorReference);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_CircuitBreakerOpen_ReturnsChannelUnavailable()
    {
        var registry = new FakeCircuitBreakerRegistry(CircuitBreakerState.Open);
        var adapter = CreateDirectDebitAdapter(circuitBreakerRegistry: registry);
        var request = CreateDirectDebitPaymentRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("circuit breaker is open", result.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_FailedResponse_ReturnsErrorCodeAndMessage()
    {
        var client = new FakeNibssDirectDebitClient(
            createResponse: new DirectDebitResponse("51", "No sufficient funds", null));

        var adapter = CreateDirectDebitAdapter(client: client);
        var request = CreateDirectDebitPaymentRequest();

        var result = await adapter.ProcessAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INSUFFICIENT_FUNDS", result.ErrorCode);
    }

    #endregion

    #region MandateLifecycleManager Tests

    private static MandateLifecycleManager CreateLifecycleManager(
        INibssDirectDebitClient? client = null,
        IMandateRepository? repository = null,
        DebitRetryScheduler? retryScheduler = null,
        IMandateEventPublisher? eventPublisher = null,
        DirectDebitOptions? options = null)
    {
        var opts = options ?? DefaultDirectDebitOptions();
        return new MandateLifecycleManager(
            client ?? new FakeNibssDirectDebitClient(),
            repository ?? new FakeMandateRepository(),
            retryScheduler ?? new DebitRetryScheduler(
                Options.Create(opts),
                NullLogger<DebitRetryScheduler>.Instance),
            eventPublisher ?? new FakeMandateEventPublisher(),
            Options.Create(opts),
            NullLogger<MandateLifecycleManager>.Instance);
    }

    private static DirectDebitMandateRequest CreateMandateRequest(
        string mandateReference = "MREF-001",
        string debtorAccount = "058:1234567890",
        string creditorAccount = "044:0987654321",
        decimal amount = 50_000_00m,
        string currencyCode = "NGN")
    {
        return new DirectDebitMandateRequest(
            MandateReference: mandateReference,
            DebtorAccount: debtorAccount,
            CreditorAccount: creditorAccount,
            Amount: amount,
            CurrencyCode: currencyCode,
            Frequency: MandateFrequency.Monthly,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddYears(1),
            TransactionReference: Guid.NewGuid().ToString());
    }

    [Fact]
    public async Task CreateMandateAsync_Success_ReturnsNibssReferenceAndSavesToRepository()
    {
        var nibssRef = "NIBSS-MANDATE-REF-100";
        var client = new FakeNibssDirectDebitClient(
            createResponse: new DirectDebitResponse("00", "Successful", nibssRef));
        var repository = new FakeMandateRepository();
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            eventPublisher: eventPublisher);

        var request = CreateMandateRequest();
        var result = await manager.CreateMandateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nibssRef, result.NibssReference);
        Assert.NotNull(result.MandateId);
        Assert.Single(repository.SavedMandates);
        Assert.Equal(nibssRef, repository.SavedMandates[0].NibssReference);
    }

    [Fact]
    public async Task SubmitScheduledDebitAsync_Success_ReturnsNibssReference()
    {
        var mandateRef = "MREF-DEBIT-001";
        var nibssRef = "NIBSS-DEBIT-REF-200";

        // Pre-populate repository with an active mandate
        var repository = new FakeMandateRepository();
        var mandate = DirectDebitMandate.Create(
            mandateReference: mandateRef,
            debtorAccount: "058:1234567890",
            creditorAccount: "044:0987654321",
            amount: new Money(50_000_00, "NGN"),
            frequency: MandateFrequency.Monthly,
            startDate: DateTime.UtcNow,
            endDate: DateTime.UtcNow.AddYears(1));
        await repository.SaveAsync(mandate, CancellationToken.None);

        var client = new FakeNibssDirectDebitClient(
            submitDebitResponse: new DirectDebitResponse("00", "Successful", nibssRef));
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            eventPublisher: eventPublisher);

        var result = await manager.SubmitScheduledDebitAsync(
            mandateRef, 10_000_00m, "NGN", "TX-REF-001", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(nibssRef, result.NibssReference);
    }

    [Fact]
    public async Task SubmitScheduledDebitWithRetryAsync_InsufficientFunds_ReturnsNeedsRetryWithCorrectRetryInfo()
    {
        var mandateRef = "MREF-RETRY-001";
        var txRef = "TX-RETRY-001";

        // Pre-populate repository with an active mandate
        var repository = new FakeMandateRepository();
        var mandate = DirectDebitMandate.Create(
            mandateReference: mandateRef,
            debtorAccount: "058:1234567890",
            creditorAccount: "044:0987654321",
            amount: new Money(50_000_00, "NGN"),
            frequency: MandateFrequency.Monthly,
            startDate: DateTime.UtcNow,
            endDate: DateTime.UtcNow.AddYears(1));
        await repository.SaveAsync(mandate, CancellationToken.None);

        // Return response code "51" (insufficient funds)
        var client = new FakeNibssDirectDebitClient(
            submitDebitResponse: new DirectDebitResponse("51", "No sufficient funds", null));

        var options = DefaultDirectDebitOptions();
        var retryScheduler = new DebitRetryScheduler(
            Options.Create(options),
            NullLogger<DebitRetryScheduler>.Instance);

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            retryScheduler: retryScheduler,
            options: options);

        var result = await manager.SubmitScheduledDebitWithRetryAsync(
            mandateRef, 10_000_00m, "NGN", txRef, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.NeedsRetry);
        Assert.False(result.RetriesExhausted);
        Assert.Equal(1, result.RetryCount);
        Assert.NotNull(result.NextRetryTime);
        Assert.Equal(mandate.Id, result.MandateId);
    }

    [Fact]
    public async Task CancelMandateAsync_Success_CallsCancelAndUpdatesRepository()
    {
        var mandateRef = "MREF-CANCEL-001";

        // Pre-populate repository with an active mandate
        var repository = new FakeMandateRepository();
        var mandate = DirectDebitMandate.Create(
            mandateReference: mandateRef,
            debtorAccount: "058:1234567890",
            creditorAccount: "044:0987654321",
            amount: new Money(50_000_00, "NGN"),
            frequency: MandateFrequency.Monthly,
            startDate: DateTime.UtcNow,
            endDate: DateTime.UtcNow.AddYears(1));
        await repository.SaveAsync(mandate, CancellationToken.None);

        var client = new FakeNibssDirectDebitClient(
            cancelResponse: new DirectDebitResponse("00", "Successful", "NIBSS-CANCEL-REF"));
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            eventPublisher: eventPublisher);

        var result = await manager.CancelMandateAsync(mandateRef, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Verify mandate status was changed to Cancelled
        var updatedMandate = await repository.GetByMandateReferenceAsync(mandateRef, CancellationToken.None);
        Assert.NotNull(updatedMandate);
        Assert.Equal(MandateStatus.Cancelled, updatedMandate.Status);
        // Verify it was updated in the repository
        Assert.Contains(mandate, repository.UpdatedMandates);
    }

    [Fact]
    public async Task CreateMandateAsync_Success_PublishesMandateCreatedEvent()
    {
        var nibssRef = "NIBSS-EVENT-REF";
        var client = new FakeNibssDirectDebitClient(
            createResponse: new DirectDebitResponse("00", "Successful", nibssRef));
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            eventPublisher: eventPublisher);

        var request = CreateMandateRequest(
            mandateReference: "MREF-EVT-001",
            debtorAccount: "058:1111111111",
            creditorAccount: "044:2222222222",
            amount: 75_000_00m);

        await manager.CreateMandateAsync(request, CancellationToken.None);

        Assert.Single(eventPublisher.CreatedEvents);
        Assert.Equal("MREF-EVT-001", eventPublisher.CreatedEvents[0].MandateReference);
        Assert.Equal("058:1111111111", eventPublisher.CreatedEvents[0].DebtorAccount);
        Assert.Equal("044:2222222222", eventPublisher.CreatedEvents[0].CreditorAccount);
        Assert.Equal(75_000_00m, eventPublisher.CreatedEvents[0].Amount);
    }

    [Fact]
    public async Task ActivateMandateAsync_Success_PublishesMandateActivatedEvent()
    {
        var mandateRef = "MREF-ACTIVATE-001";

        var repository = new FakeMandateRepository();
        var mandate = DirectDebitMandate.Create(
            mandateReference: mandateRef,
            debtorAccount: "058:1234567890",
            creditorAccount: "044:0987654321",
            amount: new Money(50_000_00, "NGN"),
            frequency: MandateFrequency.Monthly,
            startDate: DateTime.UtcNow,
            endDate: DateTime.UtcNow.AddYears(1));
        await repository.SaveAsync(mandate, CancellationToken.None);

        var client = new FakeNibssDirectDebitClient(
            activateResponse: new DirectDebitResponse("00", "Successful", "NIBSS-ACT-REF"));
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            eventPublisher: eventPublisher);

        await manager.ActivateMandateAsync(mandateRef, CancellationToken.None);

        Assert.Single(eventPublisher.ActivatedEvents);
        Assert.Equal(mandateRef, eventPublisher.ActivatedEvents[0]);
    }

    [Fact]
    public async Task SubmitScheduledDebitAsync_Success_PublishesMandateDebitedEvent()
    {
        var mandateRef = "MREF-DEBIT-EVT-001";
        var txRef = "TX-DEBIT-EVT-001";

        var repository = new FakeMandateRepository();
        var mandate = DirectDebitMandate.Create(
            mandateReference: mandateRef,
            debtorAccount: "058:1234567890",
            creditorAccount: "044:0987654321",
            amount: new Money(50_000_00, "NGN"),
            frequency: MandateFrequency.Monthly,
            startDate: DateTime.UtcNow,
            endDate: DateTime.UtcNow.AddYears(1));
        await repository.SaveAsync(mandate, CancellationToken.None);

        var client = new FakeNibssDirectDebitClient(
            submitDebitResponse: new DirectDebitResponse("00", "Successful", "NIBSS-D-REF"));
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            eventPublisher: eventPublisher);

        await manager.SubmitScheduledDebitAsync(
            mandateRef, 25_000_00m, "NGN", txRef, CancellationToken.None);

        Assert.Single(eventPublisher.DebitedEvents);
        Assert.Equal(mandateRef, eventPublisher.DebitedEvents[0].MandateReference);
        Assert.Equal(txRef, eventPublisher.DebitedEvents[0].TransactionReference);
        Assert.Equal(25_000_00m, eventPublisher.DebitedEvents[0].Amount);
    }

    [Fact]
    public async Task SubmitScheduledDebitAsync_Failure_PublishesMandateFailedEvent()
    {
        var mandateRef = "MREF-FAIL-EVT-001";
        var txRef = "TX-FAIL-EVT-001";

        var repository = new FakeMandateRepository();
        var mandate = DirectDebitMandate.Create(
            mandateReference: mandateRef,
            debtorAccount: "058:1234567890",
            creditorAccount: "044:0987654321",
            amount: new Money(50_000_00, "NGN"),
            frequency: MandateFrequency.Monthly,
            startDate: DateTime.UtcNow,
            endDate: DateTime.UtcNow.AddYears(1));
        await repository.SaveAsync(mandate, CancellationToken.None);

        var client = new FakeNibssDirectDebitClient(
            submitDebitResponse: new DirectDebitResponse("51", "No sufficient funds", null));
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            eventPublisher: eventPublisher);

        await manager.SubmitScheduledDebitAsync(
            mandateRef, 25_000_00m, "NGN", txRef, CancellationToken.None);

        Assert.Single(eventPublisher.FailedEvents);
        Assert.Equal(mandateRef, eventPublisher.FailedEvents[0].MandateReference);
        Assert.Equal(txRef, eventPublisher.FailedEvents[0].TransactionReference);
    }

    [Fact]
    public async Task CancelMandateAsync_Success_PublishesMandateCancelledEvent()
    {
        var mandateRef = "MREF-CANCEL-EVT-001";

        var repository = new FakeMandateRepository();
        var mandate = DirectDebitMandate.Create(
            mandateReference: mandateRef,
            debtorAccount: "058:1234567890",
            creditorAccount: "044:0987654321",
            amount: new Money(50_000_00, "NGN"),
            frequency: MandateFrequency.Monthly,
            startDate: DateTime.UtcNow,
            endDate: DateTime.UtcNow.AddYears(1));
        await repository.SaveAsync(mandate, CancellationToken.None);

        var client = new FakeNibssDirectDebitClient(
            cancelResponse: new DirectDebitResponse("00", "Successful", "NIBSS-C-REF"));
        var eventPublisher = new FakeMandateEventPublisher();

        var manager = CreateLifecycleManager(
            client: client,
            repository: repository,
            eventPublisher: eventPublisher);

        await manager.CancelMandateAsync(mandateRef, CancellationToken.None);

        Assert.Single(eventPublisher.CancelledEvents);
        Assert.Equal(mandateRef, eventPublisher.CancelledEvents[0]);
    }

    #endregion

    #region DebitRetryScheduler Tests

    [Fact]
    public void RecordFailureAndEvaluate_FirstFailure_ReturnsRetryWithCount1()
    {
        var options = DefaultDirectDebitOptions();
        var scheduler = new DebitRetryScheduler(
            Options.Create(options),
            NullLogger<DebitRetryScheduler>.Instance);

        var (shouldRetry, retryCount, nextRetryTime) =
            scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001");

        Assert.True(shouldRetry);
        Assert.Equal(1, retryCount);
        Assert.NotNull(nextRetryTime);
    }

    [Fact]
    public void RecordFailureAndEvaluate_ExceedsMaxRetries_ReturnsExhausted()
    {
        var options = new DirectDebitOptions
        {
            BaseUrl = "https://test.com",
            ApiKey = "key",
            MaxRetries = 2,
            RetryIntervalHours = 24
        };
        var scheduler = new DebitRetryScheduler(
            Options.Create(options),
            NullLogger<DebitRetryScheduler>.Instance);

        // Record failures up to max
        scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001"); // attempt 1
        scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001"); // attempt 2

        // Next attempt exceeds max (3 > MaxRetries=2)
        var (shouldRetry, retryCount, nextRetryTime) =
            scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001");

        Assert.False(shouldRetry);
        Assert.Equal(3, retryCount);
        Assert.Null(nextRetryTime);
    }

    [Fact]
    public void RecordFailureAndEvaluate_TracksRetriesCorrectly()
    {
        var options = DefaultDirectDebitOptions(); // MaxRetries = 3
        var scheduler = new DebitRetryScheduler(
            Options.Create(options),
            NullLogger<DebitRetryScheduler>.Instance);

        var result1 = scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001");
        Assert.True(result1.ShouldRetry);
        Assert.Equal(1, result1.RetryCount);

        var result2 = scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001");
        Assert.True(result2.ShouldRetry);
        Assert.Equal(2, result2.RetryCount);

        var result3 = scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001");
        Assert.True(result3.ShouldRetry);
        Assert.Equal(3, result3.RetryCount);

        // Fourth attempt exceeds MaxRetries=3
        var result4 = scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001");
        Assert.False(result4.ShouldRetry);
        Assert.Equal(4, result4.RetryCount);
    }

    [Fact]
    public void ClearRetryEntry_RemovesTracking()
    {
        var options = DefaultDirectDebitOptions();
        var scheduler = new DebitRetryScheduler(
            Options.Create(options),
            NullLogger<DebitRetryScheduler>.Instance);

        scheduler.RecordFailureAndEvaluate("MREF-001", "TX-001");
        Assert.Equal(1, scheduler.GetRetryCount("MREF-001", "TX-001"));

        scheduler.ClearRetryEntry("MREF-001", "TX-001");
        Assert.Equal(0, scheduler.GetRetryCount("MREF-001", "TX-001"));
    }

    #endregion

    #region Test Helpers

    private class FakeNibssDirectDebitClient : INibssDirectDebitClient
    {
        private readonly DirectDebitResponse _createResponse;
        private readonly DirectDebitResponse _activateResponse;
        private readonly DirectDebitResponse _submitDebitResponse;
        private readonly DirectDebitResponse _cancelResponse;

        public FakeNibssDirectDebitClient(
            DirectDebitResponse? createResponse = null,
            DirectDebitResponse? activateResponse = null,
            DirectDebitResponse? submitDebitResponse = null,
            DirectDebitResponse? cancelResponse = null)
        {
            _createResponse = createResponse ?? new DirectDebitResponse("00", "Successful", "DEFAULT-DD-REF");
            _activateResponse = activateResponse ?? new DirectDebitResponse("00", "Successful", "DEFAULT-ACT-REF");
            _submitDebitResponse = submitDebitResponse ?? new DirectDebitResponse("00", "Successful", "DEFAULT-DEBIT-REF");
            _cancelResponse = cancelResponse ?? new DirectDebitResponse("00", "Successful", "DEFAULT-CANCEL-REF");
        }

        public Task<DirectDebitResponse> CreateMandateAsync(DirectDebitMandateRequest request, CancellationToken ct)
            => Task.FromResult(_createResponse);

        public Task<DirectDebitResponse> ActivateMandateAsync(string mandateReference, CancellationToken ct)
            => Task.FromResult(_activateResponse);

        public Task<DirectDebitResponse> SubmitDebitAsync(DirectDebitSubmitRequest request, CancellationToken ct)
            => Task.FromResult(_submitDebitResponse);

        public Task<DirectDebitResponse> CancelMandateAsync(string mandateReference, CancellationToken ct)
            => Task.FromResult(_cancelResponse);
    }

    private class FakeMandateRepository : IMandateRepository
    {
        private readonly List<DirectDebitMandate> _mandates = new();
        public List<DirectDebitMandate> SavedMandates { get; } = new();
        public List<DirectDebitMandate> UpdatedMandates { get; } = new();

        public Task SaveAsync(DirectDebitMandate mandate, CancellationToken ct)
        {
            _mandates.Add(mandate);
            SavedMandates.Add(mandate);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DirectDebitMandate mandate, CancellationToken ct)
        {
            UpdatedMandates.Add(mandate);
            return Task.CompletedTask;
        }

        public Task<DirectDebitMandate?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult(_mandates.FirstOrDefault(m => m.Id == id));

        public Task<DirectDebitMandate?> GetByMandateReferenceAsync(string mandateReference, CancellationToken ct)
            => Task.FromResult(_mandates.FirstOrDefault(m => m.MandateReference == mandateReference));

        public Task<IReadOnlyList<DirectDebitMandate>> GetByStatusAsync(MandateStatus status, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DirectDebitMandate>>(
                _mandates.Where(m => m.Status == status).ToList().AsReadOnly());
    }

    private class FakeMandateEventPublisher : IMandateEventPublisher
    {
        public List<(string MandateReference, string DebtorAccount, string CreditorAccount, decimal Amount)> CreatedEvents { get; } = new();
        public List<string> ActivatedEvents { get; } = new();
        public List<(string MandateReference, string TransactionReference, decimal Amount)> DebitedEvents { get; } = new();
        public List<(string MandateReference, string TransactionReference, string ErrorCode, string ErrorMessage)> FailedEvents { get; } = new();
        public List<string> CancelledEvents { get; } = new();

        public Task PublishMandateCreatedAsync(string mandateReference, string debtorAccount, string creditorAccount, decimal amount, CancellationToken ct)
        {
            CreatedEvents.Add((mandateReference, debtorAccount, creditorAccount, amount));
            return Task.CompletedTask;
        }

        public Task PublishMandateActivatedAsync(string mandateReference, CancellationToken ct)
        {
            ActivatedEvents.Add(mandateReference);
            return Task.CompletedTask;
        }

        public Task PublishMandateDebitedAsync(string mandateReference, string transactionReference, decimal amount, CancellationToken ct)
        {
            DebitedEvents.Add((mandateReference, transactionReference, amount));
            return Task.CompletedTask;
        }

        public Task PublishMandateFailedAsync(string mandateReference, string transactionReference, string errorCode, string errorMessage, CancellationToken ct)
        {
            FailedEvents.Add((mandateReference, transactionReference, errorCode, errorMessage));
            return Task.CompletedTask;
        }

        public Task PublishMandateCancelledAsync(string mandateReference, CancellationToken ct)
        {
            CancelledEvents.Add(mandateReference);
            return Task.CompletedTask;
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
                [PaymentChannel.DirectDebit] = Breaker.State
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
