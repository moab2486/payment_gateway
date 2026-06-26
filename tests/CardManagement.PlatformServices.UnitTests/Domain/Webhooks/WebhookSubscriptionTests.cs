using CardManagement.Domain.PlatformServices.Webhooks;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Webhooks;

public class WebhookSubscriptionTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsActiveSubscription()
    {
        var merchantId = Guid.NewGuid();
        var url = "https://example.com/webhook";
        var eventTypes = new[] { "payment.completed", "dispute.created" };
        var secret = "whsec_test123";

        var subscription = WebhookSubscription.Create(merchantId, url, eventTypes, secret);

        Assert.Equal(merchantId, subscription.MerchantId);
        Assert.Equal(url, subscription.DestinationUrl);
        Assert.Equal(eventTypes, subscription.EventTypes);
        Assert.Equal(secret, subscription.SigningSecret);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(0, subscription.ConsecutiveFailures);
        Assert.NotEqual(Guid.Empty, subscription.Id);
    }

    [Fact]
    public void Create_WithEmptyMerchantId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookSubscription.Create(Guid.Empty, "https://example.com", new[] { "payment.completed" }, "secret"));
    }

    [Fact]
    public void Create_WithEmptyUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookSubscription.Create(Guid.NewGuid(), "", new[] { "payment.completed" }, "secret"));
    }

    [Fact]
    public void Create_WithEmptyEventTypes_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookSubscription.Create(Guid.NewGuid(), "https://example.com", Array.Empty<string>(), "secret"));
    }

    [Fact]
    public void Create_WithEmptySecret_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookSubscription.Create(Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, ""));
    }

    [Fact]
    public void RecordSuccessfulDelivery_ResetsConsecutiveFailures()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");

        subscription.RecordFailedDelivery();
        subscription.RecordFailedDelivery();
        Assert.Equal(2, subscription.ConsecutiveFailures);

        subscription.RecordSuccessfulDelivery();
        Assert.Equal(0, subscription.ConsecutiveFailures);
        Assert.NotNull(subscription.LastDeliveryAtUtc);
    }

    [Fact]
    public void RecordFailedDelivery_IncrementsConsecutiveFailures()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");

        subscription.RecordFailedDelivery();
        Assert.Equal(1, subscription.ConsecutiveFailures);

        subscription.RecordFailedDelivery();
        Assert.Equal(2, subscription.ConsecutiveFailures);
    }

    [Fact]
    public void Suspend_ActiveSubscription_TransitionsToSuspended()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");

        subscription.Suspend();

        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
    }

    [Fact]
    public void Suspend_NonActiveSubscription_Throws()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");
        subscription.Suspend();

        Assert.Throws<InvalidOperationException>(() => subscription.Suspend());
    }

    [Fact]
    public void Reactivate_SuspendedSubscription_TransitionsToActive()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");
        subscription.Suspend();

        subscription.Reactivate();

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(0, subscription.ConsecutiveFailures);
    }

    [Fact]
    public void Reactivate_NonSuspendedSubscription_Throws()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");

        Assert.Throws<InvalidOperationException>(() => subscription.Reactivate());
    }

    [Fact]
    public void Deactivate_ActiveSubscription_TransitionsToDeactivated()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");

        subscription.Deactivate();

        Assert.Equal(SubscriptionStatus.Deactivated, subscription.Status);
    }

    [Fact]
    public void Deactivate_SuspendedSubscription_TransitionsToDeactivated()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");
        subscription.Suspend();

        subscription.Deactivate();

        Assert.Equal(SubscriptionStatus.Deactivated, subscription.Status);
    }

    [Fact]
    public void Deactivate_AlreadyDeactivated_Throws()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");
        subscription.Deactivate();

        Assert.Throws<InvalidOperationException>(() => subscription.Deactivate());
    }

    [Fact]
    public void RecordDelivery_OnDeactivatedSubscription_Throws()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");
        subscription.Deactivate();

        Assert.Throws<InvalidOperationException>(() => subscription.RecordSuccessfulDelivery());
        Assert.Throws<InvalidOperationException>(() => subscription.RecordFailedDelivery());
    }

    [Fact]
    public void Update_OnDeactivatedSubscription_Throws()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");
        subscription.Deactivate();

        Assert.Throws<InvalidOperationException>(() =>
            subscription.Update("https://new-url.com", null));
    }

    [Fact]
    public void Update_ChangesUrlAndEventTypes()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");

        subscription.Update("https://new-url.com", new[] { "dispute.created" });

        Assert.Equal("https://new-url.com", subscription.DestinationUrl);
        Assert.Equal(new[] { "dispute.created" }, subscription.EventTypes);
    }

    [Fact]
    public void HasExceededFailureThreshold_ReturnsTrueWhenAtThreshold()
    {
        var subscription = WebhookSubscription.Create(
            Guid.NewGuid(), "https://example.com", new[] { "payment.completed" }, "secret");

        subscription.RecordFailedDelivery();
        subscription.RecordFailedDelivery();
        subscription.RecordFailedDelivery();

        Assert.True(subscription.HasExceededFailureThreshold(3));
        Assert.False(subscription.HasExceededFailureThreshold(4));
    }
}
