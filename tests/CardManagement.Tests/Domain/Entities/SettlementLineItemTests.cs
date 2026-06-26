using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class SettlementLineItemTests
{
    private static readonly Money ValidAmount = new(50000, "NGN");
    private static readonly Guid ValidBatchId = Guid.NewGuid();

    [Fact]
    public void Create_ValidInput_CreatesItemWithUnmatchedStatus()
    {
        var item = SettlementLineItem.Create(
            ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15));

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Equal(ValidBatchId, item.BatchId);
        Assert.Equal("TXN-001", item.TransactionReference);
        Assert.Equal("PROC-001", item.ProcessorReference);
        Assert.Equal(ValidAmount, item.Amount);
        Assert.Equal("success", item.Status);
        Assert.Equal(new DateOnly(2024, 1, 15), item.TransactionDate);
        Assert.Equal(MatchStatus.Unmatched, item.MatchStatus);
        Assert.Null(item.MatchedPaymentRequestId);
    }

    [Fact]
    public void Create_EmptyBatchId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SettlementLineItem.Create(Guid.Empty, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15)));
    }

    [Fact]
    public void Create_EmptyTransactionReference_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SettlementLineItem.Create(ValidBatchId, "", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15)));
    }

    [Fact]
    public void Create_EmptyProcessorReference_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SettlementLineItem.Create(ValidBatchId, "TXN-001", "", ValidAmount, "success", new DateOnly(2024, 1, 15)));
    }

    [Fact]
    public void Create_NullAmount_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SettlementLineItem.Create(ValidBatchId, "TXN-001", "PROC-001", null!, "success", new DateOnly(2024, 1, 15)));
    }

    [Fact]
    public void Create_EmptyStatus_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SettlementLineItem.Create(ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "", new DateOnly(2024, 1, 15)));
    }

    [Fact]
    public void MarkMatched_FromUnmatched_TransitionsToMatched()
    {
        var item = SettlementLineItem.Create(
            ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15));
        var paymentRequestId = Guid.NewGuid();

        item.MarkMatched(paymentRequestId);

        Assert.Equal(MatchStatus.Matched, item.MatchStatus);
        Assert.Equal(paymentRequestId, item.MatchedPaymentRequestId);
    }

    [Fact]
    public void MarkMatched_EmptyPaymentRequestId_ThrowsArgumentException()
    {
        var item = SettlementLineItem.Create(
            ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15));

        Assert.Throws<ArgumentException>(() => item.MarkMatched(Guid.Empty));
    }

    [Fact]
    public void MarkMatched_AlreadyMatched_ThrowsInvalidOperationException()
    {
        var item = SettlementLineItem.Create(
            ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15));
        item.MarkMatched(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => item.MarkMatched(Guid.NewGuid()));
    }

    [Fact]
    public void MarkMismatched_FromUnmatched_TransitionsToMismatched()
    {
        var item = SettlementLineItem.Create(
            ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15));
        var paymentRequestId = Guid.NewGuid();

        item.MarkMismatched(paymentRequestId);

        Assert.Equal(MatchStatus.Mismatched, item.MatchStatus);
        Assert.Equal(paymentRequestId, item.MatchedPaymentRequestId);
    }

    [Fact]
    public void MarkMismatched_EmptyPaymentRequestId_ThrowsArgumentException()
    {
        var item = SettlementLineItem.Create(
            ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15));

        Assert.Throws<ArgumentException>(() => item.MarkMismatched(Guid.Empty));
    }

    [Fact]
    public void MarkMismatched_AlreadyMismatched_ThrowsInvalidOperationException()
    {
        var item = SettlementLineItem.Create(
            ValidBatchId, "TXN-001", "PROC-001", ValidAmount, "success", new DateOnly(2024, 1, 15));
        item.MarkMismatched(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => item.MarkMismatched(Guid.NewGuid()));
    }
}
