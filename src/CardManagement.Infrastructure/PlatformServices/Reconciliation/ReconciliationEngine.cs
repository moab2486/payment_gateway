using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation;

/// <summary>
/// Implements the reconciliation matching logic: joins settlement line items to PaymentRequests
/// by Transaction_Reference, classifies results as matched/mismatch/unmatched-external/unmatched-internal,
/// evaluates auto-adjustment rules, creates adjustments, and publishes events.
/// </summary>
public class ReconciliationEngine : IReconciliationEngine
{
    private readonly CardManagementDbContext _dbContext;
    private readonly IReconciliationRepository _repository;
    private readonly IAdjustmentRuleEngine _adjustmentRuleEngine;
    private readonly IReconciliationEventPublisher _eventPublisher;
    private readonly ILogger<ReconciliationEngine> _logger;

    public ReconciliationEngine(
        CardManagementDbContext dbContext,
        IReconciliationRepository repository,
        IAdjustmentRuleEngine adjustmentRuleEngine,
        IReconciliationEventPublisher eventPublisher,
        ILogger<ReconciliationEngine> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _adjustmentRuleEngine = adjustmentRuleEngine ?? throw new ArgumentNullException(nameof(adjustmentRuleEngine));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ReconciliationBatchResult> ExecuteBatchAsync(Guid batchId, CancellationToken ct)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(batchId));

        var batch = await _repository.GetBatchAsync(batchId, ct);
        if (batch is null)
        {
            return new ReconciliationBatchResult
            {
                BatchId = batchId,
                TotalItems = 0,
                MatchedCount = 0,
                ExceptionCount = 0,
                AutoResolvedCount = 0,
                Success = false,
                ErrorMessage = $"Batch '{batchId}' not found."
            };
        }

        _logger.LogInformation("Starting reconciliation matching for batch {BatchId}.", batchId);

        // Step 1: Load all settlement line items for this batch
        var settlementItems = await _dbContext.SettlementLineItems
            .Where(s => s.BatchId == batchId)
            .ToListAsync(ct);

        if (settlementItems.Count == 0)
        {
            _logger.LogWarning("Batch {BatchId} has no settlement line items to process.", batchId);
            return new ReconciliationBatchResult
            {
                BatchId = batchId,
                TotalItems = 0,
                MatchedCount = 0,
                ExceptionCount = 0,
                AutoResolvedCount = 0,
                Success = true
            };
        }

        // Step 2: Extract unique transaction references and query matching PaymentRequests
        var transactionReferences = settlementItems
            .Select(s => s.TransactionReference)
            .Distinct()
            .ToList();

        var paymentRequests = await _dbContext.PaymentRequests
            .Where(pr => transactionReferences.Contains(pr.TransactionReference))
            .ToListAsync(ct);

        // Build lookup by TransactionReference for O(1) matching
        var paymentRequestLookup = paymentRequests
            .GroupBy(pr => pr.TransactionReference)
            .ToDictionary(g => g.Key, g => g.First());

        // Step 3: Perform matching and classify results
        var exceptions = new List<ReconciliationException>();
        int matchedCount = 0;
        var matchedPaymentRequestIds = new HashSet<Guid>();

        foreach (var item in settlementItems)
        {
            if (paymentRequestLookup.TryGetValue(item.TransactionReference, out var paymentRequest))
            {
                matchedPaymentRequestIds.Add(paymentRequest.Id);

                if (IsFullMatch(item, paymentRequest))
                {
                    // Matched: identical amount, status, and the transaction exists
                    item.MarkMatched(paymentRequest.Id);
                    matchedCount++;
                }
                else
                {
                    // Mismatch: matched by reference but amount or status differs
                    item.MarkMismatched(paymentRequest.Id);

                    var exception = ReconciliationException.CreateMismatch(
                        batchId,
                        item.Id,
                        paymentRequest.Id,
                        item.Amount,
                        paymentRequest.Amount,
                        item.Status,
                        paymentRequest.Status.ToString());

                    exceptions.Add(exception);
                }
            }
            else
            {
                // Unmatched external: settlement line has no matching internal record
                var exception = ReconciliationException.CreateUnmatchedExternal(
                    batchId,
                    item.Id,
                    item.Amount,
                    item.Status);

                exceptions.Add(exception);
            }
        }

        // Step 4: Identify unmatched internal records
        // Find PaymentRequests for the settlement period that have no matching settlement line item
        var unmatchedInternalRequests = await GetUnmatchedInternalRequestsAsync(
            batchId, batch.SettlementDate, matchedPaymentRequestIds, transactionReferences, ct);

        foreach (var pr in unmatchedInternalRequests)
        {
            var exception = ReconciliationException.CreateUnmatchedInternal(
                batchId,
                pr.Id,
                pr.Amount,
                pr.Status.ToString());

            exceptions.Add(exception);
        }

        // Step 5: Persist settlement item match status updates
        await _dbContext.SaveChangesAsync(ct);

        // Step 6: Persist exceptions
        if (exceptions.Count > 0)
        {
            await _repository.AddExceptionsAsync(exceptions, ct);
        }

        // Step 7: Evaluate auto-adjustment rules for each exception
        int autoResolvedCount = 0;
        foreach (var exception in exceptions)
        {
            var decision = _adjustmentRuleEngine.Evaluate(exception);

            if (decision.ShouldAutoAdjust && decision.AdjustmentAmount is not null)
            {
                var adjustment = Adjustment.CreateAutomatic(
                    exception.Id,
                    decision.AdjustmentAmount,
                    decision.Reason!,
                    decision.RuleName!);

                await _repository.AddAdjustmentAsync(adjustment, ct);

                // Resolve the exception automatically
                exception.ResolveAutomatically(adjustment.Id);
                await _repository.UpdateExceptionAsync(exception, ct);

                // Publish adjustment-created event to Kafka
                await _eventPublisher.PublishAdjustmentCreatedAsync(
                    new AdjustmentCreatedEvent(
                        adjustment.Id,
                        exception.Id,
                        adjustment.Amount.Amount,
                        adjustment.Amount.CurrencyCode,
                        adjustment.Reason,
                        adjustment.OperatorId,
                        adjustment.Type,
                        adjustment.CreatedAtUtc),
                    ct);

                autoResolvedCount++;
            }
        }

        _logger.LogInformation(
            "Reconciliation batch {BatchId} completed. Total: {Total}, Matched: {Matched}, Exceptions: {Exceptions}, Auto-resolved: {AutoResolved}.",
            batchId, settlementItems.Count, matchedCount, exceptions.Count, autoResolvedCount);

        // Step 8: Publish batch-completed event
        await _eventPublisher.PublishBatchCompletedAsync(batchId, ct);

        return new ReconciliationBatchResult
        {
            BatchId = batchId,
            TotalItems = settlementItems.Count,
            MatchedCount = matchedCount,
            ExceptionCount = exceptions.Count,
            AutoResolvedCount = autoResolvedCount,
            Success = true
        };
    }

    /// <summary>
    /// Determines if a settlement line item fully matches an internal PaymentRequest.
    /// A full match requires identical amount (value and currency) and compatible status.
    /// </summary>
    private static bool IsFullMatch(SettlementLineItem item, PaymentRequest paymentRequest)
    {
        // Amount comparison: both value and currency must match
        if (item.Amount.Amount != paymentRequest.Amount.Amount ||
            !string.Equals(item.Amount.CurrencyCode, paymentRequest.Amount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Status comparison: normalize external status to match internal PaymentStatus
        var normalizedExternalStatus = NormalizeExternalStatus(item.Status);
        var internalStatus = paymentRequest.Status.ToString().ToUpperInvariant();

        return string.Equals(normalizedExternalStatus, internalStatus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes processor-reported status strings to a common format for comparison.
    /// Processors may report statuses like "success", "successful", "completed", "approved" etc.
    /// </summary>
    private static string NormalizeExternalStatus(string externalStatus)
    {
        var normalized = externalStatus.Trim().ToUpperInvariant();

        return normalized switch
        {
            "SUCCESS" or "SUCCESSFUL" or "APPROVED" => "COMPLETED",
            "FAIL" or "FAILURE" or "DECLINED" or "REJECTED" => "FAILED",
            "REVERSE" or "REVERSAL" or "REVERSED" => "REVERSED",
            _ => normalized
        };
    }

    /// <summary>
    /// Finds internal PaymentRequests for the settlement date that have no corresponding
    /// settlement line item in this batch.
    /// </summary>
    private async Task<List<PaymentRequest>> GetUnmatchedInternalRequestsAsync(
        Guid batchId,
        DateOnly settlementDate,
        HashSet<Guid> matchedPaymentRequestIds,
        List<string> settlementTransactionReferences,
        CancellationToken ct)
    {
        // Find PaymentRequests created on the settlement date that were not matched
        // to any settlement line item by transaction reference
        var startOfDay = settlementDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endOfDay = settlementDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var unmatchedInternal = await _dbContext.PaymentRequests
            .Where(pr => pr.CreatedAtUtc >= startOfDay && pr.CreatedAtUtc < endOfDay)
            .Where(pr => !settlementTransactionReferences.Contains(pr.TransactionReference))
            .Where(pr => !matchedPaymentRequestIds.Contains(pr.Id))
            .ToListAsync(ct);

        return unmatchedInternal;
    }
}
