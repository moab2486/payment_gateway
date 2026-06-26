using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class TransactionRecordTests
{
    [Fact]
    public void Create_ValidInput_CreatesWithPendingStatus()
    {
        var cardId = Guid.NewGuid();

        var record = TransactionRecord.Create(
            "123456",
            "0100",
            cardId,
            50000,
            "NGN",
            ProcessorType.Interswitch);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal("123456", record.SystemTraceAuditNumber);
        Assert.Equal("0100", record.MessageType);
        Assert.Equal(cardId, record.CardId);
        Assert.Equal(50000, record.Amount);
        Assert.Equal("NGN", record.Currency);
        Assert.Equal(ProcessorType.Interswitch, record.ProcessorType);
        Assert.Equal(TransactionStatus.Pending, record.Status);
        Assert.Null(record.ResponseCode);
        Assert.Null(record.PipelineStepReached);
        Assert.Equal(DateTimeKind.Utc, record.CreatedAtUtc.Kind);
    }

    [Fact]
    public void Create_EmptyStan_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TransactionRecord.Create(
            "", "0100", Guid.NewGuid(), 1000, "NGN", ProcessorType.Interswitch));
    }

    [Fact]
    public void Create_EmptyMessageType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TransactionRecord.Create(
            "123456", "", Guid.NewGuid(), 1000, "NGN", ProcessorType.Interswitch));
    }

    [Fact]
    public void Create_EmptyCardId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TransactionRecord.Create(
            "123456", "0100", Guid.Empty, 1000, "NGN", ProcessorType.Interswitch));
    }

    [Fact]
    public void Create_EmptyCurrency_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => TransactionRecord.Create(
            "123456", "0100", Guid.NewGuid(), 1000, "", ProcessorType.Interswitch));
    }

    [Fact]
    public void Approve_SetsStatusAndResponseCode()
    {
        var record = TransactionRecord.Create("123456", "0100", Guid.NewGuid(), 1000, "NGN", ProcessorType.CardFi);
        record.Approve("00", "ResponseTransmission");

        Assert.Equal(TransactionStatus.Approved, record.Status);
        Assert.Equal("00", record.ResponseCode);
        Assert.Equal("ResponseTransmission", record.PipelineStepReached);
    }

    [Fact]
    public void Decline_SetsStatusAndResponseCode()
    {
        var record = TransactionRecord.Create("123456", "0100", Guid.NewGuid(), 1000, "NGN", ProcessorType.CardFi);
        record.Decline("51", "BalanceCheck");

        Assert.Equal(TransactionStatus.Declined, record.Status);
        Assert.Equal("51", record.ResponseCode);
        Assert.Equal("BalanceCheck", record.PipelineStepReached);
    }

    [Fact]
    public void Reverse_SetsStatusToReversed()
    {
        var record = TransactionRecord.Create("123456", "0420", Guid.NewGuid(), 1000, "NGN", ProcessorType.Interswitch);
        record.Reverse("00");

        Assert.Equal(TransactionStatus.Reversed, record.Status);
        Assert.Equal("00", record.ResponseCode);
    }

    [Fact]
    public void Create_TimestampHasMillisecondPrecision()
    {
        var record = TransactionRecord.Create("123456", "0100", Guid.NewGuid(), 1000, "NGN", ProcessorType.Interswitch);
        Assert.Equal(0, record.CreatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }
}
