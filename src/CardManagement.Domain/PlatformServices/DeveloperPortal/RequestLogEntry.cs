namespace CardManagement.Domain.PlatformServices.DeveloperPortal;

/// <summary>
/// Represents a logged API request/response pair for a developer's API key.
/// Stores request metadata with PCI-sensitive fields redacted before persistence.
/// Authorization header values, card numbers, CVVs, and PINs are masked.
/// </summary>
public class RequestLogEntry
{
    public Guid Id { get; private set; }
    public Guid ApiKeyId { get; private set; }
    public Guid DeveloperId { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public string Method { get; private set; } = string.Empty;
    public string? RequestHeaders { get; private set; }
    public string? RequestBody { get; private set; }
    public int ResponseStatus { get; private set; }
    public string? ResponseBody { get; private set; }
    public TimeSpan Latency { get; private set; }
    public DateTime TimestampUtc { get; private set; }

    private RequestLogEntry() { }

    /// <summary>
    /// Creates a new request log entry. Bodies and headers should be PCI-redacted
    /// before calling this method (redaction is an application-level concern).
    /// </summary>
    public static RequestLogEntry Create(
        Guid apiKeyId,
        Guid developerId,
        string endpoint,
        string method,
        string? requestHeaders,
        string? requestBody,
        int responseStatus,
        string? responseBody,
        TimeSpan latency)
    {
        if (apiKeyId == Guid.Empty)
            throw new ArgumentException("API key ID is required.", nameof(apiKeyId));

        if (developerId == Guid.Empty)
            throw new ArgumentException("Developer ID is required.", nameof(developerId));

        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));

        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("HTTP method is required.", nameof(method));

        if (responseStatus < 100 || responseStatus > 599)
            throw new ArgumentException("Response status must be a valid HTTP status code (100-599).", nameof(responseStatus));

        if (latency < TimeSpan.Zero)
            throw new ArgumentException("Latency cannot be negative.", nameof(latency));

        return new RequestLogEntry
        {
            Id = Guid.NewGuid(),
            ApiKeyId = apiKeyId,
            DeveloperId = developerId,
            Endpoint = endpoint,
            Method = method.ToUpperInvariant(),
            RequestHeaders = requestHeaders,
            RequestBody = requestBody,
            ResponseStatus = responseStatus,
            ResponseBody = responseBody,
            Latency = latency,
            TimestampUtc = DateTime.UtcNow
        };
    }
}
