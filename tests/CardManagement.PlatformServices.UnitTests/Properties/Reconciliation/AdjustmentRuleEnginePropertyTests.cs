using CardManagement.Application.PlatformServices.Reconciliation.Commands;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Handlers;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.PlatformServices.Reconciliation;
using CardManagement.PlatformServices.UnitTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.Reconciliation;

/// <summary>
/// Property-based tests for the Adjustment Rule Engine (Properties 6, 7, 8, 9).
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
[Trait("Feature", "platform-services")]
public class AdjustmentRuleEnginePropertyTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Property 6: Adjustment Rule Engine Completeness
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 3.1, 3.2**
    ///
    /// Property 6: Adjustment Rule Engine Completeness — For any exception,
    /// engine either auto-resolves or marks pending (mutually exclusive, exhaustive).
    /// The decision is always exactly one of: ShouldAutoAdjust=true (with amount/reason/rule)
    /// or ShouldAutoAdjust=false (no adjustment). Never both, never neither.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "6")]
    public Property AdjustmentRuleEngine_AlwaysProducesExactlyOneDecision()
    {
        var exceptionGen = ReconciliationExceptionGenerator();
        var rulesGen = AdjustmentRulesGenerator();

        return Prop.ForAll(
            exceptionGen,
            rulesGen,
            (exception, rules) =>
            {
                // Arrange: Create rule engine with the generated rules
                var options = Options.Create(new AdjustmentRuleOptions { Rules = rules });
                var logger = NullLogger<AdjustmentRuleEngine>.Instance;
                var engine = new AdjustmentRuleEngine(options, logger);

                // Act
                var decision = engine.Evaluate(exception);

                // Assert: Decision must be exactly one of auto-adjust or no-adjustment
                var isAutoAdjust = decision.ShouldAutoAdjust;
                var isNoAdjustment = !decision.ShouldAutoAdjust;

                // Mutually exclusive: exactly one must be true
                var mutuallyExclusive = isAutoAdjust != isNoAdjustment;

                // If auto-adjust, must have amount, reason, and rule name
                var autoAdjustValid = !isAutoAdjust || (
                    decision.AdjustmentAmount is not null &&
                    !string.IsNullOrWhiteSpace(decision.Reason) &&
                    !string.IsNullOrWhiteSpace(decision.RuleName));

                // If no adjustment, must not have amount or rule name
                var noAdjustValid = !isNoAdjustment || (
                    decision.AdjustmentAmount is null &&
                    decision.RuleName is null);

                return (mutuallyExclusive && autoAdjustValid && noAdjustValid)
                    .Label($"MutuallyExclusive={mutuallyExclusive}, " +
                           $"AutoAdjustValid={autoAdjustValid}, " +
                           $"NoAdjustValid={noAdjustValid}, " +
                           $"ShouldAutoAdjust={decision.ShouldAutoAdjust}, " +
                           $"Amount={decision.AdjustmentAmount}, " +
                           $"RuleName={decision.RuleName}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 3.1, 3.2**
    ///
    /// Property 6 (complement): When rules exist that match the exception,
    /// the engine auto-resolves. When no rules match, it marks pending.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "6")]
    public Property AdjustmentRuleEngine_MatchingRuleAutoResolves_NoMatchMarksPending()
    {
        var exceptionGen = ReconciliationExceptionGenerator();

        return Prop.ForAll(
            exceptionGen,
            exception =>
            {
                var logger = NullLogger<AdjustmentRuleEngine>.Instance;

                // Case 1: No rules → always pending
                var emptyOptions = Options.Create(new AdjustmentRuleOptions { Rules = new() });
                var emptyEngine = new AdjustmentRuleEngine(emptyOptions, logger);
                var emptyDecision = emptyEngine.Evaluate(exception);
                var noPending = !emptyDecision.ShouldAutoAdjust;

                // Case 2: Rule matching the exception type with high threshold → auto-resolves
                var matchingRule = new AdjustmentRuleDefinition
                {
                    Name = "CatchAll",
                    ExceptionType = exception.Type.ToString(),
                    AmountThreshold = long.MaxValue,
                    Reason = "Auto-adjusted by catch-all rule"
                };
                var matchOptions = Options.Create(new AdjustmentRuleOptions
                {
                    Rules = new List<AdjustmentRuleDefinition> { matchingRule }
                });
                var matchEngine = new AdjustmentRuleEngine(matchOptions, logger);
                var matchDecision = matchEngine.Evaluate(exception);
                var autoResolved = matchDecision.ShouldAutoAdjust;

                return (noPending && autoResolved)
                    .Label($"NoRules→Pending={noPending}, MatchingRule→AutoResolved={autoResolved}, " +
                           $"ExceptionType={exception.Type}");
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property 7: Manual Adjustment Validation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 3.3**
    ///
    /// Property 7: Manual Adjustment Validation — Reject invalid amounts
    /// (zero, negative, exceeding max), accept valid amounts with non-empty reason.
    /// The handler must reject zero amounts, empty reasons, and accept valid combinations.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "7")]
    public Property ManualAdjustment_ValidAmountAndReason_Succeeds()
    {
        var validAmountGen = Gen.Choose(1, 100_000_000);
        var currencyGen = Gen.Elements("NGN", "USD", "GBP", "EUR");
        var reasonGen = Gen.Elements(
            "Customer dispute resolution",
            "Bank correction",
            "Processing error correction",
            "Fee adjustment",
            "Settlement reconciliation");
        var operatorGen = Gen.Elements("op-001", "op-002", "op-003", "finance-admin");

        var paramsGen = (from amount in validAmountGen
                         from currency in currencyGen
                         from reason in reasonGen
                         from op in operatorGen
                         select (Amount: amount, Currency: currency, Reason: reason, Operator: op))
            .ToArbitrary();

        return Prop.ForAll(
            paramsGen,
            p =>
            {
                // Arrange
                var repository = new InMemoryReconciliationRepository();
                var auditStore = new InMemoryAuditStore();
                var eventPublisher = new InMemoryReconciliationEventPublisher();
                var handler = new CreateManualAdjustmentCommandHandler(
                    repository, auditStore, eventPublisher);

                // Create a pending exception to adjust
                var exception = ReconciliationException.CreateMismatch(
                    batchId: Guid.NewGuid(),
                    settlementLineItemId: Guid.NewGuid(),
                    paymentRequestId: Guid.NewGuid(),
                    externalAmount: new Money(5000, "NGN"),
                    internalAmount: new Money(4500, "NGN"),
                    externalStatus: "completed",
                    internalStatus: "completed");

                repository.AddExceptionForTest(exception);

                var command = new CreateManualAdjustmentCommand(
                    ExceptionId: exception.Id,
                    Amount: new Money(p.Amount, p.Currency),
                    Reason: p.Reason,
                    OperatorId: p.Operator);

                // Act
                var result = handler.HandleAsync(command, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: Valid input should succeed
                return result.Success
                    .Label($"Expected success for Amount={p.Amount} {p.Currency}, " +
                           $"Reason='{p.Reason}', Operator='{p.Operator}'. " +
                           $"Got Success={result.Success}, Error={result.ErrorMessage}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 3.3**
    ///
    /// Property 7: Manual Adjustment Validation — Empty or whitespace-only reason
    /// is always rejected regardless of amount validity.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "7")]
    public Property ManualAdjustment_EmptyReason_IsRejected()
    {
        var amountGen = Gen.Choose(1, 100_000_000);
        var invalidReasonGen = Gen.Elements("", " ", "  ", "\t", "\n");
        var operatorGen = Gen.Elements("op-001", "op-002", "op-003");

        var paramsGen = (from amount in amountGen
                         from reason in invalidReasonGen
                         from op in operatorGen
                         select (Amount: amount, Reason: reason, Operator: op))
            .ToArbitrary();

        return Prop.ForAll(
            paramsGen,
            p =>
            {
                // Arrange
                var repository = new InMemoryReconciliationRepository();
                var auditStore = new InMemoryAuditStore();
                var eventPublisher = new InMemoryReconciliationEventPublisher();
                var handler = new CreateManualAdjustmentCommandHandler(
                    repository, auditStore, eventPublisher);

                var exception = ReconciliationException.CreateMismatch(
                    batchId: Guid.NewGuid(),
                    settlementLineItemId: Guid.NewGuid(),
                    paymentRequestId: Guid.NewGuid(),
                    externalAmount: new Money(5000, "NGN"),
                    internalAmount: new Money(4500, "NGN"),
                    externalStatus: "completed",
                    internalStatus: "completed");

                repository.AddExceptionForTest(exception);

                var command = new CreateManualAdjustmentCommand(
                    ExceptionId: exception.Id,
                    Amount: new Money(p.Amount, "NGN"),
                    Reason: p.Reason,
                    OperatorId: p.Operator);

                // Act
                var result = handler.HandleAsync(command, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: Empty reason should always be rejected
                return (!result.Success)
                    .Label($"Expected rejection for empty reason '{p.Reason}' " +
                           $"but got Success={result.Success}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 3.3**
    ///
    /// Property 7: Manual Adjustment Validation — Missing operator ID is rejected.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "7")]
    public Property ManualAdjustment_MissingOperatorId_IsRejected()
    {
        var amountGen = Gen.Choose(1, 100_000_000);
        var reasonGen = Gen.Elements("Valid reason 1", "Valid reason 2");
        var invalidOperatorGen = Gen.Elements("", " ", "\t");

        var paramsGen = (from amount in amountGen
                         from reason in reasonGen
                         from op in invalidOperatorGen
                         select (Amount: amount, Reason: reason, Operator: op))
            .ToArbitrary();

        return Prop.ForAll(
            paramsGen,
            p =>
            {
                // Arrange
                var repository = new InMemoryReconciliationRepository();
                var auditStore = new InMemoryAuditStore();
                var eventPublisher = new InMemoryReconciliationEventPublisher();
                var handler = new CreateManualAdjustmentCommandHandler(
                    repository, auditStore, eventPublisher);

                var exception = ReconciliationException.CreateMismatch(
                    batchId: Guid.NewGuid(),
                    settlementLineItemId: Guid.NewGuid(),
                    paymentRequestId: Guid.NewGuid(),
                    externalAmount: new Money(5000, "NGN"),
                    internalAmount: new Money(4500, "NGN"),
                    externalStatus: "completed",
                    internalStatus: "completed");

                repository.AddExceptionForTest(exception);

                var command = new CreateManualAdjustmentCommand(
                    ExceptionId: exception.Id,
                    Amount: new Money(p.Amount, "NGN"),
                    Reason: p.Reason,
                    OperatorId: p.Operator);

                // Act
                var result = handler.HandleAsync(command, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: Missing operator should be rejected
                return (!result.Success)
                    .Label($"Expected rejection for operator '{p.Operator}' " +
                           $"but got Success={result.Success}");
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property 8: Adjustment Audit Completeness
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 3.4**
    ///
    /// Property 8: Adjustment Audit Completeness — Every successful manual adjustment
    /// produces an audit entry containing operator identity, exception reference,
    /// adjustment amount, and reason.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "8")]
    public Property ManualAdjustment_Success_ProducesCompleteAuditEntry()
    {
        var validAmountGen = Gen.Choose(1, 100_000_000);
        var currencyGen = Gen.Elements("NGN", "USD", "GBP", "EUR");
        var reasonGen = Gen.Elements(
            "Customer correction", "Fee reversal", "Settlement difference",
            "Processor error", "Manual override");
        var operatorGen = Gen.Elements("op-001", "op-002", "finance-admin", "recon-operator");

        var paramsGen = (from amount in validAmountGen
                         from currency in currencyGen
                         from reason in reasonGen
                         from op in operatorGen
                         select (Amount: amount, Currency: currency, Reason: reason, Operator: op))
            .ToArbitrary();

        return Prop.ForAll(
            paramsGen,
            p =>
            {
                // Arrange
                var repository = new InMemoryReconciliationRepository();
                var auditStore = new InMemoryAuditStore();
                var eventPublisher = new InMemoryReconciliationEventPublisher();
                var handler = new CreateManualAdjustmentCommandHandler(
                    repository, auditStore, eventPublisher);

                var exception = ReconciliationException.CreateMismatch(
                    batchId: Guid.NewGuid(),
                    settlementLineItemId: Guid.NewGuid(),
                    paymentRequestId: Guid.NewGuid(),
                    externalAmount: new Money(5000, "NGN"),
                    internalAmount: new Money(4500, "NGN"),
                    externalStatus: "completed",
                    internalStatus: "completed");

                repository.AddExceptionForTest(exception);

                var command = new CreateManualAdjustmentCommand(
                    ExceptionId: exception.Id,
                    Amount: new Money(p.Amount, p.Currency),
                    Reason: p.Reason,
                    OperatorId: p.Operator);

                // Act
                var result = handler.HandleAsync(command, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: Must produce audit entry with all required fields
                var auditEntries = auditStore.GetByTransactionReferenceAsync(
                    $"adjustment:{result.AdjustmentId}", CancellationToken.None)
                    .GetAwaiter().GetResult();

                var hasAuditEntry = auditEntries.Count == 1;
                var entry = auditEntries.FirstOrDefault();

                // Verify audit contains operator identity
                var hasOperator = entry?.ActorIdentity == p.Operator;

                // Verify audit contains exception reference
                var hasExceptionRef = entry?.CorrelationId == exception.Id.ToString();

                // Verify audit contains amount
                var hasAmount = entry?.NewState?.Contains(p.Amount.ToString()) ?? false;

                // Verify audit contains reason
                var hasReason = entry?.NewState?.Contains(p.Reason) ?? false;

                return (result.Success && hasAuditEntry && hasOperator &&
                        hasExceptionRef && hasAmount && hasReason)
                    .Label($"Success={result.Success}, HasAuditEntry={hasAuditEntry}, " +
                           $"HasOperator={hasOperator}, HasExceptionRef={hasExceptionRef}, " +
                           $"HasAmount={hasAmount}, HasReason={hasReason}");
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property 9: Adjustment Event Publishing
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 3.5**
    ///
    /// Property 9: Adjustment Event Publishing — Every successful adjustment
    /// publishes a Kafka event containing adjustment ID, exception reference,
    /// amount, and type.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "9")]
    public Property ManualAdjustment_Success_PublishesEventWithRequiredFields()
    {
        var validAmountGen = Gen.Choose(1, 100_000_000);
        var currencyGen = Gen.Elements("NGN", "USD", "GBP", "EUR");
        var reasonGen = Gen.Elements(
            "Dispute resolution", "Processor variance",
            "Manual correction", "Fee adjustment");
        var operatorGen = Gen.Elements("op-001", "op-002", "finance-admin");

        var paramsGen = (from amount in validAmountGen
                         from currency in currencyGen
                         from reason in reasonGen
                         from op in operatorGen
                         select (Amount: amount, Currency: currency, Reason: reason, Operator: op))
            .ToArbitrary();

        return Prop.ForAll(
            paramsGen,
            p =>
            {
                // Arrange
                var repository = new InMemoryReconciliationRepository();
                var auditStore = new InMemoryAuditStore();
                var eventPublisher = new InMemoryReconciliationEventPublisher();
                var handler = new CreateManualAdjustmentCommandHandler(
                    repository, auditStore, eventPublisher);

                var exception = ReconciliationException.CreateUnmatchedExternal(
                    batchId: Guid.NewGuid(),
                    settlementLineItemId: Guid.NewGuid(),
                    externalAmount: new Money(10000, "NGN"),
                    externalStatus: "completed");

                repository.AddExceptionForTest(exception);

                var command = new CreateManualAdjustmentCommand(
                    ExceptionId: exception.Id,
                    Amount: new Money(p.Amount, p.Currency),
                    Reason: p.Reason,
                    OperatorId: p.Operator);

                // Act
                var result = handler.HandleAsync(command, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: Must publish event with required fields
                var events = eventPublisher.PublishedEvents;
                var hasEvent = events.Count == 1;
                var evt = events.FirstOrDefault();

                // Event must contain adjustment ID
                var hasAdjustmentId = evt?.AdjustmentId == result.AdjustmentId;

                // Event must contain exception reference
                var hasExceptionRef = evt?.ExceptionId == exception.Id;

                // Event must contain amount
                var hasAmount = evt?.Amount == p.Amount &&
                                evt?.CurrencyCode == p.Currency;

                // Event must contain type (ManualOperator for manual adjustments)
                var hasType = evt?.Type == AdjustmentType.ManualOperator;

                return (result.Success && hasEvent && hasAdjustmentId &&
                        hasExceptionRef && hasAmount && hasType)
                    .Label($"Success={result.Success}, HasEvent={hasEvent}, " +
                           $"HasAdjustmentId={hasAdjustmentId}, " +
                           $"HasExceptionRef={hasExceptionRef}, " +
                           $"HasAmount={hasAmount}, HasType={hasType}");
            });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generator for ReconciliationException instances of all types.
    /// </summary>
    private static Arbitrary<ReconciliationException> ReconciliationExceptionGenerator()
    {
        var mismatchGen = from extAmount in Gen.Choose(1, 100_000_000)
                          from intAmount in Gen.Choose(1, 100_000_000)
                          from currency in Gen.Elements("NGN", "USD", "GBP", "EUR")
                          from extStatus in Gen.Elements("completed", "pending", "failed")
                          from intStatus in Gen.Elements("completed", "pending", "failed")
                          select ReconciliationException.CreateMismatch(
                              batchId: Guid.NewGuid(),
                              settlementLineItemId: Guid.NewGuid(),
                              paymentRequestId: Guid.NewGuid(),
                              externalAmount: new Money(extAmount, currency),
                              internalAmount: new Money(intAmount, currency),
                              externalStatus: extStatus,
                              internalStatus: intStatus);

        var unmatchedExternalGen = from amount in Gen.Choose(1, 100_000_000)
                                   from currency in Gen.Elements("NGN", "USD", "GBP", "EUR")
                                   from status in Gen.Elements("completed", "pending", "failed")
                                   select ReconciliationException.CreateUnmatchedExternal(
                                       batchId: Guid.NewGuid(),
                                       settlementLineItemId: Guid.NewGuid(),
                                       externalAmount: new Money(amount, currency),
                                       externalStatus: status);

        var unmatchedInternalGen = from amount in Gen.Choose(1, 100_000_000)
                                   from currency in Gen.Elements("NGN", "USD", "GBP", "EUR")
                                   from status in Gen.Elements("completed", "pending", "failed")
                                   select ReconciliationException.CreateUnmatchedInternal(
                                       batchId: Guid.NewGuid(),
                                       paymentRequestId: Guid.NewGuid(),
                                       internalAmount: new Money(amount, currency),
                                       internalStatus: status);

        return Gen.OneOf(mismatchGen, unmatchedExternalGen, unmatchedInternalGen)
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for lists of adjustment rule definitions.
    /// </summary>
    private static Arbitrary<List<AdjustmentRuleDefinition>> AdjustmentRulesGenerator()
    {
        var ruleGen = from name in Gen.Elements("SmallMismatch", "MediumMismatch", "UnmatchedExt", "CatchAll")
                      from exType in Gen.Elements("Mismatch", "UnmatchedExternal", "UnmatchedInternal", "")
                      from threshold in Gen.OneOf(
                          Gen.Choose(100, 10_000_000).Select(x => (long?)x),
                          Gen.Constant<long?>(null))
                      from reason in Gen.Elements(
                          "Auto-adjusted: small variance",
                          "Auto-adjusted: processor rule",
                          "Auto-adjusted: unmatched threshold")
                      select new AdjustmentRuleDefinition
                      {
                          Name = $"{name}-{Guid.NewGuid():N}"[..20],
                          ExceptionType = string.IsNullOrEmpty(exType) ? null : exType,
                          AmountThreshold = threshold,
                          Reason = reason
                      };

        return Gen.ListOf(ruleGen)
            .Select(rules => rules.Take(5).ToList())
            .ToArbitrary();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// Test Doubles for Properties 6-9
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// In-memory event publisher that captures published adjustment events for verification.
/// </summary>
internal class InMemoryReconciliationEventPublisher : IReconciliationEventPublisher
{
    private readonly List<AdjustmentCreatedEvent> _events = new();

    public IReadOnlyList<AdjustmentCreatedEvent> PublishedEvents => _events;

    public Task PublishAdjustmentCreatedAsync(AdjustmentCreatedEvent @event, CancellationToken ct)
    {
        _events.Add(@event);
        return Task.CompletedTask;
    }

    public Task PublishBatchCompletedAsync(Guid batchId, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
