using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Tracks debit attempts that failed due to insufficient funds and determines
/// whether retries should be scheduled based on the mandate retry policy.
/// </summary>
public class DebitRetryScheduler
{
    private readonly DirectDebitOptions _options;
    private readonly ILogger<DebitRetryScheduler> _logger;
    private readonly ConcurrentDictionary<string, DebitRetryEntry> _retryEntries = new();

    public DebitRetryScheduler(
        IOptions<DirectDebitOptions> options,
        ILogger<DebitRetryScheduler> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records a failed debit and determines whether it should be retried.
    /// Returns the current retry count and next retry time if applicable.
    /// </summary>
    /// <param name="mandateReference">The mandate reference for the failed debit.</param>
    /// <param name="transactionReference">The transaction reference for the failed debit.</param>
    /// <returns>
    /// A tuple indicating: (shouldRetry, retryCount, nextRetryTime).
    /// shouldRetry is false when max retries have been exhausted.
    /// </returns>
    public (bool ShouldRetry, int RetryCount, DateTime? NextRetryTime) RecordFailureAndEvaluate(
        string mandateReference,
        string transactionReference)
    {
        var key = BuildKey(mandateReference, transactionReference);

        var entry = _retryEntries.AddOrUpdate(
            key,
            _ => new DebitRetryEntry
            {
                MandateReference = mandateReference,
                TransactionReference = transactionReference,
                RetryCount = 1,
                FirstFailureUtc = DateTime.UtcNow,
                LastFailureUtc = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.RetryCount++;
                existing.LastFailureUtc = DateTime.UtcNow;
                return existing;
            });

        if (entry.RetryCount > _options.MaxRetries)
        {
            _logger.LogWarning(
                "Debit retry exhausted for MandateRef: {MandateRef}, TxRef: {TxRef}. Attempts: {Attempts}/{MaxRetries}",
                mandateReference, transactionReference, entry.RetryCount, _options.MaxRetries);

            // Remove the entry since retries are exhausted
            _retryEntries.TryRemove(key, out _);

            return (false, entry.RetryCount, null);
        }

        var nextRetryTime = DateTime.UtcNow.AddHours(_options.RetryIntervalHours);
        entry.NextRetryTimeUtc = nextRetryTime;

        _logger.LogInformation(
            "Debit scheduled for retry. MandateRef: {MandateRef}, TxRef: {TxRef}, Attempt: {Attempt}/{MaxRetries}, NextRetry: {NextRetry}",
            mandateReference, transactionReference, entry.RetryCount, _options.MaxRetries, nextRetryTime);

        return (true, entry.RetryCount, nextRetryTime);
    }

    /// <summary>
    /// Removes the retry tracking entry for a mandate/transaction pair after a successful debit.
    /// </summary>
    public void ClearRetryEntry(string mandateReference, string transactionReference)
    {
        var key = BuildKey(mandateReference, transactionReference);
        _retryEntries.TryRemove(key, out _);
    }

    /// <summary>
    /// Gets the current retry count for a mandate/transaction pair.
    /// Returns 0 if no retry entry exists.
    /// </summary>
    public int GetRetryCount(string mandateReference, string transactionReference)
    {
        var key = BuildKey(mandateReference, transactionReference);
        return _retryEntries.TryGetValue(key, out var entry) ? entry.RetryCount : 0;
    }

    /// <summary>
    /// Gets all entries that are due for retry (next retry time has passed).
    /// </summary>
    public IReadOnlyList<DebitRetryEntry> GetEntriesDueForRetry()
    {
        var now = DateTime.UtcNow;
        return _retryEntries.Values
            .Where(e => e.NextRetryTimeUtc.HasValue && e.NextRetryTimeUtc.Value <= now)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets all pending retry entries.
    /// </summary>
    public IReadOnlyList<DebitRetryEntry> GetAllPendingEntries()
    {
        return _retryEntries.Values.ToList().AsReadOnly();
    }

    private static string BuildKey(string mandateReference, string transactionReference) =>
        $"{mandateReference}::{transactionReference}";
}

/// <summary>
/// Represents a tracked debit retry entry.
/// </summary>
public class DebitRetryEntry
{
    /// <summary>
    /// The mandate reference for the failing debit.
    /// </summary>
    public string MandateReference { get; set; } = string.Empty;

    /// <summary>
    /// The transaction reference for the failing debit.
    /// </summary>
    public string TransactionReference { get; set; } = string.Empty;

    /// <summary>
    /// Number of retry attempts made so far.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When the first failure occurred.
    /// </summary>
    public DateTime FirstFailureUtc { get; set; }

    /// <summary>
    /// When the most recent failure occurred.
    /// </summary>
    public DateTime LastFailureUtc { get; set; }

    /// <summary>
    /// When the next retry should be attempted.
    /// </summary>
    public DateTime? NextRetryTimeUtc { get; set; }
}
