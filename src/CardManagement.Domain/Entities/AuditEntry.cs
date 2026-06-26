using System.Security.Cryptography;
using System.Text;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Represents an immutable audit trail entry that forms part of a hash chain.
/// Each entry links cryptographically to the previous entry for tamper detection.
/// </summary>
public class AuditEntry
{
    public long Id { get; private set; }
    public string TransactionReference { get; private set; } = string.Empty;
    public DateTime TimestampUtc { get; private set; }
    public string ActorIdentity { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string? PreviousState { get; private set; }
    public string? NewState { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? PreviousEntryHash { get; private set; }
    public string EntryHash { get; private set; } = string.Empty;

    private AuditEntry() { }

    public static AuditEntry Create(
        string transactionReference,
        string actorIdentity,
        string action,
        string? previousState,
        string? newState,
        string correlationId,
        string? previousEntryHash)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("Transaction reference is required.", nameof(transactionReference));

        if (string.IsNullOrWhiteSpace(actorIdentity))
            throw new ArgumentException("Actor identity is required.", nameof(actorIdentity));

        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required.", nameof(action));

        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Correlation ID is required.", nameof(correlationId));

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        var entry = new AuditEntry
        {
            TransactionReference = transactionReference,
            TimestampUtc = now,
            ActorIdentity = actorIdentity,
            Action = action,
            PreviousState = previousState,
            NewState = newState,
            CorrelationId = correlationId,
            PreviousEntryHash = previousEntryHash
        };

        entry.EntryHash = entry.ComputeHash();

        return entry;
    }

    /// <summary>
    /// Computes the SHA-256 hash for this entry, incorporating the previous entry hash
    /// to form an unbroken chain.
    /// </summary>
    public string ComputeHash()
    {
        var payload = string.Concat(
            TransactionReference,
            TimestampUtc.ToString("O"),
            ActorIdentity,
            Action,
            PreviousState ?? string.Empty,
            NewState ?? string.Empty,
            CorrelationId,
            PreviousEntryHash ?? string.Empty);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
