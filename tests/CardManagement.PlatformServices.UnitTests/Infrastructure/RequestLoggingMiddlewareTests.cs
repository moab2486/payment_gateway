using System.Text;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for RequestLoggingMiddleware.
/// Validates: Requirements 17.1, 17.5
/// </summary>
public class RequestLoggingMiddlewareTests
{
    private readonly InMemoryRequestLogRepository _repository;
    private readonly RequestLoggingMiddleware _middleware;

    public RequestLoggingMiddlewareTests()
    {
        _repository = new InMemoryRequestLogRepository();
        _middleware = new RequestLoggingMiddleware(
            context => Task.CompletedTask,
            NullLogger<RequestLoggingMiddleware>.Instance);
    }

    [Fact]
    public async Task InvokeAsync_LogsRequest_WhenDeveloperContextIsSet()
    {
        // Arrange
        var context = CreateHttpContext(
            method: "POST",
            path: "/api/v1/payments",
            requestBody: """{"amount":5000,"currency":"NGN"}""",
            apiKeyId: Guid.NewGuid(),
            developerId: Guid.NewGuid());

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Single(_repository.Entries);
        var entry = _repository.Entries[0];
        Assert.Equal("/api/v1/payments", entry.Endpoint);
        Assert.Equal("POST", entry.Method);
        Assert.True(entry.Latency >= TimeSpan.Zero);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotLog_WhenNoDeveloperContext()
    {
        // Arrange - no developer context set in HttpContext.Items
        var context = CreateHttpContext(
            method: "GET",
            path: "/api/v1/health",
            requestBody: null,
            apiKeyId: null,
            developerId: null);

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Empty(_repository.Entries);
    }

    [Fact]
    public async Task InvokeAsync_RedactsAuthorization_InHeaders()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var developerId = Guid.NewGuid();
        var context = CreateHttpContext(
            method: "GET",
            path: "/api/v1/payments",
            requestBody: null,
            apiKeyId: apiKeyId,
            developerId: developerId);
        context.Request.Headers["Authorization"] = "Bearer sk_live_abc123secret";

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Single(_repository.Entries);
        var entry = _repository.Entries[0];
        Assert.NotNull(entry.RequestHeaders);
        Assert.DoesNotContain("sk_live_abc123secret", entry.RequestHeaders);
        Assert.Contains("***", entry.RequestHeaders);
    }

    [Fact]
    public async Task InvokeAsync_RedactsPciData_InRequestBody()
    {
        // Arrange
        var body = """{"cardNumber":"4111111111111111","cvv":"123","amount":5000}""";
        var context = CreateHttpContext(
            method: "POST",
            path: "/api/v1/payments",
            requestBody: body,
            apiKeyId: Guid.NewGuid(),
            developerId: Guid.NewGuid());

        // Act
        await _middleware.InvokeAsync(context);

        // Assert
        Assert.Single(_repository.Entries);
        var entry = _repository.Entries[0];
        Assert.NotNull(entry.RequestBody);
        Assert.DoesNotContain("4111111111111111", entry.RequestBody);
        Assert.DoesNotContain("\"cvv\":\"123\"", entry.RequestBody);
        Assert.Contains("************1111", entry.RequestBody);
        Assert.Contains("\"cvv\":\"***\"", entry.RequestBody);
    }

    [Fact]
    public async Task InvokeAsync_CapturesResponseStatus()
    {
        // Arrange
        var apiKeyId = Guid.NewGuid();
        var developerId = Guid.NewGuid();

        var middleware = new RequestLoggingMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = 201;
                return Task.CompletedTask;
            },
            NullLogger<RequestLoggingMiddleware>.Instance);

        var context = CreateHttpContext(
            method: "POST",
            path: "/api/v1/payments",
            requestBody: """{"amount":5000}""",
            apiKeyId: apiKeyId,
            developerId: developerId);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Single(_repository.Entries);
        Assert.Equal(201, _repository.Entries[0].ResponseStatus);
    }

    [Fact]
    public async Task InvokeAsync_MeasuresLatency()
    {
        // Arrange
        var middleware = new RequestLoggingMiddleware(
            async ctx =>
            {
                await Task.Delay(10);
            },
            NullLogger<RequestLoggingMiddleware>.Instance);

        var context = CreateHttpContext(
            method: "GET",
            path: "/api/v1/status",
            requestBody: null,
            apiKeyId: Guid.NewGuid(),
            developerId: Guid.NewGuid());

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Single(_repository.Entries);
        Assert.True(_repository.Entries[0].Latency >= TimeSpan.FromMilliseconds(10));
    }

    #region Helpers

    private HttpContext CreateHttpContext(
        string method,
        string path,
        string? requestBody,
        Guid? apiKeyId,
        Guid? developerId)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        if (requestBody != null)
        {
            var bytes = Encoding.UTF8.GetBytes(requestBody);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
        }
        else
        {
            context.Request.Body = new MemoryStream();
            context.Request.ContentLength = 0;
        }

        // Set response body to a writable stream
        context.Response.Body = new MemoryStream();

        if (apiKeyId.HasValue && developerId.HasValue)
        {
            context.Items[RequestLoggingMiddleware.ApiKeyIdItemKey] = apiKeyId.Value;
            context.Items[RequestLoggingMiddleware.DeveloperIdItemKey] = developerId.Value;
        }

        // Register the repository in services
        var services = new ServiceCollection();
        services.AddSingleton<IRequestLogRepository>(_repository);
        context.RequestServices = services.BuildServiceProvider();

        return context;
    }

    #endregion
}

/// <summary>
/// In-memory implementation of IRequestLogRepository for testing.
/// </summary>
internal class InMemoryRequestLogRepository : IRequestLogRepository
{
    public List<RequestLogEntry> Entries { get; } = new();

    public Task CreateAsync(RequestLogEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<PagedResult<RequestLogEntry>> QueryAsync(RequestLogQuery query, CancellationToken ct)
    {
        var filtered = RequestLogQueryFilter.Apply(Entries, query);
        return Task.FromResult(new PagedResult<RequestLogEntry>(filtered, filtered.Count, query.Page, query.PageSize));
    }

    public Task PurgeExpiredAsync(TimeSpan retention, CancellationToken ct) => Task.CompletedTask;
}
