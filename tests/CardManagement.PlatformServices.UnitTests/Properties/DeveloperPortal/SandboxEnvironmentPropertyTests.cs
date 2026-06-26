using System.Text.Json;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.DeveloperPortal;

/// <summary>
/// Property-based tests for Sandbox Key Isolation (Property 36).
///
/// **Validates: Requirements 18.1**
///
/// Sandbox keys accepted only by sandbox, rejected by production;
/// production keys rejected by sandbox.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "36")]
public class SandboxKeyIsolationPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 18.1**
    ///
    /// Property 36: Sandbox Key Isolation — Keys are valid only when
    /// key type matches environment type. Mismatched combinations are always rejected.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property KeyIsAccepted_OnlyWhenKeyTypeMatchesEnvironment()
    {
        return Prop.ForAll(
            Arb.Default.Bool(),
            Arb.Default.Bool(),
            (keyIsSandbox, environmentIsSandbox) =>
            {
                var result = SandboxEnvironment.ValidateKeyIsolation(keyIsSandbox, environmentIsSandbox);

                var shouldBeAccepted = keyIsSandbox == environmentIsSandbox;

                return (result.IsAccepted == shouldBeAccepted)
                    .Label($"keyIsSandbox={keyIsSandbox}, envIsSandbox={environmentIsSandbox}: " +
                           $"expected accepted={shouldBeAccepted}, got={result.IsAccepted}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.1**
    ///
    /// Property 36: Sandbox keys rejected by production environment.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SandboxKey_AlwaysRejected_InProductionEnvironment()
    {
        // keyIsSandbox=true, environmentIsSandbox=false → always rejected
        return Prop.ForAll(
            Gen.Constant(true).ToArbitrary(),  // key is sandbox
            Gen.Constant(false).ToArbitrary(), // environment is production
            (bool keyIsSandbox, bool environmentIsSandbox) =>
            {
                var result = SandboxEnvironment.ValidateKeyIsolation(keyIsSandbox, environmentIsSandbox);

                return (!result.IsAccepted && result.RejectionReason != null)
                    .Label($"Sandbox key in production should be rejected. " +
                           $"Accepted={result.IsAccepted}, Reason={result.RejectionReason}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.1**
    ///
    /// Property 36: Production keys rejected by sandbox environment.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ProductionKey_AlwaysRejected_InSandboxEnvironment()
    {
        // keyIsSandbox=false, environmentIsSandbox=true → always rejected
        return Prop.ForAll(
            Gen.Constant(false).ToArbitrary(), // key is production
            Gen.Constant(true).ToArbitrary(),  // environment is sandbox
            (bool keyIsSandbox, bool environmentIsSandbox) =>
            {
                var result = SandboxEnvironment.ValidateKeyIsolation(keyIsSandbox, environmentIsSandbox);

                return (!result.IsAccepted && result.RejectionReason != null)
                    .Label($"Production key in sandbox should be rejected. " +
                           $"Accepted={result.IsAccepted}, Reason={result.RejectionReason}");
            });
    }
}

/// <summary>
/// Property-based tests for Sandbox Validation Equivalence (Property 37).
///
/// **Validates: Requirements 18.2**
///
/// Invalid inputs produce same validation errors and response format as production.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "37")]
public class SandboxValidationEquivalencePropertyTests
{
    private readonly SandboxEnvironment _sandbox;

    public SandboxValidationEquivalencePropertyTests()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<SandboxEnvironment>();
        var webhookEngine = new NoOpWebhookDeliveryEngine();
        var subscriptionRepo = new NoOpWebhookSubscriptionRepository();
        _sandbox = new SandboxEnvironment(webhookEngine, subscriptionRepo, logger);
    }

    /// <summary>
    /// **Validates: Requirements 18.2**
    ///
    /// Property 37: Empty card number always produces 422 with error response structure.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EmptyCardNumber_ProducesValidationError_WithErrorStructure()
    {
        var amountGen = Gen.Choose(1, 100000);
        var currencyGen = Gen.Elements("NGN", "USD", "GBP", "EUR");

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            amountGen.ToArbitrary(),
            currencyGen.ToArbitrary(),
            (developerId, amount, currency) =>
            {
                var body = JsonSerializer.Serialize(new
                {
                    cardNumber = "",
                    amount,
                    currency
                });
                var request = new SandboxRequest(developerId, "/api/v1/payments", "POST", body, null);
                var response = _sandbox.ProcessRequestAsync(request, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var is4xx = response.StatusCode >= 400 && response.StatusCode < 500;
                var hasErrorStructure = HasValidErrorResponseStructure(response.ResponseBody);

                return (is4xx && hasErrorStructure)
                    .Label($"Empty card: status={response.StatusCode}, hasErrorStructure={hasErrorStructure}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.2**
    ///
    /// Property 37: Zero or negative amount always produces 422 with error response structure.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ZeroOrNegativeAmount_ProducesValidationError_WithErrorStructure()
    {
        var invalidAmountGen = Gen.OneOf(
            Gen.Constant(0L),
            Gen.Choose(-100000, -1).Select(x => (long)x));

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            invalidAmountGen.ToArbitrary(),
            (developerId, amount) =>
            {
                var body = JsonSerializer.Serialize(new
                {
                    cardNumber = "4000000000000000",
                    amount,
                    currency = "NGN"
                });
                var request = new SandboxRequest(developerId, "/api/v1/payments", "POST", body, null);
                var response = _sandbox.ProcessRequestAsync(request, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var is4xx = response.StatusCode >= 400 && response.StatusCode < 500;
                var hasErrorStructure = HasValidErrorResponseStructure(response.ResponseBody);

                return (is4xx && hasErrorStructure)
                    .Label($"Invalid amount={amount}: status={response.StatusCode}, " +
                           $"hasErrorStructure={hasErrorStructure}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.2**
    ///
    /// Property 37: Empty currency always produces 422 with error response structure.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EmptyCurrency_ProducesValidationError_WithErrorStructure()
    {
        var amountGen = Gen.Choose(1, 100000);
        var cardGen = Gen.Elements(
            "4000000000000000", "5500000000000001", "4111111111111111");

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            amountGen.ToArbitrary(),
            cardGen.ToArbitrary(),
            (developerId, amount, card) =>
            {
                var body = JsonSerializer.Serialize(new
                {
                    cardNumber = card,
                    amount,
                    currency = ""
                });
                var request = new SandboxRequest(developerId, "/api/v1/payments", "POST", body, null);
                var response = _sandbox.ProcessRequestAsync(request, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var is4xx = response.StatusCode >= 400 && response.StatusCode < 500;
                var hasErrorStructure = HasValidErrorResponseStructure(response.ResponseBody);

                return (is4xx && hasErrorStructure)
                    .Label($"Empty currency: status={response.StatusCode}, " +
                           $"hasErrorStructure={hasErrorStructure}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.2**
    ///
    /// Property 37: Invalid JSON body always produces 400 with error response structure.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InvalidJson_ProducesValidationError_WithErrorStructure()
    {
        var invalidJsonGen = Gen.Elements(
            "{invalid}", "not json at all", "{\"key\":}", "[broken",
            "{{nested}}", "<xml/>", "null}", "{amount: NaN}");

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            invalidJsonGen.ToArbitrary(),
            (developerId, invalidJson) =>
            {
                var request = new SandboxRequest(
                    developerId, "/api/v1/payments", "POST", invalidJson, null);
                var response = _sandbox.ProcessRequestAsync(request, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var is4xx = response.StatusCode >= 400 && response.StatusCode < 500;
                var hasErrorStructure = HasValidErrorResponseStructure(response.ResponseBody);

                return (is4xx && hasErrorStructure)
                    .Label($"Invalid JSON '{invalidJson}': status={response.StatusCode}, " +
                           $"hasErrorStructure={hasErrorStructure}");
            });
    }

    private static bool HasValidErrorResponseStructure(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return false;

        try
        {
            var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            // Error response should have an "error" property with "code" and "message"
            if (!root.TryGetProperty("error", out var errorElement))
                return false;

            var hasCode = errorElement.TryGetProperty("code", out var code)
                          && code.ValueKind == JsonValueKind.Number;
            var hasMessage = errorElement.TryGetProperty("message", out var msg)
                            && msg.ValueKind == JsonValueKind.String
                            && msg.GetString()!.Length > 0;

            return hasCode && hasMessage;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Property-based tests for Sandbox Data Reset Idempotency (Property 38).
///
/// **Validates: Requirements 18.5**
///
/// After reset, state matches known baseline with predefined test data
/// and no residual data from previous developer interactions.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "38")]
public class SandboxDataResetIdempotencyPropertyTests
{
    private readonly SandboxEnvironment _sandbox;

    public SandboxDataResetIdempotencyPropertyTests()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<SandboxEnvironment>();
        var webhookEngine = new NoOpWebhookDeliveryEngine();
        var subscriptionRepo = new NoOpWebhookSubscriptionRepository();
        _sandbox = new SandboxEnvironment(webhookEngine, subscriptionRepo, logger);
    }

    /// <summary>
    /// **Validates: Requirements 18.5**
    ///
    /// Property 38: After reset, state always has exactly 2 baseline transactions
    /// regardless of how many transactions were created before.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property AfterReset_StateMatchesBaseline_RegardlessOfPriorActivity()
    {
        var transactionCountGen = Gen.Choose(1, 20);
        var amountGen = Gen.Choose(100, 50000);

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            transactionCountGen.ToArbitrary(),
            amountGen.ToArbitrary(),
            (developerId, txnCount, amount) =>
            {
                // Act: Create N transactions before reset
                for (int i = 0; i < txnCount; i++)
                {
                    var body = JsonSerializer.Serialize(new
                    {
                        cardNumber = "4000000000000000",
                        amount = (long)(amount + i),
                        currency = "NGN"
                    });
                    var req = new SandboxRequest(
                        developerId, "/api/v1/payments", "POST", body, null);
                    _sandbox.ProcessRequestAsync(req, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }

                // Reset
                _sandbox.ResetDataAsync(developerId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Query after reset
                var queryReq = new SandboxRequest(
                    developerId, "/api/v1/transactions", "GET", null, null);
                var response = _sandbox.ProcessRequestAsync(queryReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var doc = JsonDocument.Parse(response.ResponseBody!);
                var total = doc.RootElement.GetProperty("total").GetInt32();
                var data = doc.RootElement.GetProperty("data");

                // Baseline always has exactly 2 predefined transactions
                var hasExactlyTwoTransactions = total == 2;
                var dataMatchesTotal = data.GetArrayLength() == 2;

                return (hasExactlyTwoTransactions && dataMatchesTotal)
                    .Label($"After reset (had {txnCount + 2} txns): total={total}, " +
                           $"dataLength={data.GetArrayLength()}, expected=2");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.5**
    ///
    /// Property 38: Multiple consecutive resets produce same baseline state (idempotency).
    /// </summary>
    [Property(MaxTest = 50)]
    public Property MultipleResets_ProduceSameBaselineState()
    {
        var resetCountGen = Gen.Choose(2, 5);

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            resetCountGen.ToArbitrary(),
            (developerId, resetCount) =>
            {
                // Create some activity
                var body = JsonSerializer.Serialize(new
                {
                    cardNumber = "4000000000000000",
                    amount = 5000L,
                    currency = "NGN"
                });
                var payReq = new SandboxRequest(
                    developerId, "/api/v1/payments", "POST", body, null);
                _sandbox.ProcessRequestAsync(payReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Perform multiple resets
                for (int i = 0; i < resetCount; i++)
                {
                    _sandbox.ResetDataAsync(developerId, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }

                // Query state
                var queryReq = new SandboxRequest(
                    developerId, "/api/v1/transactions", "GET", null, null);
                var response = _sandbox.ProcessRequestAsync(queryReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var doc = JsonDocument.Parse(response.ResponseBody!);
                var total = doc.RootElement.GetProperty("total").GetInt32();

                // Should always be 2 (baseline) regardless of reset count
                return (total == 2)
                    .Label($"After {resetCount} resets: total={total}, expected=2");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.5**
    ///
    /// Property 38: No residual data from pre-reset activity remains after reset.
    /// Verifies that specific pre-reset transaction IDs are no longer accessible.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property AfterReset_NoResidualDataFromPriorActivity()
    {
        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            (developerId) =>
            {
                // Create a payment and capture its transaction ID
                var body = JsonSerializer.Serialize(new
                {
                    cardNumber = "4000000000000000",
                    amount = 7777L,
                    currency = "NGN"
                });
                var payReq = new SandboxRequest(
                    developerId, "/api/v1/payments", "POST", body, null);
                var payResponse = _sandbox.ProcessRequestAsync(payReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var payDoc = JsonDocument.Parse(payResponse.ResponseBody!);
                var txnId = payDoc.RootElement.GetProperty("transactionId").GetGuid();

                // Reset
                _sandbox.ResetDataAsync(developerId, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Try to refund the pre-reset transaction — should not be found
                var refundBody = JsonSerializer.Serialize(new
                {
                    transactionId = txnId,
                    amount = 7777L
                });
                var refundReq = new SandboxRequest(
                    developerId, "/api/v1/refunds", "POST", refundBody, null);
                var refundResponse = _sandbox.ProcessRequestAsync(refundReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Transaction from before reset should not exist (404)
                return (refundResponse.StatusCode == 404)
                    .Label($"Refund of pre-reset txn {txnId}: " +
                           $"status={refundResponse.StatusCode}, expected=404");
            });
    }
}

/// <summary>
/// Property-based tests for Sandbox Multi-Tenant Data Isolation (Property 39).
///
/// **Validates: Requirements 18.6**
///
/// One developer's test data not visible or accessible by another developer.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "39")]
public class SandboxMultiTenantDataIsolationPropertyTests
{
    private readonly SandboxEnvironment _sandbox;

    public SandboxMultiTenantDataIsolationPropertyTests()
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<SandboxEnvironment>();
        var webhookEngine = new NoOpWebhookDeliveryEngine();
        var subscriptionRepo = new NoOpWebhookSubscriptionRepository();
        _sandbox = new SandboxEnvironment(webhookEngine, subscriptionRepo, logger);
    }

    /// <summary>
    /// **Validates: Requirements 18.6**
    ///
    /// Property 39: Developer A's transactions are never visible to Developer B.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property DeveloperA_TransactionsNotVisibleTo_DeveloperB()
    {
        var txnCountGen = Gen.Choose(1, 10);

        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            txnCountGen.ToArbitrary(),
            (developerA, developerB, txnCount) =>
            {
                // Ensure they are distinct
                if (developerA == developerB)
                    return true.Label("Skipped: same developer IDs");

                // Developer A creates multiple transactions
                for (int i = 0; i < txnCount; i++)
                {
                    var body = JsonSerializer.Serialize(new
                    {
                        cardNumber = "4000000000000000",
                        amount = (long)(1000 + i),
                        currency = "NGN"
                    });
                    var req = new SandboxRequest(
                        developerA, "/api/v1/payments", "POST", body, null);
                    _sandbox.ProcessRequestAsync(req, CancellationToken.None)
                        .GetAwaiter().GetResult();
                }

                // Developer B queries transactions
                var queryReq = new SandboxRequest(
                    developerB, "/api/v1/transactions", "GET", null, null);
                var response = _sandbox.ProcessRequestAsync(queryReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var doc = JsonDocument.Parse(response.ResponseBody!);
                var total = doc.RootElement.GetProperty("total").GetInt32();

                // Developer B should only see baseline (2), not A's transactions
                return (total == 2)
                    .Label($"DevB sees {total} txns (expected 2 baseline). " +
                           $"DevA created {txnCount} extra. Isolation violated if > 2.");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.6**
    ///
    /// Property 39: Developer B cannot refund Developer A's transactions.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property DeveloperB_CannotRefund_DeveloperA_Transactions()
    {
        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            (developerA, developerB) =>
            {
                if (developerA == developerB)
                    return true.Label("Skipped: same developer IDs");

                // Developer A creates a successful payment
                var payBody = JsonSerializer.Serialize(new
                {
                    cardNumber = "4000000000000000",
                    amount = 5000L,
                    currency = "NGN"
                });
                var payReq = new SandboxRequest(
                    developerA, "/api/v1/payments", "POST", payBody, null);
                var payResponse = _sandbox.ProcessRequestAsync(payReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var payDoc = JsonDocument.Parse(payResponse.ResponseBody!);
                var txnId = payDoc.RootElement.GetProperty("transactionId").GetGuid();

                // Developer B tries to refund Developer A's transaction
                var refundBody = JsonSerializer.Serialize(new
                {
                    transactionId = txnId,
                    amount = 5000L
                });
                var refundReq = new SandboxRequest(
                    developerB, "/api/v1/refunds", "POST", refundBody, null);
                var refundResponse = _sandbox.ProcessRequestAsync(refundReq, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Should be 404 — transaction not found in Developer B's data
                return (refundResponse.StatusCode == 404)
                    .Label($"DevB refund of DevA's txn: status={refundResponse.StatusCode}, " +
                           $"expected=404 (not found)");
            });
    }

    /// <summary>
    /// **Validates: Requirements 18.6**
    ///
    /// Property 39: Resetting one developer's data does not affect another's.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ResetDeveloperA_DoesNotAffect_DeveloperB_Data()
    {
        return Prop.ForAll(
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            Arb.Generate<Guid>().Where(g => g != Guid.Empty).ToArbitrary(),
            (developerA, developerB) =>
            {
                if (developerA == developerB)
                    return true.Label("Skipped: same developer IDs");

                // Both developers create transactions
                var body = JsonSerializer.Serialize(new
                {
                    cardNumber = "4000000000000000",
                    amount = 3000L,
                    currency = "NGN"
                });
                var reqA = new SandboxRequest(
                    developerA, "/api/v1/payments", "POST", body, null);
                _sandbox.ProcessRequestAsync(reqA, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var reqB = new SandboxRequest(
                    developerB, "/api/v1/payments", "POST", body, null);
                _sandbox.ProcessRequestAsync(reqB, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Developer B should have 3 (2 baseline + 1 new)
                var queryBeforeReset = new SandboxRequest(
                    developerB, "/api/v1/transactions", "GET", null, null);
                var beforeResponse = _sandbox.ProcessRequestAsync(queryBeforeReset, CancellationToken.None)
                    .GetAwaiter().GetResult();
                var beforeDoc = JsonDocument.Parse(beforeResponse.ResponseBody!);
                var beforeTotal = beforeDoc.RootElement.GetProperty("total").GetInt32();

                // Reset Developer A
                _sandbox.ResetDataAsync(developerA, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Developer B's data should be unchanged
                var queryAfterReset = new SandboxRequest(
                    developerB, "/api/v1/transactions", "GET", null, null);
                var afterResponse = _sandbox.ProcessRequestAsync(queryAfterReset, CancellationToken.None)
                    .GetAwaiter().GetResult();
                var afterDoc = JsonDocument.Parse(afterResponse.ResponseBody!);
                var afterTotal = afterDoc.RootElement.GetProperty("total").GetInt32();

                return (beforeTotal == afterTotal && afterTotal == 3)
                    .Label($"DevB before reset={beforeTotal}, after DevA reset={afterTotal}, expected=3");
            });
    }
}

#region Test Infrastructure

/// <summary>
/// No-op webhook delivery engine for property tests that don't need webhook behavior.
/// </summary>
internal class NoOpWebhookDeliveryEngine : IWebhookDeliveryEngine
{
    public Task DeliverAsync(WebhookDelivery delivery, CancellationToken ct) => Task.CompletedTask;
    public Task RetryAsync(Guid deliveryId, CancellationToken ct) => Task.CompletedTask;
    public Task ReplayFromDlqAsync(Guid dlqItemId, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// No-op webhook subscription repository for property tests that don't need subscriptions.
/// </summary>
internal class NoOpWebhookSubscriptionRepository : IWebhookSubscriptionRepository
{
    public Task<WebhookSubscription> CreateAsync(WebhookSubscription subscription, CancellationToken ct)
        => Task.FromResult(subscription);

    public Task<WebhookSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken ct)
        => Task.FromResult<WebhookSubscription?>(null);

    public Task<IReadOnlyList<WebhookSubscription>> GetActiveByEventTypeAsync(string eventType, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<WebhookSubscription>>(new List<WebhookSubscription>());

    public Task<int> CountActiveByMerchantAsync(Guid merchantId, CancellationToken ct)
        => Task.FromResult(0);

    public Task<IReadOnlyList<WebhookSubscription>> GetByMerchantAsync(Guid merchantId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<WebhookSubscription>>(new List<WebhookSubscription>());

    public Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct)
        => Task.CompletedTask;
}

#endregion
