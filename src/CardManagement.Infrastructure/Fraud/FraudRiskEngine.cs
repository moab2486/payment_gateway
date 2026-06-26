using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Fraud;

/// <summary>
/// Rule-based fraud detection and risk scoring engine.
/// Evaluates transactions based on amount, frequency, geographic indicators,
/// device fingerprint, and historical patterns.
/// </summary>
public class FraudRiskEngine : IFraudRiskEngine
{
    private readonly FraudRiskOptions _options;
    private readonly RiskScoringRules _rules;
    private readonly IAuditStore _auditStore;
    private readonly ILogger<FraudRiskEngine> _logger;

    // Simple in-memory velocity tracker (source account -> list of timestamps)
    private readonly Dictionary<string, List<DateTime>> _velocityTracker = new();
    private readonly object _velocityLock = new();

    public FraudRiskEngine(
        IOptions<FraudRiskOptions> options,
        IOptions<RiskScoringRules> rules,
        IAuditStore auditStore,
        ILogger<FraudRiskEngine> logger)
    {
        _options = options.Value;
        _rules = rules.Value;
        _auditStore = auditStore;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates a payment request for fraud risk. Enforces a timeout based on configuration.
    /// If the engine is unavailable or exceeds the timeout, the configured fallback policy is applied.
    /// </summary>
    public async Task<RiskDecision> EvaluateAsync(PaymentRequest request, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(_options.MaxEvaluationTimeMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var decision = await PerformEvaluationAsync(request, linkedCts.Token);
            await LogDecisionAsync(request, decision, ct);
            return decision;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Fraud evaluation timed out after {TimeoutMs}ms for transaction {TransactionRef}. Applying fallback policy: {Policy}",
                _options.MaxEvaluationTimeMs, request.TransactionReference, _options.FallbackPolicy);

            var fallback = CreateFallbackDecision("Evaluation timeout exceeded");
            await LogDecisionAsync(request, fallback, ct);
            return fallback;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancelled — rethrow
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Fraud engine unavailable for transaction {TransactionRef}. Applying fallback policy: {Policy}",
                request.TransactionReference, _options.FallbackPolicy);

            var fallback = CreateFallbackDecision("Engine unavailable");
            await LogDecisionAsync(request, fallback, ct);
            return fallback;
        }
    }

    internal virtual async Task<RiskDecision> PerformEvaluationAsync(PaymentRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var reasons = new List<string>();

        // Score each factor
        int amountScore = ScoreAmount(request, reasons);
        int frequencyScore = ScoreFrequency(request, reasons);
        int geoScore = ScoreGeographic(request, reasons);
        int deviceScore = ScoreDevice(request, reasons);
        int historyScore = ScoreHistory(request, reasons);

        // Compute weighted score normalized to 0-100
        int totalWeight = _rules.TotalWeight;
        if (totalWeight == 0)
            totalWeight = 1; // Prevent division by zero

        int weightedSum =
            (amountScore * _rules.AmountWeight) +
            (frequencyScore * _rules.FrequencyWeight) +
            (geoScore * _rules.GeoWeight) +
            (deviceScore * _rules.DeviceWeight) +
            (historyScore * _rules.HistoryWeight);

        int finalScore = Math.Clamp(weightedSum / totalWeight, 0, 100);

        // Determine verdict
        RiskVerdict verdict;
        if (finalScore >= _options.BlockThreshold)
        {
            verdict = RiskVerdict.Deny;
            reasons.Add($"Score {finalScore} exceeds block threshold {_options.BlockThreshold}");
        }
        else if (finalScore >= _options.ReviewThreshold)
        {
            verdict = RiskVerdict.Review;
            reasons.Add($"Score {finalScore} exceeds review threshold {_options.ReviewThreshold}");
        }
        else
        {
            verdict = RiskVerdict.Approve;
        }

        await Task.CompletedTask; // Maintain async signature for future extension
        return new RiskDecision(finalScore, verdict, reasons.AsReadOnly());
    }

    /// <summary>
    /// Scores the transaction amount. Higher amounts relative to thresholds get higher scores.
    /// </summary>
    private int ScoreAmount(PaymentRequest request, List<string> reasons)
    {
        long amount = request.Amount.Amount;

        if (amount >= _rules.CriticalAmountThreshold)
        {
            reasons.Add($"Transaction amount {amount} exceeds critical threshold");
            return 100;
        }

        if (amount >= _rules.HighAmountThreshold)
        {
            // Linear scaling between high and critical thresholds (50-100)
            long range = _rules.CriticalAmountThreshold - _rules.HighAmountThreshold;
            long overHigh = amount - _rules.HighAmountThreshold;
            int score = 50 + (int)(50L * overHigh / Math.Max(range, 1));
            reasons.Add($"Transaction amount {amount} exceeds high threshold");
            return Math.Clamp(score, 50, 100);
        }

        // Below high threshold: linear scale 0-49
        int lowScore = (int)(49L * amount / Math.Max(_rules.HighAmountThreshold, 1));
        return Math.Clamp(lowScore, 0, 49);
    }

    /// <summary>
    /// Scores transaction frequency (velocity check).
    /// </summary>
    private int ScoreFrequency(PaymentRequest request, List<string> reasons)
    {
        string sourceAccount = request.SourceAccount;
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-_rules.VelocityWindowSeconds);

        int recentCount;
        lock (_velocityLock)
        {
            if (!_velocityTracker.TryGetValue(sourceAccount, out var timestamps))
            {
                timestamps = new List<DateTime>();
                _velocityTracker[sourceAccount] = timestamps;
            }

            // Remove entries outside the window
            timestamps.RemoveAll(t => t < windowStart);

            // Add current transaction
            timestamps.Add(now);
            recentCount = timestamps.Count;
        }

        if (recentCount >= _rules.MaxTransactionsPerWindow)
        {
            reasons.Add($"Velocity check: {recentCount} transactions in {_rules.VelocityWindowSeconds}s window (max: {_rules.MaxTransactionsPerWindow})");
            return 100;
        }

        // Linear scale based on proximity to max
        int score = (int)(100L * recentCount / Math.Max(_rules.MaxTransactionsPerWindow, 1));
        if (score > 50)
        {
            reasons.Add($"Elevated velocity: {recentCount} transactions in window");
        }
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Scores geographic indicators. Placeholder implementation returning 0.
    /// </summary>
    private int ScoreGeographic(PaymentRequest request, List<string> reasons)
    {
        // Placeholder: future extension point for geographic risk assessment
        return 0;
    }

    /// <summary>
    /// Scores device fingerprint. Placeholder implementation returning 0.
    /// </summary>
    private int ScoreDevice(PaymentRequest request, List<string> reasons)
    {
        // Placeholder: future extension point for device fingerprint risk assessment
        return 0;
    }

    /// <summary>
    /// Scores historical patterns. Placeholder implementation returning 0.
    /// </summary>
    private int ScoreHistory(PaymentRequest request, List<string> reasons)
    {
        // Placeholder: future extension point for historical pattern risk assessment
        return 0;
    }

    private RiskDecision CreateFallbackDecision(string reason)
    {
        var verdict = _options.FallbackPolicy switch
        {
            FallbackPolicy.Approve => RiskVerdict.Approve,
            FallbackPolicy.Deny => RiskVerdict.Deny,
            FallbackPolicy.Review => RiskVerdict.Review,
            _ => RiskVerdict.Review
        };

        return new RiskDecision(0, verdict, new List<string> { $"Fallback applied: {reason}" }.AsReadOnly());
    }

    private async Task LogDecisionAsync(PaymentRequest request, RiskDecision decision, CancellationToken ct)
    {
        try
        {
            var entry = AuditEntry.Create(
                transactionReference: request.TransactionReference,
                actorIdentity: "FraudRiskEngine",
                action: "RiskEvaluation",
                previousState: null,
                newState: $"Score={decision.Score}, Decision={decision.Decision}",
                correlationId: request.TransactionReference,
                previousEntryHash: null);

            await _auditStore.AppendAsync(entry, ct);
        }
        catch (Exception ex)
        {
            // Audit logging failure should not affect fraud decision
            _logger.LogWarning(ex, "Failed to log risk decision to audit store for transaction {TransactionRef}",
                request.TransactionReference);
        }
    }
}
