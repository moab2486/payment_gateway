namespace CardManagement.Domain.PlatformServices.Notifications;

/// <summary>
/// Represents a recipient's notification delivery preferences including
/// primary/fallback channel selection and per-category opt-in settings.
/// </summary>
public class NotificationPreference
{
    public Guid Id { get; private set; }
    public string RecipientId { get; private set; } = string.Empty;
    public NotificationChannel PrimaryChannel { get; private set; }
    public NotificationChannel? FallbackChannel { get; private set; }
    public Dictionary<string, bool> CategoryOptIn { get; private set; } = new();

    private NotificationPreference() { }

    public static NotificationPreference Create(
        string recipientId,
        NotificationChannel primaryChannel,
        NotificationChannel? fallbackChannel = null)
    {
        if (string.IsNullOrWhiteSpace(recipientId))
            throw new ArgumentException("Recipient ID is required.", nameof(recipientId));

        if (fallbackChannel.HasValue && fallbackChannel.Value == primaryChannel)
            throw new ArgumentException("Fallback channel must differ from primary channel.", nameof(fallbackChannel));

        return new NotificationPreference
        {
            Id = Guid.NewGuid(),
            RecipientId = recipientId,
            PrimaryChannel = primaryChannel,
            FallbackChannel = fallbackChannel
        };
    }

    /// <summary>
    /// Updates the channel preferences.
    /// </summary>
    public void UpdateChannels(NotificationChannel primaryChannel, NotificationChannel? fallbackChannel)
    {
        if (fallbackChannel.HasValue && fallbackChannel.Value == primaryChannel)
            throw new ArgumentException("Fallback channel must differ from primary channel.", nameof(fallbackChannel));

        PrimaryChannel = primaryChannel;
        FallbackChannel = fallbackChannel;
    }

    /// <summary>
    /// Sets the opt-in status for a notification category.
    /// </summary>
    public void SetCategoryOptIn(string category, bool optedIn)
    {
        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Category is required.", nameof(category));

        CategoryOptIn[category] = optedIn;
    }

    /// <summary>
    /// Returns whether the recipient has opted in for the specified category.
    /// Defaults to true if no explicit preference is set for the category.
    /// </summary>
    public bool IsOptedIn(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return true;

        return !CategoryOptIn.TryGetValue(category, out var optedIn) || optedIn;
    }
}
