namespace CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;

/// <summary>
/// Denormalized read model projection for dispute metrics dashboard.
/// Aggregates dispute lifecycle data for operations monitoring.
/// </summary>
public class DisputeMetricsReadModel
{
    public DateOnly Date { get; set; }
    public string DisputeType { get; set; } = string.Empty;
    public int OpenedCount { get; set; }
    public int ResolvedCount { get; set; }
    public int EscalatedCount { get; set; }
    public long TotalDisputedAmountKobo { get; set; }
    public double AverageResolutionHours { get; set; }
    public DateTime ProjectedAtUtc { get; set; }
}
