namespace CardManagement.Application.PlatformServices.Notifications.Queries;

/// <summary>
/// Query to retrieve notification preferences for a specific recipient.
/// </summary>
public record GetPreferencesQuery(string RecipientId);
