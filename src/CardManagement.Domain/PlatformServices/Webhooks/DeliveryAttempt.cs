namespace CardManagement.Domain.PlatformServices.Webhooks;

/// <summary>
/// Value object representing a single attempt to deliver a webhook payload to the subscriber endpoint.
/// Records the outcome (HTTP status, response time) for observability and debugging.
/// </summary>
public record DeliveryAttempt(
    DateTime AttemptedAtUtc,
    int HttpStatusCode,
    TimeSpan ResponseTime,
    string? ErrorMessage
);
