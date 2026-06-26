using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal;

/// <summary>
/// ASP.NET Core middleware that captures API request/response data for developer request logging.
/// Applies PCI redaction to request and response bodies before storage.
/// Only logs requests that have a resolved developer context (ApiKeyId and DeveloperId set in HttpContext.Items).
/// </summary>
public class RequestLoggingMiddleware
{
    /// <summary>
    /// HttpContext.Items key for the API key ID resolved from authentication.
    /// </summary>
    public const string ApiKeyIdItemKey = "DeveloperPortal.ApiKeyId";

    /// <summary>
    /// HttpContext.Items key for the developer ID resolved from authentication.
    /// </summary>
    public const string DeveloperIdItemKey = "DeveloperPortal.DeveloperId";

    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Start timing
        var stopwatch = Stopwatch.StartNew();

        // Enable request body buffering so we can read it after the pipeline executes
        context.Request.EnableBuffering();

        // Read request body
        var requestBody = await ReadRequestBodyAsync(context.Request).ConfigureAwait(false);

        // Capture request headers (with Authorization redacted)
        var requestHeaders = CaptureHeaders(context.Request.Headers);

        // Replace the response body stream so we can capture the response
        var originalResponseBody = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();

            // Read response body
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            var responseBody = await new StreamReader(responseBodyStream, Encoding.UTF8).ReadToEndAsync().ConfigureAwait(false);

            // Copy response body back to original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalResponseBody).ConfigureAwait(false);
            context.Response.Body = originalResponseBody;

            // Only log if developer context is available (meaning the request was authenticated via API key)
            if (TryGetDeveloperContext(context, out var apiKeyId, out var developerId))
            {
                await LogRequestAsync(
                    context,
                    apiKeyId,
                    developerId,
                    requestHeaders,
                    requestBody,
                    responseBody,
                    stopwatch.Elapsed).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength == 0 || request.Body == null)
            return null;

        request.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        request.Body.Seek(0, SeekOrigin.Begin);

        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    private static string CaptureHeaders(IHeaderDictionary headers)
    {
        var headerDict = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                headerDict[header.Key] = "***";
            }
            else
            {
                headerDict[header.Key] = header.Value.ToString();
            }
        }

        return JsonSerializer.Serialize(headerDict);
    }

    private static bool TryGetDeveloperContext(
        HttpContext context,
        out Guid apiKeyId,
        out Guid developerId)
    {
        apiKeyId = Guid.Empty;
        developerId = Guid.Empty;

        if (context.Items.TryGetValue(ApiKeyIdItemKey, out var apiKeyIdObj) &&
            context.Items.TryGetValue(DeveloperIdItemKey, out var developerIdObj))
        {
            if (apiKeyIdObj is Guid keyId && developerIdObj is Guid devId)
            {
                apiKeyId = keyId;
                developerId = devId;
                return apiKeyId != Guid.Empty && developerId != Guid.Empty;
            }
        }

        return false;
    }

    private async Task LogRequestAsync(
        HttpContext context,
        Guid apiKeyId,
        Guid developerId,
        string requestHeaders,
        string? requestBody,
        string? responseBody,
        TimeSpan latency)
    {
        try
        {
            // Apply PCI redaction before storage
            var redactedRequestBody = PciRedactionFilter.Redact(requestBody);
            var redactedResponseBody = PciRedactionFilter.Redact(responseBody);

            var endpoint = context.Request.Path.Value ?? string.Empty;
            var method = context.Request.Method;
            var responseStatus = context.Response.StatusCode;

            var logEntry = RequestLogEntry.Create(
                apiKeyId: apiKeyId,
                developerId: developerId,
                endpoint: endpoint,
                method: method,
                requestHeaders: requestHeaders,
                requestBody: string.IsNullOrEmpty(redactedRequestBody) ? null : redactedRequestBody,
                responseStatus: responseStatus,
                responseBody: string.IsNullOrEmpty(redactedResponseBody) ? null : redactedResponseBody,
                latency: latency);

            // Resolve the repository from the request's service scope
            var repository = context.RequestServices.GetService<IRequestLogRepository>();
            if (repository != null)
            {
                await repository.CreateAsync(logEntry, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("IRequestLogRepository is not registered. Request log entry will not be persisted.");
            }
        }
        catch (Exception ex)
        {
            // Log storage failures should not break the request pipeline
            _logger.LogError(ex, "Failed to persist request log entry for API key {ApiKeyId}.", apiKeyId);
        }
    }
}
