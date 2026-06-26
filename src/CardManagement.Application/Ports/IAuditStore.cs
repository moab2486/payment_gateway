using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports;

/// <summary>
/// Append-only audit store with hash chain integrity for tamper-evident logging.
/// </summary>
public interface IAuditStore
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct);
    Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct);

    /// <summary>
    /// Queries audit entries with filtering by actor, action, and date range. Supports pagination.
    /// This is a read-only interface; no modification or deletion of entries is permitted.
    /// </summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? actorIdentity,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        int offset,
        CancellationToken ct)
    {
        // Default implementation returns empty list for backward compatibility with test fakes.
        return Task.FromResult<IReadOnlyList<AuditEntry>>(Array.Empty<AuditEntry>());
    }
}
