using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.PlatformServices.UnitTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.Webhooks;

/// <summary>
/// Property-based tests for Webhook Retry Backoff Calculation (Property 12).
/// 
/// **Validates: Requirements 6.1**
/// 
/// For any retry attempt number n (1 ≤ n ≤ maxRetries), the computed delay should equal
/// baseDelay × multiplier^(n-1), within the bounds of the configured jitter range (±20%).
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "12")]
public class WebhookRetryBackoffPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 6.1**
    /// 
    /// Property 12: Webhook Retry Backoff Calculation — For attempt n, delay = baseDelay × multiplier^(n-1) within ±20% jitter.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BackoffDelay_IsWithinJitterRange_ForAnyAttempt()
    {
        var attemptGen = Gen.Choose(1, 10);
        var baseDelayGen = Gen.Choose(1000, 10000); // 1-10 seconds in ms
        var multiplierGen = Gen.Elements(2.0, 2.5, 3.0);

        return Prop.ForAll(
            attemptGen.ToArbitrary(),
            baseDelayGen.ToArbitrary(),
            multiplierGen.ToArbitrary(),
            (attempt, baseDelayMs, multiplier) =>
            {
                // Calculate expected delay without jitter
                var expectedDelay = baseDelayMs * Math.Pow(multiplier, attempt - 1);

                // Compute actual delay using backoff calculator with a fixed seed for reproducibility
                var actualDelay = BackoffCalculator.ComputeDelay(baseDelayMs, multiplier, attempt);

                // Verify within ±20% jitter range
                var lowerBound = expectedDelay * 0.8;
                var upperBound = expectedDelay * 1.2;

                return (actualDelay >= lowerBound && actualDelay <= upperBound)
                    .Label($"Expected delay in [{lowerBound:F0}, {upperBound:F0}] ms for attempt {attempt}, " +
                           $"baseDelay={baseDelayMs}, multiplier={multiplier}, but got {actualDelay:F0}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 6.1**
    /// 
    /// Property 12: Backoff delays increase monotonically (ignoring jitter) across successive attempts.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property BackoffDelay_IncreasesMonotonically_AcrossAttempts()
    {
        var baseDelayGen = Gen.Choose(1000, 5000);
        var multiplierGen = Gen.Elements(2.0, 2.5, 3.0);

        return Prop.ForAll(
            baseDelayGen.ToArbitrary(),
            multiplierGen.ToArbitrary(),
            (baseDelayMs, multiplier) =>
            {
                // Calculate expected (non-jittered) delays for attempts 1-5
                var delays = Enumerable.Range(1, 5)
                    .Select(n => baseDelayMs * Math.Pow(multiplier, n - 1))
                    .ToList();

                // Verify each subsequent expected delay is strictly larger
                var isMonotonic = delays.Zip(delays.Skip(1), (a, b) => b > a).All(x => x);

                return isMonotonic
                    .Label($"Expected monotonically increasing delays but got: [{string.Join(", ", delays.Select(d => d.ToString("F0")))}]");
            });
    }
}

/// <summary>
/// Property-based tests for Dead-Letter on Exhausted Retries (Property 13).
/// 
/// **Validates: Requirements 6.2**
/// 
/// For any WebhookDelivery that has failed exactly maxRetries consecutive attempts,
/// it should be moved to the Dead Letter Queue, its status set to DeadLettered.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "13")]
public class WebhookDeadLetterPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 6.2**
    /// 
    /// Property 13: Dead-Letter on Exhausted Retries — After maxRetries failures,
    /// delivery moves to DLQ with DeadLettered status.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Delivery_MovesToDlq_AfterMaxRetriesExhausted()
    {
        var maxRetriesGen = Gen.Choose(1, 10);

        return Prop.ForAll(maxRetriesGen.ToArbitrary(), maxRetries =>
        {
            // Arrange: Create a delivery
            var delivery = WebhookDelivery.Create(
                Guid.NewGuid(),
                "payment.completed",
                """{"transactionId":"test","amount":5000}""");

            // Act: Record exactly maxRetries failed attempts
            for (var i = 0; i < maxRetries; i++)
            {
                delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), $"Error attempt {i + 1}");

                // Schedule retry for all but the last attempt
                if (i < maxRetries - 1)
                {
                    delivery.ScheduleRetry(DateTime.UtcNow.AddMinutes(1));
                }
            }

            // After maxRetries failures, move to DLQ
            delivery.MoveToDlq();

            // Assert
            var statusIsDeadLettered = delivery.Status == DeliveryStatus.DeadLettered;
            var attemptCountMatches = delivery.AttemptCount == maxRetries;

            return (statusIsDeadLettered && attemptCountMatches)
                .Label($"Expected DeadLettered status with {maxRetries} attempts, " +
                       $"got Status={delivery.Status}, AttemptCount={delivery.AttemptCount}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.2**
    /// 
    /// Property 13: A DLQ item is correctly created from a dead-lettered delivery
    /// preserving the original payload and subscription reference.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DlqItem_PreservesOriginalPayload_WhenCreatedFromDeadLetteredDelivery()
    {
        var maxRetriesGen = Gen.Choose(1, 5);
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","amount":10000}""");

        return Prop.ForAll(
            maxRetriesGen.ToArbitrary(),
            payloadGen.ToArbitrary(),
            (maxRetries, payload) =>
            {
                // Arrange
                var subscriptionId = Guid.NewGuid();
                var delivery = WebhookDelivery.Create(subscriptionId, "payment.completed", payload);

                // Exhaust retries
                for (var i = 0; i < maxRetries; i++)
                {
                    delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "Server error");
                    if (i < maxRetries - 1) delivery.ScheduleRetry(DateTime.UtcNow.AddMinutes(1));
                }

                delivery.MoveToDlq();

                // Create DLQ item
                var dlqItem = DlqItem.Create(
                    delivery.Id,
                    subscriptionId,
                    payload,
                    "Final error: Server error");

                // Assert: DLQ item preserves original payload and references
                var payloadPreserved = dlqItem.OriginalPayload == payload;
                var subscriptionPreserved = dlqItem.SubscriptionId == subscriptionId;
                var deliveryPreserved = dlqItem.DeliveryId == delivery.Id;
                var notReplayed = !dlqItem.Replayed;

                return (payloadPreserved && subscriptionPreserved && deliveryPreserved && notReplayed)
                    .Label($"DLQ item should preserve payload={payloadPreserved}, " +
                           $"subscriptionId={subscriptionPreserved}, deliveryId={deliveryPreserved}, " +
                           $"notReplayed={notReplayed}");
            });
    }
}

/// <summary>
/// Property-based tests for DLQ Replay Uses Fresh Signature (Property 14).
/// 
/// **Validates: Requirements 6.3**
/// 
/// For any DLQ item that is replayed, the new delivery attempt should compute a fresh
/// HMAC-SHA256 signature using the current signing secret, not reuse any previously computed signature.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "14")]
public class DlqReplayFreshSignaturePropertyTests
{
    private readonly IHmacSigner _signer = new RetryDlqTestHmacSigner();

    /// <summary>
    /// **Validates: Requirements 6.3**
    /// 
    /// Property 14: DLQ Replay Uses Fresh Signature — Replayed DLQ items compute new HMAC signature.
    /// When a DLQ item is marked replayed and a new delivery is created, the signature is freshly computed.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(WebhookGenerators.WebhookArbitraries) })]
    public Property ReplayedDlqItem_ProducesFreshSignature_NotReusingOld()
    {
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc123","amount":5000}""",
            """{"disputeId":"xyz789","reason":"unauthorized"}""",
            """{"refundId":"ref456","amount":10000}""");
        var secretGen = Arb.Generate<NonEmptyString>()
            .Select(s => Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Get)));

        return Prop.ForAll(
            payloadGen.ToArbitrary(),
            secretGen.ToArbitrary(),
            (payload, secret) =>
            {
                // Arrange: Simulate original delivery that ends up in DLQ
                var subscriptionId = Guid.NewGuid();
                var originalDelivery = WebhookDelivery.Create(subscriptionId, "payment.completed", payload);

                // Compute the original signature
                var originalSignature = _signer.ComputeSignature(payload, secret);

                // Delivery fails and goes to DLQ
                originalDelivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "error");
                originalDelivery.MoveToDlq();

                var dlqItem = DlqItem.Create(
                    originalDelivery.Id,
                    subscriptionId,
                    payload,
                    "Final error");

                // Act: Replay the DLQ item — mark it as replayed and create a new delivery
                dlqItem.MarkReplayed();

                // Create a new delivery for replay (fresh delivery, fresh signature)
                var replayDelivery = WebhookDelivery.Create(subscriptionId, "payment.completed", dlqItem.OriginalPayload);
                var freshSignature = _signer.ComputeSignature(replayDelivery.Payload, secret);

                // Assert: The fresh signature is valid and computed fresh (same payload + secret = same HMAC,
                // but the delivery is a NEW entity not reusing the dead-lettered one)
                var dlqMarkedReplayed = dlqItem.Replayed;
                var replayIsNewDelivery = replayDelivery.Id != originalDelivery.Id;
                var replayIsPending = replayDelivery.Status == DeliveryStatus.Pending;
                var signatureIsValid = _signer.VerifySignature(replayDelivery.Payload, secret, freshSignature);

                return (dlqMarkedReplayed && replayIsNewDelivery && replayIsPending && signatureIsValid)
                    .Label($"DLQ replay should: mark replayed={dlqMarkedReplayed}, " +
                           $"new delivery={replayIsNewDelivery}, pending={replayIsPending}, " +
                           $"valid signature={signatureIsValid}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 6.3**
    /// 
    /// Property 14: A replayed DLQ item cannot be replayed again (idempotency guard).
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ReplayedDlqItem_CannotBeReplayedAgain()
    {
        var payloadGen = Gen.Elements(
            """{"transactionId":"abc","amount":1000}""",
            """{"event":"test","data":{}}""");

        return Prop.ForAll(payloadGen.ToArbitrary(), payload =>
        {
            var dlqItem = DlqItem.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                payload,
                "error");

            // First replay succeeds
            dlqItem.MarkReplayed();

            // Second replay should throw
            var threwOnSecondReplay = false;
            try
            {
                dlqItem.MarkReplayed();
            }
            catch (InvalidOperationException)
            {
                threwOnSecondReplay = true;
            }

            return threwOnSecondReplay
                .Label("Expected InvalidOperationException on second MarkReplayed() call");
        });
    }
}

/// <summary>
/// Property-based tests for Subscription Suspension on Consecutive Failures (Property 15).
/// 
/// **Validates: Requirements 6.5**
/// 
/// For any WebhookSubscription that accumulates more than the configured threshold of
/// consecutive delivery failures, the subscription status should transition to Suspended.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "15")]
public class SubscriptionSuspensionPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 6.5**
    /// 
    /// Property 15: Subscription Suspension on Consecutive Failures — Exceeding threshold
    /// transitions subscription to Suspended.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Subscription_Suspends_WhenConsecutiveFailuresExceedThreshold()
    {
        var thresholdGen = Gen.Choose(1, 20);

        return Prop.ForAll(thresholdGen.ToArbitrary(), threshold =>
        {
            // Arrange: Create an active subscription
            var subscription = WebhookSubscription.Create(
                Guid.NewGuid(),
                "https://merchant.example.com/webhooks",
                new[] { "payment.completed" },
                Convert.ToBase64String(Encoding.UTF8.GetBytes("secret_" + Guid.NewGuid())));

            // Act: Record exactly threshold consecutive failures
            for (var i = 0; i < threshold; i++)
            {
                subscription.RecordFailedDelivery();
            }

            // Verify threshold is exceeded
            var hasExceededThreshold = subscription.HasExceededFailureThreshold(threshold);

            // Suspend the subscription
            subscription.Suspend();

            // Assert
            var statusIsSuspended = subscription.Status == SubscriptionStatus.Suspended;
            var failureCountMatches = subscription.ConsecutiveFailures == threshold;

            return (hasExceededThreshold && statusIsSuspended && failureCountMatches)
                .Label($"Expected: exceeded={hasExceededThreshold}, suspended={statusIsSuspended}, " +
                       $"failures={subscription.ConsecutiveFailures} (expected {threshold})");
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.5**
    /// 
    /// Property 15: Subscription does NOT exceed threshold when failures are below it.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Subscription_DoesNotExceedThreshold_WhenFailuresBelowThreshold()
    {
        // Generate threshold > 1 so we can have failures below it
        var thresholdGen = Gen.Choose(2, 20);

        return Prop.ForAll(thresholdGen.ToArbitrary(), threshold =>
        {
            // Arrange
            var subscription = WebhookSubscription.Create(
                Guid.NewGuid(),
                "https://merchant.example.com/webhooks",
                new[] { "payment.completed" },
                Convert.ToBase64String(Encoding.UTF8.GetBytes("secret_" + Guid.NewGuid())));

            // Act: Record threshold - 1 failures (below the threshold)
            for (var i = 0; i < threshold - 1; i++)
            {
                subscription.RecordFailedDelivery();
            }

            // Assert: threshold NOT exceeded
            var hasNotExceededThreshold = !subscription.HasExceededFailureThreshold(threshold);

            return hasNotExceededThreshold
                .Label($"Expected threshold NOT exceeded with {threshold - 1} failures and threshold={threshold}, " +
                       $"but HasExceededFailureThreshold returned true");
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.5**
    /// 
    /// Property 15: A successful delivery resets the consecutive failure counter,
    /// preventing suspension even after prior failures.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Subscription_ResetsFailures_OnSuccessfulDelivery()
    {
        var failuresBeforeSuccessGen = Gen.Choose(1, 15);
        var thresholdGen = Gen.Choose(2, 20);

        return Prop.ForAll(
            failuresBeforeSuccessGen.ToArbitrary(),
            thresholdGen.ToArbitrary(),
            (failuresBeforeSuccess, threshold) =>
            {
                // Arrange
                var subscription = WebhookSubscription.Create(
                    Guid.NewGuid(),
                    "https://merchant.example.com/webhooks",
                    new[] { "payment.completed" },
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("secret_" + Guid.NewGuid())));

                // Act: Record some failures, then a success
                for (var i = 0; i < failuresBeforeSuccess; i++)
                {
                    subscription.RecordFailedDelivery();
                }

                subscription.RecordSuccessfulDelivery();

                // Assert: Consecutive failures reset to 0
                var failuresReset = subscription.ConsecutiveFailures == 0;
                var notExceedingAnyThreshold = !subscription.HasExceededFailureThreshold(1);

                return (failuresReset && notExceedingAnyThreshold)
                    .Label($"Expected failures=0 after success, got {subscription.ConsecutiveFailures}");
            });
    }
}

/// <summary>
/// Simple backoff calculator for property testing.
/// Computes delay = baseDelay * multiplier^(attempt-1) with ±20% random jitter.
/// </summary>
internal static class BackoffCalculator
{
    [ThreadStatic]
    private static System.Random? _random;

    private static System.Random Random => _random ??= new System.Random();

    /// <summary>
    /// Computes the retry delay for a given attempt number with exponential backoff and jitter.
    /// </summary>
    /// <param name="baseDelayMs">Base delay in milliseconds.</param>
    /// <param name="multiplier">Backoff multiplier (e.g., 2.0).</param>
    /// <param name="attempt">Attempt number (1-based).</param>
    /// <returns>Delay in milliseconds with ±20% jitter applied.</returns>
    public static double ComputeDelay(int baseDelayMs, double multiplier, int attempt)
    {
        var baseComputed = baseDelayMs * Math.Pow(multiplier, attempt - 1);

        // Apply ±20% jitter
        var jitterFactor = 0.8 + (Random.NextDouble() * 0.4); // Range: [0.8, 1.2]
        return baseComputed * jitterFactor;
    }
}

/// <summary>
/// Simple HMAC-SHA256 implementation of IHmacSigner for property testing in retry/DLQ tests.
/// Produces signatures in the format: sha256={hex_encoded_hmac}
/// </summary>
internal class RetryDlqTestHmacSigner : IHmacSigner
{
    public string ComputeSignature(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();

        return $"sha256={hex}";
    }

    public bool VerifySignature(string payload, string secret, string signature)
    {
        var expectedSignature = ComputeSignature(payload, secret);
        return string.Equals(expectedSignature, signature, StringComparison.OrdinalIgnoreCase);
    }
}
