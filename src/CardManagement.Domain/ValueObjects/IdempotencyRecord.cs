namespace CardManagement.Domain.ValueObjects;

/// <summary>
/// Represents an idempotency record that ensures duplicate payment requests
/// produce the same result without reprocessing.
/// </summary>
public record IdempotencyRecord
{
    public string Key { get; }
    public Guid PaymentRequestId { get; }
    public string? ResponsePayload { get; }
    public DateTime CreatedAtUtc { get; }
    public DateTime ExpiresAtUtc { get; }

    public IdempotencyRecord(
        string key,
        Guid paymentRequestId,
        string? responsePayload,
        DateTime createdAtUtc,
        DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Idempotency key is required.", nameof(key));

        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("Expiry must be after creation time.", nameof(expiresAtUtc));

        Key = key;
        PaymentRequestId = paymentRequestId;
        ResponsePayload = responsePayload;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }
}
