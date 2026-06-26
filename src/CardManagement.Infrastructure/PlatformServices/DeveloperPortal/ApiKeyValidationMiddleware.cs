using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.DeveloperPortal;

/// <summary>
/// ASP.NET Core middleware that validates API keys on incoming requests.
/// Extracts the API key from the X-Api-Key header, computes its SHA-256 hash,
/// and validates it using a Redis cache-first strategy with DB fallback.
/// 
/// On successful validation, attaches developer context to HttpContext.Items:
/// - DeveloperPortal.ApiKeyId (Guid)
/// - DeveloperPortal.DeveloperId (Guid)
/// - DeveloperPortal.Scopes (string[])
/// - DeveloperPortal.IsSandbox (bool)
/// 
/// Returns 401 Unauthorized for missing, invalid, expired, or revoked keys.
/// Skips validation for configured excluded paths (health checks, swagger, etc.).
/// </summary>
public class ApiKeyValidationMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    /// <summary>
    /// HttpContext.Items key for the resolved scopes.
    /// </summary>
    public const string ScopesItemKey = "DeveloperPortal.Scopes";

    /// <summary>
    /// HttpContext.Items key for the sandbox flag.
    /// </summary>
    public const string IsSandboxItemKey = "DeveloperPortal.IsSandbox";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyValidationMiddleware> _logger;
    private readonly ApiKeyValidationOptions _options;

    public ApiKeyValidationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyValidationMiddleware> logger,
        IOptions<ApiKeyValidationOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip validation entirely if disabled (e.g., in test environments)
        if (!_options.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Skip validation for excluded paths
        if (ShouldSkipValidation(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Extract API key from header
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValues)
            || string.IsNullOrWhiteSpace(apiKeyValues.FirstOrDefault()))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Missing API key. Provide a valid key via the X-Api-Key header." }).ConfigureAwait(false);
            return;
        }

        var rawApiKey = apiKeyValues.First()!;
        var keyHash = ComputeSha256Hash(rawApiKey);

        // Resolve services from the request scope
        var cacheService = context.RequestServices.GetService<IApiKeyCacheService>();
        var keyRepository = context.RequestServices.GetRequiredService<IApiKeyRepository>();

        // Attempt cache-first validation
        var cachedInfo = cacheService != null
            ? await cacheService.GetAsync(keyHash, context.RequestAborted).ConfigureAwait(false)
            : null;

        if (cachedInfo != null)
        {
            // Cache hit — validate status and expiry
            if (!IsValidCachedKey(cachedInfo))
            {
                _logger.LogInformation("API key validation failed (cache hit). KeyId={KeyId}, Status={Status}",
                    cachedInfo.KeyId, cachedInfo.Status);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired API key." }).ConfigureAwait(false);
                return;
            }

            // Attach developer context from cache
            AttachDeveloperContext(context, cachedInfo.KeyId, cachedInfo.DeveloperId, cachedInfo.Scopes, cachedInfo.IsSandbox);
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Cache miss — query DB
        var apiKey = await keyRepository.GetByHashAsync(keyHash, context.RequestAborted).ConfigureAwait(false);

        if (apiKey == null || !apiKey.IsValid())
        {
            _logger.LogInformation("API key validation failed (DB lookup). Hash prefix={HashPrefix}, Found={Found}",
                keyHash[..Math.Min(8, keyHash.Length)], apiKey != null);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired API key." }).ConfigureAwait(false);
            return;
        }

        // Populate cache for subsequent requests
        if (cacheService != null)
        {
            var cacheEntry = new CachedKeyInfo(
                apiKey.Id,
                apiKey.DeveloperId,
                apiKey.Scopes,
                apiKey.Status,
                apiKey.IsSandbox,
                apiKey.ExpiresAtUtc);

            await cacheService.SetAsync(keyHash, cacheEntry, _options.CacheTtl, context.RequestAborted).ConfigureAwait(false);
        }

        // Attach developer context from DB entity
        AttachDeveloperContext(context, apiKey.Id, apiKey.DeveloperId, apiKey.Scopes, apiKey.IsSandbox);
        await _next(context).ConfigureAwait(false);
    }

    private bool ShouldSkipValidation(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
            return false;

        foreach (var excludedPath in _options.ExcludedPaths)
        {
            if (path.StartsWith(excludedPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsValidCachedKey(CachedKeyInfo info)
    {
        var now = DateTime.UtcNow;

        if (info.Status == KeyStatus.Active)
        {
            if (info.ExpiresAtUtc.HasValue && now >= info.ExpiresAtUtc.Value)
                return false;
            return true;
        }

        if (info.Status == KeyStatus.Rotated)
        {
            // Rotated keys are valid during grace period. Since CachedKeyInfo
            // doesn't include GracePeriodEndsAtUtc, a rotated key in cache is
            // assumed valid (it would have been invalidated if grace expired).
            // The TTL ensures stale entries expire.
            return true;
        }

        // Expired and Revoked keys are never valid
        return false;
    }

    private static void AttachDeveloperContext(
        HttpContext context,
        Guid apiKeyId,
        Guid developerId,
        string[] scopes,
        bool isSandbox)
    {
        context.Items[RequestLoggingMiddleware.ApiKeyIdItemKey] = apiKeyId;
        context.Items[RequestLoggingMiddleware.DeveloperIdItemKey] = developerId;
        context.Items[ScopesItemKey] = scopes;
        context.Items[IsSandboxItemKey] = isSandbox;
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
