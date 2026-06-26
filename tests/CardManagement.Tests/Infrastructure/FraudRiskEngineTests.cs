using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Fraud;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class FraudRiskEngineTests
{
    private static FraudRiskOptions DefaultOptions(
        int blockThreshold = 80,
        int reviewThreshold = 50,
        FallbackPolicy fallback = FallbackPolicy.Review,
        int maxEvalTimeMs = 200) => new()
    {
        BlockThreshold = blockThreshold,
        ReviewThreshold = reviewThreshold,
        FallbackPolicy = fallback,
        MaxEvaluationTimeMs = maxEvalTimeMs
    };

    private static RiskScoringRules DefaultRules(
        int amountWeight = 40,
        int frequencyWeight = 30,
        int geoWeight = 10,
        int deviceWeight = 10,
        int historyWeight = 10,
        long highAmountThreshold = 500_000_00,
        long criticalAmountThreshold = 5_000_000_00) => new()
    {
        AmountWeight = amountWeight,
        FrequencyWeight = frequencyWeight,
        GeoWeight = geoWeight,
        DeviceWeight = deviceWeight,
        HistoryWeight = historyWeight,
        HighAmountThreshold = highAmountThreshold,
        CriticalAmountThreshold = criticalAmountThreshold,
        MaxTransactionsPerWindow = 10,
        VelocityWindowSeconds = 300
    };

    private static PaymentRequest CreateRequest(long amountKobo = 1000_00, string sourceAccount = "1234567890")
    {
        return PaymentRequest.Create(
            idempotencyKey: Guid.NewGuid().ToString(),
            transactionReference: Guid.NewGuid().ToString(),
            transactionType: PaymentTransactionType.InterbankTransfer,
            amount: new Money(amountKobo, "NGN"),
            sourceAccount: sourceAccount,
            destinationAccount: "0987654321",
            channel: PaymentChannel.NIP);
    }

    private static FraudRiskEngine CreateEngine(
        FraudRiskOptions? options = null,
        RiskScoringRules? rules = null,
        IAuditStore? auditStore = null)
    {
        return new FraudRiskEngine(
            Options.Create(options ?? DefaultOptions()),
            Options.Create(rules ?? DefaultRules()),
            auditStore ?? new InMemoryAuditStore(),
            NullLogger<FraudRiskEngine>.Instance);
    }

    [Fact]
    public async Task LowScore_ReturnsApprove()
    {
        // Low amount, no velocity issues → should approve
        var engine = CreateEngine(
            options: DefaultOptions(blockThreshold: 80, reviewThreshold: 50),
            rules: DefaultRules(highAmountThreshold: 500_000_00));

        var request = CreateRequest(amountKobo: 10_000); // Very low amount (100 NGN)

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(RiskVerdict.Approve, decision.Decision);
        Assert.True(decision.Score < 50, $"Expected score below 50, got {decision.Score}");
    }

    [Fact]
    public async Task HighScore_ReturnsDeny()
    {
        // Very high amount with amount-heavy weighting → should deny
        var engine = CreateEngine(
            options: DefaultOptions(blockThreshold: 80, reviewThreshold: 50),
            rules: DefaultRules(
                amountWeight: 100,
                frequencyWeight: 0,
                geoWeight: 0,
                deviceWeight: 0,
                historyWeight: 0,
                highAmountThreshold: 100_00,
                criticalAmountThreshold: 200_00));

        var request = CreateRequest(amountKobo: 300_00); // Above critical threshold

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(RiskVerdict.Deny, decision.Decision);
        Assert.True(decision.Score >= 80, $"Expected score >= 80, got {decision.Score}");
    }

    [Fact]
    public async Task MidScore_ReturnsReview()
    {
        // Amount between thresholds with amount-heavy weighting → should review
        var engine = CreateEngine(
            options: DefaultOptions(blockThreshold: 80, reviewThreshold: 50),
            rules: DefaultRules(
                amountWeight: 100,
                frequencyWeight: 0,
                geoWeight: 0,
                deviceWeight: 0,
                historyWeight: 0,
                highAmountThreshold: 100_00,
                criticalAmountThreshold: 1000_00));

        // Amount between high and critical thresholds → score in 50-100 range
        // With block at 80 and review at 50, we need score in [50, 80)
        var request = CreateRequest(amountKobo: 200_00); // Slightly above high threshold

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(RiskVerdict.Review, decision.Decision);
        Assert.True(decision.Score >= 50 && decision.Score < 80,
            $"Expected score in [50, 80), got {decision.Score}");
    }

    [Fact]
    public async Task Timeout_AppliesFallbackPolicy_Deny()
    {
        // Create engine with very short timeout and a slow audit store
        var slowAuditStore = new InMemoryAuditStore();
        var engine = new SlowFraudRiskEngine(
            Options.Create(DefaultOptions(maxEvalTimeMs: 50, fallback: FallbackPolicy.Deny)),
            Options.Create(DefaultRules()),
            slowAuditStore,
            NullLogger<FraudRiskEngine>.Instance,
            evaluationDelayMs: 500); // Simulates slow evaluation

        var request = CreateRequest();

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(RiskVerdict.Deny, decision.Decision);
        Assert.Equal(0, decision.Score);
        Assert.Contains(decision.Reasons, r => r.Contains("Fallback applied"));
    }

    [Fact]
    public async Task Timeout_AppliesFallbackPolicy_Approve()
    {
        var engine = new SlowFraudRiskEngine(
            Options.Create(DefaultOptions(maxEvalTimeMs: 50, fallback: FallbackPolicy.Approve)),
            Options.Create(DefaultRules()),
            new InMemoryAuditStore(),
            NullLogger<FraudRiskEngine>.Instance,
            evaluationDelayMs: 500);

        var request = CreateRequest();

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(RiskVerdict.Approve, decision.Decision);
        Assert.Equal(0, decision.Score);
    }

    [Fact]
    public async Task Timeout_AppliesFallbackPolicy_Review()
    {
        var engine = new SlowFraudRiskEngine(
            Options.Create(DefaultOptions(maxEvalTimeMs: 50, fallback: FallbackPolicy.Review)),
            Options.Create(DefaultRules()),
            new InMemoryAuditStore(),
            NullLogger<FraudRiskEngine>.Instance,
            evaluationDelayMs: 500);

        var request = CreateRequest();

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(RiskVerdict.Review, decision.Decision);
        Assert.Equal(0, decision.Score);
    }

    [Fact]
    public async Task EngineUnavailable_AppliesFallbackPolicy()
    {
        var engine = new ThrowingFraudRiskEngine(
            Options.Create(DefaultOptions(fallback: FallbackPolicy.Deny)),
            Options.Create(DefaultRules()),
            new InMemoryAuditStore(),
            NullLogger<FraudRiskEngine>.Instance);

        var request = CreateRequest();

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.Equal(RiskVerdict.Deny, decision.Decision);
        Assert.Equal(0, decision.Score);
        Assert.Contains(decision.Reasons, r => r.Contains("Fallback applied"));
    }

    [Fact]
    public async Task EvaluationCompletesWithin200ms()
    {
        var engine = CreateEngine();
        var request = CreateRequest();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var decision = await engine.EvaluateAsync(request, CancellationToken.None);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Evaluation took {sw.ElapsedMilliseconds}ms, expected < 200ms");
        Assert.NotNull(decision);
    }

    [Fact]
    public async Task ScoreRange_IsBetween0And100()
    {
        var engine = CreateEngine();
        var request = CreateRequest(amountKobo: 10_000_000_00); // Very high amount

        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.InRange(decision.Score, 0, 100);
    }

    [Fact]
    public async Task VelocityCheck_HighFrequency_IncreasesScore()
    {
        var engine = CreateEngine(
            rules: DefaultRules(
                amountWeight: 0,
                frequencyWeight: 100,
                geoWeight: 0,
                deviceWeight: 0,
                historyWeight: 0));

        string sourceAccount = "velocity-test-account";

        // Simulate many transactions from same account
        for (int i = 0; i < 15; i++)
        {
            var req = CreateRequest(amountKobo: 100_00, sourceAccount: sourceAccount);
            await engine.EvaluateAsync(req, CancellationToken.None);
        }

        // Next one should have very high frequency score
        var finalRequest = CreateRequest(amountKobo: 100_00, sourceAccount: sourceAccount);
        var decision = await engine.EvaluateAsync(finalRequest, CancellationToken.None);

        // With 16 transactions and max of 10 per window, frequency should be at max
        Assert.True(decision.Score >= 80,
            $"Expected high velocity score, got {decision.Score}");
    }

    [Fact]
    public async Task AuditStore_ReceivesDecisionLog()
    {
        var auditStore = new InMemoryAuditStore();
        var engine = CreateEngine(auditStore: auditStore);

        var request = CreateRequest();

        await engine.EvaluateAsync(request, CancellationToken.None);

        var entries = await auditStore.GetByTransactionReferenceAsync(
            request.TransactionReference, CancellationToken.None);

        Assert.Single(entries);
        Assert.Equal("FraudRiskEngine", entries[0].ActorIdentity);
        Assert.Equal("RiskEvaluation", entries[0].Action);
        Assert.Contains("Score=", entries[0].NewState!);
        Assert.Contains("Decision=", entries[0].NewState!);
    }

    [Fact]
    public async Task AuditStoreFailure_DoesNotAffectDecision()
    {
        var failingAuditStore = new FailingAuditStore();
        var engine = CreateEngine(auditStore: failingAuditStore);

        var request = CreateRequest();

        // Should not throw even if audit store fails
        var decision = await engine.EvaluateAsync(request, CancellationToken.None);

        Assert.NotNull(decision);
        Assert.Equal(RiskVerdict.Approve, decision.Decision);
    }

    [Fact]
    public async Task CallerCancellation_ThrowsOperationCanceled()
    {
        var engine = CreateEngine();
        var request = CreateRequest();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => engine.EvaluateAsync(request, cts.Token));
    }

    #region Test Helpers

    /// <summary>
    /// In-memory audit store for testing.
    /// </summary>
    private class InMemoryAuditStore : IAuditStore
    {
        private readonly List<AuditEntry> _entries = new();

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            var result = _entries.Where(e => e.TransactionReference == transactionReference).ToList();
            return Task.FromResult<IReadOnlyList<AuditEntry>>(result.AsReadOnly());
        }
    }

    /// <summary>
    /// Audit store that always throws.
    /// </summary>
    private class FailingAuditStore : IAuditStore
    {
        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
            => throw new InvalidOperationException("Audit store unavailable");

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
            => throw new InvalidOperationException("Audit store unavailable");
    }

    /// <summary>
    /// FraudRiskEngine subclass that simulates slow evaluation for timeout testing.
    /// </summary>
    private class SlowFraudRiskEngine : FraudRiskEngine
    {
        private readonly int _delayMs;

        public SlowFraudRiskEngine(
            IOptions<FraudRiskOptions> options,
            IOptions<RiskScoringRules> rules,
            IAuditStore auditStore,
            ILogger<FraudRiskEngine> logger,
            int evaluationDelayMs)
            : base(options, rules, auditStore, logger)
        {
            _delayMs = evaluationDelayMs;
        }

        internal override async Task<RiskDecision> PerformEvaluationAsync(PaymentRequest request, CancellationToken ct)
        {
            await Task.Delay(_delayMs, ct);
            return await base.PerformEvaluationAsync(request, ct);
        }
    }

    /// <summary>
    /// FraudRiskEngine subclass that throws during evaluation to simulate unavailability.
    /// </summary>
    private class ThrowingFraudRiskEngine : FraudRiskEngine
    {
        public ThrowingFraudRiskEngine(
            IOptions<FraudRiskOptions> options,
            IOptions<RiskScoringRules> rules,
            IAuditStore auditStore,
            ILogger<FraudRiskEngine> logger)
            : base(options, rules, auditStore, logger)
        {
        }

        internal override Task<RiskDecision> PerformEvaluationAsync(PaymentRequest request, CancellationToken ct)
        {
            throw new InvalidOperationException("Fraud engine unavailable - simulated failure");
        }
    }

    #endregion
}
