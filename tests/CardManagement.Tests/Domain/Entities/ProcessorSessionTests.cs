using CardManagement.Domain.Entities;
using CardManagement.Domain.ValueObjects;
using Xunit;

namespace CardManagement.Tests.Domain.Entities;

public class ProcessorSessionTests
{
    [Fact]
    public void Create_ValidInput_CreatesSessionNotSignedOn()
    {
        var session = ProcessorSession.Create(ProcessorType.Interswitch, "192.168.1.1:9000");

        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(ProcessorType.Interswitch, session.ProcessorType);
        Assert.Equal("192.168.1.1:9000", session.Endpoint);
        Assert.False(session.IsSignedOn);
        Assert.Null(session.LastSignOnUtc);
        Assert.Null(session.LastHeartbeatUtc);
    }

    [Fact]
    public void Create_EmptyEndpoint_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => ProcessorSession.Create(ProcessorType.CardFi, ""));
    }

    [Fact]
    public void SignOn_SetsIsSignedOnAndTimestamp()
    {
        var session = ProcessorSession.Create(ProcessorType.CardFi, "10.0.0.1:8080");
        session.SignOn();

        Assert.True(session.IsSignedOn);
        Assert.NotNull(session.LastSignOnUtc);
        Assert.Equal(DateTimeKind.Utc, session.LastSignOnUtc!.Value.Kind);
        Assert.Equal(0, session.LastSignOnUtc.Value.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [Fact]
    public void SignOff_SetsIsSignedOnToFalse()
    {
        var session = ProcessorSession.Create(ProcessorType.Interswitch, "192.168.1.1:9000");
        session.SignOn();
        session.SignOff();

        Assert.False(session.IsSignedOn);
    }

    [Fact]
    public void RecordHeartbeat_SetsLastHeartbeatUtc()
    {
        var session = ProcessorSession.Create(ProcessorType.Interswitch, "192.168.1.1:9000");
        session.RecordHeartbeat();

        Assert.NotNull(session.LastHeartbeatUtc);
        Assert.Equal(DateTimeKind.Utc, session.LastHeartbeatUtc!.Value.Kind);
        Assert.Equal(0, session.LastHeartbeatUtc.Value.Ticks % TimeSpan.TicksPerMillisecond);
    }
}
