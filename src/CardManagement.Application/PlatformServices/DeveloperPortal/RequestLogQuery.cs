namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Query parameters for filtering request log entries.
/// Supports filtering by endpoint, status code, and date range.
/// </summary>
public record RequestLogQuery(
    Guid DeveloperId,
    string? Endpoint = null,
    int? StatusCode = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 50);
