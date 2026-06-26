using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Idempotency;

/// <summary>
/// PostgreSQL-backed idempotency guard that prevents duplicate payment processing.
/// Records are stored with a 24-hour TTL for automatic cleanup.
/// </summary>
public class IdempotencyGuard : IIdempotencyGuard
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    private readonly CardManagementDbContext _dbContext;

    public IdempotencyGuard(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<IdempotencyCheckResult> CheckAsync(string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        var record = await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == idempotencyKey, ct);

        if (record is null)
        {
            return new IdempotencyCheckResult.NotFound();
        }

        // If the record has expired, treat it as not found
        if (record.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return new IdempotencyCheckResult.NotFound();
        }

        if (record.IsCompleted && record.ResponsePayload is not null)
        {
            var cachedResponse = JsonSerializer.Deserialize<object>(record.ResponsePayload)!;
            return new IdempotencyCheckResult.Completed(cachedResponse);
        }

        // Record exists but not completed — request is in progress
        return new IdempotencyCheckResult.InProgress();
    }

    /// <inheritdoc />
    public async Task StoreAsync(string idempotencyKey, Guid paymentRequestId, object response, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        var now = DateTime.UtcNow;
        var serializedResponse = JsonSerializer.Serialize(response);

        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == idempotencyKey, ct);

        if (existing is not null)
        {
            // Update the existing in-progress record with the completed response
            existing.ResponsePayload = serializedResponse;
            existing.IsCompleted = true;
        }
        else
        {
            // Create a new completed record
            var entity = new IdempotencyRecordEntity
            {
                Key = idempotencyKey,
                RequestId = paymentRequestId,
                ResponsePayload = serializedResponse,
                IsCompleted = true,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.Add(DefaultTtl)
            };

            _dbContext.IdempotencyRecords.Add(entity);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Registers an in-progress idempotency record before processing begins.
    /// This is used to detect concurrent duplicate requests.
    /// </summary>
    public async Task RegisterInProgressAsync(string idempotencyKey, Guid paymentRequestId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));
        }

        var now = DateTime.UtcNow;

        var entity = new IdempotencyRecordEntity
        {
            Key = idempotencyKey,
            RequestId = paymentRequestId,
            ResponsePayload = null,
            IsCompleted = false,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(DefaultTtl)
        };

        _dbContext.IdempotencyRecords.Add(entity);
        await _dbContext.SaveChangesAsync(ct);
    }
}
