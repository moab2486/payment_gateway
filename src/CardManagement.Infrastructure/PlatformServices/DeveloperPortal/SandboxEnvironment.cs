using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal;

/// <summary>
/// Provides an isolated sandbox environment with preconfigured test scenarios
/// that mirrors production API behavior without routing to real payment processors.
/// 
/// Test scenarios are triggered by specific input values:
/// - Card number "4000000000000000" → successful payment
/// - Card number "4000000000000002" → declined payment
/// - Card number "4000000000000010" → timeout
/// - Card number "4000000000000019" → insufficient funds
/// - Amount 99999 → declined (amount-triggered)
/// - Amount 88888 → timeout (amount-triggered)
/// 
/// Sandbox keys are rejected in production and production keys are rejected in sandbox.
/// Each developer's data is isolated and can be reset to a known baseline state.
/// </summary>
public class SandboxEnvironment : ISandboxEnvironment
{
    private readonly IWebhookDeliveryEngine _webhookDeliveryEngine;
    private readonly IWebhookSubscriptionRepository _webhookSubscriptionRepository;
    private readonly ILogger<SandboxEnvironment> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Per-developer isolated sandbox data store. Each developer gets their own
    /// collection of transactions, keyed by developer ID.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SandboxDeveloperData> _developerData = new();

    /// <summary>
    /// Test card numbers mapped to their expected scenario outcomes.
    /// </summary>
    private static readonly Dictionary<string, SandboxScenario> CardScenarios = new()
    {
        ["4000000000000000"] = SandboxScenario.SuccessfulPayment,
        ["4000000000000002"] = SandboxScenario.DeclinedPayment,
        ["4000000000000010"] = SandboxScenario.Timeout,
        ["4000000000000019"] = SandboxScenario.InsufficientFunds
    };

    /// <summary>
    /// Test amounts mapped to their expected scenario outcomes.
    /// </summary>
    private static readonly Dictionary<long, SandboxScenario> AmountScenarios = new()
    {
        [99999] = SandboxScenario.DeclinedPayment,
        [88888] = SandboxScenario.Timeout
    };

    public SandboxEnvironment(
        IWebhookDeliveryEngine webhookDeliveryEngine,
        IWebhookSubscriptionRepository webhookSubscriptionRepository,
        ILogger<SandboxEnvironment> logger)
    {
        _webhookDeliveryEngine = webhookDeliveryEngine ?? throw new ArgumentNullException(nameof(webhookDeliveryEngine));
        _webhookSubscriptionRepository = webhookSubscriptionRepository ?? throw new ArgumentNullException(nameof(webhookSubscriptionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    /// <inheritdoc/>
    public async Task<SandboxResponse> ProcessRequestAsync(SandboxRequest request, CancellationToken ct)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.DeveloperId == Guid.Empty)
            return CreateErrorResponse(400, "Developer ID is required.");

        _logger.LogDebug(
            "Processing sandbox request for developer {DeveloperId}: {Method} {Endpoint}",
            request.DeveloperId, request.Method, request.Endpoint);

        // Route request to appropriate handler based on endpoint
        var endpoint = request.Endpoint?.TrimEnd('/') ?? string.Empty;

        return endpoint.ToLowerInvariant() switch
        {
            var e when e.StartsWith("/api/v1/payments", StringComparison.OrdinalIgnoreCase)
                => await ProcessPaymentRequestAsync(request, ct),
            var e when e.StartsWith("/api/v1/refunds", StringComparison.OrdinalIgnoreCase)
                => await ProcessRefundRequestAsync(request, ct),
            var e when e.StartsWith("/api/v1/transactions", StringComparison.OrdinalIgnoreCase)
                => ProcessTransactionQueryRequest(request),
            _ => CreateErrorResponse(404, $"Sandbox endpoint not found: {request.Endpoint}")
        };
    }

    /// <inheritdoc/>
    public Task ResetDataAsync(Guid developerId, CancellationToken ct)
    {
        if (developerId == Guid.Empty)
            throw new ArgumentException("Developer ID is required.", nameof(developerId));

        _logger.LogInformation("Resetting sandbox data for developer {DeveloperId}", developerId);

        // Replace developer data with a fresh baseline state
        _developerData[developerId] = CreateBaselineData(developerId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates that an API key is appropriate for the sandbox environment.
    /// Sandbox keys are only accepted in sandbox; production keys are rejected.
    /// </summary>
    public static SandboxKeyValidationResult ValidateKeyIsolation(bool keyIsSandbox, bool environmentIsSandbox)
    {
        if (environmentIsSandbox && !keyIsSandbox)
        {
            return SandboxKeyValidationResult.Rejected(
                "Production API keys cannot be used in the sandbox environment. Please use a sandbox key.");
        }

        if (!environmentIsSandbox && keyIsSandbox)
        {
            return SandboxKeyValidationResult.Rejected(
                "Sandbox API keys cannot be used in the production environment. Please use a production key.");
        }

        return SandboxKeyValidationResult.Accepted();
    }

    private async Task<SandboxResponse> ProcessPaymentRequestAsync(SandboxRequest request, CancellationToken ct)
    {
        if (request.Method?.ToUpperInvariant() != "POST")
        {
            return CreateErrorResponse(405, "Method not allowed. Use POST for payment requests.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestBody))
        {
            return CreateErrorResponse(400, "Request body is required for payment requests.");
        }

        PaymentRequestBody? paymentBody;
        try
        {
            paymentBody = JsonSerializer.Deserialize<PaymentRequestBody>(request.RequestBody, _jsonOptions);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(400, "Invalid JSON in request body.");
        }

        if (paymentBody is null)
        {
            return CreateErrorResponse(400, "Request body cannot be empty.");
        }

        // Validate required fields (same validation rules as production)
        if (string.IsNullOrWhiteSpace(paymentBody.CardNumber))
        {
            return CreateErrorResponse(422, "Card number is required.");
        }

        if (paymentBody.Amount <= 0)
        {
            return CreateErrorResponse(422, "Amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(paymentBody.Currency))
        {
            return CreateErrorResponse(422, "Currency is required.");
        }

        // Determine scenario based on test input values
        var scenario = DetermineScenario(paymentBody.CardNumber, paymentBody.Amount);

        // Generate the payment result
        var transactionId = Guid.NewGuid();
        var result = GeneratePaymentResult(scenario, transactionId, paymentBody);

        // Store transaction in developer's isolated data
        var developerData = GetOrCreateDeveloperData(request.DeveloperId);
        developerData.Transactions[transactionId] = new SandboxTransaction
        {
            Id = transactionId,
            Amount = paymentBody.Amount,
            Currency = paymentBody.Currency,
            CardNumberLast4 = paymentBody.CardNumber.Length >= 4
                ? paymentBody.CardNumber[^4..]
                : paymentBody.CardNumber,
            Status = result.Status,
            CreatedAtUtc = DateTime.UtcNow,
            Scenario = scenario
        };

        // Trigger webhook delivery for sandbox subscriptions
        await DeliverSandboxWebhookAsync(
            request.DeveloperId,
            GetEventTypeForScenario(scenario),
            transactionId,
            result,
            ct);

        var responseBody = JsonSerializer.Serialize(result, _jsonOptions);
        var statusCode = scenario == SandboxScenario.Timeout ? 504 : 200;

        return new SandboxResponse(
            statusCode,
            responseBody,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["X-Sandbox"] = "true",
                ["X-Request-Id"] = Guid.NewGuid().ToString()
            });
    }

    private async Task<SandboxResponse> ProcessRefundRequestAsync(SandboxRequest request, CancellationToken ct)
    {
        if (request.Method?.ToUpperInvariant() != "POST")
        {
            return CreateErrorResponse(405, "Method not allowed. Use POST for refund requests.");
        }

        if (string.IsNullOrWhiteSpace(request.RequestBody))
        {
            return CreateErrorResponse(400, "Request body is required for refund requests.");
        }

        RefundRequestBody? refundBody;
        try
        {
            refundBody = JsonSerializer.Deserialize<RefundRequestBody>(request.RequestBody, _jsonOptions);
        }
        catch (JsonException)
        {
            return CreateErrorResponse(400, "Invalid JSON in request body.");
        }

        if (refundBody is null)
        {
            return CreateErrorResponse(400, "Request body cannot be empty.");
        }

        if (refundBody.TransactionId == Guid.Empty)
        {
            return CreateErrorResponse(422, "Transaction ID is required.");
        }

        if (refundBody.Amount <= 0)
        {
            return CreateErrorResponse(422, "Refund amount must be greater than zero.");
        }

        // Check developer isolation - only access own transactions
        var developerData = GetOrCreateDeveloperData(request.DeveloperId);
        if (!developerData.Transactions.TryGetValue(refundBody.TransactionId, out var transaction))
        {
            return CreateErrorResponse(404, "Transaction not found.");
        }

        if (transaction.Status != "succeeded")
        {
            return CreateErrorResponse(422, "Can only refund successful transactions.");
        }

        if (refundBody.Amount > transaction.Amount)
        {
            return CreateErrorResponse(422, "Refund amount cannot exceed original transaction amount.");
        }

        var refundId = Guid.NewGuid();
        var refundResult = new
        {
            refundId,
            transactionId = refundBody.TransactionId,
            amount = refundBody.Amount,
            currency = transaction.Currency,
            status = "refunded",
            createdAt = DateTime.UtcNow.ToString("O")
        };

        transaction.Status = "refunded";

        // Trigger webhook for refund
        await DeliverSandboxWebhookAsync(
            request.DeveloperId,
            "refund.processed",
            refundId,
            refundResult,
            ct);

        return new SandboxResponse(
            200,
            JsonSerializer.Serialize(refundResult, _jsonOptions),
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["X-Sandbox"] = "true",
                ["X-Request-Id"] = Guid.NewGuid().ToString()
            });
    }

    private SandboxResponse ProcessTransactionQueryRequest(SandboxRequest request)
    {
        if (request.Method?.ToUpperInvariant() != "GET")
        {
            return CreateErrorResponse(405, "Method not allowed. Use GET for transaction queries.");
        }

        var developerData = GetOrCreateDeveloperData(request.DeveloperId);
        var transactions = developerData.Transactions.Values
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new
            {
                id = t.Id,
                amount = t.Amount,
                currency = t.Currency,
                cardLast4 = t.CardNumberLast4,
                status = t.Status,
                createdAt = t.CreatedAtUtc.ToString("O")
            })
            .ToList();

        var responseBody = JsonSerializer.Serialize(
            new { data = transactions, total = transactions.Count },
            _jsonOptions);

        return new SandboxResponse(
            200,
            responseBody,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["X-Sandbox"] = "true"
            });
    }

    private async Task DeliverSandboxWebhookAsync(
        Guid developerId,
        string eventType,
        Guid resourceId,
        object eventData,
        CancellationToken ct)
    {
        try
        {
            // Find sandbox webhook subscriptions for this developer matching the event type
            var subscriptions = await _webhookSubscriptionRepository
                .GetActiveByEventTypeAsync(eventType, ct);

            // Filter to only sandbox subscriptions for this developer
            var sandboxSubscriptions = subscriptions
                .Where(s => s.MerchantId == developerId)
                .ToList();

            if (sandboxSubscriptions.Count == 0)
            {
                _logger.LogDebug(
                    "No sandbox webhook subscriptions found for developer {DeveloperId} and event {EventType}",
                    developerId, eventType);
                return;
            }

            var payload = JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString(),
                type = eventType,
                data = eventData,
                resourceId,
                timestamp = DateTime.UtcNow.ToString("O"),
                sandbox = true
            }, _jsonOptions);

            foreach (var subscription in sandboxSubscriptions)
            {
                var delivery = WebhookDelivery.Create(
                    subscription.Id,
                    eventType,
                    payload);

                // Use same delivery mechanics as production
                await _webhookDeliveryEngine.DeliverAsync(delivery, ct);
            }
        }
        catch (Exception ex)
        {
            // Webhook delivery failures in sandbox should not break the main request
            _logger.LogWarning(ex,
                "Failed to deliver sandbox webhook for developer {DeveloperId}, event {EventType}",
                developerId, eventType);
        }
    }

    private static SandboxScenario DetermineScenario(string cardNumber, long amount)
    {
        // Card number takes priority for scenario determination
        var sanitizedCard = cardNumber.Replace(" ", "").Replace("-", "");
        if (CardScenarios.TryGetValue(sanitizedCard, out var cardScenario))
        {
            return cardScenario;
        }

        // Fall back to amount-based scenarios
        if (AmountScenarios.TryGetValue(amount, out var amountScenario))
        {
            return amountScenario;
        }

        // Default: successful payment for any other valid input
        return SandboxScenario.SuccessfulPayment;
    }

    private static SandboxPaymentResult GeneratePaymentResult(
        SandboxScenario scenario,
        Guid transactionId,
        PaymentRequestBody paymentBody)
    {
        return scenario switch
        {
            SandboxScenario.SuccessfulPayment => new SandboxPaymentResult
            {
                TransactionId = transactionId,
                Amount = paymentBody.Amount,
                Currency = paymentBody.Currency,
                Status = "succeeded",
                AuthorizationCode = "AUTH" + Random.Shared.Next(100000, 999999),
                ProcessorReference = "SBX_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CreatedAt = DateTime.UtcNow.ToString("O"),
                Message = "Payment processed successfully."
            },
            SandboxScenario.DeclinedPayment => new SandboxPaymentResult
            {
                TransactionId = transactionId,
                Amount = paymentBody.Amount,
                Currency = paymentBody.Currency,
                Status = "declined",
                AuthorizationCode = null,
                ProcessorReference = null,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                Message = "Payment declined by issuer.",
                DeclineCode = "card_declined",
                DeclineReason = "The card was declined. Please try a different card."
            },
            SandboxScenario.Timeout => new SandboxPaymentResult
            {
                TransactionId = transactionId,
                Amount = paymentBody.Amount,
                Currency = paymentBody.Currency,
                Status = "failed",
                AuthorizationCode = null,
                ProcessorReference = null,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                Message = "Payment processing timed out. Please retry.",
                DeclineCode = "processing_timeout",
                DeclineReason = "The request to the payment processor timed out."
            },
            SandboxScenario.InsufficientFunds => new SandboxPaymentResult
            {
                TransactionId = transactionId,
                Amount = paymentBody.Amount,
                Currency = paymentBody.Currency,
                Status = "declined",
                AuthorizationCode = null,
                ProcessorReference = null,
                CreatedAt = DateTime.UtcNow.ToString("O"),
                Message = "Insufficient funds.",
                DeclineCode = "insufficient_funds",
                DeclineReason = "The card has insufficient funds to complete this transaction."
            },
            _ => new SandboxPaymentResult
            {
                TransactionId = transactionId,
                Amount = paymentBody.Amount,
                Currency = paymentBody.Currency,
                Status = "succeeded",
                AuthorizationCode = "AUTH" + Random.Shared.Next(100000, 999999),
                ProcessorReference = "SBX_" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                CreatedAt = DateTime.UtcNow.ToString("O"),
                Message = "Payment processed successfully."
            }
        };
    }

    private static string GetEventTypeForScenario(SandboxScenario scenario) => scenario switch
    {
        SandboxScenario.SuccessfulPayment => "payment.completed",
        SandboxScenario.DeclinedPayment => "payment.failed",
        SandboxScenario.Timeout => "payment.failed",
        SandboxScenario.InsufficientFunds => "payment.failed",
        _ => "payment.completed"
    };

    private SandboxDeveloperData GetOrCreateDeveloperData(Guid developerId)
    {
        return _developerData.GetOrAdd(developerId, id => CreateBaselineData(id));
    }

    private static SandboxDeveloperData CreateBaselineData(Guid developerId)
    {
        var data = new SandboxDeveloperData { DeveloperId = developerId };

        // Populate with a few predefined baseline transactions
        var baselineSuccessful = new SandboxTransaction
        {
            Id = Guid.NewGuid(),
            Amount = 5000,
            Currency = "NGN",
            CardNumberLast4 = "0000",
            Status = "succeeded",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            Scenario = SandboxScenario.SuccessfulPayment
        };

        var baselineDeclined = new SandboxTransaction
        {
            Id = Guid.NewGuid(),
            Amount = 10000,
            Currency = "NGN",
            CardNumberLast4 = "0002",
            Status = "declined",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
            Scenario = SandboxScenario.DeclinedPayment
        };

        data.Transactions[baselineSuccessful.Id] = baselineSuccessful;
        data.Transactions[baselineDeclined.Id] = baselineDeclined;

        return data;
    }

    private static SandboxResponse CreateErrorResponse(int statusCode, string message)
    {
        var body = JsonSerializer.Serialize(new
        {
            error = new { code = statusCode, message }
        });

        return new SandboxResponse(
            statusCode,
            body,
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["X-Sandbox"] = "true"
            });
    }
}

/// <summary>
/// Result of sandbox/production key isolation validation.
/// </summary>
public record SandboxKeyValidationResult(bool IsAccepted, string? RejectionReason)
{
    public static SandboxKeyValidationResult Accepted() => new(true, null);
    public static SandboxKeyValidationResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Predefined test scenarios triggered by specific input values.
/// </summary>
internal enum SandboxScenario
{
    SuccessfulPayment,
    DeclinedPayment,
    Timeout,
    InsufficientFunds
}

/// <summary>
/// Per-developer isolated sandbox data container.
/// </summary>
internal class SandboxDeveloperData
{
    public Guid DeveloperId { get; init; }
    public ConcurrentDictionary<Guid, SandboxTransaction> Transactions { get; } = new();
}

/// <summary>
/// Represents a sandbox transaction stored in developer's isolated data.
/// </summary>
internal class SandboxTransaction
{
    public Guid Id { get; init; }
    public long Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string CardNumberLast4 { get; init; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public SandboxScenario Scenario { get; init; }
}

/// <summary>
/// Payment request body structure for sandbox processing.
/// Mirrors the production payment request format.
/// </summary>
internal class PaymentRequestBody
{
    [JsonPropertyName("cardNumber")]
    public string? CardNumber { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("merchantReference")]
    public string? MerchantReference { get; set; }
}

/// <summary>
/// Refund request body structure for sandbox processing.
/// </summary>
internal class RefundRequestBody
{
    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>
/// Sandbox payment result matching production response format.
/// </summary>
internal class SandboxPaymentResult
{
    [JsonPropertyName("transactionId")]
    public Guid TransactionId { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("authorizationCode")]
    public string? AuthorizationCode { get; set; }

    [JsonPropertyName("processorReference")]
    public string? ProcessorReference { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("declineCode")]
    public string? DeclineCode { get; set; }

    [JsonPropertyName("declineReason")]
    public string? DeclineReason { get; set; }
}
