namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Classifies the type of reconciliation exception identified during matching.
/// </summary>
public enum ExceptionType
{
    /// <summary>
    /// Settlement line item matched an internal record but amount or status differs.
    /// </summary>
    Mismatch,

    /// <summary>
    /// Settlement line item has no matching internal record.
    /// </summary>
    UnmatchedExternal,

    /// <summary>
    /// Internal record has no matching settlement line item for the period.
    /// </summary>
    UnmatchedInternal
}
