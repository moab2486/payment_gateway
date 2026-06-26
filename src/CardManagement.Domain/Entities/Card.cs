using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Card aggregate root representing a payment card (virtual or physical).
/// Contains encrypted sensitive data (PAN, CVV2) and references the owning account.
/// </summary>
public class Card
{
    public Guid Id { get; private set; }
    public string PanEncrypted { get; private set; } = string.Empty;
    public string PanHash { get; private set; } = string.Empty;
    public string Cvv2Encrypted { get; private set; } = string.Empty;
    public string ExpiryDate { get; private set; } = string.Empty;
    public Guid AccountId { get; private set; }
    public CardScheme CardScheme { get; private set; }
    public string BinRange { get; private set; } = string.Empty;
    public CardStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private Card() { }

    public static Card Create(
        string panEncrypted,
        string panHash,
        string cvv2Encrypted,
        string expiryDate,
        Guid accountId,
        CardScheme cardScheme,
        string binRange)
    {
        var now = TruncateToMilliseconds(DateTime.UtcNow);

        return new Card
        {
            Id = Guid.NewGuid(),
            PanEncrypted = panEncrypted ?? throw new ArgumentNullException(nameof(panEncrypted)),
            PanHash = panHash ?? throw new ArgumentNullException(nameof(panHash)),
            Cvv2Encrypted = cvv2Encrypted ?? throw new ArgumentNullException(nameof(cvv2Encrypted)),
            ExpiryDate = expiryDate ?? throw new ArgumentNullException(nameof(expiryDate)),
            AccountId = accountId,
            CardScheme = cardScheme,
            BinRange = binRange ?? throw new ArgumentNullException(nameof(binRange)),
            Status = CardStatus.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void Block()
    {
        if (Status == CardStatus.Cancelled)
            throw new InvalidOperationException("Cannot block a cancelled card.");

        Status = CardStatus.Blocked;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Expire()
    {
        Status = CardStatus.Expired;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Cancel()
    {
        Status = CardStatus.Cancelled;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
