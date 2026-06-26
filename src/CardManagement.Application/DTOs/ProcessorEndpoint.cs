using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.DTOs;

/// <summary>
/// DTO representing the network endpoint configuration for a card processor switch.
/// </summary>
public record ProcessorEndpoint
{
    /// <summary>
    /// The hostname or IP address of the processor endpoint.
    /// </summary>
    public string Address { get; init; } = string.Empty;

    /// <summary>
    /// The TCP port number for the processor endpoint.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// The type of card processor (Interswitch or CardFi).
    /// </summary>
    public ProcessorType ProcessorType { get; init; }
}
