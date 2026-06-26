using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Queries;

/// <summary>
/// Query to retrieve aggregated delivery statistics, optionally filtered by channel and template.
/// </summary>
public record GetDeliveryStatisticsQuery(
    NotificationChannel? Channel,
    Guid? TemplateId,
    DateRange Range);
