namespace CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;

/// <summary>
/// Denormalized read model projection for reconciliation status dashboard.
/// Tracks batch processing progress and exception resolution rates per processor.
/// </summary>
public class ReconciliationStatusReadModel
{
    public Guid BatchId { get; set; }
    public string Processor { get; set; } = string.Empty;
    public DateOnly SettlementDate { get; set; }
    public int TotalRecords { get; set; }
    public int MatchedRecords { get; set; }
    public int ExceptionCount { get; set; }
    public int ResolvedCount { get; set; }
    public DateTime ProjectedAtUtc { get; set; }
}
