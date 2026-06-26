namespace CardManagement.Domain.PlatformServices.Notifications;

/// <summary>
/// Represents a notification template with channel-specific content variants.
/// Templates define the structure of messages sent via Email, SMS, or WhatsApp,
/// with required variables that must be provided at dispatch time.
/// </summary>
public class NotificationTemplate
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string? EmailSubjectTemplate { get; private set; }
    public string? EmailBodyTemplate { get; private set; }
    public string? SmsBodyTemplate { get; private set; }
    public string? WhatsAppBodyTemplate { get; private set; }
    public string[] RequiredVariables { get; private set; } = Array.Empty<string>();
    public int Version { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private NotificationTemplate() { }

    public static NotificationTemplate Create(
        string name,
        string category,
        string[] requiredVariables,
        string? emailSubjectTemplate = null,
        string? emailBodyTemplate = null,
        string? smsBodyTemplate = null,
        string? whatsAppBodyTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        if (string.IsNullOrWhiteSpace(category))
            throw new ArgumentException("Template category is required.", nameof(category));

        if (requiredVariables is null)
            throw new ArgumentNullException(nameof(requiredVariables));

        if (emailSubjectTemplate is null && emailBodyTemplate is null &&
            smsBodyTemplate is null && whatsAppBodyTemplate is null)
        {
            throw new ArgumentException("At least one channel template must be provided.");
        }

        return new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = category,
            RequiredVariables = requiredVariables,
            EmailSubjectTemplate = emailSubjectTemplate,
            EmailBodyTemplate = emailBodyTemplate,
            SmsBodyTemplate = smsBodyTemplate,
            WhatsAppBodyTemplate = whatsAppBodyTemplate,
            Version = 1,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Updates the template content and increments the version.
    /// </summary>
    public void Update(
        string? emailSubjectTemplate,
        string? emailBodyTemplate,
        string? smsBodyTemplate,
        string? whatsAppBodyTemplate,
        string[] requiredVariables)
    {
        if (requiredVariables is null)
            throw new ArgumentNullException(nameof(requiredVariables));

        if (emailSubjectTemplate is null && emailBodyTemplate is null &&
            smsBodyTemplate is null && whatsAppBodyTemplate is null)
        {
            throw new ArgumentException("At least one channel template must be provided.");
        }

        EmailSubjectTemplate = emailSubjectTemplate;
        EmailBodyTemplate = emailBodyTemplate;
        SmsBodyTemplate = smsBodyTemplate;
        WhatsAppBodyTemplate = whatsAppBodyTemplate;
        RequiredVariables = requiredVariables;
        Version++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns whether this template supports the specified channel.
    /// </summary>
    public bool SupportsChannel(NotificationChannel channel)
    {
        return channel switch
        {
            NotificationChannel.Email => EmailSubjectTemplate is not null && EmailBodyTemplate is not null,
            NotificationChannel.Sms => SmsBodyTemplate is not null,
            NotificationChannel.WhatsApp => WhatsAppBodyTemplate is not null,
            _ => false
        };
    }
}
