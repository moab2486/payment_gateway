namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the result of an idempotency key check.
/// </summary>
public abstract record IdempotencyCheckResult
{
    private IdempotencyCheckResult() { }

    /// <summary>
    /// The idempotency key was not found — this is a new request.
    /// </summary>
    public sealed record NotFound() : IdempotencyCheckResult;

    /// <summary>
    /// A previous request with this key has already completed.
    /// </summary>
    public sealed record Completed(object CachedResponse) : IdempotencyCheckResult;

    /// <summary>
    /// A request with this key is currently being processed (conflict).
    /// </summary>
    public sealed record InProgress() : IdempotencyCheckResult;
}
