using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository interface for ProcessorSession persistence operations.
/// Tracks the sign-on state and connection health of external card processor switches.
/// </summary>
public interface IProcessorSessionRepository
{
    /// <summary>
    /// Retrieves the processor session for a given processor type.
    /// </summary>
    Task<ProcessorSession?> GetByTypeAsync(ProcessorType processorType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists updates to an existing processor session (e.g., sign-on state, heartbeat timestamp).
    /// </summary>
    Task UpdateAsync(ProcessorSession session, CancellationToken cancellationToken = default);
}
