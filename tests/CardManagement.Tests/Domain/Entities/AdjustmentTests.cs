using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class AdjustmentTests
{
    private static readonly Guid ValidExceptionId = Guid.NewGuid();
    private static readonly Money ValidAmount = new(5000, "NGN");

    [Fact]
    public void CreateAutomatic_ValidInput_CreatesAutoRuleAdjustment()
    {
        var adjustment = Adjustment.CreateAutomatic(
            ValidExceptionId, ValidAmount, "Amount below threshold", "SmallDifference");

        Assert.NotEqual(Guid.Empty, adjustment.Id);
        Assert.Equal(ValidExceptionId, adjustment.ExceptionId);
        Assert.Equal(AdjustmentType.AutoRule, adjustment.Type);
        Assert.Equal(ValidAmount, adjustment.Amount);
        Assert.Equal("Amount below threshold", adjustment.Reason);
        Assert.Equal("SmallDifference", adjustment.RuleName);
        Assert.Equal("system", adjustment.OperatorId);
    }

    [Fact]
    public void CreateAutomatic_EmptyExceptionId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Adjustment.CreateAutomatic(Guid.Empty, ValidAmount, "reason", "rule"));
    }

    [Fact]
    public void CreateAutomatic_NullAmount_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Adjustment.CreateAutomatic(ValidExceptionId, null!, "reason", "rule"));
    }

    [Fact]
    public void CreateAutomatic_EmptyReason_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Adjustment.CreateAutomatic(ValidExceptionId, ValidAmount, "", "rule"));
    }

    [Fact]
    public void CreateAutomatic_EmptyRuleName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Adjustment.CreateAutomatic(ValidExceptionId, ValidAmount, "reason", ""));
    }

    [Fact]
    public void CreateManual_ValidInput_CreatesManualOperatorAdjustment()
    {
        var adjustment = Adjustment.CreateManual(
            ValidExceptionId, ValidAmount, "Operator verified mismatch", "op-123");

        Assert.NotEqual(Guid.Empty, adjustment.Id);
        Assert.Equal(ValidExceptionId, adjustment.ExceptionId);
        Assert.Equal(AdjustmentType.ManualOperator, adjustment.Type);
        Assert.Equal(ValidAmount, adjustment.Amount);
        Assert.Equal("Operator verified mismatch", adjustment.Reason);
        Assert.Null(adjustment.RuleName);
        Assert.Equal("op-123", adjustment.OperatorId);
    }

    [Fact]
    public void CreateManual_EmptyExceptionId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Adjustment.CreateManual(Guid.Empty, ValidAmount, "reason", "op-123"));
    }

    [Fact]
    public void CreateManual_NullAmount_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Adjustment.CreateManual(ValidExceptionId, null!, "reason", "op-123"));
    }

    [Fact]
    public void CreateManual_EmptyReason_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Adjustment.CreateManual(ValidExceptionId, ValidAmount, "", "op-123"));
    }

    [Fact]
    public void CreateManual_EmptyOperatorId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Adjustment.CreateManual(ValidExceptionId, ValidAmount, "reason", ""));
    }
}
