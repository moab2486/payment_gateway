namespace CardManagement.Application.PlatformServices.Notifications.DTOs;

/// <summary>
/// Represents a date range used for filtering delivery statistics queries.
/// </summary>
public record DateRange
{
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }

    public DateRange(DateTime startUtc, DateTime endUtc)
    {
        if (endUtc < startUtc)
            throw new ArgumentException("End date must be on or after start date.", nameof(endUtc));

        StartUtc = startUtc;
        EndUtc = endUtc;
    }
}
