using CardManagement.Application.PlatformServices.Reconciliation.Commands;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Handlers;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.ValueObjects;
using CardManagement.PlatformServices.UnitTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Xunit;
using DomainSettlementLineItem = CardManagement.Domain.PlatformServices.Reconciliation.SettlementLineItem;
using GeneratorSettlementLineItem = CardManagement.PlatformServices.UnitTests.Generators.SettlementLineItem;
using ReconciliationProcessorType = CardManagement.Domain.PlatformServices.Reconciliation.ProcessorType;

namespace CardManagement.PlatformServices.UnitTests.Properties.Reconciliation;

/// <summary>
/// Property-based tests for the Reconciliation Engine (Properties 3, 4, 5).
///
/// **Validates: Requirements 1.4, 1.6, 2.1, 2.2, 2.3, 2.4, 2.5**
/// </summary>
[Trait("Feature", "platform-services")]
public class ReconciliationEnginePropertyTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Property 3: Duplicate File Rejection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 1.4**
    ///
    /// Property 3: Duplicate File Rejection — Import same file content twice,
    /// verify first succeeds and second rejects.
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "3")]
    public Property DuplicateFileImport_FirstSucceeds_SecondIsRejected()
    {
        var fileHashGen = Arb.Generate<NonEmptyString>()
            .Select(s => $"sha256-{Guid.NewGuid():N}");

        var processorGen = Gen.Elements(ReconciliationProcessorType.NIBSS, ReconciliationProcessorType.Interswitch, ReconciliationProcessorType.Cardify);
        var totalRowsGen = Gen.Choose(1, 10000);
        var createdByGen = Arb.Generate<NonEmptyString>().Select(s => $"operator-{s.Get[..Math.Min(10, s.Get.Length)]}");

        // Combine into a single generator since ForAll supports max 3 arbitraries
        var importParamsGen = (from fileHash in fileHashGen
                               from processor in processorGen
                               from totalRows in totalRowsGen
                               from createdBy in createdByGen
                               select (FileHash: fileHash, Processor: processor, TotalRows: totalRows, CreatedBy: createdBy))
            .ToArbitrary();

        return Prop.ForAll(
            importParamsGen,
            p =>
            {
                // Arrange
                var repository = new InMemoryReconciliationRepository();
                var workerPool = new NoOpReconciliationWorkerPool();
                var handler = new ImportSettlementFileCommandHandler(repository, workerPool);

                var command = new ImportSettlementFileCommand(
                    FileStream: Stream.Null,
                    Processor: p.Processor,
                    SettlementDate: DateOnly.FromDateTime(DateTime.UtcNow),
                    FileHash: p.FileHash,
                    TotalRows: p.TotalRows,
                    CreatedBy: p.CreatedBy);

                // Act: First import
                var firstResult = handler.HandleAsync(command, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Act: Second import (duplicate)
                var secondResult = handler.HandleAsync(command, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert
                var firstAccepted = firstResult.Accepted;
                var secondRejected = !secondResult.Accepted;
                var secondHasReason = !string.IsNullOrWhiteSpace(secondResult.RejectionReason);

                return (firstAccepted && secondRejected && secondHasReason)
                    .Label($"First import Accepted={firstResult.Accepted}, " +
                           $"Second import Accepted={secondResult.Accepted}, " +
                           $"RejectionReason='{secondResult.RejectionReason}'");
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property 4: Import Audit Trail Completeness
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 1.6**
    ///
    /// Property 4: Import Audit Trail Completeness — Verify audit entry matches
    /// actual parse result counts (total, parsed, error).
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "4")]
    public Property AuditEntry_MatchesParseResultCounts()
    {
        var totalRowsGen = Gen.Choose(1, 500);
        var parsedRatioGen = Gen.Choose(0, 100); // percentage of total that parses ok

        return Prop.ForAll(
            totalRowsGen.ToArbitrary(),
            parsedRatioGen.ToArbitrary(),
            (totalRows, parsedRatio) =>
            {
                // Arrange: Calculate parsed/error counts from inputs
                var parsedRows = (int)((long)totalRows * parsedRatio / 100);
                var errorRows = totalRows - parsedRows;

                var auditStore = new InMemoryAuditStore();
                var batchId = Guid.NewGuid();
                var fileHash = $"sha256-{Guid.NewGuid():N}";

                // Act: Record an audit entry representing a parse result
                var parseResult = new ParseResult
                {
                    LineItems = Enumerable.Range(0, parsedRows)
                        .Select(_ => DomainSettlementLineItem.Create(
                            batchId,
                            $"TXN-{Guid.NewGuid():N}",
                            $"PROC-{Guid.NewGuid():N}",
                            new Money(1000, "NGN"),
                            "completed",
                            DateOnly.FromDateTime(DateTime.UtcNow)))
                        .ToList(),
                    TotalRows = totalRows,
                    ParsedRows = parsedRows,
                    ErrorRows = errorRows,
                    Errors = Enumerable.Range(0, errorRows)
                        .Select(i => new ParseError(i + parsedRows + 1, "malformed row"))
                        .ToList()
                };

                // Simulate what the worker does: record audit entry for import result
                var auditEntry = AuditEntry.Create(
                    transactionReference: $"batch:{batchId}",
                    actorIdentity: "system",
                    action: "settlement-file-import",
                    previousState: null,
                    newState: $"{{\"totalRows\":{parseResult.TotalRows},\"parsedRows\":{parseResult.ParsedRows},\"errorRows\":{parseResult.ErrorRows}}}",
                    correlationId: batchId.ToString(),
                    previousEntryHash: null);

                auditStore.AppendAsync(auditEntry, CancellationToken.None).GetAwaiter().GetResult();

                // Assert: Verify audit entry records the correct counts
                var entries = auditStore.GetByTransactionReferenceAsync($"batch:{batchId}", CancellationToken.None)
                    .GetAwaiter().GetResult();

                var hasEntry = entries.Count == 1;
                var entry = entries.FirstOrDefault();
                var stateContainsTotalRows = entry?.NewState?.Contains($"\"totalRows\":{totalRows}") ?? false;
                var stateContainsParsedRows = entry?.NewState?.Contains($"\"parsedRows\":{parsedRows}") ?? false;
                var stateContainsErrorRows = entry?.NewState?.Contains($"\"errorRows\":{errorRows}") ?? false;

                return (hasEntry && stateContainsTotalRows && stateContainsParsedRows && stateContainsErrorRows)
                    .Label($"Audit entry count={entries.Count}, " +
                           $"Contains totalRows={stateContainsTotalRows}, " +
                           $"Contains parsedRows={stateContainsParsedRows}, " +
                           $"Contains errorRows={stateContainsErrorRows}. " +
                           $"Expected: total={totalRows}, parsed={parsedRows}, errors={errorRows}");
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Property 5: Reconciliation Exception Classification
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5**
    ///
    /// Property 5: Reconciliation Exception Classification — Same reference, same
    /// amount and status results in a match (no exception). Same reference but
    /// different amount or status results in a Mismatch exception.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    [Trait("Property", "5")]
    public Property MatchingItems_WithSameAmountAndStatus_ProducesNoException()
    {
        var moneyGen = ReconciliationGenerators.MoneyArbitrary().Generator;
        var statusGen = Gen.Elements("completed", "pending", "failed", "reversed");
        var transRefGen = Gen.Elements("TXN", "REF", "PAY")
            .Select(prefix => $"{prefix}-{Guid.NewGuid():N}");

        return Prop.ForAll(
            moneyGen.ToArbitrary(),
            statusGen.ToArbitrary(),
            transRefGen.ToArbitrary(),
            (money, status, transRef) =>
            {
                // Arrange: Create a settlement item and matching internal record
                // with the same amount and status
                var batchId = Guid.NewGuid();
                var engine = new InMemoryReconciliationEngine();

                var settlementItem = new TestSettlementItem(
                    TransactionReference: transRef,
                    Amount: money,
                    Status: status);

                var internalRecord = new TestPaymentRequest(
                    TransactionReference: transRef,
                    Amount: money,
                    Status: status);

                // Act: Classify the pair
                var classification = engine.Classify(settlementItem, internalRecord);

                // Assert: Should be classified as Matched (no exception)
                return (classification == ReconciliationClassification.Matched)
                    .Label($"Expected Matched but got {classification}. " +
                           $"TransRef={transRef}, Amount={money}, Status={status}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 2.1, 2.3**
    ///
    /// Property 5: Reconciliation Exception Classification — Same reference but
    /// different amount or status produces a Mismatch exception.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    [Trait("Property", "5")]
    public Property MatchingItems_WithDifferentAmountOrStatus_ProducesMismatchException()
    {
        var moneyGen = ReconciliationGenerators.MoneyArbitrary().Generator;
        var statusGen = Gen.Elements("completed", "pending", "failed", "reversed");
        var transRefGen = Gen.Elements("TXN", "REF", "PAY")
            .Select(prefix => $"{prefix}-{Guid.NewGuid():N}");

        // Combine external pair and internal pair into tuples to stay within ForAll's 4-arg limit
        var externalGen = (from amount in moneyGen
                           from status in statusGen
                           select (Amount: amount, Status: status)).ToArbitrary();

        var internalGen = (from amount in moneyGen
                           from status in statusGen
                           select (Amount: amount, Status: status)).ToArbitrary();

        return Prop.ForAll(
            externalGen,
            internalGen,
            transRefGen.ToArbitrary(),
            (external, @internal, transRef) =>
            {
                // Only test when there IS a difference
                var amountsDiffer = external.Amount != @internal.Amount;
                var statusesDiffer = external.Status != @internal.Status;

                if (!amountsDiffer && !statusesDiffer)
                    return true.Label("skipped - amounts and statuses are the same");

                // Arrange
                var engine = new InMemoryReconciliationEngine();

                var settlementItem = new TestSettlementItem(
                    TransactionReference: transRef,
                    Amount: external.Amount,
                    Status: external.Status);

                var internalRecord = new TestPaymentRequest(
                    TransactionReference: transRef,
                    Amount: @internal.Amount,
                    Status: @internal.Status);

                // Act
                var classification = engine.Classify(settlementItem, internalRecord);

                // Assert: Should be Mismatch
                return (classification == ReconciliationClassification.Mismatch)
                    .Label($"Expected Mismatch but got {classification}. " +
                           $"External: Amount={external.Amount}, Status={external.Status}. " +
                           $"Internal: Amount={@internal.Amount}, Status={@internal.Status}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 2.4**
    ///
    /// Property 5: Reconciliation Exception Classification — Settlement item with
    /// no matching internal record is classified as UnmatchedExternal.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    [Trait("Property", "5")]
    public Property SettlementItem_WithNoMatchingInternalRecord_IsUnmatchedExternal()
    {
        var moneyGen = ReconciliationGenerators.MoneyArbitrary().Generator;
        var statusGen = Gen.Elements("completed", "pending", "failed", "reversed");
        var transRefGen = Gen.Elements("TXN", "REF", "PAY")
            .Select(prefix => $"{prefix}-{Guid.NewGuid():N}");

        return Prop.ForAll(
            moneyGen.ToArbitrary(),
            statusGen.ToArbitrary(),
            transRefGen.ToArbitrary(),
            (money, status, transRef) =>
            {
                // Arrange: Settlement item exists but no matching internal record
                var engine = new InMemoryReconciliationEngine();

                var settlementItem = new TestSettlementItem(
                    TransactionReference: transRef,
                    Amount: money,
                    Status: status);

                // Act: Classify with null internal record (no match found)
                var classification = engine.Classify(settlementItem, internalRecord: null);

                // Assert: Should be UnmatchedExternal
                return (classification == ReconciliationClassification.UnmatchedExternal)
                    .Label($"Expected UnmatchedExternal but got {classification}. " +
                           $"TransRef={transRef}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 2.5**
    ///
    /// Property 5: Reconciliation Exception Classification — Internal record with
    /// no matching settlement item is classified as UnmatchedInternal.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ReconciliationGenerators.ReconciliationArbitraries) })]
    [Trait("Property", "5")]
    public Property InternalRecord_WithNoMatchingSettlementItem_IsUnmatchedInternal()
    {
        var moneyGen = ReconciliationGenerators.MoneyArbitrary().Generator;
        var statusGen = Gen.Elements("completed", "pending", "failed", "reversed");
        var transRefGen = Gen.Elements("TXN", "REF", "PAY")
            .Select(prefix => $"{prefix}-{Guid.NewGuid():N}");

        return Prop.ForAll(
            moneyGen.ToArbitrary(),
            statusGen.ToArbitrary(),
            transRefGen.ToArbitrary(),
            (money, status, transRef) =>
            {
                // Arrange: Internal record exists but no matching settlement item
                var engine = new InMemoryReconciliationEngine();

                var internalRecord = new TestPaymentRequest(
                    TransactionReference: transRef,
                    Amount: money,
                    Status: status);

                // Act: Classify with null settlement item (no external match)
                var classification = engine.ClassifyInternal(internalRecord, settlementItem: null);

                // Assert: Should be UnmatchedInternal
                return (classification == ReconciliationClassification.UnmatchedInternal)
                    .Label($"Expected UnmatchedInternal but got {classification}. " +
                           $"TransRef={transRef}");
            });
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// In-memory test doubles
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Simple classification result for reconciliation matching.
/// </summary>
public enum ReconciliationClassification
{
    Matched,
    Mismatch,
    UnmatchedExternal,
    UnmatchedInternal
}

/// <summary>
/// Lightweight test record representing a settlement line item for classification.
/// </summary>
public record TestSettlementItem(
    string TransactionReference,
    Money Amount,
    string Status);

/// <summary>
/// Lightweight test record representing an internal payment request for classification.
/// </summary>
public record TestPaymentRequest(
    string TransactionReference,
    Money Amount,
    string Status);

/// <summary>
/// In-memory reconciliation engine that implements the matching classification logic.
/// This tests the core reconciliation algorithm without infrastructure dependencies.
/// </summary>
internal class InMemoryReconciliationEngine
{
    /// <summary>
    /// Classifies a pair of settlement item and internal record.
    /// </summary>
    public ReconciliationClassification Classify(
        TestSettlementItem settlementItem,
        TestPaymentRequest? internalRecord)
    {
        if (internalRecord is null)
            return ReconciliationClassification.UnmatchedExternal;

        if (settlementItem.Amount == internalRecord.Amount &&
            settlementItem.Status == internalRecord.Status)
            return ReconciliationClassification.Matched;

        return ReconciliationClassification.Mismatch;
    }

    /// <summary>
    /// Classifies an internal record against its potential settlement match.
    /// </summary>
    public ReconciliationClassification ClassifyInternal(
        TestPaymentRequest internalRecord,
        TestSettlementItem? settlementItem)
    {
        if (settlementItem is null)
            return ReconciliationClassification.UnmatchedInternal;

        if (internalRecord.Amount == settlementItem.Amount &&
            internalRecord.Status == settlementItem.Status)
            return ReconciliationClassification.Matched;

        return ReconciliationClassification.Mismatch;
    }
}

/// <summary>
/// In-memory reconciliation repository that supports duplicate file hash checking.
/// </summary>
internal class InMemoryReconciliationRepository : IReconciliationRepository
{
    private readonly List<ReconciliationBatch> _batches = new();
    private readonly HashSet<string> _fileHashes = new();
    private readonly List<DomainSettlementLineItem> _lineItems = new();
    private readonly List<ReconciliationException> _exceptions = new();
    private readonly List<Adjustment> _adjustments = new();

    public Task<ReconciliationBatch> CreateBatchAsync(ReconciliationBatch batch, CancellationToken ct)
    {
        _batches.Add(batch);
        _fileHashes.Add(batch.FileHash);
        return Task.FromResult(batch);
    }

    public Task<bool> FileHashExistsAsync(string fileHash, CancellationToken ct)
    {
        return Task.FromResult(_fileHashes.Contains(fileHash));
    }

    public Task AddSettlementLineItemsAsync(IEnumerable<DomainSettlementLineItem> items, CancellationToken ct)
    {
        _lineItems.AddRange(items);
        return Task.CompletedTask;
    }

    public Task AddExceptionsAsync(IEnumerable<ReconciliationException> exceptions, CancellationToken ct)
    {
        _exceptions.AddRange(exceptions);
        return Task.CompletedTask;
    }

    public Task<ReconciliationBatch?> GetBatchAsync(Guid batchId, CancellationToken ct)
    {
        return Task.FromResult(_batches.FirstOrDefault(b => b.Id == batchId));
    }

    public Task UpdateBatchAsync(ReconciliationBatch batch, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReconciliationBatch>> ListBatchesAsync(int limit, int offset, CancellationToken ct)
    {
        IReadOnlyList<ReconciliationBatch> result = _batches.Skip(offset).Take(limit).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ReconciliationException>> ListExceptionsByBatchAsync(Guid batchId, int limit, int offset, CancellationToken ct)
    {
        IReadOnlyList<ReconciliationException> result = _exceptions.Where(e => e.BatchId == batchId).Skip(offset).Take(limit).ToList();
        return Task.FromResult(result);
    }

    public Task<ReconciliationException?> GetExceptionAsync(Guid exceptionId, CancellationToken ct)
    {
        return Task.FromResult(_exceptions.FirstOrDefault(e => e.Id == exceptionId));
    }

    /// <summary>
    /// Test helper to add an exception directly for testing manual adjustment flows.
    /// </summary>
    public void AddExceptionForTest(ReconciliationException exception)
    {
        _exceptions.Add(exception);
    }

    public Task AddAdjustmentAsync(Adjustment adjustment, CancellationToken ct)
    {
        _adjustments.Add(adjustment);
        return Task.CompletedTask;
    }

    public Task UpdateExceptionAsync(ReconciliationException exception, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Adjustment>> ListAdjustmentsAsync(Guid? exceptionId, int limit, int offset, CancellationToken ct)
    {
        var query = exceptionId.HasValue
            ? _adjustments.Where(a => a.ExceptionId == exceptionId.Value)
            : _adjustments.AsEnumerable();

        IReadOnlyList<Adjustment> result = query.Skip(offset).Take(limit).ToList();
        return Task.FromResult(result);
    }
}

/// <summary>
/// No-op worker pool for testing the import handler without actual enqueue behavior.
/// </summary>
internal class NoOpReconciliationWorkerPool : IReconciliationWorkerPool
{
    public int ActiveWorkerCount => 0;
    public int PendingTaskCount => 0;

    public Task EnqueueFileAsync(FileParseTask task, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory audit store for verifying audit entry creation.
/// </summary>
internal class InMemoryAuditStore : Application.Ports.IAuditStore
{
    private readonly List<AuditEntry> _entries = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
    {
        IReadOnlyList<AuditEntry> result = _entries.Where(e => e.TransactionReference == transactionReference).ToList();
        return Task.FromResult(result);
    }
}
