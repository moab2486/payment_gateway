using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class ReconciliationExceptionTests
{
    private static readonly Guid ValidBatchId = Guid.NewGuid();
    private static readonly Guid ValidLineItemId = Guid.NewGuid();
    private static readonly Guid ValidPaymentRequestId = Guid.NewGuid();
    private static readonly Money ExternalAmount = new(50000, "NGN");
    private static readonly Money InternalAmount = new(49500, "NGN");

    [Fact]
    public void CreateMismatch_ValidInput_CreatesExceptionWithMismatchType()
    {
        var exception = ReconciliationException.CreateMismatch(
            ValidBatchId, ValidLineItemId, ValidPaymentRequestId,
            ExternalAmount, InternalAmount, "settled", "completed");

        Assert.NotEqual(Guid.Empty, exception.Id);
        Assert.Equal(ValidBatchId, exception.BatchId);
        Assert.Equal(ExceptionType.Mismatch, exception.Type);
        Assert.Equal(ValidLineItemId, exception.SettlementLineItemId);
        Assert.Equal(ValidPaymentRequestId, exception.PaymentRequestId);
        Assert.Equal(ExternalAmount, exception.ExternalAmount);
        Assert.Equal(InternalAmount, exception.InternalAmount);
        Assert.Equal("settled", exception.ExternalStatus);
        Assert.Equal("completed", exception.InternalStatus);
        Assert.Equal(ExceptionResolutionStatus.Pending, exception.ResolutionStatus);
        Assert.Null(exception.AdjustmentId);
    }

    [Fact]
    public void CreateMismatch_EmptyBatchId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconciliationException.CreateMismatch(
                Guid.Empty, ValidLineItemId, ValidPaymentRequestId,
                ExternalAmount, InternalAmount, null, null));
    }

    [Fact]
    public void CreateMismatch_EmptyLineItemId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconciliationException.CreateMismatch(
                ValidBatchId, Guid.Empty, ValidPaymentRequestId,
                ExternalAmount, InternalAmount, null, null));
    }

    [Fact]
    public void CreateMismatch_NullExternalAmount_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ReconciliationException.CreateMismatch(
                ValidBatchId, ValidLineItemId, ValidPaymentRequestId,
                null!, InternalAmount, null, null));
    }

    [Fact]
    public void CreateUnmatchedExternal_ValidInput_CreatesCorrectType()
    {
        var exception = ReconciliationException.CreateUnmatchedExternal(
            ValidBatchId, ValidLineItemId, ExternalAmount, "settled");

        Assert.Equal(ExceptionType.UnmatchedExternal, exception.Type);
        Assert.Equal(ValidLineItemId, exception.SettlementLineItemId);
        Assert.Null(exception.PaymentRequestId);
        Assert.Equal(ExternalAmount, exception.ExternalAmount);
        Assert.Null(exception.InternalAmount);
        Assert.Equal("settled", exception.ExternalStatus);
        Assert.Null(exception.InternalStatus);
        Assert.Equal(ExceptionResolutionStatus.Pending, exception.ResolutionStatus);
    }

    [Fact]
    public void CreateUnmatchedExternal_EmptyLineItemId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconciliationException.CreateUnmatchedExternal(
                ValidBatchId, Guid.Empty, ExternalAmount, null));
    }

    [Fact]
    public void CreateUnmatchedInternal_ValidInput_CreatesCorrectType()
    {
        var exception = ReconciliationException.CreateUnmatchedInternal(
            ValidBatchId, ValidPaymentRequestId, InternalAmount, "completed");

        Assert.Equal(ExceptionType.UnmatchedInternal, exception.Type);
        Assert.Null(exception.SettlementLineItemId);
        Assert.Equal(ValidPaymentRequestId, exception.PaymentRequestId);
        Assert.Null(exception.ExternalAmount);
        Assert.Equal(InternalAmount, exception.InternalAmount);
        Assert.Null(exception.ExternalStatus);
        Assert.Equal("completed", exception.InternalStatus);
        Assert.Equal(ExceptionResolutionStatus.Pending, exception.ResolutionStatus);
    }

    [Fact]
    public void CreateUnmatchedInternal_EmptyPaymentRequestId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconciliationException.CreateUnmatchedInternal(
                ValidBatchId, Guid.Empty, InternalAmount, null));
    }

    [Fact]
    public void ResolveAutomatically_FromPending_TransitionsToAutoResolved()
    {
        var exception = ReconciliationException.CreateMismatch(
            ValidBatchId, ValidLineItemId, ValidPaymentRequestId,
            ExternalAmount, InternalAmount, null, null);
        var adjustmentId = Guid.NewGuid();

        exception.ResolveAutomatically(adjustmentId);

        Assert.Equal(ExceptionResolutionStatus.AutoResolved, exception.ResolutionStatus);
        Assert.Equal(adjustmentId, exception.AdjustmentId);
    }

    [Fact]
    public void ResolveAutomatically_EmptyAdjustmentId_ThrowsArgumentException()
    {
        var exception = ReconciliationException.CreateMismatch(
            ValidBatchId, ValidLineItemId, ValidPaymentRequestId,
            ExternalAmount, InternalAmount, null, null);

        Assert.Throws<ArgumentException>(() => exception.ResolveAutomatically(Guid.Empty));
    }

    [Fact]
    public void ResolveAutomatically_AlreadyResolved_ThrowsInvalidOperationException()
    {
        var exception = ReconciliationException.CreateMismatch(
            ValidBatchId, ValidLineItemId, ValidPaymentRequestId,
            ExternalAmount, InternalAmount, null, null);
        exception.ResolveAutomatically(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => exception.ResolveAutomatically(Guid.NewGuid()));
    }

    [Fact]
    public void ResolveManually_FromPending_TransitionsToManualResolved()
    {
        var exception = ReconciliationException.CreateUnmatchedExternal(
            ValidBatchId, ValidLineItemId, ExternalAmount, null);
        var adjustmentId = Guid.NewGuid();

        exception.ResolveManually(adjustmentId);

        Assert.Equal(ExceptionResolutionStatus.ManualResolved, exception.ResolutionStatus);
        Assert.Equal(adjustmentId, exception.AdjustmentId);
    }

    [Fact]
    public void ResolveManually_AlreadyExpired_ThrowsInvalidOperationException()
    {
        var exception = ReconciliationException.CreateUnmatchedInternal(
            ValidBatchId, ValidPaymentRequestId, InternalAmount, null);
        exception.MarkExpired();

        Assert.Throws<InvalidOperationException>(() => exception.ResolveManually(Guid.NewGuid()));
    }

    [Fact]
    public void MarkExpired_FromPending_TransitionsToExpired()
    {
        var exception = ReconciliationException.CreateUnmatchedExternal(
            ValidBatchId, ValidLineItemId, ExternalAmount, null);

        exception.MarkExpired();

        Assert.Equal(ExceptionResolutionStatus.Expired, exception.ResolutionStatus);
    }

    [Fact]
    public void MarkExpired_AlreadyResolved_ThrowsInvalidOperationException()
    {
        var exception = ReconciliationException.CreateMismatch(
            ValidBatchId, ValidLineItemId, ValidPaymentRequestId,
            ExternalAmount, InternalAmount, null, null);
        exception.ResolveAutomatically(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => exception.MarkExpired());
    }
}
