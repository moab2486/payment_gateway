using CardManagement.Domain.PlatformServices.DeveloperPortal;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.DeveloperPortal;

public class CachedKeyInfoTests
{
    [Fact]
    public void CachedKeyInfo_IsValueObject_WithEquality()
    {
        var keyId = Guid.NewGuid();
        var developerId = Guid.NewGuid();
        var scopes = new[] { "payments:read", "payments:write" };
        var expiry = DateTime.UtcNow.AddDays(30);

        var info1 = new CachedKeyInfo(keyId, developerId, scopes, KeyStatus.Active, false, expiry);
        var info2 = new CachedKeyInfo(keyId, developerId, scopes, KeyStatus.Active, false, expiry);

        // Records use structural equality for value types, but arrays use reference equality
        Assert.Equal(info1.KeyId, info2.KeyId);
        Assert.Equal(info1.DeveloperId, info2.DeveloperId);
        Assert.Equal(info1.Status, info2.Status);
        Assert.Equal(info1.IsSandbox, info2.IsSandbox);
        Assert.Equal(info1.ExpiresAtUtc, info2.ExpiresAtUtc);
    }

    [Fact]
    public void CachedKeyInfo_StoresAllProperties()
    {
        var keyId = Guid.NewGuid();
        var developerId = Guid.NewGuid();
        var scopes = new[] { "payments:read" };
        var expiry = DateTime.UtcNow.AddDays(90);

        var info = new CachedKeyInfo(keyId, developerId, scopes, KeyStatus.Rotated, true, expiry);

        Assert.Equal(keyId, info.KeyId);
        Assert.Equal(developerId, info.DeveloperId);
        Assert.Equal(scopes, info.Scopes);
        Assert.Equal(KeyStatus.Rotated, info.Status);
        Assert.True(info.IsSandbox);
        Assert.Equal(expiry, info.ExpiresAtUtc);
    }

    [Fact]
    public void CachedKeyInfo_AllowsNullExpiry()
    {
        var info = new CachedKeyInfo(Guid.NewGuid(), Guid.NewGuid(), new[] { "read" }, KeyStatus.Active, false, null);

        Assert.Null(info.ExpiresAtUtc);
    }
}
