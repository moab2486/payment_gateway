using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

/// <summary>
/// Unit tests for ApiKeyValidationMiddleware.
/// Validates: Requirements 15.1, 15.2, 15.3, 15.4, 15.5
/// </summary>
public class ApiKeyValidationMiddlewareTests
{
    private readonly InMemoryApiKeyCacheService _cacheService;
    private readonly InMemoryApiKeyRepository _repository;
    private readonly ApiKeyValidationOptions _options;
    private bool _nextCalled;

    public ApiKeyValidationMiddlewareTests()
    {
        _cacheService = new InMemoryApiKeyCacheService();
        _repository = new InMemoryApiKeyRepository();
        _options = new ApiKeyValidationOptions
        {
            CacheTtl = TimeSpan.FromMinutes(5),
            ExcludedPaths = ["/health", "/swagger"]
        };
        _nextCalled = false;
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenApiKeyHeaderMissing()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: null);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenApiKeyHeaderEmpty()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: "");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenKeyNotFoundInCacheOrDb()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: "invalid-key-12345678");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_AuthorizesFromCache_WhenCacheHitWithActiveKey()
    {
        // Arrange
        var rawKey = "test-api-key-active-12345678";
        var hash = ApiKey.ComputeHash(rawKey);
        var developerId = Guid.NewGuid();
        var keyId = Guid.NewGuid();
        var scopes = new[] { "payments:read", "payments:write" };

        _cacheService.Store[hash] = new CachedKeyInfo(keyId, developerId, scopes, KeyStatus.Active, false, null);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(keyId, context.Items[RequestLoggingMiddleware.ApiKeyIdItemKey]);
        Assert.Equal(developerId, context.Items[RequestLoggingMiddleware.DeveloperIdItemKey]);
        Assert.Equal(scopes, context.Items[ApiKeyValidationMiddleware.ScopesItemKey]);
        Assert.Equal(false, context.Items[ApiKeyValidationMiddleware.IsSandboxItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenCacheHitWithRevokedKey()
    {
        // Arrange
        var rawKey = "test-api-key-revoked-12345678";
        var hash = ApiKey.ComputeHash(rawKey);
        var developerId = Guid.NewGuid();
        var keyId = Guid.NewGuid();

        _cacheService.Store[hash] = new CachedKeyInfo(keyId, developerId, new[] { "read" }, KeyStatus.Revoked, false, null);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenCacheHitWithExpiredKey()
    {
        // Arrange
        var rawKey = "test-api-key-expired-12345678";
        var hash = ApiKey.ComputeHash(rawKey);
        var developerId = Guid.NewGuid();
        var keyId = Guid.NewGuid();

        _cacheService.Store[hash] = new CachedKeyInfo(keyId, developerId, new[] { "read" }, KeyStatus.Expired, false, null);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenCacheHitWithActiveKeyPastExpiry()
    {
        // Arrange
        var rawKey = "test-api-key-pastexp-12345678";
        var hash = ApiKey.ComputeHash(rawKey);
        var developerId = Guid.NewGuid();
        var keyId = Guid.NewGuid();

        // Active key but with ExpiresAtUtc in the past
        _cacheService.Store[hash] = new CachedKeyInfo(
            keyId, developerId, new[] { "read" }, KeyStatus.Active, false,
            DateTime.UtcNow.AddMinutes(-10));

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_AuthorizesFromDb_WhenCacheMiss_AndPopulatesCache()
    {
        // Arrange
        var rawKey = "test-db-lookup-key-12345678";
        var apiKey = ApiKey.CreateFromRawKey(
            Guid.NewGuid(), rawKey, new[] { "payments:read" }, isSandbox: true, expiresAtUtc: null);
        _repository.Keys.Add(apiKey);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(apiKey.Id, context.Items[RequestLoggingMiddleware.ApiKeyIdItemKey]);
        Assert.Equal(apiKey.DeveloperId, context.Items[RequestLoggingMiddleware.DeveloperIdItemKey]);
        Assert.Equal(new[] { "payments:read" }, context.Items[ApiKeyValidationMiddleware.ScopesItemKey]);
        Assert.Equal(true, context.Items[ApiKeyValidationMiddleware.IsSandboxItemKey]);

        // Verify cache was populated
        var hash = ApiKey.ComputeHash(rawKey);
        Assert.True(_cacheService.Store.ContainsKey(hash));
        var cached = _cacheService.Store[hash];
        Assert.Equal(apiKey.Id, cached.KeyId);
        Assert.Equal(apiKey.DeveloperId, cached.DeveloperId);
    }

    [Fact]
    public async Task InvokeAsync_Returns401_WhenDbKeyIsRevoked()
    {
        // Arrange
        var rawKey = "test-db-revoked-key-12345678";
        var apiKey = ApiKey.CreateFromRawKey(
            Guid.NewGuid(), rawKey, new[] { "payments:read" }, isSandbox: false, expiresAtUtc: null);
        apiKey.Revoke();
        _repository.Keys.Add(apiKey);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_SkipsValidation_ForExcludedPaths()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: null, path: "/health");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_SkipsValidation_ForSwaggerPaths()
    {
        // Arrange
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: null, path: "/swagger/v1/swagger.json");

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_AllowsRotatedKey_WithinGracePeriod()
    {
        // Arrange
        var rawKey = "test-rotated-key-12345678ab";
        var hash = ApiKey.ComputeHash(rawKey);
        var developerId = Guid.NewGuid();
        var keyId = Guid.NewGuid();

        // Rotated key is valid in cache (assumed within grace period)
        _cacheService.Store[hash] = new CachedKeyInfo(
            keyId, developerId, new[] { "read" }, KeyStatus.Rotated, false, null);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(keyId, context.Items[RequestLoggingMiddleware.ApiKeyIdItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_WorksWithoutCacheService_FallsBackToDbOnly()
    {
        // Arrange: no cache service registered
        var rawKey = "test-nocache-key-12345678ab";
        var apiKey = ApiKey.CreateFromRawKey(
            Guid.NewGuid(), rawKey, new[] { "payments:write" }, isSandbox: false, expiresAtUtc: null);
        _repository.Keys.Add(apiKey);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey, registerCacheService: false);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(apiKey.Id, context.Items[RequestLoggingMiddleware.ApiKeyIdItemKey]);
    }

    [Fact]
    public async Task InvokeAsync_AttachesSandboxFlag_WhenKeyIsSandbox()
    {
        // Arrange
        var rawKey = "test-sandbox-key-12345678ab";
        var hash = ApiKey.ComputeHash(rawKey);
        var developerId = Guid.NewGuid();
        var keyId = Guid.NewGuid();

        _cacheService.Store[hash] = new CachedKeyInfo(
            keyId, developerId, new[] { "sandbox:all" }, KeyStatus.Active, true, null);

        var middleware = CreateMiddleware();
        var context = CreateHttpContext(apiKey: rawKey);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(_nextCalled);
        Assert.Equal(true, context.Items[ApiKeyValidationMiddleware.IsSandboxItemKey]);
    }

    #region Helpers

    private ApiKeyValidationMiddleware CreateMiddleware()
    {
        return new ApiKeyValidationMiddleware(
            context =>
            {
                _nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<ApiKeyValidationMiddleware>.Instance,
            Options.Create(_options));
    }

    private HttpContext CreateHttpContext(
        string? apiKey,
        string path = "/api/v1/payments",
        bool registerCacheService = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (apiKey != null)
        {
            context.Request.Headers["X-Api-Key"] = apiKey;
        }

        var services = new ServiceCollection();
        if (registerCacheService)
        {
            services.AddSingleton<IApiKeyCacheService>(_cacheService);
        }
        services.AddSingleton<IApiKeyRepository>(_repository);
        context.RequestServices = services.BuildServiceProvider();

        return context;
    }

    #endregion
}

/// <summary>
/// In-memory IApiKeyCacheService for testing.
/// </summary>
internal class InMemoryApiKeyCacheService : IApiKeyCacheService
{
    public Dictionary<string, CachedKeyInfo> Store { get; } = new();

    public Task<CachedKeyInfo?> GetAsync(string apiKeyHash, CancellationToken ct)
    {
        Store.TryGetValue(apiKeyHash, out var info);
        return Task.FromResult(info);
    }

    public Task SetAsync(string apiKeyHash, CachedKeyInfo info, TimeSpan ttl, CancellationToken ct)
    {
        Store[apiKeyHash] = info;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string apiKeyHash, CancellationToken ct)
    {
        Store.Remove(apiKeyHash);
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory IApiKeyRepository for testing.
/// </summary>
internal class InMemoryApiKeyRepository : IApiKeyRepository
{
    public List<ApiKey> Keys { get; } = new();

    public Task<ApiKey> CreateAsync(ApiKey key, CancellationToken ct)
    {
        Keys.Add(key);
        return Task.FromResult(key);
    }

    public Task<ApiKey?> GetByIdAsync(Guid keyId, CancellationToken ct)
    {
        return Task.FromResult(Keys.FirstOrDefault(k => k.Id == keyId));
    }

    public Task<ApiKey?> GetByHashAsync(string keyHash, CancellationToken ct)
    {
        return Task.FromResult(Keys.FirstOrDefault(k => k.KeyHash == keyHash));
    }

    public Task<int> CountActiveByDeveloperAsync(Guid developerId, CancellationToken ct)
    {
        return Task.FromResult(Keys.Count(k => k.DeveloperId == developerId && k.Status == KeyStatus.Active));
    }

    public Task UpdateAsync(ApiKey key, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ApiKey>> GetByDeveloperAsync(Guid developerId, CancellationToken ct)
    {
        var result = Keys.Where(k => k.DeveloperId == developerId).ToList();
        return Task.FromResult<IReadOnlyList<ApiKey>>(result);
    }
}
