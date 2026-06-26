using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.PlatformServices.Reconciliation.DTOs;

/// <summary>
/// Result of evaluating a reconciliation exception against auto-adjustment rules.
/// </summary>
public record AdjustmentDecision
{
    /// <summary>
    /// Whether an automatic adjustment should be applied.
    /// </summary>
    public required bool ShouldAutoAdjust { get; init; }

    /// <summary>
    /// The adjustment amount if auto-adjustment is recommended.
    /// </summary>
    public Money? AdjustmentAmount { get; init; }

    /// <summary>
    /// The reason for the adjustment decision.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// The name of the rule that triggered the decision.
    /// </summary>
    public string? RuleName { get; init; }

    /// <summary>
    /// Creates a decision indicating no auto-adjustment should be applied.
    /// </summary>
    public static AdjustmentDecision NoAdjustment() => new()
    {
        ShouldAutoAdjust = false,
        AdjustmentAmount = null,
        Reason = null,
        RuleName = null
    };

    /// <summary>
    /// Creates a decision indicating an auto-adjustment should be applied.
    /// </summary>
    public static AdjustmentDecision AutoAdjust(Money amount, string reason, string ruleName) => new()
    {
        ShouldAutoAdjust = true,
        AdjustmentAmount = amount,
        Reason = reason,
        RuleName = ruleName
    };
}
