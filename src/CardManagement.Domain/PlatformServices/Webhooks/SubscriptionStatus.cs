namespace CardManagement.Domain.PlatformServices.Webhooks;

/// <summary>
/// Represents the lifecycle status of a webhook subscription.
/// </summary>
public enum SubscriptionStatus
{
    Active,
    Suspended,
    Deactivated
}
