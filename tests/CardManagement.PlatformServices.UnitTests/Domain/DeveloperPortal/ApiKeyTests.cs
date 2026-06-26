using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.DeveloperPortal;

public class ApiKeyTests
{
    private static readonly string[] DefaultScopes = new[] { "payments:read", "payments:write" };

    [Fact]
    public void Create_ReturnsActiveKey_WithHashedValue()
    {
        var developerId = Guid.NewGuid();

        var key = ApiKey.Create(developerId, DefaultScopes, false, null, out var rawKey);

        Assert.Equal(developerId, key.DeveloperId);
        Assert.Equal(KeyStatus.Active, key.Status);
        Assert.False(key.IsSandbox);
        Assert.Null(key.ExpiresAtUtc);
        Assert.Null(key.GracePeriodEndsAtUtc);
        Assert.NotEmpty(rawKey);
        Assert.Equal(rawKey[..8], key.KeyPrefix);
        Assert.Equal(ApiKey.ComputeHash(rawKey), key.KeyHash);
    }

    [Fact]
    public void Create_NeverStoresRawKey()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out var rawKey);

        // The KeyHash should NOT equal the raw key
        Assert.NotEqual(rawKey, key.KeyHash);
        // The KeyHash should be the SHA-256 hex string of the raw key
        Assert.Equal(64, key.KeyHash.Length); // SHA-256 produces 64 hex chars
    }

    [Fact]
    public void Create_WithSandboxFlag_SetsSandboxTrue()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, true, null, out _);

        Assert.True(key.IsSandbox);
    }

    [Fact]
    public void Create_WithExpiry_SetsExpiresAtUtc()
    {
        var expiry = DateTime.UtcNow.AddDays(90);

        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, expiry, out _);

        Assert.Equal(expiry, key.ExpiresAtUtc);
    }

    [Fact]
    public void Create_WithEmptyDeveloperId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ApiKey.Create(Guid.Empty, DefaultScopes, false, null, out _));
    }

    [Fact]
    public void Create_WithNoScopes_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ApiKey.Create(Guid.NewGuid(), Array.Empty<string>(), false, null, out _));
    }

    [Fact]
    public void Create_WithNullScopes_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ApiKey.Create(Guid.NewGuid(), null!, false, null, out _));
    }

    [Fact]
    public void Rotate_FromActive_SetsStatusAndGracePeriod()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        var gracePeriod = TimeSpan.FromHours(24);

        key.Rotate(gracePeriod);

        Assert.Equal(KeyStatus.Rotated, key.Status);
        Assert.NotNull(key.GracePeriodEndsAtUtc);
        // Grace period end should be approximately 24 hours from now
        Assert.True(key.GracePeriodEndsAtUtc.Value > DateTime.UtcNow);
        Assert.True(key.GracePeriodEndsAtUtc.Value <= DateTime.UtcNow.AddHours(24).AddSeconds(5));
    }

    [Fact]
    public void Rotate_FromNonActive_Throws()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key.Revoke();

        Assert.Throws<InvalidOperationException>(() => key.Rotate(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Rotate_WithZeroGracePeriod_Throws()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);

        Assert.Throws<ArgumentException>(() => key.Rotate(TimeSpan.Zero));
    }

    [Fact]
    public void Rotate_WithNegativeGracePeriod_Throws()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);

        Assert.Throws<ArgumentException>(() => key.Rotate(TimeSpan.FromHours(-1)));
    }

    [Fact]
    public void Revoke_FromActive_SetsStatusRevoked()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);

        key.Revoke();

        Assert.Equal(KeyStatus.Revoked, key.Status);
    }

    [Fact]
    public void Revoke_FromNonActive_Throws()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key.Rotate(TimeSpan.FromHours(1));

        Assert.Throws<InvalidOperationException>(() => key.Revoke());
    }

    [Fact]
    public void Expire_FromActive_SetsStatusExpired()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);

        key.Expire();

        Assert.Equal(KeyStatus.Expired, key.Status);
    }

    [Fact]
    public void Expire_FromNonActive_Throws()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key.Revoke();

        Assert.Throws<InvalidOperationException>(() => key.Expire());
    }

    [Fact]
    public void IsValid_ActiveKeyWithNoExpiry_ReturnsTrue()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);

        Assert.True(key.IsValid());
    }

    [Fact]
    public void IsValid_ActiveKeyBeforeExpiry_ReturnsTrue()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, DateTime.UtcNow.AddDays(30), out _);

        Assert.True(key.IsValid());
    }

    [Fact]
    public void IsValid_ActiveKeyPastExpiry_ReturnsFalse()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, DateTime.UtcNow.AddDays(30), out _);

        // Check validity at a time past expiry
        Assert.False(key.IsValid(DateTime.UtcNow.AddDays(31)));
    }

    [Fact]
    public void IsValid_RotatedKeyDuringGracePeriod_ReturnsTrue()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key.Rotate(TimeSpan.FromHours(24));

        // During the grace period
        Assert.True(key.IsValid(DateTime.UtcNow.AddHours(12)));
    }

    [Fact]
    public void IsValid_RotatedKeyAfterGracePeriod_ReturnsFalse()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key.Rotate(TimeSpan.FromHours(24));

        // After the grace period
        Assert.False(key.IsValid(DateTime.UtcNow.AddHours(25)));
    }

    [Fact]
    public void IsValid_RevokedKey_ReturnsFalse()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key.Revoke();

        Assert.False(key.IsValid());
    }

    [Fact]
    public void IsValid_ExpiredKey_ReturnsFalse()
    {
        var key = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key.Expire();

        Assert.False(key.IsValid());
    }

    [Fact]
    public void ComputeHash_SameInput_ProducesSameOutput()
    {
        var rawKey = "test-api-key-12345";

        var hash1 = ApiKey.ComputeHash(rawKey);
        var hash2 = ApiKey.ComputeHash(rawKey);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_DifferentInputs_ProduceDifferentOutputs()
    {
        var hash1 = ApiKey.ComputeHash("key-a");
        var hash2 = ApiKey.ComputeHash("key-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_EmptyInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => ApiKey.ComputeHash(""));
    }

    [Fact]
    public void CreateFromRawKey_ProducesCorrectHash()
    {
        var rawKey = "my-test-api-key-value-12345";
        var expectedHash = ApiKey.ComputeHash(rawKey);

        var key = ApiKey.CreateFromRawKey(Guid.NewGuid(), rawKey, DefaultScopes, false, null);

        Assert.Equal(expectedHash, key.KeyHash);
        Assert.Equal("my-test-", key.KeyPrefix);
        Assert.Equal(KeyStatus.Active, key.Status);
    }

    [Fact]
    public void StatusTransitions_OnlyValidFromActive()
    {
        // Rotate only from Active
        var key1 = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key1.Expire();
        Assert.Throws<InvalidOperationException>(() => key1.Rotate(TimeSpan.FromHours(1)));

        // Revoke only from Active
        var key2 = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key2.Rotate(TimeSpan.FromHours(1));
        Assert.Throws<InvalidOperationException>(() => key2.Revoke());

        // Expire only from Active
        var key3 = ApiKey.Create(Guid.NewGuid(), DefaultScopes, false, null, out _);
        key3.Revoke();
        Assert.Throws<InvalidOperationException>(() => key3.Expire());
    }
}
