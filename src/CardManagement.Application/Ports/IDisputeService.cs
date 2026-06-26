using CardManagement.Application.DTOs;
using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports;

/// <summary>
/// Manages the dispute lifecycle: raising, resolving, and querying disputes.
/// </summary>
public interface IDisputeService
{
    Task<DisputeResult> RaiseDisputeAsync(DisputeRequest request, CancellationToken ct);
    Task<DisputeResult> ResolveDisputeAsync(Guid disputeId, DisputeResolution resolution, CancellationToken ct);
    Task<DisputeRecord?> GetDisputeAsync(Guid disputeId, CancellationToken ct);
}
