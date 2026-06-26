namespace CardManagement.Infrastructure.PlatformServices.Reconciliation;

/// <summary>
/// Configuration options for the auto-adjustment rule engine.
/// Loaded from configuration section "Reconciliation:AdjustmentRules".
/// </summary>
public sealed class AdjustmentRuleOptions
{
    /// <summary>
    /// List of configurable adjustment rules evaluated in order.
    /// </summary>
    public List<AdjustmentRuleDefinition> Rules { get; set; } = new();
}

/// <summary>
/// Defines a single auto-adjustment rule evaluated against reconciliation exceptions.
/// A rule matches when all specified conditions (type, threshold, processor) are satisfied.
/// </summary>
public sealed class AdjustmentRuleDefinition
{
    /// <summary>
    /// The name of this rule for audit/tracing purposes.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The exception type this rule applies to (Mismatch, UnmatchedExternal, UnmatchedInternal).
    /// If null, the rule applies to any exception type.
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Maximum absolute amount difference (in smallest currency unit) for auto-adjustment eligibility.
    /// Exceptions with differences exceeding this threshold are not auto-adjusted.
    /// If null, no threshold is applied.
    /// </summary>
    public long? AmountThreshold { get; set; }

    /// <summary>
    /// The processor this rule applies to (NIBSS, Interswitch, Cardify).
    /// If null, the rule applies to any processor.
    /// </summary>
    public string? Processor { get; set; }

    /// <summary>
    /// The reason text to record when this rule auto-resolves an exception.
    /// </summary>
    public string Reason { get; set; } = "Auto-adjusted by rule engine";
}
