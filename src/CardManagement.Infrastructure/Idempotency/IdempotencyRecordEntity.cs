namespace CardManagement.Infrastructure.Idempotency;

/// <summary>
/// Persistence entity for idempotency records stored in PostgreSQL.
/// Wraps the domain value object with additional state needed for EF Core mapping.
/// </summary>
public class IdempotencyRecordEntity
{
    /// <summary>
    /// The client-provided idempotency key (primary key).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The associated payment request ID.
    /// </summary>
    public Guid RequestId { get; set; }

    /// <summary>
    /// The serialized response payload (JSONB). Null while request is in-progress.
    /// </summary>
    public string? ResponsePayload { get; set; }

    /// <summary>
    /// Whether the request has been completed (true) or is still in-progress (false).
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// When the record was created (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// When the record expires and can be cleaned up (UTC).
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }
}
