namespace CardManagement.Application.PlatformServices.AdminConsole.DTOs;

/// <summary>
/// Query parameters for read model queries supporting filtering, pagination, and date ranges.
/// </summary>
public record ReadModelQuery(
    string ReadModelName,
    int Page,
    int PageSize,
    DateTime? FromDate,
    DateTime? ToDate,
    Dictionary<string, string>? Filters);
