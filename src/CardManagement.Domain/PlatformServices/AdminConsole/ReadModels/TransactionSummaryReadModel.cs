namespace CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;

/// <summary>
/// Denormalized read model projection for transaction summary dashboard.
/// Aggregates payment transaction counts and amounts by date and channel.
/// </summary>
public class TransactionSummaryReadModel
{
    public DateOnly Date { get; set; }
    public string Channel { get; set; } = string.Empty;
    public long TotalCount { get; set; }
    public long SuccessCount { get; set; }
    public long FailedCount { get; set; }
    public long TotalAmountKobo { get; set; }
    public DateTime ProjectedAtUtc { get; set; }
}
