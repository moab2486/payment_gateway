namespace CardManagement.Infrastructure.Fraud;

/// <summary>
/// Configurable weights for each risk scoring factor.
/// Each weight is 0-100 representing relative importance.
/// The final score is the weighted sum normalized to 0-100.
/// </summary>
public class RiskScoringRules
{
    public const string SectionName = "FraudRisk:ScoringRules";

    /// <summary>
    /// Weight for the transaction amount factor (0-100).
    /// Higher amounts relative to thresholds increase risk.
    /// </summary>
    public int AmountWeight { get; set; } = 40;

    /// <summary>
    /// Weight for the frequency/velocity factor (0-100).
    /// Rapid consecutive transactions increase risk.
    /// </summary>
    public int FrequencyWeight { get; set; } = 30;

    /// <summary>
    /// Weight for geographic indicator factor (0-100).
    /// Unusual geographic patterns increase risk.
    /// </summary>
    public int GeoWeight { get; set; } = 10;

    /// <summary>
    /// Weight for device fingerprint factor (0-100).
    /// Unrecognized or suspicious devices increase risk.
    /// </summary>
    public int DeviceWeight { get; set; } = 10;

    /// <summary>
    /// Weight for historical pattern factor (0-100).
    /// Deviation from historical behavior increases risk.
    /// </summary>
    public int HistoryWeight { get; set; } = 10;

    /// <summary>
    /// Amount threshold in smallest currency unit (e.g., kobo) above which
    /// the amount factor starts scoring high risk.
    /// </summary>
    public long HighAmountThreshold { get; set; } = 500_000_00; // 500,000 NGN in kobo

    /// <summary>
    /// Amount threshold in smallest currency unit above which the
    /// amount factor scores maximum risk.
    /// </summary>
    public long CriticalAmountThreshold { get; set; } = 5_000_000_00; // 5,000,000 NGN in kobo

    /// <summary>
    /// Maximum number of transactions per time window before velocity triggers.
    /// </summary>
    public int MaxTransactionsPerWindow { get; set; } = 10;

    /// <summary>
    /// Time window in seconds for velocity checks.
    /// </summary>
    public int VelocityWindowSeconds { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Returns the total weight across all factors for normalization.
    /// </summary>
    public int TotalWeight => AmountWeight + FrequencyWeight + GeoWeight + DeviceWeight + HistoryWeight;
}
