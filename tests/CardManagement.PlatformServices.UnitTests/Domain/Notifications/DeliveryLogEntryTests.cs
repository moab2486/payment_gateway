using CardManagement.Domain.PlatformServices.Notifications;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Notifications;

public class DeliveryLogEntryTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsPendingEntry()
    {
        var templateId = Guid.NewGuid();
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Email, templateId);

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal("user-123", entry.RecipientId);
        Assert.Equal(NotificationChannel.Email, entry.Channel);
        Assert.Equal(templateId, entry.TemplateId);
        Assert.Equal(NotificationDeliveryStatus.Pending, entry.Status);
        Assert.Null(entry.DeliveredAtUtc);
        Assert.Null(entry.FailureReason);
        Assert.Null(entry.ProviderMessageId);
    }

    [Fact]
    public void Create_WithEmptyRecipientId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryLogEntry.Create("", NotificationChannel.Email, Guid.NewGuid()));
    }

    [Fact]
    public void Create_WithEmptyTemplateId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryLogEntry.Create("user-123", NotificationChannel.Email, Guid.Empty));
    }

    [Fact]
    public void MarkSent_FromPending_TransitionsToSent()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Sms, Guid.NewGuid());

        entry.MarkSent("msg-abc-123");

        Assert.Equal(NotificationDeliveryStatus.Sent, entry.Status);
        Assert.Equal("msg-abc-123", entry.ProviderMessageId);
    }

    [Fact]
    public void MarkSent_WithEmptyMessageId_Throws()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Sms, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => entry.MarkSent(""));
    }

    [Fact]
    public void MarkSent_FromNonPending_Throws()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Sms, Guid.NewGuid());
        entry.MarkSent("msg-123");

        Assert.Throws<InvalidOperationException>(() => entry.MarkSent("msg-456"));
    }

    [Fact]
    public void MarkDelivered_FromPending_TransitionsToDelivered()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Email, Guid.NewGuid());

        entry.MarkDelivered();

        Assert.Equal(NotificationDeliveryStatus.Delivered, entry.Status);
        Assert.NotNull(entry.DeliveredAtUtc);
    }

    [Fact]
    public void MarkDelivered_FromSent_TransitionsToDelivered()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Email, Guid.NewGuid());
        entry.MarkSent("msg-123");

        entry.MarkDelivered();

        Assert.Equal(NotificationDeliveryStatus.Delivered, entry.Status);
        Assert.NotNull(entry.DeliveredAtUtc);
    }

    [Fact]
    public void MarkDelivered_FromFailed_Throws()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Email, Guid.NewGuid());
        entry.MarkFailed("timeout");

        Assert.Throws<InvalidOperationException>(() => entry.MarkDelivered());
    }

    [Fact]
    public void MarkFailed_FromPending_TransitionsToFailed()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Sms, Guid.NewGuid());

        entry.MarkFailed("Provider timeout");

        Assert.Equal(NotificationDeliveryStatus.Failed, entry.Status);
        Assert.Equal("Provider timeout", entry.FailureReason);
    }

    [Fact]
    public void MarkFailed_FromSent_TransitionsToFailed()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Sms, Guid.NewGuid());
        entry.MarkSent("msg-123");

        entry.MarkFailed("Delivery bounced");

        Assert.Equal(NotificationDeliveryStatus.Failed, entry.Status);
        Assert.Equal("Delivery bounced", entry.FailureReason);
    }

    [Fact]
    public void MarkFailed_WithEmptyReason_Throws()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Sms, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => entry.MarkFailed(""));
    }

    [Fact]
    public void MarkFailed_FromDelivered_Throws()
    {
        var entry = DeliveryLogEntry.Create("user-123", NotificationChannel.Sms, Guid.NewGuid());
        entry.MarkDelivered();

        Assert.Throws<InvalidOperationException>(() => entry.MarkFailed("late failure"));
    }
}
