using FsCheck;

namespace CardManagement.PlatformServices.UnitTests.Generators;

/// <summary>
/// FsCheck generators for Webhook domain entities.
/// </summary>
public static class WebhookGenerators
{
    private static readonly string[] EventTypes =
    {
        "payment.completed", "payment.failed", "payment.reversed",
        "dispute.created", "dispute.resolved", "refund.processed"
    };

    public static Arbitrary<WebhookSubscriptionData> WebhookSubscriptionArbitrary()
    {
        return (from id in Arb.Generate<Guid>()
                from merchantId in Arb.Generate<Guid>()
                from url in Gen.Elements(
                    "https://merchant-a.example.com/webhooks",
                    "https://merchant-b.example.com/hooks",
                    "https://api.partner.io/callback")
                from eventTypeCount in Gen.Choose(1, EventTypes.Length)
                from eventTypes in Gen.ArrayOf(eventTypeCount, Gen.Elements(EventTypes))
                    .Select(arr => arr.Distinct().ToArray())
                from secret in Gen.Elements("secret1", "secret2", "secret3")
                    .Select(s => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s + Guid.NewGuid().ToString())))
                select new WebhookSubscriptionData(
                    id,
                    merchantId,
                    url,
                    eventTypes,
                    secret))
            .ToArbitrary();
    }

    public static Arbitrary<WebhookPayloadData> WebhookPayloadArbitrary()
    {
        return (from deliveryId in Arb.Generate<Guid>()
                from eventType in Gen.Elements(EventTypes)
                from timestamp in Arb.Generate<DateTime>().Select(dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc))
                from payloadBody in Gen.Elements(
                    """{"transactionId":"abc123","amount":5000,"currency":"NGN"}""",
                    """{"disputeId":"xyz789","reason":"unauthorized"}""",
                    """{"refundId":"ref456","originalAmount":10000}""")
                select new WebhookPayloadData(
                    deliveryId,
                    eventType,
                    timestamp,
                    payloadBody))
            .ToArbitrary();
    }

    /// <summary>
    /// Registers all webhook-related arbitraries with FsCheck.
    /// </summary>
    public class WebhookArbitraries
    {
        public static Arbitrary<WebhookSubscriptionData> WebhookSubscriptions() => WebhookSubscriptionArbitrary();
        public static Arbitrary<WebhookPayloadData> WebhookPayloads() => WebhookPayloadArbitrary();
    }
}

/// <summary>
/// Data record for webhook subscription generation in property-based tests.
/// </summary>
public record WebhookSubscriptionData(
    Guid Id,
    Guid MerchantId,
    string DestinationUrl,
    string[] EventTypes,
    string SigningSecret);

/// <summary>
/// Data record for webhook payload generation in property-based tests.
/// </summary>
public record WebhookPayloadData(
    Guid DeliveryId,
    string EventType,
    DateTime Timestamp,
    string PayloadBody);
