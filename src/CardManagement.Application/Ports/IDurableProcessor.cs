namespace CardManagement.Application.Ports;

/// <summary>
/// Background processor for durable message consumption with at-least-once delivery guarantees.
/// </summary>
public interface IDurableProcessor
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
