using CardManagement.Domain.PlatformServices.Webhooks;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Webhooks;

public class WebhookDeliveryTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsPendingDelivery()
    {
        var subscriptionId = Guid.NewGuid();
        var eventType = "payment.completed";
        var payload = "{\"amount\":1000}";

        var delivery = WebhookDelivery.Create(subscriptionId, eventType, payload);

        Assert.Equal(subscriptionId, delivery.SubscriptionId);
        Assert.Equal(eventType, delivery.EventType);
        Assert.Equal(payload, delivery.Payload);
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Empty(delivery.Attempts);
        Assert.NotEqual(Guid.Empty, delivery.Id);
    }

    [Fact]
    public void Create_WithEmptySubscriptionId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookDelivery.Create(Guid.Empty, "payment.completed", "{\"data\":1}"));
    }

    [Fact]
    public void Create_WithEmptyEventType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookDelivery.Create(Guid.NewGuid(), "", "{\"data\":1}"));
    }

    [Fact]
    public void Create_WithEmptyPayload_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", ""));
    }

    [Fact]
    public void MarkDelivered_RecordsAttemptAndTransitionsStatus()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");

        delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(150));

        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Single(delivery.Attempts);
        Assert.Equal(200, delivery.Attempts[0].HttpStatusCode);
        Assert.Null(delivery.NextRetryAtUtc);
    }

    [Fact]
    public void MarkDelivered_WhenAlreadyDelivered_Throws()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");
        delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(100));

        Assert.Throws<InvalidOperationException>(() =>
            delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void RecordFailedAttempt_IncrementsCountAndAddsAttempt()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");

        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(2000), "Internal Server Error");

        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Single(delivery.Attempts);
        Assert.Equal(500, delivery.Attempts[0].HttpStatusCode);
        Assert.Equal("Internal Server Error", delivery.Attempts[0].ErrorMessage);
    }

    [Fact]
    public void RecordFailedAttempt_WhenDeadLettered_Throws()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");
        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "error");
        delivery.MoveToDlq();

        Assert.Throws<InvalidOperationException>(() =>
            delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "error"));
    }

    [Fact]
    public void ScheduleRetry_SetsNextRetryAndTransitionsToPending()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");
        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "error");

        var retryTime = DateTime.UtcNow.AddMinutes(5);
        delivery.ScheduleRetry(retryTime);

        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.NotNull(delivery.NextRetryAtUtc);
    }

    [Fact]
    public void ScheduleRetry_WhenNotFailed_Throws()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");

        Assert.Throws<InvalidOperationException>(() =>
            delivery.ScheduleRetry(DateTime.UtcNow.AddMinutes(5)));
    }

    [Fact]
    public void MoveToDlq_TransitionsToDeadLettered()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");
        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "error");

        delivery.MoveToDlq();

        Assert.Equal(DeliveryStatus.DeadLettered, delivery.Status);
        Assert.Null(delivery.NextRetryAtUtc);
    }

    [Fact]
    public void MoveToDlq_WhenNotFailed_Throws()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");

        Assert.Throws<InvalidOperationException>(() => delivery.MoveToDlq());
    }

    [Fact]
    public void ResetForReplay_TransitionsDeadLetteredToPending()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");
        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "error");
        delivery.MoveToDlq();

        delivery.ResetForReplay();

        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Null(delivery.NextRetryAtUtc);
    }

    [Fact]
    public void ResetForReplay_WhenNotDeadLettered_Throws()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");

        Assert.Throws<InvalidOperationException>(() => delivery.ResetForReplay());
    }

    [Fact]
    public void FullLifecycle_FailRetryFailMoveToDlqReplay()
    {
        var delivery = WebhookDelivery.Create(Guid.NewGuid(), "payment.completed", "{\"data\":1}");

        // First attempt fails
        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "error");
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);

        // Schedule and execute retry
        delivery.ScheduleRetry(DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);

        // Second attempt fails
        delivery.RecordFailedAttempt(503, TimeSpan.FromMilliseconds(200), "service unavailable");
        Assert.Equal(2, delivery.AttemptCount);

        // Move to DLQ
        delivery.MoveToDlq();
        Assert.Equal(DeliveryStatus.DeadLettered, delivery.Status);

        // Replay
        delivery.ResetForReplay();
        Assert.Equal(DeliveryStatus.Pending, delivery.Status);

        // Final successful delivery
        delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(50));
        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
    }
}
