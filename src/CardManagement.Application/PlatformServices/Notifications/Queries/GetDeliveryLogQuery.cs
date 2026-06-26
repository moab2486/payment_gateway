namespace CardManagement.Application.PlatformServices.Notifications.Queries;

/// <summary>
/// Query to retrieve delivery log entries for a specific recipient.
/// </summary>
public record GetDeliveryLogQuery(string RecipientId, int Limit = 50);
