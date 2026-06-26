using CardManagement.Domain.PlatformServices.Notifications;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Domain.Notifications;

public class NotificationTemplateTests
{
    [Fact]
    public void Create_WithValidEmailTemplate_ReturnsTemplate()
    {
        var template = NotificationTemplate.Create(
            name: "payment_confirmation",
            category: "payment",
            requiredVariables: new[] { "amount", "reference" },
            emailSubjectTemplate: "Payment of {{amount}} confirmed",
            emailBodyTemplate: "Your payment {{reference}} for {{amount}} has been confirmed.");

        Assert.NotEqual(Guid.Empty, template.Id);
        Assert.Equal("payment_confirmation", template.Name);
        Assert.Equal("payment", template.Category);
        Assert.Equal(new[] { "amount", "reference" }, template.RequiredVariables);
        Assert.Equal("Payment of {{amount}} confirmed", template.EmailSubjectTemplate);
        Assert.Equal(1, template.Version);
    }

    [Fact]
    public void Create_WithValidSmsTemplate_ReturnsTemplate()
    {
        var template = NotificationTemplate.Create(
            name: "otp_code",
            category: "account",
            requiredVariables: new[] { "code" },
            smsBodyTemplate: "Your OTP is {{code}}. Valid for 5 minutes.");

        Assert.True(template.SupportsChannel(NotificationChannel.Sms));
        Assert.False(template.SupportsChannel(NotificationChannel.Email));
    }

    [Fact]
    public void Create_WithEmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            NotificationTemplate.Create("", "payment", new[] { "amount" }, smsBodyTemplate: "test"));
    }

    [Fact]
    public void Create_WithEmptyCategory_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            NotificationTemplate.Create("test", "", new[] { "amount" }, smsBodyTemplate: "test"));
    }

    [Fact]
    public void Create_WithNoChannelTemplates_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            NotificationTemplate.Create("test", "payment", new[] { "amount" }));
    }

    [Fact]
    public void Create_WithNullRequiredVariables_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            NotificationTemplate.Create("test", "payment", null!, smsBodyTemplate: "test"));
    }

    [Fact]
    public void Update_IncrementsVersionAndUpdatesContent()
    {
        var template = NotificationTemplate.Create(
            name: "test",
            category: "payment",
            requiredVariables: new[] { "amount" },
            smsBodyTemplate: "Original {{amount}}");

        template.Update(
            emailSubjectTemplate: null,
            emailBodyTemplate: null,
            smsBodyTemplate: "Updated {{amount}} {{ref}}",
            whatsAppBodyTemplate: null,
            requiredVariables: new[] { "amount", "ref" });

        Assert.Equal(2, template.Version);
        Assert.Equal("Updated {{amount}} {{ref}}", template.SmsBodyTemplate);
        Assert.Equal(new[] { "amount", "ref" }, template.RequiredVariables);
    }

    [Fact]
    public void Update_WithNoChannelTemplates_Throws()
    {
        var template = NotificationTemplate.Create(
            name: "test",
            category: "payment",
            requiredVariables: new[] { "amount" },
            smsBodyTemplate: "test");

        Assert.Throws<ArgumentException>(() =>
            template.Update(null, null, null, null, new[] { "amount" }));
    }

    [Fact]
    public void SupportsChannel_Email_RequiresBothSubjectAndBody()
    {
        var withBoth = NotificationTemplate.Create(
            "test", "payment", Array.Empty<string>(),
            emailSubjectTemplate: "Subject",
            emailBodyTemplate: "Body");

        Assert.True(withBoth.SupportsChannel(NotificationChannel.Email));
    }

    [Fact]
    public void SupportsChannel_WhatsApp_ReturnsTrue_WhenTemplateProvided()
    {
        var template = NotificationTemplate.Create(
            "test", "payment", Array.Empty<string>(),
            whatsAppBodyTemplate: "Hello {{name}}");

        Assert.True(template.SupportsChannel(NotificationChannel.WhatsApp));
        Assert.False(template.SupportsChannel(NotificationChannel.Sms));
        Assert.False(template.SupportsChannel(NotificationChannel.Email));
    }
}
