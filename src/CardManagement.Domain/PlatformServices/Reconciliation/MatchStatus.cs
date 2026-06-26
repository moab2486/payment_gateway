namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Indicates the matching result for a settlement line item against internal records.
/// </summary>
public enum MatchStatus
{
    Unmatched,
    Matched,
    Mismatched
}
