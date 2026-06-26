using CardManagement.Domain.PlatformServices.Notifications;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Notifications;

public class NotificationPreferenceTests
{
    [Fact]
    public void Create_WithValidInputs_ReturnsPreference()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email, NotificationChannel.Sms);

        Assert.NotEqual(Guid.Empty, pref.Id);
        Assert.Equal("user-123", pref.RecipientId);
        Assert.Equal(NotificationChannel.Email, pref.PrimaryChannel);
        Assert.Equal(NotificationChannel.Sms, pref.FallbackChannel);
        Assert.Empty(pref.CategoryOptIn);
    }

    [Fact]
    public void Create_WithNoFallback_ReturnsPreferenceWithNullFallback()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Sms);

        Assert.Null(pref.FallbackChannel);
    }

    [Fact]
    public void Create_WithEmptyRecipientId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            NotificationPreference.Create("", NotificationChannel.Email));
    }

    [Fact]
    public void Create_WithSamePrimaryAndFallback_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            NotificationPreference.Create("user-123", NotificationChannel.Email, NotificationChannel.Email));
    }

    [Fact]
    public void UpdateChannels_WithValidDifferentChannels_Updates()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email, NotificationChannel.Sms);

        pref.UpdateChannels(NotificationChannel.WhatsApp, NotificationChannel.Email);

        Assert.Equal(NotificationChannel.WhatsApp, pref.PrimaryChannel);
        Assert.Equal(NotificationChannel.Email, pref.FallbackChannel);
    }

    [Fact]
    public void UpdateChannels_WithSamePrimaryAndFallback_Throws()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email);

        Assert.Throws<ArgumentException>(() =>
            pref.UpdateChannels(NotificationChannel.Sms, NotificationChannel.Sms));
    }

    [Fact]
    public void SetCategoryOptIn_StoresPreference()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email);

        pref.SetCategoryOptIn("payment", true);
        pref.SetCategoryOptIn("marketing", false);

        Assert.True(pref.CategoryOptIn["payment"]);
        Assert.False(pref.CategoryOptIn["marketing"]);
    }

    [Fact]
    public void SetCategoryOptIn_WithEmptyCategory_Throws()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email);

        Assert.Throws<ArgumentException>(() => pref.SetCategoryOptIn("", true));
    }

    [Fact]
    public void IsOptedIn_DefaultsToTrue_WhenNoPrefSet()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email);

        Assert.True(pref.IsOptedIn("payment"));
    }

    [Fact]
    public void IsOptedIn_ReturnsFalse_WhenOptedOut()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email);
        pref.SetCategoryOptIn("marketing", false);

        Assert.False(pref.IsOptedIn("marketing"));
    }

    [Fact]
    public void IsOptedIn_ReturnsTrue_WhenOptedIn()
    {
        var pref = NotificationPreference.Create("user-123", NotificationChannel.Email);
        pref.SetCategoryOptIn("payment", true);

        Assert.True(pref.IsOptedIn("payment"));
    }
}
