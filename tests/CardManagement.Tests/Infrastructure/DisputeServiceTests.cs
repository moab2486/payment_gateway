using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Disputes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for DisputeService covering:
/// - Dispute creation validates timeframe
/// - Lifecycle state transitions
/// - Resolution in favour triggers credit
/// - Resolution against closes record
/// - Events published for each transition
/// </summary>
public class DisputeServiceTests
{
    private readonly FakeDisputeRepository _disputeRepository;
    private readonly FakePaymentRequestRepository _paymentRequestRepository;
    private readonly FakePaymentOrchestrator _paymentOrchestrator;
    private readonly FakeAuditStore _auditStore;
    private readonly FakeDisputeEventPublisher _eventPublisher;
    private readonly DisputeOptions _options;
    private readonly DisputeService _service;

    public DisputeServiceTests()
    {
        _disputeRepository = new FakeDisputeRepository();
        _paymentRequestRepository = new FakePaymentRequestRepository();
        _paymentOrchestrator = new FakePaymentOrchestrator();
        _auditStore = new FakeAuditStore();
        _eventPublisher = new FakeDisputeEventPublisher();
        _options = new DisputeOptions { MaxDisputeWindowDays = 120 };

        _service = new DisputeService(
            _disputeRepository,
            _paymentRequestRepository,
            _paymentOrchestrator,
            _auditStore,
            _eventPublisher,
            Options.Create(_options),
            NullLogger<DisputeService>.Instance);
    }

    #region Dispute Creation Validates Timeframe

    [Fact]
    public async Task RaiseDisputeAsync_WithinTimeframe_CreatesDispute()
    {
        // Arrange - transaction created 10 days ago (within 120-day window)
        var payment = CreateCompletedPayment(daysAgo: 10);
        _paymentRequestRepository.Add(payment);

        var request = new DisputeRequest(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), "Evidence text");

        // Act
        var result = await _service.RaiseDisputeAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(DisputeStatus.Opened, result.Status);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task RaiseDisputeAsync_OutsideTimeframe_RejectsDispute()
    {
        // Arrange - transaction created 150 days ago (outside 120-day window)
        var payment = CreateCompletedPayment(daysAgo: 150);
        _paymentRequestRepository.Add(payment);

        var request = new DisputeRequest(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);

        // Act
        var result = await _service.RaiseDisputeAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("expired", result.ErrorMessage);
        Assert.Contains("120", result.ErrorMessage);
    }

    [Fact]
    public async Task RaiseDisputeAsync_ExactlyAtBoundary_CreatesDispute()
    {
        // Arrange - transaction created exactly 120 days ago (at boundary)
        var payment = CreateCompletedPayment(daysAgo: 120);
        _paymentRequestRepository.Add(payment);

        var request = new DisputeRequest(
            payment.TransactionReference, "DUPLICATE", new Money(25000, "NGN"), null);

        // Act
        var result = await _service.RaiseDisputeAsync(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RaiseDisputeAsync_TransactionNotFound_ReturnsError()
    {
        // Arrange - no payment in repository
        var request = new DisputeRequest(
            "TXN-NONEXISTENT", "UNAUTHORIZED", new Money(50000, "NGN"), null);

        // Act
        var result = await _service.RaiseDisputeAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task RaiseDisputeAsync_CustomWindowDays_Validates()
    {
        // Arrange - use a 30-day window, transaction 45 days ago
        var shortWindowService = CreateServiceWithWindowDays(30);
        var payment = CreateCompletedPayment(daysAgo: 45);
        _paymentRequestRepository.Add(payment);

        var request = new DisputeRequest(
            payment.TransactionReference, "FRAUD", new Money(10000, "NGN"), null);

        // Act
        var result = await shortWindowService.RaiseDisputeAsync(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("expired", result.ErrorMessage);
    }

    #endregion

    #region Lifecycle State Transitions

    [Fact]
    public async Task DisputeRecord_FullLifecycle_OpensToClosedViaResolvedInFavour()
    {
        // Create a dispute and verify it starts as Opened
        var dispute = DisputeRecord.Create("TXN-001", "UNAUTHORIZED", new Money(50000, "NGN"), null);
        Assert.Equal(DisputeStatus.Opened, dispute.Status);

        // Opened → UnderReview
        dispute.MarkUnderReview();
        Assert.Equal(DisputeStatus.UnderReview, dispute.Status);

        // UnderReview → ResolvedInFavour
        dispute.ResolveInFavour();
        Assert.Equal(DisputeStatus.ResolvedInFavour, dispute.Status);
        Assert.NotNull(dispute.ResolvedAtUtc);

        // ResolvedInFavour → Closed
        dispute.Close();
        Assert.Equal(DisputeStatus.Closed, dispute.Status);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task DisputeRecord_FullLifecycle_OpensToClosedViaEscalated()
    {
        // Opened → UnderReview → Escalated → ResolvedAgainst → Closed
        var dispute = DisputeRecord.Create("TXN-002", "INCORRECT_AMOUNT", new Money(30000, "NGN"), "receipt.pdf");
        Assert.Equal(DisputeStatus.Opened, dispute.Status);

        dispute.MarkUnderReview();
        Assert.Equal(DisputeStatus.UnderReview, dispute.Status);

        dispute.Escalate();
        Assert.Equal(DisputeStatus.Escalated, dispute.Status);

        dispute.ResolveAgainst();
        Assert.Equal(DisputeStatus.ResolvedAgainst, dispute.Status);
        Assert.NotNull(dispute.ResolvedAtUtc);

        dispute.Close();
        Assert.Equal(DisputeStatus.Closed, dispute.Status);

        await Task.CompletedTask;
    }

    [Fact]
    public void DisputeRecord_InvalidTransition_OpenedToEscalated_Throws()
    {
        var dispute = DisputeRecord.Create("TXN-003", "FRAUD", new Money(100000, "NGN"), null);

        // Cannot escalate directly from Opened (must go through UnderReview)
        var ex = Assert.Throws<InvalidOperationException>(() => dispute.Escalate());
        Assert.Contains("under review", ex.Message);
    }

    [Fact]
    public void DisputeRecord_InvalidTransition_OpenedToResolved_Throws()
    {
        var dispute = DisputeRecord.Create("TXN-004", "FRAUD", new Money(100000, "NGN"), null);

        // Cannot resolve directly from Opened
        var ex = Assert.Throws<InvalidOperationException>(() => dispute.ResolveInFavour());
        Assert.Contains("under review or escalated", ex.Message);
    }

    [Fact]
    public void DisputeRecord_InvalidTransition_ClosedToAnything_Throws()
    {
        var dispute = DisputeRecord.Create("TXN-005", "FRAUD", new Money(100000, "NGN"), null);
        dispute.MarkUnderReview();
        dispute.ResolveInFavour();
        dispute.Close();

        // Cannot transition from Closed
        Assert.Throws<InvalidOperationException>(() => dispute.MarkUnderReview());
    }

    [Fact]
    public void DisputeRecord_InvalidTransition_ResolvedInFavourToEscalated_Throws()
    {
        var dispute = DisputeRecord.Create("TXN-006", "FRAUD", new Money(100000, "NGN"), null);
        dispute.MarkUnderReview();
        dispute.ResolveInFavour();

        // Cannot escalate after resolution
        Assert.Throws<InvalidOperationException>(() => dispute.Escalate());
    }

    [Fact]
    public void DisputeRecord_EscalatedToResolvedInFavour_Succeeds()
    {
        var dispute = DisputeRecord.Create("TXN-007", "FRAUD", new Money(100000, "NGN"), null);
        dispute.MarkUnderReview();
        dispute.Escalate();

        // Can resolve from escalated state
        dispute.ResolveInFavour();
        Assert.Equal(DisputeStatus.ResolvedInFavour, dispute.Status);
    }

    #endregion

    #region Resolution In Favour Triggers Credit

    [Fact]
    public async Task ResolveDisputeAsync_InFavour_InitiatesCreditPayment()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        // Create a dispute and put it in UnderReview state
        var dispute = DisputeRecord.Create(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);
        dispute.MarkUnderReview();
        _disputeRepository.Add(dispute);

        _paymentOrchestrator.Result = new PaymentResult(
            "TXN-CREDIT-001", PaymentStatus.Completed, true, null, "REF-CREDIT");

        // Act
        var result = await _service.ResolveDisputeAsync(
            dispute.Id, new DisputeResolution(DisputeDecision.InFavour, "Verified fraud"), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(DisputeStatus.Closed, result.Status);

        // Verify credit was initiated
        Assert.Single(_paymentOrchestrator.InitiatedPayments);
        var creditPayment = _paymentOrchestrator.InitiatedPayments[0];
        Assert.Equal(payment.SourceAccount, creditPayment.DestinationAccount);
        Assert.Equal(payment.DestinationAccount, creditPayment.SourceAccount);
        Assert.Equal(50000, creditPayment.Amount.Amount);
    }

    [Fact]
    public async Task ResolveDisputeAsync_InFavour_CreditReferencesOriginalTransaction()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var dispute = DisputeRecord.Create(
            payment.TransactionReference, "UNAUTHORIZED", new Money(75000, "NGN"), null);
        dispute.MarkUnderReview();
        _disputeRepository.Add(dispute);

        _paymentOrchestrator.Result = new PaymentResult(
            "TXN-CREDIT", PaymentStatus.Completed, true, null, null);

        // Act
        await _service.ResolveDisputeAsync(
            dispute.Id, new DisputeResolution(DisputeDecision.InFavour, null), CancellationToken.None);

        // Assert - credit transaction reference links back to original
        var creditPayment = _paymentOrchestrator.InitiatedPayments[0];
        Assert.Contains(payment.TransactionReference, creditPayment.TransactionReference);
    }

    #endregion

    #region Resolution Against Closes Record

    [Fact]
    public async Task ResolveDisputeAsync_Against_ClosesDisputeRecord()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var dispute = DisputeRecord.Create(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);
        dispute.MarkUnderReview();
        _disputeRepository.Add(dispute);

        // Act
        var result = await _service.ResolveDisputeAsync(
            dispute.Id, new DisputeResolution(DisputeDecision.Against, "Insufficient evidence"), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(DisputeStatus.Closed, result.Status);
    }

    [Fact]
    public async Task ResolveDisputeAsync_Against_DoesNotInitiateCredit()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var dispute = DisputeRecord.Create(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);
        dispute.MarkUnderReview();
        _disputeRepository.Add(dispute);

        // Act
        await _service.ResolveDisputeAsync(
            dispute.Id, new DisputeResolution(DisputeDecision.Against, null), CancellationToken.None);

        // Assert - no credit initiated
        Assert.Empty(_paymentOrchestrator.InitiatedPayments);
    }

    [Fact]
    public async Task ResolveDisputeAsync_DisputeNotFound_ReturnsError()
    {
        // Act
        var result = await _service.ResolveDisputeAsync(
            Guid.NewGuid(), new DisputeResolution(DisputeDecision.InFavour, null), CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveDisputeAsync_DisputeInOpenedState_ReturnsError()
    {
        // Arrange - dispute is still in Opened state (not yet under review)
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var dispute = DisputeRecord.Create(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);
        // Explicitly NOT calling MarkUnderReview() — stays in Opened
        _disputeRepository.Add(dispute);

        // Act
        var result = await _service.ResolveDisputeAsync(
            dispute.Id, new DisputeResolution(DisputeDecision.InFavour, null), CancellationToken.None);

        // Assert - invalid state transition
        Assert.False(result.Success);
        Assert.Contains("under review or escalated", result.ErrorMessage);
    }

    #endregion

    #region Events Published for Each Transition

    [Fact]
    public async Task RaiseDisputeAsync_PublishesDisputeOpenedEvent()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var request = new DisputeRequest(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);

        // Act
        var result = await _service.RaiseDisputeAsync(request, CancellationToken.None);

        // Assert
        Assert.Single(_eventPublisher.OpenedEvents);
        var evt = _eventPublisher.OpenedEvents[0];
        Assert.Equal(result.Id, evt.DisputeId);
        Assert.Equal(payment.TransactionReference, evt.TransactionReference);
        Assert.Equal("UNAUTHORIZED", evt.ReasonCode);
        Assert.Equal(50000m, evt.Amount);
    }

    [Fact]
    public async Task ResolveDisputeAsync_InFavour_PublishesResolvedAndClosedEvents()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var dispute = DisputeRecord.Create(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);
        dispute.MarkUnderReview();
        _disputeRepository.Add(dispute);

        _paymentOrchestrator.Result = new PaymentResult(
            "TXN-CREDIT", PaymentStatus.Completed, true, null, null);

        // Act
        await _service.ResolveDisputeAsync(
            dispute.Id, new DisputeResolution(DisputeDecision.InFavour, null), CancellationToken.None);

        // Assert - both resolved and closed events published
        Assert.Single(_eventPublisher.ResolvedEvents);
        Assert.Equal("in-favour", _eventPublisher.ResolvedEvents[0].Resolution);
        Assert.Equal(dispute.Id, _eventPublisher.ResolvedEvents[0].DisputeId);

        Assert.Single(_eventPublisher.ClosedEvents);
        Assert.Equal(dispute.Id, _eventPublisher.ClosedEvents[0].DisputeId);
    }

    [Fact]
    public async Task ResolveDisputeAsync_Against_PublishesResolvedAndClosedEvents()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var dispute = DisputeRecord.Create(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);
        dispute.MarkUnderReview();
        _disputeRepository.Add(dispute);

        // Act
        await _service.ResolveDisputeAsync(
            dispute.Id, new DisputeResolution(DisputeDecision.Against, null), CancellationToken.None);

        // Assert
        Assert.Single(_eventPublisher.ResolvedEvents);
        Assert.Equal("against", _eventPublisher.ResolvedEvents[0].Resolution);

        Assert.Single(_eventPublisher.ClosedEvents);
        Assert.Equal(dispute.Id, _eventPublisher.ClosedEvents[0].DisputeId);
    }

    [Fact]
    public async Task RaiseDisputeAsync_RecordsAuditEntry()
    {
        // Arrange
        var payment = CreateCompletedPayment(daysAgo: 5);
        _paymentRequestRepository.Add(payment);

        var request = new DisputeRequest(
            payment.TransactionReference, "UNAUTHORIZED", new Money(50000, "NGN"), null);

        // Act
        await _service.RaiseDisputeAsync(request, CancellationToken.None);

        // Assert - at least one audit entry for the dispute-opened transition
        Assert.True(_auditStore.Entries.Count >= 1);
        var audit = _auditStore.Entries.First(e => e.Action == "dispute-opened");
        Assert.Equal(payment.TransactionReference, audit.TransactionReference);
    }

    #endregion

    #region GetDispute

    [Fact]
    public async Task GetDisputeAsync_Existing_ReturnsDispute()
    {
        // Arrange
        var dispute = DisputeRecord.Create("TXN-001", "FRAUD", new Money(10000, "NGN"), null);
        _disputeRepository.Add(dispute);

        // Act
        var result = await _service.GetDisputeAsync(dispute.Id, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(dispute.Id, result.Id);
        Assert.Equal("TXN-001", result.TransactionReference);
    }

    [Fact]
    public async Task GetDisputeAsync_NonExistent_ReturnsNull()
    {
        // Act
        var result = await _service.GetDisputeAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Helpers

    private PaymentRequest CreateCompletedPayment(int daysAgo)
    {
        var payment = PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: $"TXN-{Guid.NewGuid():N}",
            transactionType: PaymentTransactionType.InterbankTransfer,
            amount: new Money(50000, "NGN"),
            sourceAccount: "058:1234567890",
            destinationAccount: "044:0987654321",
            channel: PaymentChannel.NIP);

        // Simulate the payment going through its lifecycle to Completed
        payment.MarkPendingFraudCheck();
        payment.Approve(10);
        payment.MarkRouting();
        payment.MarkProcessing();
        payment.Complete();

        // Adjust CreatedAtUtc via reflection to simulate age
        var createdAtProp = typeof(PaymentRequest).GetProperty("CreatedAtUtc");
        var setter = createdAtProp!.GetSetMethod(true);
        setter?.Invoke(payment, new object[] { DateTime.UtcNow.AddDays(-daysAgo) });

        return payment;
    }

    private DisputeService CreateServiceWithWindowDays(int windowDays)
    {
        return new DisputeService(
            _disputeRepository,
            _paymentRequestRepository,
            _paymentOrchestrator,
            _auditStore,
            _eventPublisher,
            Options.Create(new DisputeOptions { MaxDisputeWindowDays = windowDays }),
            NullLogger<DisputeService>.Instance);
    }

    #endregion

    #region Fake Implementations

    private sealed class FakeDisputeRepository : IDisputeRepository
    {
        private readonly List<DisputeRecord> _disputes = new();
        public List<DisputeRecord> UpdatedDisputes { get; } = new();

        public void Add(DisputeRecord dispute)
        {
            _disputes.Add(dispute);
        }

        public Task SaveAsync(DisputeRecord dispute, CancellationToken ct)
        {
            _disputes.Add(dispute);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(DisputeRecord dispute, CancellationToken ct)
        {
            UpdatedDisputes.Add(dispute);
            return Task.CompletedTask;
        }

        public Task<DisputeRecord?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            return Task.FromResult(_disputes.FirstOrDefault(d => d.Id == id));
        }

        public Task<DisputeRecord?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            return Task.FromResult(_disputes.FirstOrDefault(d => d.TransactionReference == transactionReference));
        }
    }

    private sealed class FakePaymentRequestRepository : IPaymentRequestRepository
    {
        private readonly List<PaymentRequest> _payments = new();

        public void Add(PaymentRequest payment)
        {
            _payments.Add(payment);
        }

        public Task SaveAsync(PaymentRequest request, CancellationToken ct)
        {
            var existing = _payments.FirstOrDefault(p => p.TransactionReference == request.TransactionReference);
            if (existing is null)
                _payments.Add(request);
            return Task.CompletedTask;
        }

        public Task<PaymentRequest?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            return Task.FromResult(_payments.FirstOrDefault(p => p.TransactionReference == transactionReference));
        }

        public Task<PaymentRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
        {
            return Task.FromResult(_payments.FirstOrDefault(p => p.IdempotencyKey == idempotencyKey));
        }
    }

    private sealed class FakePaymentOrchestrator : IPaymentOrchestrator
    {
        public PaymentResult Result { get; set; } = new("TXN-CREDIT", PaymentStatus.Completed, true, null, null);
        public List<PaymentRequest> InitiatedPayments { get; } = new();

        public Task<PaymentResult> InitiatePaymentAsync(PaymentRequest request, CancellationToken ct)
        {
            InitiatedPayments.Add(request);
            return Task.FromResult(Result);
        }

        public Task<PaymentStatusResult> GetStatusAsync(string transactionReference, CancellationToken ct)
        {
            return Task.FromResult(new PaymentStatusResult(
                transactionReference, PaymentStatus.Created, PaymentChannel.NIP, DateTime.UtcNow, null, false));
        }

        public Task<PaymentStatusResult> GetStatusByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
        {
            return Task.FromResult(new PaymentStatusResult(
                string.Empty, PaymentStatus.Created, PaymentChannel.NIP, DateTime.UtcNow, null, false));
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

    private sealed class FakeDisputeEventPublisher : IDisputeEventPublisher
    {
        public List<DisputeOpenedEvent> OpenedEvents { get; } = new();
        public List<DisputeEscalatedEvent> EscalatedEvents { get; } = new();
        public List<DisputeResolvedEvent> ResolvedEvents { get; } = new();
        public List<DisputeClosedEvent> ClosedEvents { get; } = new();

        public Task PublishDisputeOpenedAsync(DisputeOpenedEvent evt, CancellationToken ct)
        {
            OpenedEvents.Add(evt);
            return Task.CompletedTask;
        }

        public Task PublishDisputeEscalatedAsync(DisputeEscalatedEvent evt, CancellationToken ct)
        {
            EscalatedEvents.Add(evt);
            return Task.CompletedTask;
        }

        public Task PublishDisputeResolvedAsync(DisputeResolvedEvent evt, CancellationToken ct)
        {
            ResolvedEvents.Add(evt);
            return Task.CompletedTask;
        }

        public Task PublishDisputeClosedAsync(DisputeClosedEvent evt, CancellationToken ct)
        {
            ClosedEvents.Add(evt);
            return Task.CompletedTask;
        }
    }

    #endregion
}
