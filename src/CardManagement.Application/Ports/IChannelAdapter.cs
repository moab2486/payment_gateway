using CardManagement.Application.DTOs;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;

namespace CardManagement.Application.Ports;

/// <summary>
/// Adapter interface for a specific payment channel (e.g., NIP, NQR, Interswitch).
/// Each channel implements this interface to process, reverse, and report health.
/// </summary>
public interface IChannelAdapter
{
    PaymentChannel Channel { get; }
    Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct);
    Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct);
    Task<HealthStatus> CheckHealthAsync(CancellationToken ct);
}
