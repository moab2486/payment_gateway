using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class CardTests
{
    [Fact]
    public void Create_ValidInput_CreatesCardWithActiveStatus()
    {
        var card = Card.Create(
            panEncrypted: "enc_pan_123",
            panHash: "hash_abc",
            cvv2Encrypted: "enc_cvv2",
            expiryDate: "12/25",
            accountId: Guid.NewGuid(),
            cardScheme: CardScheme.Visa,
            binRange: "456789");

        Assert.NotEqual(Guid.Empty, card.Id);
        Assert.Equal("enc_pan_123", card.PanEncrypted);
        Assert.Equal("hash_abc", card.PanHash);
        Assert.Equal("enc_cvv2", card.Cvv2Encrypted);
        Assert.Equal("12/25", card.ExpiryDate);
        Assert.Equal(CardScheme.Visa, card.CardScheme);
        Assert.Equal("456789", card.BinRange);
        Assert.Equal(CardStatus.Active, card.Status);
        Assert.Equal(DateTimeKind.Utc, card.CreatedAtUtc.Kind);
        Assert.Equal(0, card.CreatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public void Create_NullPanEncrypted_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Card.Create(
            null!, "hash", "cvv2", "12/25", Guid.NewGuid(), CardScheme.Visa, "456789"));
    }

    [Fact]
    public void Create_NullPanHash_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => Card.Create(
            "pan", null!, "cvv2", "12/25", Guid.NewGuid(), CardScheme.Visa, "456789"));
    }

    [Fact]
    public void Block_ActiveCard_SetsStatusToBlocked()
    {
        var card = Card.Create("pan", "hash", "cvv2", "12/25", Guid.NewGuid(), CardScheme.Verve, "506");
        card.Block();
        Assert.Equal(CardStatus.Blocked, card.Status);
    }

    [Fact]
    public void Block_CancelledCard_ThrowsInvalidOperationException()
    {
        var card = Card.Create("pan", "hash", "cvv2", "12/25", Guid.NewGuid(), CardScheme.Verve, "506");
        card.Cancel();
        Assert.Throws<InvalidOperationException>(() => card.Block());
    }

    [Fact]
    public void Expire_SetsStatusToExpired()
    {
        var card = Card.Create("pan", "hash", "cvv2", "12/25", Guid.NewGuid(), CardScheme.Mastercard, "5412");
        card.Expire();
        Assert.Equal(CardStatus.Expired, card.Status);
    }

    [Fact]
    public void Cancel_SetsStatusToCancelled()
    {
        var card = Card.Create("pan", "hash", "cvv2", "12/25", Guid.NewGuid(), CardScheme.Mastercard, "5412");
        card.Cancel();
        Assert.Equal(CardStatus.Cancelled, card.Status);
    }

    [Fact]
    public void Create_TimestampsHaveMillisecondPrecision()
    {
        var card = Card.Create("pan", "hash", "cvv2", "12/25", Guid.NewGuid(), CardScheme.Visa, "4");
        // Verify sub-millisecond ticks are truncated
        Assert.Equal(0, card.CreatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(0, card.UpdatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }
}
