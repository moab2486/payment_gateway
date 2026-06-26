using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Entity representing the session state with an external card processor switch.
/// Tracks sign-on status and heartbeat for connection health monitoring.
/// </summary>
public class ProcessorSession
{
    public Guid Id { get; private set; }
    public ProcessorType ProcessorType { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;
    public bool IsSignedOn { get; private set; }
    public DateTime? LastSignOnUtc { get; private set; }
    public DateTime? LastHeartbeatUtc { get; private set; }

    private ProcessorSession() { }

    public static ProcessorSession Create(ProcessorType processorType, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required.", nameof(endpoint));

        return new ProcessorSession
        {
            Id = Guid.NewGuid(),
            ProcessorType = processorType,
            Endpoint = endpoint,
            IsSignedOn = false,
            LastSignOnUtc = null,
            LastHeartbeatUtc = null
        };
    }

    public void SignOn()
    {
        IsSignedOn = true;
        LastSignOnUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void SignOff()
    {
        IsSignedOn = false;
    }

    public void RecordHeartbeat()
    {
        LastHeartbeatUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
