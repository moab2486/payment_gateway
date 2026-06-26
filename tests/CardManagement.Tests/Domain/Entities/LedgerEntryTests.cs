using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class LedgerEntryTests
{
    [Fact]
    public void Create_ValidInput_CreatesImmutableEntry()
    {
        var transactionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        var entry = LedgerEntry.Create(
            transactionId,
            accountId,
            EntryType.Debit,
            5000,
            "Authorization debit",
            "AUTH-001");

        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.Equal(transactionId, entry.TransactionId);
        Assert.Equal(accountId, entry.AccountId);
        Assert.Equal(EntryType.Debit, entry.EntryType);
        Assert.Equal(5000, entry.Amount);
        Assert.Equal("Authorization debit", entry.Description);
        Assert.Equal("AUTH-001", entry.OperationIdentifier);
        Assert.Equal(DateTimeKind.Utc, entry.CreatedAtUtc.Kind);
    }

    [Fact]
    public void Create_EmptyTransactionId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(
            Guid.Empty, Guid.NewGuid(), EntryType.Debit, 1000, "desc", "op-1"));
    }

    [Fact]
    public void Create_EmptyAccountId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(
            Guid.NewGuid(), Guid.Empty, EntryType.Debit, 1000, "desc", "op-1"));
    }

    [Fact]
    public void Create_ZeroAmount_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), EntryType.Debit, 0, "desc", "op-1"));
    }

    [Fact]
    public void Create_NegativeAmount_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), EntryType.Credit, -100, "desc", "op-1"));
    }

    [Fact]
    public void Create_EmptyDescription_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), EntryType.Debit, 1000, "", "op-1"));
    }

    [Fact]
    public void Create_EmptyOperationIdentifier_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => LedgerEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), EntryType.Debit, 1000, "desc", ""));
    }

    [Fact]
    public void Create_TimestampHasMillisecondPrecision()
    {
        var entry = LedgerEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), EntryType.Credit, 1000, "desc", "op-1");
        Assert.Equal(0, entry.CreatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public void Create_CreditEntryType_CreatesSuccessfully()
    {
        var entry = LedgerEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(), EntryType.Credit, 2500, "Payment received", "PAY-001");
        Assert.Equal(EntryType.Credit, entry.EntryType);
    }
}
