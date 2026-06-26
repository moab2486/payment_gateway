namespace CardManagement.Application.PlatformServices.Notifications.DTOs;

/// <summary>
/// Aggregated delivery statistics for notifications, optionally filtered by channel and template.
/// </summary>
public record DeliveryStatistics(
    long TotalSent,
    long TotalDelivered,
    long TotalFailed,
    double AverageDeliveryTimeMs,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc);
