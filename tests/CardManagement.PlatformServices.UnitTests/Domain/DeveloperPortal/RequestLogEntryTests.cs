using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.DeveloperPortal;

public class RequestLogEntryTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsEntry()
    {
        var apiKeyId = Guid.NewGuid();
        var developerId = Guid.NewGuid();

        var entry = RequestLogEntry.Create(
            apiKeyId,
            developerId,
            "/api/v1/payments",
            "POST",
            """{"Content-Type": "application/json"}""",
            """{"amount": 5000}""",
            200,
            """{"status": "success"}""",
            TimeSpan.FromMilliseconds(42));

        Assert.Equal(apiKeyId, entry.ApiKeyId);
        Assert.Equal(developerId, entry.DeveloperId);
        Assert.Equal("/api/v1/payments", entry.Endpoint);
        Assert.Equal("POST", entry.Method);
        Assert.Equal(200, entry.ResponseStatus);
        Assert.Equal(TimeSpan.FromMilliseconds(42), entry.Latency);
        Assert.NotEqual(Guid.Empty, entry.Id);
        Assert.True(entry.TimestampUtc <= DateTime.UtcNow);
    }

    [Fact]
    public void Create_NormalizesMethodToUpperCase()
    {
        var entry = RequestLogEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            "/api/v1/payments", "post",
            null, null, 200, null,
            TimeSpan.FromMilliseconds(10));

        Assert.Equal("POST", entry.Method);
    }

    [Fact]
    public void Create_WithNullBodies_Succeeds()
    {
        var entry = RequestLogEntry.Create(
            Guid.NewGuid(), Guid.NewGuid(),
            "/api/v1/payments", "GET",
            null, null, 204, null,
            TimeSpan.FromMilliseconds(5));

        Assert.Null(entry.RequestHeaders);
        Assert.Null(entry.RequestBody);
        Assert.Null(entry.ResponseBody);
    }

    [Fact]
    public void Create_WithEmptyApiKeyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RequestLogEntry.Create(
                Guid.Empty, Guid.NewGuid(),
                "/api/v1/payments", "GET",
                null, null, 200, null,
                TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public void Create_WithEmptyDeveloperId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RequestLogEntry.Create(
                Guid.NewGuid(), Guid.Empty,
                "/api/v1/payments", "GET",
                null, null, 200, null,
                TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public void Create_WithEmptyEndpoint_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RequestLogEntry.Create(
                Guid.NewGuid(), Guid.NewGuid(),
                "", "GET",
                null, null, 200, null,
                TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public void Create_WithEmptyMethod_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RequestLogEntry.Create(
                Guid.NewGuid(), Guid.NewGuid(),
                "/api/v1/payments", "",
                null, null, 200, null,
                TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public void Create_WithInvalidStatusCode_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RequestLogEntry.Create(
                Guid.NewGuid(), Guid.NewGuid(),
                "/api/v1/payments", "GET",
                null, null, 99, null,
                TimeSpan.FromMilliseconds(5)));

        Assert.Throws<ArgumentException>(() =>
            RequestLogEntry.Create(
                Guid.NewGuid(), Guid.NewGuid(),
                "/api/v1/payments", "GET",
                null, null, 600, null,
                TimeSpan.FromMilliseconds(5)));
    }

    [Fact]
    public void Create_WithNegativeLatency_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RequestLogEntry.Create(
                Guid.NewGuid(), Guid.NewGuid(),
                "/api/v1/payments", "GET",
                null, null, 200, null,
                TimeSpan.FromMilliseconds(-1)));
    }
}
