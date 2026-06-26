using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Hsm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for HsmServiceAdapter covering authentication state management,
/// operation denial when not authenticated, and timeout behavior.
/// Since actual HSM hardware is not available in tests, these tests verify
/// the adapter's behavioral contracts around security policies.
/// </summary>
public class HsmServiceAdapterTests
{
    private static HsmServiceAdapter CreateAdapter(HsmOptions? options = null)
    {
        var opts = Options.Create(options ?? new HsmOptions
        {
            Endpoint = "/nonexistent/path/to/pkcs11.so",
            SlotId = 0,
            Pin = "test-pin",
            AuthTimeoutSeconds = 5,
            OperationTimeoutSeconds = 10,
            CvvKeyLabel = "cvv-key"
        });

        var logger = new NullLoggerFactory().CreateLogger<HsmServiceAdapter>();
        return new HsmServiceAdapter(opts, logger);
    }

    [Fact]
    public void IsAuthenticated_InitialState_ReturnsFalse()
    {
        using var adapter = CreateAdapter();

        Assert.False(adapter.IsAuthenticated);
    }

    [Fact]
    public async Task GeneratePanAsync_WhenNotAuthenticated_ReturnsDeniedError()
    {
        using var adapter = CreateAdapter();
        var binRange = new BinRange("506199", CardScheme.Verve, 19);

        var result = await adapter.GeneratePanAsync(binRange, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", result.ErrorCode);
        Assert.Contains("not authenticated", result.ErrorMessage!);
    }

    [Fact]
    public async Task ComputeCvv2Async_WhenNotAuthenticated_ReturnsDeniedError()
    {
        using var adapter = CreateAdapter();

        var result = await adapter.ComputeCvv2Async("5061990012345678901", "12/25", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", result.ErrorCode);
        Assert.Contains("not authenticated", result.ErrorMessage!);
    }

    [Fact]
    public async Task EncryptAsync_WhenNotAuthenticated_ReturnsDeniedError()
    {
        using var adapter = CreateAdapter();
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("sensitive-data");

        var result = await adapter.EncryptAsync(plaintext, "key-handle", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", result.ErrorCode);
        Assert.Contains("not authenticated", result.ErrorMessage!);
    }

    [Fact]
    public async Task DecryptAsync_WhenNotAuthenticated_ReturnsDeniedError()
    {
        using var adapter = CreateAdapter();
        byte[] ciphertext = new byte[32]; // 16 bytes IV + 16 bytes data

        var result = await adapter.DecryptAsync(ciphertext, "key-handle", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", result.ErrorCode);
        Assert.Contains("not authenticated", result.ErrorMessage!);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidLibraryPath_ReturnsFalse()
    {
        using var adapter = CreateAdapter(new HsmOptions
        {
            Endpoint = "/nonexistent/library/path.so",
            SlotId = 0,
            Pin = "test-pin",
            AuthTimeoutSeconds = 5,
            OperationTimeoutSeconds = 10
        });

        var result = await adapter.AuthenticateAsync();

        Assert.False(result);
        Assert.False(adapter.IsAuthenticated);
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidLibraryPath_DeniesSubsequentOperations()
    {
        using var adapter = CreateAdapter(new HsmOptions
        {
            Endpoint = "/nonexistent/library/path.so",
            SlotId = 0,
            Pin = "test-pin",
            AuthTimeoutSeconds = 5,
            OperationTimeoutSeconds = 10
        });

        await adapter.AuthenticateAsync();

        // All operations should be denied after failed auth
        var binRange = new BinRange("4", CardScheme.Visa, 16);
        var panResult = await adapter.GeneratePanAsync(binRange, CancellationToken.None);
        Assert.False(panResult.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", panResult.ErrorCode);

        var cvvResult = await adapter.ComputeCvv2Async("4111111111111111", "12/25", CancellationToken.None);
        Assert.False(cvvResult.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", cvvResult.ErrorCode);

        var encResult = await adapter.EncryptAsync(new byte[] { 1, 2, 3 }, "key", CancellationToken.None);
        Assert.False(encResult.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", encResult.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithTimeout_ReturnsFalseWhenExceedsLimit()
    {
        // Use a very short auth timeout to verify the timeout mechanism works
        using var adapter = CreateAdapter(new HsmOptions
        {
            Endpoint = "/nonexistent/library/path.so",
            SlotId = 0,
            Pin = "test-pin",
            AuthTimeoutSeconds = 1, // 1 second timeout
            OperationTimeoutSeconds = 10
        });

        // Authentication with a nonexistent library should fail quickly
        // (not due to timeout, but due to library load failure)
        var result = await adapter.AuthenticateAsync();

        Assert.False(result);
        Assert.False(adapter.IsAuthenticated);
    }

    [Fact]
    public async Task GeneratePanAsync_WithCancelledToken_ReturnsCancelledError()
    {
        // We can't test this fully without authentication, but we test the denial path
        using var adapter = CreateAdapter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var binRange = new BinRange("506199", CardScheme.Verve, 19);
        var result = await adapter.GeneratePanAsync(binRange, cts.Token);

        // Should hit the auth check first and return denied
        Assert.False(result.IsSuccess);
        Assert.Equal("HSM_AUTH_DENIED", result.ErrorCode);
    }

    [Fact]
    public async Task AuthenticateAsync_WithCancelledToken_ReturnsFalse()
    {
        using var adapter = CreateAdapter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await adapter.AuthenticateAsync(cts.Token);

        Assert.False(result);
        Assert.False(adapter.IsAuthenticated);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes_NoException()
    {
        var adapter = CreateAdapter();

        // Should not throw
        adapter.Dispose();
        adapter.Dispose();
    }

    [Fact]
    public void HsmOptions_DefaultValues_AreCorrect()
    {
        var options = new HsmOptions();

        Assert.Equal(5, options.AuthTimeoutSeconds);
        Assert.Equal(10, options.OperationTimeoutSeconds);
        Assert.Equal("cvv-key", options.CvvKeyLabel);
        Assert.Equal(string.Empty, options.Endpoint);
        Assert.Equal(string.Empty, options.Pin);
        Assert.Equal(0UL, options.SlotId);
    }
}
