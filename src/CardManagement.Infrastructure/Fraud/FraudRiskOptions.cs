namespace CardManagement.Infrastructure.Fraud;

/// <summary>
/// Configurable fallback policy when the fraud engine is unavailable or times out.
/// </summary>
public enum FallbackPolicy
{
    Approve,
    Deny,
    Review
}

/// <summary>
/// Configuration options for the Fraud Risk Engine.
/// </summary>
public class FraudRiskOptions
{
    public const string SectionName = "FraudRisk";

    /// <summary>
    /// Risk score at or above which transactions are denied. Range 0-100.
    /// </summary>
    public int BlockThreshold { get; set; } = 80;

    /// <summary>
    /// Risk score at or above which transactions are queued for manual review (but below BlockThreshold). Range 0-100.
    /// </summary>
    public int ReviewThreshold { get; set; } = 50;

    /// <summary>
    /// Policy to apply when the engine is unavailable or exceeds the evaluation timeout.
    /// </summary>
    public FallbackPolicy FallbackPolicy { get; set; } = FallbackPolicy.Review;

    /// <summary>
    /// Maximum time in milliseconds allowed for fraud evaluation. Default 200ms.
    /// </summary>
    public int MaxEvaluationTimeMs { get; set; } = 200;
}
