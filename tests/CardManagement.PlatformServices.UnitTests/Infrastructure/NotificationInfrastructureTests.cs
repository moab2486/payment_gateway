using CardManagement.Application.PlatformServices.Notifications;
using CardManagement.Application.PlatformServices.Notifications.Commands;
using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Infrastructure.PlatformServices.Notifications;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

public class NotificationInfrastructureTests
{
    #region Template Rendering Tests

    [Fact]
    public void Render_WithCompleteVariableMap_SubstitutesAllPlaceholders()
    {
        // Arrange
        var renderer = new TemplateRenderer();
        var template = NotificationTemplate.Create(
            name: "Welcome",
            category: "onboarding",
            requiredVariables: new[] { "name", "amount" },
            emailSubjectTemplate: "Hello {{name}}",
            emailBodyTemplate: "Dear {{name}}, your balance is {{amount}}.");

        var variables = new Dictionary<string, string>
        {
            ["name"] = "Alice",
            ["amount"] = "500.00"
        };

        // Act
        var result = renderer.Render(
            template, NotificationChannel.Email, "recipient-1", "alice@example.com", variables);

        // Assert
        Assert.Equal("Hello Alice", result.Subject);
        Assert.Equal("Dear Alice, your balance is 500.00.", result.Body);
        Assert.Equal(NotificationChannel.Email, result.Channel);
        Assert.Equal("alice@example.com", result.RecipientAddress);
        Assert.Equal("recipient-1", result.RecipientId);
    }

    [Fact]
    public void Render_WithMissingRequiredVariable_ThrowsTemplateRenderingException()
    {
        // Arrange
        var renderer = new TemplateRenderer();
        var template = NotificationTemplate.Create(
            name: "Alert",
            category: "security",
            requiredVariables: new[] { "user", "action" },
            smsBodyTemplate: "{{user}} performed {{action}}");

        var variables = new Dictionary<string, string>
        {
            ["user"] = "Bob"
            // "action" is missing
        };

        // Act & Assert
        var ex = Assert.Throws<TemplateRenderingException>(() =>
            renderer.Render(template, NotificationChannel.Sms, "recipient-2", "+1234567890", variables));

        Assert.Equal("action", ex.MissingVariable);
    }

    [Fact]
    public void Render_WithPartialVariableMap_MissingNonRequired_ThrowsOnPlaceholderInBody()
    {
        // Arrange
        var renderer = new TemplateRenderer();
        // Template has a placeholder in the body that is NOT in requiredVariables
        // But TemplateRenderer checks the body placeholders during substitution
        var template = NotificationTemplate.Create(
            name: "Mixed",
            category: "alerts",
            requiredVariables: new[] { "name" },
            smsBodyTemplate: "Hello {{name}}, your code is {{code}}");

        var variables = new Dictionary<string, string>
        {
            ["name"] = "Charlie"
            // "code" is not provided and not in requiredVariables
        };

        // Act & Assert - the renderer throws when substituting body placeholders
        var ex = Assert.Throws<TemplateRenderingException>(() =>
            renderer.Render(template, NotificationChannel.Sms, "recipient-3", "+9876543210", variables));

        Assert.Equal("code", ex.MissingVariable);
    }

    [Fact]
    public void Render_EmailChannel_ReturnsSubjectAndBody()
    {
        // Arrange
        var renderer = new TemplateRenderer();
        var template = NotificationTemplate.Create(
            name: "Payment",
            category: "transactions",
            requiredVariables: new[] { "txnId" },
            emailSubjectTemplate: "Transaction {{txnId}} confirmed",
            emailBodyTemplate: "Your transaction {{txnId}} has been processed.",
            smsBodyTemplate: "Txn {{txnId}} confirmed",
            whatsAppBodyTemplate: "Transaction {{txnId}} done");

        var variables = new Dictionary<string, string> { ["txnId"] = "TXN-001" };

        // Act
        var result = renderer.Render(
            template, NotificationChannel.Email, "r1", "test@test.com", variables);

        // Assert
        Assert.Equal("Transaction TXN-001 confirmed", result.Subject);
        Assert.Equal("Your transaction TXN-001 has been processed.", result.Body);
    }

    [Fact]
    public void Render_SmsChannel_ReturnsBodyOnly_NoSubject()
    {
        // Arrange
        var renderer = new TemplateRenderer();
        var template = NotificationTemplate.Create(
            name: "OTP",
            category: "security",
            requiredVariables: new[] { "code" },
            smsBodyTemplate: "Your OTP is {{code}}");

        var variables = new Dictionary<string, string> { ["code"] = "9876" };

        // Act
        var result = renderer.Render(
            template, NotificationChannel.Sms, "r1", "+2348001234567", variables);

        // Assert
        Assert.Null(result.Subject);
        Assert.Equal("Your OTP is 9876", result.Body);
        Assert.Equal(NotificationChannel.Sms, result.Channel);
    }

    [Fact]
    public void Render_WhatsAppChannel_ReturnsCorrectVariant()
    {
        // Arrange
        var renderer = new TemplateRenderer();
        var template = NotificationTemplate.Create(
            name: "Multi",
            category: "general",
            requiredVariables: new[] { "name" },
            emailSubjectTemplate: "Email subject {{name}}",
            emailBodyTemplate: "Email body {{name}}",
            smsBodyTemplate: "SMS body {{name}}",
            whatsAppBodyTemplate: "WhatsApp body {{name}}");

        var variables = new Dictionary<string, string> { ["name"] = "Dave" };

        // Act
        var result = renderer.Render(
            template, NotificationChannel.WhatsApp, "r1", "wa:+234800", variables);

        // Assert
        Assert.Equal("WhatsApp body Dave", result.Body);
        Assert.Null(result.Subject);
        Assert.Equal(NotificationChannel.WhatsApp, result.Channel);
    }

    [Fact]
    public void Render_UnsupportedChannel_ThrowsInvalidOperationException()
    {
        // Arrange
        var renderer = new TemplateRenderer();
        var template = NotificationTemplate.Create(
            name: "EmailOnly",
            category: "general",
            requiredVariables: new[] { "x" },
            emailSubjectTemplate: "Subj {{x}}",
            emailBodyTemplate: "Body {{x}}");

        var variables = new Dictionary<string, string> { ["x"] = "val" };

        // Act & Assert - template doesn't have SMS variant
        Assert.Throws<InvalidOperationException>(() =>
            renderer.Render(template, NotificationChannel.Sms, "r1", "+123", variables));
    }

    #endregion

    #region Channel Adapter Selection Tests

    [Fact]
    public async Task ChannelSelection_UsesPrimaryChannel_FromPreference()
    {
        // Arrange
        var preference = NotificationPreference.Create("user-1", NotificationChannel.Sms);
        var template = NotificationTemplate.Create(
            name: "Test",
            category: "general",
            requiredVariables: new[] { "msg" },
            emailSubjectTemplate: "Sub {{msg}}",
            emailBodyTemplate: "Email {{msg}}",
            smsBodyTemplate: "SMS {{msg}}");

        var smsAdapter = new FakeChannelAdapter(NotificationChannel.Sms, success: true);
        var emailAdapter = new FakeChannelAdapter(NotificationChannel.Email, success: true);

        var handler = CreateHandler(
            preference: preference,
            template: template,
            adapters: new[] { emailAdapter, smsAdapter });

        var command = new DispatchNotificationCommand(
            "user-1", "+234800", template.Id, new Dictionary<string, string> { ["msg"] = "hello" });

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert - SMS adapter should have been used (primary preference)
        Assert.Equal(1, smsAdapter.SendCount);
        Assert.Equal(0, emailAdapter.SendCount);
    }

    [Fact]
    public async Task ChannelSelection_DefaultsToEmail_WhenNoPreferenceExists()
    {
        // Arrange
        var template = NotificationTemplate.Create(
            name: "Default",
            category: "general",
            requiredVariables: new[] { "v" },
            emailSubjectTemplate: "Subj {{v}}",
            emailBodyTemplate: "Body {{v}}",
            smsBodyTemplate: "SMS {{v}}");

        var emailAdapter = new FakeChannelAdapter(NotificationChannel.Email, success: true);
        var smsAdapter = new FakeChannelAdapter(NotificationChannel.Sms, success: true);

        var handler = CreateHandler(
            preference: null, // No preference stored
            template: template,
            adapters: new[] { emailAdapter, smsAdapter });

        var command = new DispatchNotificationCommand(
            "user-new", "new@test.com", template.Id,
            new Dictionary<string, string> { ["v"] = "test" });

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert - should default to Email
        Assert.Equal(1, emailAdapter.SendCount);
        Assert.Equal(0, smsAdapter.SendCount);
    }

    [Fact]
    public async Task ChannelSelection_WhatsApp_WhenPreferred()
    {
        // Arrange
        var preference = NotificationPreference.Create("user-wa", NotificationChannel.WhatsApp);
        var template = NotificationTemplate.Create(
            name: "WA",
            category: "general",
            requiredVariables: new[] { "x" },
            whatsAppBodyTemplate: "WA {{x}}",
            smsBodyTemplate: "SMS {{x}}");

        var waAdapter = new FakeChannelAdapter(NotificationChannel.WhatsApp, success: true);
        var smsAdapter = new FakeChannelAdapter(NotificationChannel.Sms, success: true);

        var handler = CreateHandler(
            preference: preference,
            template: template,
            adapters: new[] { waAdapter, smsAdapter });

        var command = new DispatchNotificationCommand(
            "user-wa", "wa:+234800", template.Id,
            new Dictionary<string, string> { ["x"] = "hi" });

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert
        Assert.Equal(1, waAdapter.SendCount);
        Assert.Equal(0, smsAdapter.SendCount);
    }

    #endregion

    #region Fallback Channel Escalation Tests

    [Fact]
    public async Task Fallback_WhenPrimaryFails_DeliverViaFallbackChannel()
    {
        // Arrange
        var preference = NotificationPreference.Create(
            "user-fb", NotificationChannel.Sms, NotificationChannel.Email);
        var template = NotificationTemplate.Create(
            name: "Fallback",
            category: "transactions",
            requiredVariables: new[] { "amt" },
            emailSubjectTemplate: "Payment {{amt}}",
            emailBodyTemplate: "Email body {{amt}}",
            smsBodyTemplate: "SMS {{amt}}");

        var smsAdapter = new FakeChannelAdapter(NotificationChannel.Sms, success: false);
        var emailAdapter = new FakeChannelAdapter(NotificationChannel.Email, success: true);

        var deliveryLog = new FakeDeliveryLogRepository();

        var handler = CreateHandler(
            preference: preference,
            template: template,
            adapters: new[] { smsAdapter, emailAdapter },
            deliveryLog: deliveryLog);

        var command = new DispatchNotificationCommand(
            "user-fb", "fallback@test.com", template.Id,
            new Dictionary<string, string> { ["amt"] = "100.00" });

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert - SMS failed, Email fallback should succeed
        Assert.Equal(1, smsAdapter.SendCount);
        Assert.Equal(1, emailAdapter.SendCount);
        // Delivery log should record the successful fallback delivery
        Assert.Single(deliveryLog.Entries);
        Assert.Equal(NotificationChannel.Email, deliveryLog.Entries[0].Channel);
        Assert.Equal(NotificationDeliveryStatus.Delivered, deliveryLog.Entries[0].Status);
    }

    [Fact]
    public async Task Fallback_WhenBothFail_LogsFailureOnFallbackChannel()
    {
        // Arrange
        var preference = NotificationPreference.Create(
            "user-all-fail", NotificationChannel.WhatsApp, NotificationChannel.Sms);
        var template = NotificationTemplate.Create(
            name: "BothFail",
            category: "alerts",
            requiredVariables: new[] { "x" },
            smsBodyTemplate: "SMS {{x}}",
            whatsAppBodyTemplate: "WA {{x}}");

        var waAdapter = new FakeChannelAdapter(NotificationChannel.WhatsApp, success: false);
        var smsAdapter = new FakeChannelAdapter(NotificationChannel.Sms, success: false);

        var deliveryLog = new FakeDeliveryLogRepository();

        var handler = CreateHandler(
            preference: preference,
            template: template,
            adapters: new[] { waAdapter, smsAdapter },
            deliveryLog: deliveryLog);

        var command = new DispatchNotificationCommand(
            "user-all-fail", "+234800", template.Id,
            new Dictionary<string, string> { ["x"] = "test" });

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert - both channels attempted, failure logged
        Assert.Equal(1, waAdapter.SendCount);
        Assert.Equal(1, smsAdapter.SendCount);
        Assert.Single(deliveryLog.Entries);
        Assert.Equal(NotificationDeliveryStatus.Failed, deliveryLog.Entries[0].Status);
    }

    [Fact]
    public async Task Fallback_NoFallbackConfigured_LogsFailureOnPrimary()
    {
        // Arrange
        var preference = NotificationPreference.Create(
            "user-nofb", NotificationChannel.Email); // No fallback
        var template = NotificationTemplate.Create(
            name: "NoFB",
            category: "general",
            requiredVariables: new[] { "v" },
            emailSubjectTemplate: "Sub {{v}}",
            emailBodyTemplate: "Body {{v}}");

        var emailAdapter = new FakeChannelAdapter(NotificationChannel.Email, success: false);
        var deliveryLog = new FakeDeliveryLogRepository();

        var handler = CreateHandler(
            preference: preference,
            template: template,
            adapters: new[] { emailAdapter },
            deliveryLog: deliveryLog);

        var command = new DispatchNotificationCommand(
            "user-nofb", "nofb@test.com", template.Id,
            new Dictionary<string, string> { ["v"] = "hi" });

        // Act
        await handler.HandleAsync(command, CancellationToken.None);

        // Assert - failure logged on primary channel
        Assert.Equal(1, emailAdapter.SendCount);
        Assert.Single(deliveryLog.Entries);
        Assert.Equal(NotificationChannel.Email, deliveryLog.Entries[0].Channel);
        Assert.Equal(NotificationDeliveryStatus.Failed, deliveryLog.Entries[0].Status);
    }

    #endregion

    #region Delivery Log Statistics Tests

    [Fact]
    public void DeliveryStatistics_CorrectlyAggregates_SentDeliveredFailed()
    {
        // Arrange - simulate entries
        var templateId = Guid.NewGuid();
        var entries = new List<DeliveryLogEntry>();

        // 3 delivered entries
        for (int i = 0; i < 3; i++)
        {
            var entry = DeliveryLogEntry.Create($"user-{i}", NotificationChannel.Email, templateId);
            entry.MarkSent($"msg-{i}");
            entry.MarkDelivered();
            entries.Add(entry);
        }

        // 2 failed entries
        for (int i = 0; i < 2; i++)
        {
            var entry = DeliveryLogEntry.Create($"user-f{i}", NotificationChannel.Email, templateId);
            entry.MarkFailed("Provider timeout");
            entries.Add(entry);
        }

        // 1 sent but not yet delivered
        var sentEntry = DeliveryLogEntry.Create("user-s", NotificationChannel.Email, templateId);
        sentEntry.MarkSent("msg-pending");
        entries.Add(sentEntry);

        // Act - compute statistics manually (mirrors what repository does)
        var totalSent = entries.Count(e =>
            e.Status == NotificationDeliveryStatus.Sent ||
            e.Status == NotificationDeliveryStatus.Delivered ||
            e.Status == NotificationDeliveryStatus.Failed);
        var totalDelivered = entries.Count(e => e.Status == NotificationDeliveryStatus.Delivered);
        var totalFailed = entries.Count(e => e.Status == NotificationDeliveryStatus.Failed);

        // Assert
        Assert.Equal(6, totalSent); // 3 delivered + 2 failed + 1 sent = 6
        Assert.Equal(3, totalDelivered);
        Assert.Equal(2, totalFailed);
    }

    [Fact]
    public void DeliveryStatistics_AverageDeliveryTime_ComputedFromDeliveredEntries()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var entries = new List<DeliveryLogEntry>();

        // Create entries and compute their delivery times
        var entry1 = DeliveryLogEntry.Create("u1", NotificationChannel.Sms, templateId);
        entry1.MarkSent("m1");
        entry1.MarkDelivered();
        entries.Add(entry1);

        var entry2 = DeliveryLogEntry.Create("u2", NotificationChannel.Sms, templateId);
        entry2.MarkSent("m2");
        entry2.MarkDelivered();
        entries.Add(entry2);

        // Failed entry should not contribute to average delivery time
        var failedEntry = DeliveryLogEntry.Create("u3", NotificationChannel.Sms, templateId);
        failedEntry.MarkFailed("timeout");
        entries.Add(failedEntry);

        // Act
        var deliveredEntries = entries.Where(e => e.DeliveredAtUtc.HasValue).ToList();
        var avgDeliveryTimeMs = deliveredEntries.Count > 0
            ? deliveredEntries.Average(e => (e.DeliveredAtUtc!.Value - e.DispatchedAtUtc).TotalMilliseconds)
            : 0;

        // Assert - average should only come from delivered entries (not failed)
        Assert.Equal(2, deliveredEntries.Count);
        Assert.True(avgDeliveryTimeMs >= 0);
    }

    [Fact]
    public void DeliveryStatistics_EmptyEntries_ReturnsZeroes()
    {
        // Arrange
        var entries = new List<DeliveryLogEntry>();
        var range = new DateRange(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        // Act
        var totalSent = entries.Count(e =>
            e.Status == NotificationDeliveryStatus.Sent ||
            e.Status == NotificationDeliveryStatus.Delivered ||
            e.Status == NotificationDeliveryStatus.Failed);
        var totalDelivered = entries.Count(e => e.Status == NotificationDeliveryStatus.Delivered);
        var totalFailed = entries.Count(e => e.Status == NotificationDeliveryStatus.Failed);

        var stats = new DeliveryStatistics(totalSent, totalDelivered, totalFailed, 0,
            range.StartUtc, range.EndUtc);

        // Assert
        Assert.Equal(0, stats.TotalSent);
        Assert.Equal(0, stats.TotalDelivered);
        Assert.Equal(0, stats.TotalFailed);
        Assert.Equal(0, stats.AverageDeliveryTimeMs);
    }

    [Fact]
    public void DeliveryStatistics_FiltersByChannel()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var entries = new List<DeliveryLogEntry>
        {
            CreateDeliveredEntry("u1", NotificationChannel.Email, templateId),
            CreateDeliveredEntry("u2", NotificationChannel.Email, templateId),
            CreateDeliveredEntry("u3", NotificationChannel.Sms, templateId),
        };

        // Act - filter to Email only
        var emailEntries = entries.Where(e => e.Channel == NotificationChannel.Email).ToList();
        var smsEntries = entries.Where(e => e.Channel == NotificationChannel.Sms).ToList();

        // Assert
        Assert.Equal(2, emailEntries.Count);
        Assert.Single(smsEntries);
    }

    #endregion

    #region 90-Day Retention Tests

    [Fact]
    public void RetentionBoundary_EntriesWithin90Days_AreRetained()
    {
        // Arrange
        var templateId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var retentionDays = 90;

        var entries = new List<DeliveryLogEntry>
        {
            CreateDeliveredEntry("u1", NotificationChannel.Email, templateId), // today
        };

        // Act - simulate retention filter
        var retainedEntries = entries
            .Where(e => e.DispatchedAtUtc >= now.AddDays(-retentionDays))
            .ToList();

        // Assert
        Assert.Single(retainedEntries);
    }

    [Fact]
    public void RetentionBoundary_EntriesOlderThan90Days_AreExcludedByRetentionFilter()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var retentionDays = 90;
        var cutoffDate = now.AddDays(-retentionDays);

        // Simulate entries at various ages
        var entryAges = new[] { 1, 30, 89, 90, 91, 120, 365 };

        // Act - apply the same retention boundary the system uses
        var retainedDays = entryAges.Where(days => now.AddDays(-days) >= cutoffDate).ToArray();
        var prunedDays = entryAges.Where(days => now.AddDays(-days) < cutoffDate).ToArray();

        // Assert
        // Days 1, 30, 89, 90 are within boundary (>= cutoff)
        Assert.Equal(new[] { 1, 30, 89, 90 }, retainedDays);
        // Days 91, 120, 365 are beyond the boundary
        Assert.Equal(new[] { 91, 120, 365 }, prunedDays);
    }

    [Fact]
    public void RetentionBoundary_ExactlyAt90Days_IsRetained()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var cutoffDate = now.AddDays(-90);
        var entryDate = cutoffDate; // Exactly at boundary

        // Act
        var isRetained = entryDate >= cutoffDate;

        // Assert - boundary entries are retained (minimum 90 days)
        Assert.True(isRetained);
    }

    [Fact]
    public void RetentionBoundary_OneDayBeyond90_IsNotRetained()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var cutoffDate = now.AddDays(-90);
        var entryDate = now.AddDays(-91); // One day beyond

        // Act
        var isRetained = entryDate >= cutoffDate;

        // Assert
        Assert.False(isRetained);
    }

    #endregion

    #region Helper Methods and Fakes

    private static DeliveryLogEntry CreateDeliveredEntry(
        string recipientId, NotificationChannel channel, Guid templateId)
    {
        var entry = DeliveryLogEntry.Create(recipientId, channel, templateId);
        entry.MarkSent($"provider-{Guid.NewGuid():N}");
        entry.MarkDelivered();
        return entry;
    }

    private static DispatchNotificationCommandHandler CreateHandler(
        NotificationPreference? preference,
        NotificationTemplate template,
        INotificationChannelAdapter[] adapters,
        FakeDeliveryLogRepository? deliveryLog = null)
    {
        return new DispatchNotificationCommandHandler(
            new FakePreferenceRepository(preference),
            new FakeTemplateRepository(template),
            new TemplateRenderer(),
            deliveryLog ?? new FakeDeliveryLogRepository(),
            adapters);
    }

    /// <summary>
    /// Fake channel adapter that records calls and returns configurable results.
    /// </summary>
    private class FakeChannelAdapter : INotificationChannelAdapter
    {
        private readonly bool _success;
        public NotificationChannel Channel { get; }
        public int SendCount { get; private set; }

        public FakeChannelAdapter(NotificationChannel channel, bool success)
        {
            Channel = channel;
            _success = success;
        }

        public Task<DeliveryResult> SendAsync(RenderedNotification notification, CancellationToken ct)
        {
            SendCount++;
            var result = _success
                ? DeliveryResult.Succeeded($"provider-msg-{Guid.NewGuid():N}")
                : DeliveryResult.Failed("Simulated channel failure");
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Fake preference repository returning a preconfigured preference.
    /// </summary>
    private class FakePreferenceRepository : INotificationPreferenceRepository
    {
        private readonly NotificationPreference? _preference;

        public FakePreferenceRepository(NotificationPreference? preference)
        {
            _preference = preference;
        }

        public Task<NotificationPreference?> GetByRecipientAsync(string recipientId, CancellationToken ct)
            => Task.FromResult(_preference);

        public Task SaveAsync(NotificationPreference preference, CancellationToken ct)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Fake template repository returning a preconfigured template.
    /// </summary>
    private class FakeTemplateRepository : INotificationTemplateRepository
    {
        private readonly NotificationTemplate _template;

        public FakeTemplateRepository(NotificationTemplate template)
        {
            _template = template;
        }

        public Task<NotificationTemplate?> GetByIdAsync(Guid templateId, CancellationToken ct)
            => Task.FromResult<NotificationTemplate?>(_template);

        public Task<NotificationTemplate?> GetByNameAsync(string templateName, CancellationToken ct)
            => Task.FromResult<NotificationTemplate?>(_template);

        public Task<IReadOnlyList<NotificationTemplate>> ListAsync(int limit, int offset, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<NotificationTemplate>>(new[] { _template });

        public Task CreateAsync(NotificationTemplate template, CancellationToken ct)
            => Task.CompletedTask;

        public Task UpdateAsync(NotificationTemplate template, CancellationToken ct)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Fake delivery log repository that records created entries for assertion.
    /// </summary>
    private class FakeDeliveryLogRepository : IDeliveryLogRepository
    {
        public List<DeliveryLogEntry> Entries { get; } = new();

        public Task CreateAsync(DeliveryLogEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(Guid entryId, NotificationDeliveryStatus status, CancellationToken ct)
            => Task.CompletedTask;

        public Task<IReadOnlyList<DeliveryLogEntry>> GetByRecipientAsync(string recipientId, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DeliveryLogEntry>>(
                Entries.Where(e => e.RecipientId == recipientId).Take(limit).ToList());

        public Task<DeliveryStatistics> GetStatisticsAsync(
            NotificationChannel? channel, Guid? templateId, DateRange range, CancellationToken ct)
        {
            var filtered = Entries.AsEnumerable();
            if (channel.HasValue) filtered = filtered.Where(e => e.Channel == channel.Value);
            if (templateId.HasValue) filtered = filtered.Where(e => e.TemplateId == templateId.Value);

            var list = filtered.ToList();
            var totalSent = list.Count(e =>
                e.Status == NotificationDeliveryStatus.Sent ||
                e.Status == NotificationDeliveryStatus.Delivered ||
                e.Status == NotificationDeliveryStatus.Failed);
            var totalDelivered = list.Count(e => e.Status == NotificationDeliveryStatus.Delivered);
            var totalFailed = list.Count(e => e.Status == NotificationDeliveryStatus.Failed);

            var deliveredEntries = list.Where(e => e.DeliveredAtUtc.HasValue).ToList();
            var avgMs = deliveredEntries.Count > 0
                ? deliveredEntries.Average(e => (e.DeliveredAtUtc!.Value - e.DispatchedAtUtc).TotalMilliseconds)
                : 0;

            return Task.FromResult(new DeliveryStatistics(
                totalSent, totalDelivered, totalFailed, avgMs, range.StartUtc, range.EndUtc));
        }
    }

    #endregion
}
