using CardManagement.Domain.PlatformServices.Notifications;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Notifications;

public class RenderedNotificationTests
{
    [Fact]
    public void Constructor_WithValidEmailInputs_CreatesNotification()
    {
        var templateId = Guid.NewGuid();
        var notification = new RenderedNotification(
            body: "Your payment of 5000 NGN was successful.",
            channel: NotificationChannel.Email,
            recipientAddress: "user@example.com",
            templateId: templateId,
            recipientId: "user-123",
            subject: "Payment Confirmed");

        Assert.Equal("Your payment of 5000 NGN was successful.", notification.Body);
        Assert.Equal(NotificationChannel.Email, notification.Channel);
        Assert.Equal("user@example.com", notification.RecipientAddress);
        Assert.Equal(templateId, notification.TemplateId);
        Assert.Equal("user-123", notification.RecipientId);
        Assert.Equal("Payment Confirmed", notification.Subject);
    }

    [Fact]
    public void Constructor_WithValidSmsInputs_CreatesNotification()
    {
        var notification = new RenderedNotification(
            body: "Your OTP is 123456",
            channel: NotificationChannel.Sms,
            recipientAddress: "+2348012345678",
            templateId: Guid.NewGuid(),
            recipientId: "user-456");

        Assert.Equal(NotificationChannel.Sms, notification.Channel);
        Assert.Null(notification.Subject);
    }

    [Fact]
    public void Constructor_WithEmptyBody_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RenderedNotification("", NotificationChannel.Sms, "+234801234", Guid.NewGuid(), "user-1"));
    }

    [Fact]
    public void Constructor_WithEmptyRecipientAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RenderedNotification("body", NotificationChannel.Sms, "", Guid.NewGuid(), "user-1"));
    }

    [Fact]
    public void Constructor_WithEmptyTemplateId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RenderedNotification("body", NotificationChannel.Sms, "+234801234", Guid.Empty, "user-1"));
    }

    [Fact]
    public void Constructor_WithEmptyRecipientId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RenderedNotification("body", NotificationChannel.Sms, "+234801234", Guid.NewGuid(), ""));
    }

    [Fact]
    public void Constructor_EmailWithoutSubject_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RenderedNotification("body", NotificationChannel.Email, "user@example.com", Guid.NewGuid(), "user-1"));
    }

    [Fact]
    public void Constructor_SmsWithSubject_IsAccepted()
    {
        // Subject for non-email channels is optional and acceptable
        var notification = new RenderedNotification(
            body: "test",
            channel: NotificationChannel.Sms,
            recipientAddress: "+2348012345678",
            templateId: Guid.NewGuid(),
            recipientId: "user-1",
            subject: "optional subject");

        Assert.Equal("optional subject", notification.Subject);
    }
}
