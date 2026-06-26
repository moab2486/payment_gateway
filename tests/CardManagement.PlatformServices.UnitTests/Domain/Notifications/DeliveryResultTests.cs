using CardManagement.Domain.PlatformServices.Notifications;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Notifications;

public class DeliveryResultTests
{
    [Fact]
    public void Succeeded_WithValidMessageId_ReturnsSuccessResult()
    {
        var result = DeliveryResult.Succeeded("msg-abc-123");

        Assert.True(result.Success);
        Assert.Equal("msg-abc-123", result.ProviderMessageId);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void Succeeded_WithEmptyMessageId_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeliveryResult.Succeeded(""));
    }

    [Fact]
    public void Failed_WithValidReason_ReturnsFailureResult()
    {
        var result = DeliveryResult.Failed("Connection timeout");

        Assert.False(result.Success);
        Assert.Null(result.ProviderMessageId);
        Assert.Equal("Connection timeout", result.FailureReason);
    }

    [Fact]
    public void Failed_WithEmptyReason_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeliveryResult.Failed(""));
    }

    [Fact]
    public void FailedWithMessageId_WithValidInputs_ReturnsFailureWithId()
    {
        var result = DeliveryResult.FailedWithMessageId("msg-123", "Bounced");

        Assert.False(result.Success);
        Assert.Equal("msg-123", result.ProviderMessageId);
        Assert.Equal("Bounced", result.FailureReason);
    }

    [Fact]
    public void FailedWithMessageId_WithEmptyMessageId_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeliveryResult.FailedWithMessageId("", "reason"));
    }

    [Fact]
    public void FailedWithMessageId_WithEmptyReason_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeliveryResult.FailedWithMessageId("msg-123", ""));
    }
}
