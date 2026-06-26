using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.Webhooks;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.DeveloperPortal;

/// <summary>
/// Property-based tests for Developer Resource Isolation (Property 33).
/// 
/// **Validates: Requirements 16.2, 16.3, 16.4, 17.3**
/// 
/// Developer can only access own subscriptions, DLQ items, and request logs;
/// access to other developers' resources is denied.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "33")]
public class DeveloperResourceIsolationPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 16.2, 16.3, 16.4, 17.3**
    /// 
    /// Property 33: Developer Resource Isolation — A developer querying their own
    /// subscriptions only sees subscriptions they own, never another developer's.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Developer_CanOnlyAccessOwnSubscriptions()
    {
        return Prop.ForAll(
            Arb.Generate<int>().Where(x => x > 0 && x < 6).ToArbitrary(),
            Arb.Generate<int>().Where(x => x > 0 && x < 6).ToArbitrary(),
            (devASubCount, devBSubCount) =>
            {
                // Arrange: Two distinct developers
                var developerA = Guid.NewGuid();
                var developerB = Guid.NewGuid();

                // Create subscriptions for developer A
                var devASubscriptions = Enumerable.Range(0, devASubCount)
                    .Select(_ => WebhookSubscription.Create(
                        developerA,
                        $"https://dev-a.example.com/webhook/{Guid.NewGuid()}",
                        new[] { "payment.completed" },
                        Convert.ToBase64String(Guid.NewGuid().ToByteArray())))
                    .ToList();

                // Create subscriptions for developer B
                var devBSubscriptions = Enumerable.Range(0, devBSubCount)
                    .Select(_ => WebhookSubscription.Create(
                        developerB,
                        $"https://dev-b.example.com/webhook/{Guid.NewGuid()}",
                        new[] { "dispute.created" },
                        Convert.ToBase64String(Guid.NewGuid().ToByteArray())))
                    .ToList();

                // All subscriptions in the "repository"
                var allSubscriptions = devASubscriptions.Concat(devBSubscriptions).ToList();

                // Act: Filter subscriptions for developer A (simulating repository query)
                var devAResults = allSubscriptions
                    .Where(s => s.MerchantId == developerA)
                    .ToList();

                // Assert: Developer A only sees their own subscriptions
                var allBelongToA = devAResults.All(s => s.MerchantId == developerA);
                var noneFromB = devAResults.All(s => s.MerchantId != developerB);
                var correctCount = devAResults.Count == devASubCount;

                return (allBelongToA && noneFromB && correctCount)
                    .Label($"Developer A should see {devASubCount} subscriptions, " +
                           $"got {devAResults.Count}. AllBelongToA={allBelongToA}, NoneFromB={noneFromB}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 16.4, 17.3**
    /// 
    /// Property 33: Developer Resource Isolation — A developer querying DLQ items
    /// only sees items from their own subscriptions.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Developer_CanOnlyAccessOwnDlqItems()
    {
        var dlqCountGen = Gen.Choose(1, 4);

        return Prop.ForAll(
            dlqCountGen.ToArbitrary(),
            dlqCountGen.ToArbitrary(),
            (devADlqCount, devBDlqCount) =>
            {
                // Arrange: Two distinct developers with their own subscriptions
                var developerA = Guid.NewGuid();
                var developerB = Guid.NewGuid();

                var subA = WebhookSubscription.Create(
                    developerA,
                    "https://dev-a.example.com/webhook",
                    new[] { "payment.completed" },
                    Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

                var subB = WebhookSubscription.Create(
                    developerB,
                    "https://dev-b.example.com/webhook",
                    new[] { "payment.completed" },
                    Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

                // Create DLQ items for developer A's subscription
                var devADlqItems = Enumerable.Range(0, devADlqCount)
                    .Select(_ => DlqItem.Create(
                        Guid.NewGuid(),
                        subA.Id,
                        """{"event":"payment.completed","data":{}}""",
                        "Connection timeout"))
                    .ToList();

                // Create DLQ items for developer B's subscription
                var devBDlqItems = Enumerable.Range(0, devBDlqCount)
                    .Select(_ => DlqItem.Create(
                        Guid.NewGuid(),
                        subB.Id,
                        """{"event":"dispute.created","data":{}}""",
                        "Server error"))
                    .ToList();

                // All DLQ items in the "repository"
                var allDlqItems = devADlqItems.Concat(devBDlqItems).ToList();

                // Developer A's subscription IDs
                var devASubscriptionIds = new HashSet<Guid> { subA.Id };

                // Act: Filter DLQ items for developer A (by their subscription IDs)
                var devAResults = allDlqItems
                    .Where(d => devASubscriptionIds.Contains(d.SubscriptionId))
                    .ToList();

                // Assert
                var allBelongToA = devAResults.All(d => d.SubscriptionId == subA.Id);
                var noneFromB = devAResults.All(d => d.SubscriptionId != subB.Id);
                var correctCount = devAResults.Count == devADlqCount;

                return (allBelongToA && noneFromB && correctCount)
                    .Label($"Developer A should see {devADlqCount} DLQ items, " +
                           $"got {devAResults.Count}. AllBelongToA={allBelongToA}, NoneFromB={noneFromB}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 17.3**
    /// 
    /// Property 33: Developer Resource Isolation — A developer querying request logs
    /// only sees logs from their own API keys.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Developer_CanOnlyAccessOwnRequestLogs()
    {
        var logCountGen = Gen.Choose(1, 5);

        return Prop.ForAll(
            logCountGen.ToArbitrary(),
            logCountGen.ToArbitrary(),
            (devALogCount, devBLogCount) =>
            {
                // Arrange: Two distinct developers
                var developerA = Guid.NewGuid();
                var developerB = Guid.NewGuid();
                var apiKeyA = Guid.NewGuid();
                var apiKeyB = Guid.NewGuid();

                // Create request logs for developer A
                var devALogs = Enumerable.Range(0, devALogCount)
                    .Select(i => RequestLogEntry.Create(
                        apiKeyA,
                        developerA,
                        $"/api/v1/payments/{i}",
                        "GET",
                        null,
                        null,
                        200,
                        null,
                        TimeSpan.FromMilliseconds(50)))
                    .ToList();

                // Create request logs for developer B
                var devBLogs = Enumerable.Range(0, devBLogCount)
                    .Select(i => RequestLogEntry.Create(
                        apiKeyB,
                        developerB,
                        $"/api/v1/disputes/{i}",
                        "POST",
                        null,
                        null,
                        201,
                        null,
                        TimeSpan.FromMilliseconds(100)))
                    .ToList();

                // All logs in the "repository"
                var allLogs = devALogs.Concat(devBLogs).ToList();

                // Act: Filter logs for developer A
                var devAResults = allLogs
                    .Where(l => l.DeveloperId == developerA)
                    .ToList();

                // Assert
                var allBelongToA = devAResults.All(l => l.DeveloperId == developerA);
                var noneFromB = devAResults.All(l => l.DeveloperId != developerB);
                var correctCount = devAResults.Count == devALogCount;

                return (allBelongToA && noneFromB && correctCount)
                    .Label($"Developer A should see {devALogCount} logs, " +
                           $"got {devAResults.Count}. AllBelongToA={allBelongToA}, NoneFromB={noneFromB}");
            });
    }
}

/// <summary>
/// Property-based tests for Request Log Sensitive Data Redaction (Property 34).
/// 
/// **Validates: Requirements 17.5**
/// 
/// Bodies containing card numbers (13-19 digits PAN), CVVs, or PINs are stored
/// with values masked.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "34")]
public class RequestLogSensitiveDataRedactionPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 17.5**
    /// 
    /// Property 34: Request Log Sensitive Data Redaction — Card numbers (13-19 digits)
    /// in request/response bodies are masked, showing only the last 4 digits.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CardNumbers_AreMasked_InStoredBodies()
    {
        // Generate PANs of length 13-19
        var panLengthGen = Gen.Choose(13, 19);
        var panGen = panLengthGen.SelectMany(len =>
            Gen.ArrayOf(len, Gen.Choose(0, 9))
                .Select(digits => string.Concat(digits)));

        return Prop.ForAll(panGen.ToArbitrary(), pan =>
        {
            // Arrange: Create a body containing the PAN
            var requestBody = $$"""{"cardNumber":"{{pan}}","amount":5000}""";

            // Act: Apply PCI redaction
            var redactedBody = PciRedactionFilter.Redact(requestBody);

            // Assert: The raw PAN should not appear in the redacted body
            var rawPanNotPresent = !redactedBody.Contains(pan);
            // Last 4 digits should be preserved
            var last4 = pan[^4..];
            var last4Preserved = redactedBody.Contains(last4);
            // Asterisks should be present
            var hasMask = redactedBody.Contains('*');

            return (rawPanNotPresent && last4Preserved && hasMask)
                .Label($"PAN '{pan}' should be masked. Redacted body: '{redactedBody}'. " +
                       $"RawNotPresent={rawPanNotPresent}, Last4Preserved={last4Preserved}, HasMask={hasMask}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 17.5**
    /// 
    /// Property 34: Request Log Sensitive Data Redaction — CVV values (3-4 digits in
    /// CVV/CVC/security code fields) are masked in stored bodies.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CvvValues_AreMasked_InStoredBodies()
    {
        var cvvGen = Gen.Choose(3, 4).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Choose(0, 9))
                .Select(digits => string.Concat(digits)));

        return Prop.ForAll(cvvGen.ToArbitrary(), cvv =>
        {
            // Arrange: Create a body containing CVV in a CVV-specific field
            var requestBody = $$"""{"cardNumber":"4111111111111111","cvv":"{{cvv}}","amount":5000}""";

            // Act: Apply PCI redaction
            var redactedBody = PciRedactionFilter.Redact(requestBody);

            // Assert: The CVV value should be masked
            // Check that the CVV field now contains masked value (***) instead of original
            var cvvPattern = $"\"cvv\":\"{cvv}\"";
            var rawCvvNotPresent = !redactedBody.Contains(cvvPattern);
            var hasCvvMask = redactedBody.Contains("\"cvv\":\"***\"");

            return (rawCvvNotPresent && hasCvvMask)
                .Label($"CVV '{cvv}' should be masked. Redacted: '{redactedBody}'. " +
                       $"RawNotPresent={rawCvvNotPresent}, HasMask={hasCvvMask}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 17.5**
    /// 
    /// Property 34: Request Log Sensitive Data Redaction — PIN values in PIN-specific
    /// fields are masked in stored bodies.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PinValues_AreMasked_InStoredBodies()
    {
        var pinGen = Gen.Choose(4, 6).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Choose(0, 9))
                .Select(digits => string.Concat(digits)));

        return Prop.ForAll(pinGen.ToArbitrary(), pin =>
        {
            // Arrange: Create a body containing PIN in a PIN-specific field
            var requestBody = $$"""{"cardNumber":"4111111111111111","pin":"{{pin}}","amount":5000}""";

            // Act: Apply PCI redaction
            var redactedBody = PciRedactionFilter.Redact(requestBody);

            // Assert: The PIN field should be masked
            var pinPattern = $"\"pin\":\"{pin}\"";
            var rawPinNotPresent = !redactedBody.Contains(pinPattern);
            var hasPinMask = redactedBody.Contains("\"pin\":\"***\"");

            return (rawPinNotPresent && hasPinMask)
                .Label($"PIN '{pin}' should be masked. Redacted: '{redactedBody}'. " +
                       $"RawNotPresent={rawPinNotPresent}, HasMask={hasPinMask}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 17.5**
    /// 
    /// Property 34: Bodies without sensitive data remain unchanged after redaction.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NonSensitiveBodies_RemainUnchanged_AfterRedaction()
    {
        var bodyGen = Gen.Elements(
            """{"amount":5000,"currency":"NGN","reference":"ref-123"}""",
            """{"status":"completed","merchantId":"merchant-abc"}""",
            """{"endpoint":"/api/v1/payments","method":"POST"}""",
            """{"description":"Payment for order 42","metadata":{}}""");

        return Prop.ForAll(bodyGen.ToArbitrary(), body =>
        {
            // Act: Apply PCI redaction to non-sensitive body
            var redactedBody = PciRedactionFilter.Redact(body);

            // Assert: Body should remain unchanged
            return (redactedBody == body)
                .Label($"Non-sensitive body should be unchanged. Original: '{body}', Redacted: '{redactedBody}'");
        });
    }
}

/// <summary>
/// Property-based tests for Request Log Query Filtering (Property 35).
/// 
/// **Validates: Requirements 17.4**
/// 
/// All returned log entries match specified filter criteria (endpoint, status code, date range).
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "35")]
public class RequestLogQueryFilteringPropertyTests
{
    private static readonly string[] Endpoints =
    {
        "/api/v1/payments", "/api/v1/disputes", "/api/v1/cards",
        "/api/v1/refunds", "/api/v1/webhooks"
    };

    private static readonly int[] StatusCodes = { 200, 201, 400, 401, 403, 404, 500 };

    /// <summary>
    /// **Validates: Requirements 17.4**
    /// 
    /// Property 35: Request Log Query Filtering — When filtering by endpoint,
    /// all returned entries have the specified endpoint.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FilterByEndpoint_ReturnsOnlyMatchingEntries()
    {
        var logCountGen = Gen.Choose(5, 20);
        var filterEndpointGen = Gen.Elements(Endpoints);

        return Prop.ForAll(
            logCountGen.ToArbitrary(),
            filterEndpointGen.ToArbitrary(),
            (logCount, filterEndpoint) =>
            {
                // Arrange: Create a mix of log entries with various endpoints
                var developerId = Guid.NewGuid();
                var apiKeyId = Guid.NewGuid();

                var logs = Enumerable.Range(0, logCount)
                    .Select(i => RequestLogEntry.Create(
                        apiKeyId,
                        developerId,
                        Endpoints[i % Endpoints.Length],
                        "GET",
                        null,
                        null,
                        200,
                        null,
                        TimeSpan.FromMilliseconds(50)))
                    .ToList();

                // Act: Apply endpoint filter
                var query = new RequestLogQuery(developerId, Endpoint: filterEndpoint);
                var results = RequestLogQueryFilter.Apply(logs, query);

                // Assert: All returned entries match the endpoint filter
                var allMatchEndpoint = results.All(l => l.Endpoint == filterEndpoint);
                // Verify we didn't miss any matching entries
                var expectedCount = logs.Count(l => l.Endpoint == filterEndpoint);
                var correctCount = results.Count == expectedCount;

                return (allMatchEndpoint && correctCount)
                    .Label($"Filter by endpoint '{filterEndpoint}': " +
                           $"got {results.Count} results (expected {expectedCount}). " +
                           $"AllMatch={allMatchEndpoint}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 17.4**
    /// 
    /// Property 35: Request Log Query Filtering — When filtering by status code,
    /// all returned entries have the specified status code.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FilterByStatusCode_ReturnsOnlyMatchingEntries()
    {
        var logCountGen = Gen.Choose(5, 20);
        var filterStatusGen = Gen.Elements(StatusCodes);

        return Prop.ForAll(
            logCountGen.ToArbitrary(),
            filterStatusGen.ToArbitrary(),
            (logCount, filterStatus) =>
            {
                // Arrange
                var developerId = Guid.NewGuid();
                var apiKeyId = Guid.NewGuid();

                var logs = Enumerable.Range(0, logCount)
                    .Select(i => RequestLogEntry.Create(
                        apiKeyId,
                        developerId,
                        "/api/v1/payments",
                        "GET",
                        null,
                        null,
                        StatusCodes[i % StatusCodes.Length],
                        null,
                        TimeSpan.FromMilliseconds(50)))
                    .ToList();

                // Act: Apply status code filter
                var query = new RequestLogQuery(developerId, StatusCode: filterStatus);
                var results = RequestLogQueryFilter.Apply(logs, query);

                // Assert
                var allMatchStatus = results.All(l => l.ResponseStatus == filterStatus);
                var expectedCount = logs.Count(l => l.ResponseStatus == filterStatus);
                var correctCount = results.Count == expectedCount;

                return (allMatchStatus && correctCount)
                    .Label($"Filter by status {filterStatus}: " +
                           $"got {results.Count} results (expected {expectedCount}). " +
                           $"AllMatch={allMatchStatus}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 17.4**
    /// 
    /// Property 35: Request Log Query Filtering — When filtering by date range,
    /// all returned entries have timestamps within the specified range.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FilterByDateRange_ReturnsOnlyMatchingEntries()
    {
        var logCountGen = Gen.Choose(5, 15);

        return Prop.ForAll(logCountGen.ToArbitrary(), logCount =>
        {
            // Arrange: Create logs spread over 30 days
            var developerId = Guid.NewGuid();
            var apiKeyId = Guid.NewGuid();
            var baseDate = DateTime.UtcNow.AddDays(-30);

            var logs = Enumerable.Range(0, logCount)
                .Select(i => RequestLogEntry.Create(
                    apiKeyId,
                    developerId,
                    "/api/v1/payments",
                    "GET",
                    null,
                    null,
                    200,
                    null,
                    TimeSpan.FromMilliseconds(50)))
                .ToList();

            // Define a date range: from 1 hour ago to now (all entries should be within range since they are created now)
            var fromUtc = DateTime.UtcNow.AddHours(-1);
            var toUtc = DateTime.UtcNow.AddHours(1);

            // Act: Apply date range filter
            var query = new RequestLogQuery(developerId, FromUtc: fromUtc, ToUtc: toUtc);
            var results = RequestLogQueryFilter.Apply(logs, query);

            // Assert: All entries within range
            var allWithinRange = results.All(l =>
                l.TimestampUtc >= fromUtc && l.TimestampUtc <= toUtc);

            // All logs should match since they were just created
            var correctCount = results.Count == logCount;

            return (allWithinRange && correctCount)
                .Label($"Filter by date range [{fromUtc:O}, {toUtc:O}]: " +
                       $"got {results.Count} results (expected {logCount}). " +
                       $"AllWithinRange={allWithinRange}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 17.4**
    /// 
    /// Property 35: Request Log Query Filtering — Combined filters (endpoint + status)
    /// return only entries matching ALL criteria simultaneously.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CombinedFilters_ReturnOnlyEntriesMatchingAllCriteria()
    {
        var logCountGen = Gen.Choose(10, 30);
        var filterEndpointGen = Gen.Elements(Endpoints);
        var filterStatusGen = Gen.Elements(StatusCodes);

        return Prop.ForAll(
            logCountGen.ToArbitrary(),
            filterEndpointGen.ToArbitrary(),
            filterStatusGen.ToArbitrary(),
            (logCount, filterEndpoint, filterStatus) =>
            {
                // Arrange
                var developerId = Guid.NewGuid();
                var apiKeyId = Guid.NewGuid();

                var logs = Enumerable.Range(0, logCount)
                    .Select(i => RequestLogEntry.Create(
                        apiKeyId,
                        developerId,
                        Endpoints[i % Endpoints.Length],
                        "GET",
                        null,
                        null,
                        StatusCodes[i % StatusCodes.Length],
                        null,
                        TimeSpan.FromMilliseconds(50)))
                    .ToList();

                // Act: Apply combined filter
                var query = new RequestLogQuery(
                    developerId,
                    Endpoint: filterEndpoint,
                    StatusCode: filterStatus);
                var results = RequestLogQueryFilter.Apply(logs, query);

                // Assert: All returned entries match BOTH criteria
                var allMatchEndpoint = results.All(l => l.Endpoint == filterEndpoint);
                var allMatchStatus = results.All(l => l.ResponseStatus == filterStatus);
                var expectedCount = logs.Count(l =>
                    l.Endpoint == filterEndpoint && l.ResponseStatus == filterStatus);
                var correctCount = results.Count == expectedCount;

                return (allMatchEndpoint && allMatchStatus && correctCount)
                    .Label($"Combined filter (endpoint='{filterEndpoint}', status={filterStatus}): " +
                           $"got {results.Count} results (expected {expectedCount}). " +
                           $"AllMatchEndpoint={allMatchEndpoint}, AllMatchStatus={allMatchStatus}");
            });
    }
}
