using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.PlatformServices.UnitTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.Webhooks;

/// <summary>
/// Property-based tests for Webhook Delivery Payload Completeness (Property 11).
/// 
/// **Validates: Requirements 5.1, 5.2**
/// 
/// For any qualifying event, the constructed webhook delivery payload should contain
/// the event type, event data, a UTC timestamp, and a unique delivery identifier (UUID).
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "11")]
public class WebhookDeliveryPayloadCompletenessPropertyTests
{
    private static readonly string[] EventTypes =
    {
        "payment.completed", "payment.failed", "payment.reversed",
        "dispute.created", "dispute.resolved", "refund.processed"
    };

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 11: Webhook Delivery Payload Completeness — delivery has non-empty ID.
    /// Any qualifying event produces a delivery with a non-empty unique identifier.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property WebhookDelivery_Create_AlwaysHasNonEmptyId()
    {
        var subscriptionIdGen = Arb.Generate<Guid>().Where(id => id != Guid.Empty);
        var eventTypeGen = Gen.Elements(EventTypes);
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000,"currency":"NGN"}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","originalAmount":10000}""");

        return Prop.ForAll(
            subscriptionIdGen.ToArbitrary(),
            eventTypeGen.ToArbitrary(),
            payloadGen.ToArbitrary(),
            (subscriptionId, eventType, payload) =>
            {
                var delivery = WebhookDelivery.Create(subscriptionId, eventType, payload);

                return (delivery.Id != Guid.Empty)
                    .Label($"Expected non-empty delivery ID but got {delivery.Id}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 11: Webhook Delivery Payload Completeness — delivery has correct subscription ID.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property WebhookDelivery_Create_HasCorrectSubscriptionId()
    {
        var subscriptionIdGen = Arb.Generate<Guid>().Where(id => id != Guid.Empty);
        var eventTypeGen = Gen.Elements(EventTypes);
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000,"currency":"NGN"}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","originalAmount":10000}""");

        return Prop.ForAll(
            subscriptionIdGen.ToArbitrary(),
            eventTypeGen.ToArbitrary(),
            payloadGen.ToArbitrary(),
            (subscriptionId, eventType, payload) =>
            {
                var delivery = WebhookDelivery.Create(subscriptionId, eventType, payload);

                return (delivery.SubscriptionId == subscriptionId)
                    .Label($"Expected SubscriptionId={subscriptionId} but got {delivery.SubscriptionId}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 11: Webhook Delivery Payload Completeness — delivery has event type.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property WebhookDelivery_Create_HasEventType()
    {
        var subscriptionIdGen = Arb.Generate<Guid>().Where(id => id != Guid.Empty);
        var eventTypeGen = Gen.Elements(EventTypes);
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000,"currency":"NGN"}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","originalAmount":10000}""");

        return Prop.ForAll(
            subscriptionIdGen.ToArbitrary(),
            eventTypeGen.ToArbitrary(),
            payloadGen.ToArbitrary(),
            (subscriptionId, eventType, payload) =>
            {
                var delivery = WebhookDelivery.Create(subscriptionId, eventType, payload);

                return (!string.IsNullOrWhiteSpace(delivery.EventType) && delivery.EventType == eventType)
                    .Label($"Expected EventType='{eventType}' but got '{delivery.EventType}'");
            });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 11: Webhook Delivery Payload Completeness — delivery has payload (event data).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property WebhookDelivery_Create_HasPayload()
    {
        var subscriptionIdGen = Arb.Generate<Guid>().Where(id => id != Guid.Empty);
        var eventTypeGen = Gen.Elements(EventTypes);
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000,"currency":"NGN"}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","originalAmount":10000}""");

        return Prop.ForAll(
            subscriptionIdGen.ToArbitrary(),
            eventTypeGen.ToArbitrary(),
            payloadGen.ToArbitrary(),
            (subscriptionId, eventType, payload) =>
            {
                var delivery = WebhookDelivery.Create(subscriptionId, eventType, payload);

                return (!string.IsNullOrWhiteSpace(delivery.Payload) && delivery.Payload == payload)
                    .Label($"Expected non-empty Payload matching input but got '{delivery.Payload}'");
            });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 11: Webhook Delivery Payload Completeness — delivery has UTC creation timestamp.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property WebhookDelivery_Create_HasUtcTimestamp()
    {
        var subscriptionIdGen = Arb.Generate<Guid>().Where(id => id != Guid.Empty);
        var eventTypeGen = Gen.Elements(EventTypes);
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000,"currency":"NGN"}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","originalAmount":10000}""");

        return Prop.ForAll(
            subscriptionIdGen.ToArbitrary(),
            eventTypeGen.ToArbitrary(),
            payloadGen.ToArbitrary(),
            (subscriptionId, eventType, payload) =>
            {
                var beforeCreate = DateTime.UtcNow;
                var delivery = WebhookDelivery.Create(subscriptionId, eventType, payload);
                var afterCreate = DateTime.UtcNow;

                var hasTimestamp = delivery.CreatedAtUtc != default;
                var isUtc = delivery.CreatedAtUtc.Kind == DateTimeKind.Utc;
                var isReasonable = delivery.CreatedAtUtc >= beforeCreate.AddSeconds(-1) &&
                                   delivery.CreatedAtUtc <= afterCreate.AddSeconds(1);

                return (hasTimestamp && isUtc && isReasonable)
                    .Label($"Expected valid UTC timestamp near now, got {delivery.CreatedAtUtc} (Kind={delivery.CreatedAtUtc.Kind})");
            });
    }

    /// <summary>
    /// **Validates: Requirements 5.1, 5.2**
    /// 
    /// Property 11: Webhook Delivery Payload Completeness — each delivery gets a unique UUID.
    /// Multiple deliveries for the same event should have distinct IDs.
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property WebhookDelivery_Create_ProducesUniqueIds()
    {
        var countGen = Gen.Choose(2, 20);
        var subscriptionIdGen = Arb.Generate<Guid>().Where(id => id != Guid.Empty);
        var eventTypeGen = Gen.Elements(EventTypes);

        return Prop.ForAll(
            countGen.ToArbitrary(),
            subscriptionIdGen.ToArbitrary(),
            eventTypeGen.ToArbitrary(),
            (count, subscriptionId, eventType) =>
            {
                var payload = """{"transactionId":"abc123","amount":5000,"currency":"NGN"}""";
                var ids = new HashSet<Guid>();
                for (int i = 0; i < count; i++)
                {
                    var delivery = WebhookDelivery.Create(subscriptionId, eventType, payload);
                    ids.Add(delivery.Id);
                }

                return (ids.Count == count)
                    .Label($"Expected {count} unique IDs but got {ids.Count} distinct values");
            });
    }
}
