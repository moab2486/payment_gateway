using CardManagement.Domain.PlatformServices.Webhooks;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Webhooks;

public class DlqItemTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsUnreplayedItem()
    {
        var deliveryId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var payload = "{\"event\":\"payment.completed\",\"amount\":1000}";
        var lastError = "Connection timeout after 30s";

        var dlqItem = DlqItem.Create(deliveryId, subscriptionId, payload, lastError);

        Assert.Equal(deliveryId, dlqItem.DeliveryId);
        Assert.Equal(subscriptionId, dlqItem.SubscriptionId);
        Assert.Equal(payload, dlqItem.OriginalPayload);
        Assert.Equal(lastError, dlqItem.LastError);
        Assert.False(dlqItem.Replayed);
        Assert.NotEqual(Guid.Empty, dlqItem.Id);
    }

    [Fact]
    public void Create_WithEmptyDeliveryId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DlqItem.Create(Guid.Empty, Guid.NewGuid(), "{}", "error"));
    }

    [Fact]
    public void Create_WithEmptySubscriptionId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DlqItem.Create(Guid.NewGuid(), Guid.Empty, "{}", "error"));
    }

    [Fact]
    public void Create_WithEmptyPayload_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DlqItem.Create(Guid.NewGuid(), Guid.NewGuid(), "", "error"));
    }

    [Fact]
    public void Create_WithEmptyLastError_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            DlqItem.Create(Guid.NewGuid(), Guid.NewGuid(), "{}", ""));
    }

    [Fact]
    public void MarkReplayed_SetsReplayedToTrue()
    {
        var dlqItem = DlqItem.Create(Guid.NewGuid(), Guid.NewGuid(), "{\"data\":1}", "timeout");

        dlqItem.MarkReplayed();

        Assert.True(dlqItem.Replayed);
    }

    [Fact]
    public void MarkReplayed_WhenAlreadyReplayed_Throws()
    {
        var dlqItem = DlqItem.Create(Guid.NewGuid(), Guid.NewGuid(), "{\"data\":1}", "timeout");
        dlqItem.MarkReplayed();

        Assert.Throws<InvalidOperationException>(() => dlqItem.MarkReplayed());
    }
}
