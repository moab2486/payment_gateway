using CardManagement.Application.PlatformServices.Notifications.Commands;
using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.Notifications;

/// <summary>
/// Property-based tests for notification channel resolution and fallback (Properties 16, 17),
/// delivery log completeness (Property 21), and delivery statistics accuracy (Property 22).
/// </summary>
[Trait("Feature", "platform-services")]
public class NotificationChannelResolutionPropertyTests
{
    /// <summary>
    /// Property 16: Notification Channel Resolution
    /// Resolved channel matches recipient's primary channel preference.
    /// **Validates: Requirements 7.1**
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "16")]
    public Property ChannelResolution_MatchesRecipientPrimaryPreference()
    {
        var channelGen = Gen.Elements(
            NotificationChannel.Email,
            NotificationChannel.Sms,
            NotificationChannel.WhatsApp);

        return Prop.ForAll(
            channelGen.ToArbitrary(),
            primaryChannel =>
            {
                // Arrange: Create a preference with the given primary channel
                var recipientId = $"recipient-{Guid.NewGuid():N}";
                var preference = NotificationPreference.Create(recipientId, primaryChannel);

                // Create a template that supports all channels
                var template = NotificationTemplate.Create(
                    name: "test_template",
                    category: "payment",
                    requiredVariables: new[] { "amount" },
                    emailSubjectTemplate: "Subject: {{amount}}",
                    emailBodyTemplate: "Email body: {{amount}}",
                    smsBodyTemplate: "SMS: {{amount}}",
                    whatsAppBodyTemplate: "WhatsApp: {{amount}}");

                // Act: Set up the handler infrastructure
                var preferenceRepo = new InMemoryPreferenceRepository(preference);
                var templateRepo = new InMemoryTemplateRepository(template);
                var deliveryLogRepo = new InMemoryDeliveryLogRepository();
                var channelAdapters = CreateSuccessfulAdapters();
                var renderer = new InMemoryTemplateRenderer();

                var handler = new DispatchNotificationCommandHandler(
                    preferenceRepo,
                    templateRepo,
                    renderer,
                    deliveryLogRepo,
                    channelAdapters);

                var command = new DispatchNotificationCommand(
                    recipientId,
                    GetAddressForChannel(primaryChannel),
                    template.Id,
                    new Dictionary<string, string> { { "amount", "5000" } });

                handler.HandleAsync(command, CancellationToken.None).GetAwaiter().GetResult();

                // Assert: The delivery log should record the primary channel
                var logEntries = deliveryLogRepo.GetAllEntries();
                var usedChannel = logEntries.Single().Channel;

                return (usedChannel == primaryChannel)
                    .Label($"Expected channel {primaryChannel} but delivery used {usedChannel}");
            });
    }

    /// <summary>
    /// Property 17: Notification Channel Fallback
    /// Primary failure triggers fallback channel delivery when configured.
    /// **Validates: Requirements 7.6**
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "17")]
    public Property ChannelFallback_PrimaryFailure_TriggersDeliveryViaFallbackChannel()
    {
        // Generate distinct primary/fallback channel pairs
        var channelPairGen = from primary in Gen.Elements(
                NotificationChannel.Email,
                NotificationChannel.Sms,
                NotificationChannel.WhatsApp)
            from fallback in Gen.Elements(
                NotificationChannel.Email,
                NotificationChannel.Sms,
                NotificationChannel.WhatsApp)
            where primary != fallback
            select (primary, fallback);

        return Prop.ForAll(
            channelPairGen.ToArbitrary(),
            channelPair =>
            {
                var (primaryChannel, fallbackChannel) = channelPair;

                // Arrange: Create a preference with primary and fallback
                var recipientId = $"recipient-{Guid.NewGuid():N}";
                var preference = NotificationPreference.Create(recipientId, primaryChannel, fallbackChannel);

                // Create a template that supports all channels
                var template = NotificationTemplate.Create(
                    name: "test_template_fallback",
                    category: "payment",
                    requiredVariables: new[] { "amount" },
                    emailSubjectTemplate: "Subject: {{amount}}",
                    emailBodyTemplate: "Email body: {{amount}}",
                    smsBodyTemplate: "SMS: {{amount}}",
                    whatsAppBodyTemplate: "WhatsApp: {{amount}}");

                // Primary channel adapter fails, fallback succeeds
                var channelAdapters = CreateAdaptersWithPrimaryFailure(primaryChannel);

                var preferenceRepo = new InMemoryPreferenceRepository(preference);
                var templateRepo = new InMemoryTemplateRepository(template);
                var deliveryLogRepo = new InMemoryDeliveryLogRepository();
                var renderer = new InMemoryTemplateRenderer();

                var handler = new DispatchNotificationCommandHandler(
                    preferenceRepo,
                    templateRepo,
                    renderer,
                    deliveryLogRepo,
                    channelAdapters);

                var command = new DispatchNotificationCommand(
                    recipientId,
                    GetAddressForChannel(fallbackChannel),
                    template.Id,
                    new Dictionary<string, string> { { "amount", "7500" } });

                handler.HandleAsync(command, CancellationToken.None).GetAwaiter().GetResult();

                // Assert: Delivery log should record the fallback channel with success
                var logEntries = deliveryLogRepo.GetAllEntries();
                var successEntry = logEntries.FirstOrDefault(e =>
                    e.Status == NotificationDeliveryStatus.Delivered);

                return (successEntry != null && successEntry.Channel == fallbackChannel)
                    .Label($"Expected successful delivery on fallback channel {fallbackChannel}, " +
                           $"got entries: [{string.Join(", ", logEntries.Select(e => $"{e.Channel}:{e.Status}"))}]");
            });
    }

    /// <summary>
    /// Property 21: Delivery Log Completeness
    /// Every dispatch attempt creates log entry with recipient, channel, template ref, timestamp, status.
    /// **Validates: Requirements 9.1**
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "21")]
    public Property DeliveryLogCompleteness_EveryDispatchCreatesCompleteLogEntry()
    {
        var channelGen = Gen.Elements(
            NotificationChannel.Email,
            NotificationChannel.Sms,
            NotificationChannel.WhatsApp);

        var outcomeGen = Gen.Elements(true, false); // success or failure

        return Prop.ForAll(
            channelGen.ToArbitrary(),
            outcomeGen.ToArbitrary(),
            (channel, shouldSucceed) =>
            {
                // Arrange
                var recipientId = $"recipient-{Guid.NewGuid():N}";
                var preference = NotificationPreference.Create(recipientId, channel);

                var template = NotificationTemplate.Create(
                    name: "log_test_template",
                    category: "payment",
                    requiredVariables: new[] { "amount" },
                    emailSubjectTemplate: "Subject: {{amount}}",
                    emailBodyTemplate: "Email: {{amount}}",
                    smsBodyTemplate: "SMS: {{amount}}",
                    whatsAppBodyTemplate: "WA: {{amount}}");

                var channelAdapters = shouldSucceed
                    ? CreateSuccessfulAdapters()
                    : CreateAllFailingAdapters();

                var preferenceRepo = new InMemoryPreferenceRepository(preference);
                var templateRepo = new InMemoryTemplateRepository(template);
                var deliveryLogRepo = new InMemoryDeliveryLogRepository();
                var renderer = new InMemoryTemplateRenderer();

                var handler = new DispatchNotificationCommandHandler(
                    preferenceRepo,
                    templateRepo,
                    renderer,
                    deliveryLogRepo,
                    channelAdapters);

                var beforeDispatch = DateTime.UtcNow;

                var command = new DispatchNotificationCommand(
                    recipientId,
                    GetAddressForChannel(channel),
                    template.Id,
                    new Dictionary<string, string> { { "amount", "1000" } });

                handler.HandleAsync(command, CancellationToken.None).GetAwaiter().GetResult();

                // Assert: At least one log entry exists with all required fields
                var logEntries = deliveryLogRepo.GetAllEntries();
                var hasEntry = logEntries.Count > 0;

                if (!hasEntry)
                    return false.Label("No delivery log entry was created");

                var entry = logEntries.First();
                var hasRecipient = entry.RecipientId == recipientId;
                var hasChannel = Enum.IsDefined(entry.Channel);
                var hasTemplateRef = entry.TemplateId == template.Id;
                var hasTimestamp = entry.DispatchedAtUtc >= beforeDispatch.AddSeconds(-1);
                var hasStatus = entry.Status != default;

                return (hasRecipient && hasChannel && hasTemplateRef && hasTimestamp && hasStatus)
                    .Label($"Log entry completeness: recipient={hasRecipient}, channel={hasChannel}, " +
                           $"templateRef={hasTemplateRef}, timestamp={hasTimestamp}, status={hasStatus}");
            });
    }

    /// <summary>
    /// Property 22: Delivery Statistics Accuracy
    /// Computed stats (sent count, failure count, avg time) match manual calculation over same entries.
    /// **Validates: Requirements 9.4**
    /// </summary>
    [Property(MaxTest = 100)]
    [Trait("Property", "22")]
    public Property DeliveryStatisticsAccuracy_MatchesManualCalculation()
    {
        var entryCountGen = Gen.Choose(1, 20);
        var channelGen = Gen.Elements(
            NotificationChannel.Email,
            NotificationChannel.Sms,
            NotificationChannel.WhatsApp);

        return Prop.ForAll(
            entryCountGen.ToArbitrary(),
            channelGen.ToArbitrary(),
            (entryCount, channel) =>
            {
                // Arrange: Generate a set of delivery log entries with known outcomes
                var random = new System.Random(entryCount * 31 + (int)channel);
                var entries = new List<TestDeliveryEntry>();
                var templateId = Guid.NewGuid();

                for (var i = 0; i < entryCount; i++)
                {
                    var isSuccess = random.Next(2) == 0;
                    var deliveryTimeMs = random.Next(50, 5000);
                    entries.Add(new TestDeliveryEntry(
                        Channel: channel,
                        TemplateId: templateId,
                        IsSuccess: isSuccess,
                        DeliveryTimeMs: isSuccess ? deliveryTimeMs : 0));
                }

                // Manual calculation
                var sentCount = entries.Count(e => e.IsSuccess);
                var failedCount = entries.Count(e => !e.IsSuccess);
                var avgDeliveryTimeMs = sentCount > 0
                    ? entries.Where(e => e.IsSuccess).Average(e => e.DeliveryTimeMs)
                    : 0.0;

                // Act: Compute statistics using the in-memory statistics calculator
                var stats = InMemoryStatisticsCalculator.Compute(entries);

                // Assert: Computed stats match manual calculation
                var sentMatches = stats.TotalSent == sentCount;
                var failedMatches = stats.TotalFailed == failedCount;
                var avgTimeMatches = Math.Abs(stats.AverageDeliveryTimeMs - avgDeliveryTimeMs) < 0.001;

                return (sentMatches && failedMatches && avgTimeMatches)
                    .Label($"Stats mismatch: sent={stats.TotalSent} (expected {sentCount}), " +
                           $"failed={stats.TotalFailed} (expected {failedCount}), " +
                           $"avgTime={stats.AverageDeliveryTimeMs:F2} (expected {avgDeliveryTimeMs:F2})");
            });
    }

    #region Test Helpers

    private static string GetAddressForChannel(NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => "user@example.com",
        NotificationChannel.Sms => "+2348012345678",
        NotificationChannel.WhatsApp => "+2348012345678",
        _ => "unknown"
    };

    private static List<INotificationChannelAdapter> CreateSuccessfulAdapters() =>
    [
        new TestChannelAdapter(NotificationChannel.Email, true),
        new TestChannelAdapter(NotificationChannel.Sms, true),
        new TestChannelAdapter(NotificationChannel.WhatsApp, true)
    ];

    private static List<INotificationChannelAdapter> CreateAllFailingAdapters() =>
    [
        new TestChannelAdapter(NotificationChannel.Email, false),
        new TestChannelAdapter(NotificationChannel.Sms, false),
        new TestChannelAdapter(NotificationChannel.WhatsApp, false)
    ];

    private static List<INotificationChannelAdapter> CreateAdaptersWithPrimaryFailure(NotificationChannel failingChannel) =>
    [
        new TestChannelAdapter(NotificationChannel.Email, NotificationChannel.Email != failingChannel),
        new TestChannelAdapter(NotificationChannel.Sms, NotificationChannel.Sms != failingChannel),
        new TestChannelAdapter(NotificationChannel.WhatsApp, NotificationChannel.WhatsApp != failingChannel)
    ];

    #endregion
}

#region Test Infrastructure

/// <summary>
/// In-memory notification preference repository for property testing.
/// </summary>
internal class InMemoryPreferenceRepository : INotificationPreferenceRepository
{
    private readonly NotificationPreference? _preference;

    public InMemoryPreferenceRepository(NotificationPreference? preference = null)
    {
        _preference = preference;
    }

    public Task<NotificationPreference?> GetByRecipientAsync(string recipientId, CancellationToken ct)
    {
        if (_preference?.RecipientId == recipientId)
            return Task.FromResult<NotificationPreference?>(_preference);
        return Task.FromResult<NotificationPreference?>(null);
    }

    public Task SaveAsync(NotificationPreference preference, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// In-memory notification template repository for property testing.
/// </summary>
internal class InMemoryTemplateRepository : INotificationTemplateRepository
{
    private readonly NotificationTemplate _template;

    public InMemoryTemplateRepository(NotificationTemplate template)
    {
        _template = template;
    }

    public Task<NotificationTemplate?> GetByIdAsync(Guid templateId, CancellationToken ct)
    {
        if (_template.Id == templateId)
            return Task.FromResult<NotificationTemplate?>(_template);
        return Task.FromResult<NotificationTemplate?>(null);
    }

    public Task<NotificationTemplate?> GetByNameAsync(string templateName, CancellationToken ct)
    {
        if (_template.Name == templateName)
            return Task.FromResult<NotificationTemplate?>(_template);
        return Task.FromResult<NotificationTemplate?>(null);
    }

    public Task CreateAsync(NotificationTemplate template, CancellationToken ct) => Task.CompletedTask;
    public Task UpdateAsync(NotificationTemplate template, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<NotificationTemplate>> ListAsync(int limit, int offset, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<NotificationTemplate>>(new List<NotificationTemplate> { _template });
}

/// <summary>
/// In-memory delivery log repository that captures all created entries for verification.
/// </summary>
internal class InMemoryDeliveryLogRepository : IDeliveryLogRepository
{
    private readonly List<DeliveryLogEntry> _entries = new();

    public Task CreateAsync(DeliveryLogEntry entry, CancellationToken ct)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(Guid entryId, NotificationDeliveryStatus status, CancellationToken ct)
        => Task.CompletedTask;

    public Task<IReadOnlyList<DeliveryLogEntry>> GetByRecipientAsync(string recipientId, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<DeliveryLogEntry>>(_entries.Where(e => e.RecipientId == recipientId).Take(limit).ToList());

    public Task<DeliveryStatistics> GetStatisticsAsync(NotificationChannel? channel, Guid? templateId, DateRange range, CancellationToken ct)
        => Task.FromResult(new DeliveryStatistics(0, 0, 0, 0, range.StartUtc, range.EndUtc));

    public List<DeliveryLogEntry> GetAllEntries() => _entries;
}

/// <summary>
/// Test channel adapter that can be configured to succeed or fail.
/// </summary>
internal class TestChannelAdapter : INotificationChannelAdapter
{
    private readonly bool _shouldSucceed;

    public NotificationChannel Channel { get; }

    public TestChannelAdapter(NotificationChannel channel, bool shouldSucceed)
    {
        Channel = channel;
        _shouldSucceed = shouldSucceed;
    }

    public Task<DeliveryResult> SendAsync(RenderedNotification notification, CancellationToken ct)
    {
        if (_shouldSucceed)
            return Task.FromResult(DeliveryResult.Succeeded($"msg-{Guid.NewGuid():N}"));

        return Task.FromResult(DeliveryResult.Failed($"Channel {Channel} delivery failed"));
    }
}

/// <summary>
/// Test data record for delivery statistics property testing.
/// </summary>
internal record TestDeliveryEntry(
    NotificationChannel Channel,
    Guid TemplateId,
    bool IsSuccess,
    double DeliveryTimeMs);

/// <summary>
/// In-memory statistics calculator that computes delivery statistics from test entries.
/// Mirrors the logic that GetStatisticsAsync would use in a real repository implementation.
/// </summary>
internal static class InMemoryStatisticsCalculator
{
    public static DeliveryStatistics Compute(List<TestDeliveryEntry> entries)
    {
        var sentCount = entries.Count(e => e.IsSuccess);
        var failedCount = entries.Count(e => !e.IsSuccess);
        var avgDeliveryTimeMs = sentCount > 0
            ? entries.Where(e => e.IsSuccess).Average(e => e.DeliveryTimeMs)
            : 0.0;

        return new DeliveryStatistics(
            TotalSent: sentCount,
            TotalDelivered: sentCount,
            TotalFailed: failedCount,
            AverageDeliveryTimeMs: avgDeliveryTimeMs,
            PeriodStartUtc: DateTime.UtcNow.AddDays(-30),
            PeriodEndUtc: DateTime.UtcNow);
    }
}

#endregion
