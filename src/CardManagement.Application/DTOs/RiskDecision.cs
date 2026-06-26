namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the outcome of a fraud/risk evaluation.
/// </summary>
public record RiskDecision(
    int Score,
    RiskVerdict Decision,
    IReadOnlyList<string> Reasons);

/// <summary>
/// The fraud risk engine's verdict on a transaction.
/// </summary>
public enum RiskVerdict
{
    Approve,
    Review,
    Deny
}
