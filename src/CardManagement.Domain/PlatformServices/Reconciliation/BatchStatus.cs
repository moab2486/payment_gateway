namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Represents the processing status of a reconciliation batch.
/// </summary>
public enum BatchStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
