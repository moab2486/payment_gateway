namespace CardManagement.Domain.PlatformServices.Notifications;

/// <summary>
/// Represents a fully rendered notification ready for delivery.
/// Holds the rendered content after template variable substitution,
/// along with recipient and channel routing information.
/// </summary>
public record RenderedNotification
{
    /// <summary>
    /// The rendered subject line (applicable for Email channel).
    /// </summary>
    public string? Subject { get; }

    /// <summary>
    /// The rendered message body.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// The delivery channel for this rendered notification.
    /// </summary>
    public NotificationChannel Channel { get; }

    /// <summary>
    /// The recipient identifier (email address, phone number, or WhatsApp ID).
    /// </summary>
    public string RecipientAddress { get; }

    /// <summary>
    /// The ID of the template used for rendering.
    /// </summary>
    public Guid TemplateId { get; }

    /// <summary>
    /// The recipient's internal identifier.
    /// </summary>
    public string RecipientId { get; }

    public RenderedNotification(
        string body,
        NotificationChannel channel,
        string recipientAddress,
        Guid templateId,
        string recipientId,
        string? subject = null)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Body is required.", nameof(body));

        if (string.IsNullOrWhiteSpace(recipientAddress))
            throw new ArgumentException("Recipient address is required.", nameof(recipientAddress));

        if (templateId == Guid.Empty)
            throw new ArgumentException("Template ID is required.", nameof(templateId));

        if (string.IsNullOrWhiteSpace(recipientId))
            throw new ArgumentException("Recipient ID is required.", nameof(recipientId));

        if (channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject is required for email notifications.", nameof(subject));

        Body = body;
        Channel = channel;
        RecipientAddress = recipientAddress;
        TemplateId = templateId;
        RecipientId = recipientId;
        Subject = subject;
    }
}
