namespace CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;

/// <summary>
/// Denormalized read model projection for channel health dashboard.
/// Tracks delivery success/failure rates per notification and webhook channel.
/// </summary>
public class ChannelHealthReadModel
{
    public DateOnly Date { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public long TotalDeliveries { get; set; }
    public long SuccessfulDeliveries { get; set; }
    public long FailedDeliveries { get; set; }
    public double AverageLatencyMs { get; set; }
    public double UptimePercentage { get; set; }
    public DateTime ProjectedAtUtc { get; set; }
}
