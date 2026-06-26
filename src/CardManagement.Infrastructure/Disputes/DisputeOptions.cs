using CardManagement.Domain.Enums;

namespace CardManagement.Infrastructure.Disputes;

/// <summary>
/// Configuration for the Dispute Service including network-mandated timeframe rules per channel.
/// </summary>
public sealed class DisputeOptions
{
    /// <summary>
    /// Maximum number of days after a transaction within which a dispute can be raised.
    /// This value is used when no per-channel override is specified in ChannelTimeframeDays.
    /// </summary>
    public int MaxDisputeWindowDays { get; set; } = 120;

    /// <summary>
    /// Per-channel override for the dispute timeframe (in days).
    /// Keys are PaymentChannel enum names. If a channel is not specified here,
    /// MaxDisputeWindowDays applies as the default.
    /// </summary>
    public Dictionary<string, int> ChannelTimeframeDays { get; set; } = new();

    /// <summary>
    /// Kafka topic for dispute lifecycle events.
    /// </summary>
    public string KafkaTopic { get; set; } = "cardmgmt.events.disputes";
}
