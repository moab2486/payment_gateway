using CardManagement.Domain.PlatformServices.DeveloperPortal;

namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Applies filtering criteria from a RequestLogQuery to a collection of log entries.
/// Supports filtering by developer ID, endpoint, status code, and date range.
/// </summary>
public static class RequestLogQueryFilter
{
    /// <summary>
    /// Applies the query's filter criteria to the log entries and returns
    /// only entries matching ALL specified filters.
    /// </summary>
    public static List<RequestLogEntry> Apply(
        IEnumerable<RequestLogEntry> entries,
        RequestLogQuery query)
    {
        var filtered = entries.Where(e => e.DeveloperId == query.DeveloperId);

        if (!string.IsNullOrEmpty(query.Endpoint))
        {
            filtered = filtered.Where(e => e.Endpoint == query.Endpoint);
        }

        if (query.StatusCode.HasValue)
        {
            filtered = filtered.Where(e => e.ResponseStatus == query.StatusCode.Value);
        }

        if (query.FromUtc.HasValue)
        {
            filtered = filtered.Where(e => e.TimestampUtc >= query.FromUtc.Value);
        }

        if (query.ToUtc.HasValue)
        {
            filtered = filtered.Where(e => e.TimestampUtc <= query.ToUtc.Value);
        }

        return filtered.ToList();
    }
}
