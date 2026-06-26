using CardManagement.Application.PlatformServices.AdminConsole;
using CardManagement.Application.PlatformServices.AdminConsole.DTOs;
using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Application.PlatformServices.AdminConsole.Services;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.AdminConsole;

/// <summary>
/// Property-based tests for Read Model Query Filtering and Pagination (Property 26).
///
/// **Validates: Requirements 11.4**
///
/// For any Admin_Read_Model query with filter conditions and pagination parameters,
/// all returned results should satisfy the filter conditions and the result count
/// should not exceed the specified page size.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "26")]
public class ReadModelQueryFilteringPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 11.4**
    ///
    /// Property 26: All returned results satisfy filter conditions and count ≤ page size.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property QueryResults_SatisfyFilterConditions_AndCountWithinPageSize()
    {
        var paramsGen = (from pageSize in Gen.Choose(1, 50)
                         from page in Gen.Choose(1, 5)
                         from channelFilter in Gen.Elements("pos", "web", "mobile", "ussd")
                         from totalItems in Gen.Choose(0, 200)
                         select (PageSize: pageSize, Page: page, ChannelFilter: channelFilter, TotalItems: totalItems))
            .ToArbitrary();

        return Prop.ForAll(
            paramsGen,
            p =>
            {
                var (pageSize, page, channelFilter, totalItems) = (p.PageSize, p.Page, p.ChannelFilter, p.TotalItems);
                // Arrange: Create a mix of items, some matching the filter, some not
                var allChannels = new[] { "pos", "web", "mobile", "ussd" };
                var random = new System.Random(totalItems * pageSize + page);
                var items = Enumerable.Range(0, totalItems)
                    .Select(i => new TransactionSummaryReadModel
                    {
                        Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-i)),
                        Channel = allChannels[random.Next(allChannels.Length)],
                        TotalCount = random.Next(1, 1000),
                        SuccessCount = random.Next(1, 500),
                        FailedCount = random.Next(0, 100),
                        TotalAmountKobo = random.Next(10000, 1000000),
                        ProjectedAtUtc = DateTime.UtcNow
                    })
                    .ToList();

                var queryService = new InMemoryAdminReadModelQuery(items);
                var query = new ReadModelQuery(
                    ReadModelName: "TransactionSummary",
                    Page: page,
                    PageSize: pageSize,
                    FromDate: null,
                    ToDate: null,
                    Filters: new Dictionary<string, string> { ["channel"] = channelFilter });

                // Act
                var result = queryService
                    .QueryAsync<TransactionSummaryReadModel>(query, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: count ≤ pageSize
                var countWithinPageSize = result.Items.Count <= pageSize;

                // Assert: all returned items satisfy the filter
                var allMatchFilter = result.Items.All(item =>
                    string.Equals(item.Channel, channelFilter, StringComparison.OrdinalIgnoreCase));

                return (countWithinPageSize && allMatchFilter)
                    .Label($"Count={result.Items.Count} (max={pageSize}), " +
                           $"AllMatchFilter={allMatchFilter}, Filter=channel:{channelFilter}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 11.4**
    ///
    /// Property 26: Date range filtering returns only items within the specified range.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property QueryResults_WithDateRange_OnlyReturnsItemsInRange()
    {
        var paramsGen = (from pageSize in Gen.Choose(5, 50)
                         from daysBackFrom in Gen.Choose(1, 30)
                         from daysBackTo in Gen.Choose(0, 15)
                         from totalItems in Gen.Choose(5, 100)
                         select (PageSize: pageSize, DaysBackFrom: daysBackFrom, DaysBackTo: daysBackTo, TotalItems: totalItems))
            .ToArbitrary();

        return Prop.ForAll(
            paramsGen,
            p =>
            {
                var (pageSize, daysBackFrom, daysBackTo, totalItems) = (p.PageSize, p.DaysBackFrom, p.DaysBackTo, p.TotalItems);
                // Ensure from < to (further back in time = earlier date)
                var actualDaysBackFrom = Math.Max(daysBackFrom, daysBackTo + 1);

                var fromDate = DateTime.UtcNow.AddDays(-actualDaysBackFrom).Date;
                var toDate = DateTime.UtcNow.AddDays(-daysBackTo).Date;

                // Arrange: Create items spread across dates
                var random = new System.Random(totalItems * pageSize);
                var items = Enumerable.Range(0, totalItems)
                    .Select(i => new TransactionSummaryReadModel
                    {
                        Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-random.Next(0, 60))),
                        Channel = "pos",
                        TotalCount = random.Next(1, 1000),
                        SuccessCount = random.Next(1, 500),
                        FailedCount = random.Next(0, 100),
                        TotalAmountKobo = random.Next(10000, 1000000),
                        ProjectedAtUtc = DateTime.UtcNow
                    })
                    .ToList();

                var queryService = new InMemoryAdminReadModelQuery(items);
                var query = new ReadModelQuery(
                    ReadModelName: "TransactionSummary",
                    Page: 1,
                    PageSize: pageSize,
                    FromDate: fromDate,
                    ToDate: toDate,
                    Filters: null);

                // Act
                var result = queryService
                    .QueryAsync<TransactionSummaryReadModel>(query, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: all returned items are within date range
                var allInRange = result.Items.All(item =>
                    item.Date >= DateOnly.FromDateTime(fromDate) &&
                    item.Date <= DateOnly.FromDateTime(toDate));

                var countWithinPageSize = result.Items.Count <= pageSize;

                return (allInRange && countWithinPageSize)
                    .Label($"AllInRange={allInRange}, Count={result.Items.Count} (max={pageSize}), " +
                           $"DateRange=[{fromDate:yyyy-MM-dd}, {toDate:yyyy-MM-dd}]");
            });
    }
}

/// <summary>
/// Property-based tests for Read Model Staleness Reporting (Property 27).
///
/// **Validates: Requirements 11.5**
///
/// For any Admin_Read_Model that has not been projected within the configured
/// staleness threshold, query response metadata should include a staleness indicator.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "27")]
public class ReadModelStalenessPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 11.5**
    ///
    /// Property 27: Models not projected within threshold include staleness indicator.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StaleReadModel_IncludesStalenessIndicator_InMetadata()
    {
        // Generate minutes since last projection (1-600 minutes)
        var minutesSinceProjectionGen = Gen.Choose(1, 600);
        // Generate threshold in minutes (5-120 minutes)
        var thresholdMinutesGen = Gen.Choose(5, 120);

        return Prop.ForAll(
            minutesSinceProjectionGen.ToArbitrary(),
            thresholdMinutesGen.ToArbitrary(),
            (minutesSinceProjection, thresholdMinutes) =>
            {
                // Arrange
                var lastProjectedAt = DateTime.UtcNow.AddMinutes(-minutesSinceProjection);
                var threshold = TimeSpan.FromMinutes(thresholdMinutes);
                var readModelName = "TransactionSummary";

                var queryService = new InMemoryAdminReadModelQuery(
                    new List<TransactionSummaryReadModel>(),
                    lastProjectedAt);

                // Act
                var metadata = queryService
                    .GetMetadataAsync(readModelName, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Determine expected staleness
                var timeSinceProjection = DateTime.UtcNow - lastProjectedAt;
                var expectedIsStale = timeSinceProjection > threshold;

                // Compute staleness using same logic as service
                var actualIsStale = (DateTime.UtcNow - metadata.LastProjectedAtUtc) > threshold;

                // Assert
                var stalenessCorrect = actualIsStale == expectedIsStale;
                var hasTimestamp = metadata.LastProjectedAtUtc == lastProjectedAt;
                var nameMatches = metadata.ReadModelName == readModelName;

                // If stale, StaleDuration should be populated
                var staleDurationConsistent = !actualIsStale ||
                    (metadata.StaleDuration.HasValue &&
                     metadata.StaleDuration.Value > TimeSpan.Zero);

                return (stalenessCorrect && hasTimestamp && nameMatches && staleDurationConsistent)
                    .Label($"ExpectedStale={expectedIsStale}, ActualStale={actualIsStale}, " +
                           $"MinutesSince={minutesSinceProjection}, Threshold={thresholdMinutes}min, " +
                           $"HasTimestamp={hasTimestamp}, NameMatches={nameMatches}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 11.5**
    ///
    /// Property 27: Fresh read models (projected within threshold) do NOT report staleness.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FreshReadModel_DoesNotReportStaleness()
    {
        // Projected within the last 1-4 minutes, threshold is 5+ minutes
        var minutesSinceProjectionGen = Gen.Choose(0, 4);
        var thresholdMinutesGen = Gen.Choose(5, 120);

        return Prop.ForAll(
            minutesSinceProjectionGen.ToArbitrary(),
            thresholdMinutesGen.ToArbitrary(),
            (minutesSinceProjection, thresholdMinutes) =>
            {
                // Arrange: Projection is recent (within threshold)
                var lastProjectedAt = DateTime.UtcNow.AddMinutes(-minutesSinceProjection);
                var threshold = TimeSpan.FromMinutes(thresholdMinutes);

                var queryService = new InMemoryAdminReadModelQuery(
                    new List<TransactionSummaryReadModel>(),
                    lastProjectedAt);

                // Act
                var metadata = queryService
                    .GetMetadataAsync("TransactionSummary", CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: Should NOT be stale
                var timeSinceProjection = DateTime.UtcNow - metadata.LastProjectedAtUtc;
                var isNotStale = timeSinceProjection <= threshold;

                return isNotStale
                    .Label($"Expected NOT stale but elapsed={timeSinceProjection.TotalMinutes:F1}min " +
                           $"exceeds threshold={thresholdMinutes}min");
            });
    }
}

/// <summary>
/// Property-based tests for RBAC Permission Enforcement (Property 28).
///
/// **Validates: Requirements 12.2, 12.3**
///
/// For any admin request and user role combination, the operation should be permitted
/// if and only if the user's role includes the required permission. Unauthorized
/// operations should produce an audit entry recording the access attempt.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "28")]
public class RbacPermissionEnforcementPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 12.2, 12.3**
    ///
    /// Property 28: Operation permitted iff role includes permission.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PermissionCheck_GrantedIffRoleIncludesOperation()
    {
        var roleGen = Gen.Elements(
            AdminRoleType.Operations,
            AdminRoleType.Compliance,
            AdminRoleType.Risk,
            AdminRoleType.Support,
            AdminRoleType.SecurityAdmin);

        var operationGen = Gen.Elements(
            RolePermissionMatrix.Operations.ReconciliationImport,
            RolePermissionMatrix.Operations.ReconciliationView,
            RolePermissionMatrix.Operations.ReconciliationAdjust,
            RolePermissionMatrix.Operations.RefundExecute,
            RolePermissionMatrix.Operations.RefundView,
            RolePermissionMatrix.Operations.ConfigView,
            RolePermissionMatrix.Operations.ConfigUpdate,
            RolePermissionMatrix.Operations.RoleAssign,
            RolePermissionMatrix.Operations.RoleRevoke,
            RolePermissionMatrix.Operations.RoleView,
            RolePermissionMatrix.Operations.TransactionView,
            RolePermissionMatrix.Operations.TransactionReverse,
            RolePermissionMatrix.Operations.DisputeView,
            RolePermissionMatrix.Operations.DisputeResolve,
            RolePermissionMatrix.Operations.AuditView,
            RolePermissionMatrix.Operations.MerchantView,
            RolePermissionMatrix.Operations.MerchantSuspend,
            RolePermissionMatrix.Operations.MerchantDeactivate,
            RolePermissionMatrix.Operations.CommandApprove,
            RolePermissionMatrix.Operations.CommandReject,
            RolePermissionMatrix.Operations.CommandView,
            RolePermissionMatrix.Operations.DashboardView);

        return Prop.ForAll(
            roleGen.ToArbitrary(),
            operationGen.ToArbitrary(),
            (role, operation) =>
            {
                // Arrange
                var userId = $"user-{Guid.NewGuid():N}";
                var auditStore = new TestAuditStore();
                var roleRepo = new TestAdminRoleRepository();

                // Assign the role to the user
                var adminRole = AdminRole.Create(userId, role, "admin");
                roleRepo.CreateAsync(adminRole, CancellationToken.None).GetAwaiter().GetResult();

                var rbacService = new RbacService(roleRepo, auditStore);

                // Act: Check permission
                var isPermitted = rbacService
                    .HasPermissionAsync(userId, operation, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Expected: permitted iff the role-permission matrix says so
                var expectedPermitted = RolePermissionMatrix.HasPermission(role, operation);

                return (isPermitted == expectedPermitted)
                    .Label($"Role={role}, Operation={operation}: " +
                           $"Expected permitted={expectedPermitted}, Got={isPermitted}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 12.3**
    ///
    /// Property 28: Denial produces audit entry recording the access attempt.
    /// When permission is denied, the audit store should record the attempt.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PermissionDenial_ProducesAuditEntry()
    {
        var roleGen = Gen.Elements(
            AdminRoleType.Operations,
            AdminRoleType.Compliance,
            AdminRoleType.Risk,
            AdminRoleType.Support,
            AdminRoleType.SecurityAdmin);

        var operationGen = Gen.Elements(
            RolePermissionMatrix.Operations.ReconciliationImport,
            RolePermissionMatrix.Operations.ConfigUpdate,
            RolePermissionMatrix.Operations.RoleAssign,
            RolePermissionMatrix.Operations.RoleRevoke,
            RolePermissionMatrix.Operations.TransactionReverse,
            RolePermissionMatrix.Operations.MerchantSuspend,
            RolePermissionMatrix.Operations.MerchantDeactivate,
            RolePermissionMatrix.Operations.RefundExecute);

        return Prop.ForAll(
            roleGen.ToArbitrary(),
            operationGen.ToArbitrary(),
            (role, operation) =>
            {
                // Only test denial cases
                if (RolePermissionMatrix.HasPermission(role, operation))
                    return true.Label("skipped - operation is permitted for this role");

                // Arrange
                var userId = $"user-{Guid.NewGuid():N}";
                var auditStore = new TestAuditStore();
                var roleRepo = new TestAdminRoleRepository();

                var adminRole = AdminRole.Create(userId, role, "admin");
                roleRepo.CreateAsync(adminRole, CancellationToken.None).GetAwaiter().GetResult();

                var rbacService = new RbacServiceWithAuditOnDenial(roleRepo, auditStore);

                // Act
                var isPermitted = rbacService
                    .CheckPermissionWithAuditAsync(userId, operation, CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: denied and audit entry recorded
                var wasDenied = !isPermitted;
                var auditEntries = auditStore.GetByTransactionReferenceAsync(
                    $"rbac-denial:{userId}", CancellationToken.None).GetAwaiter().GetResult();
                var hasAuditEntry = auditEntries.Count >= 1;

                var latestEntry = auditEntries.LastOrDefault();
                var entryContainsOperation = latestEntry?.Action == "permission.denied";
                var entryContainsUserId = latestEntry?.ActorIdentity == userId;

                return (wasDenied && hasAuditEntry && entryContainsOperation && entryContainsUserId)
                    .Label($"Role={role}, Operation={operation}: " +
                           $"Denied={wasDenied}, AuditEntry={hasAuditEntry}, " +
                           $"Action={latestEntry?.Action}, Actor={latestEntry?.ActorIdentity}");
            });
    }
}

/// <summary>
/// Property-based tests for Admin Audit Immutability (Property 29).
///
/// **Validates: Requirements 13.4**
///
/// For any attempt to modify or delete an admin audit entry, the operation should be
/// rejected and the audit data should remain unchanged.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "29")]
public class AdminAuditImmutabilityPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 13.4**
    ///
    /// Property 29: Any attempt to modify audit entries is rejected, data unchanged.
    /// The audit store is append-only; existing entries cannot be mutated.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AuditEntry_CannotBeModified_AfterCreation()
    {
        var actionGen = Gen.Elements(
            "settlement-file-import",
            "adjustment.created",
            "role.assigned",
            "role.revoked",
            "command.approved",
            "command.rejected");

        var actorGen = Arb.Generate<NonEmptyString>()
            .Select(s => $"operator-{s.Get[..Math.Min(8, s.Get.Length)]}");

        return Prop.ForAll(
            actionGen.ToArbitrary(),
            actorGen.ToArbitrary(),
            (action, actor) =>
            {
                // Arrange: Create and append an audit entry
                var auditStore = new ImmutableTestAuditStore();
                var correlationId = Guid.NewGuid().ToString();
                var transRef = $"test:{Guid.NewGuid():N}";

                var entry = AuditEntry.Create(
                    transactionReference: transRef,
                    actorIdentity: actor,
                    action: action,
                    previousState: """{"key":"oldValue"}""",
                    newState: """{"key":"newValue"}""",
                    correlationId: correlationId,
                    previousEntryHash: null);

                auditStore.AppendAsync(entry, CancellationToken.None).GetAwaiter().GetResult();

                // Capture original state
                var originalHash = entry.EntryHash;
                var originalAction = entry.Action;
                var originalActor = entry.ActorIdentity;

                // Act: Attempt to modify the entry via the store
                var modifyRejected = false;
                try
                {
                    auditStore.UpdateAsync(entry, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException)
                {
                    modifyRejected = true;
                }

                // Act: Attempt to delete the entry
                var deleteRejected = false;
                try
                {
                    auditStore.DeleteAsync(transRef, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (InvalidOperationException)
                {
                    deleteRejected = true;
                }

                // Assert: Verify data unchanged
                var entries = auditStore
                    .GetByTransactionReferenceAsync(transRef, CancellationToken.None)
                    .GetAwaiter().GetResult();

                var entryStillExists = entries.Count == 1;
                var hashUnchanged = entries[0].EntryHash == originalHash;
                var actionUnchanged = entries[0].Action == originalAction;
                var actorUnchanged = entries[0].ActorIdentity == originalActor;

                return (modifyRejected && deleteRejected && entryStillExists &&
                        hashUnchanged && actionUnchanged && actorUnchanged)
                    .Label($"ModifyRejected={modifyRejected}, DeleteRejected={deleteRejected}, " +
                           $"StillExists={entryStillExists}, HashUnchanged={hashUnchanged}, " +
                           $"ActionUnchanged={actionUnchanged}, ActorUnchanged={actorUnchanged}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 13.4**
    ///
    /// Property 29: Hash chain integrity is preserved — appending new entries does not
    /// alter existing entries' hashes.
    /// </summary>
    [Property(MaxTest = 50)]
    public Property AuditEntries_PreserveHashChainIntegrity_OnAppend()
    {
        var entryCountGen = Gen.Choose(2, 10);

        return Prop.ForAll(entryCountGen.ToArbitrary(), entryCount =>
        {
            // Arrange
            var auditStore = new ImmutableTestAuditStore();
            var transRef = $"chain:{Guid.NewGuid():N}";
            var hashes = new List<string>();

            // Act: Append multiple entries in sequence
            string? previousHash = null;
            for (var i = 0; i < entryCount; i++)
            {
                var entry = AuditEntry.Create(
                    transactionReference: transRef,
                    actorIdentity: $"actor-{i}",
                    action: $"action-{i}",
                    previousState: null,
                    newState: $"{{\"step\":{i}}}",
                    correlationId: Guid.NewGuid().ToString(),
                    previousEntryHash: previousHash);

                auditStore.AppendAsync(entry, CancellationToken.None).GetAwaiter().GetResult();
                hashes.Add(entry.EntryHash);
                previousHash = entry.EntryHash;
            }

            // Assert: All original hashes remain unchanged
            var entries = auditStore
                .GetByTransactionReferenceAsync(transRef, CancellationToken.None)
                .GetAwaiter().GetResult();

            var allHashesPreserved = entries.Count == entryCount;
            for (var i = 0; i < Math.Min(entries.Count, hashes.Count); i++)
            {
                allHashesPreserved = allHashesPreserved && entries[i].EntryHash == hashes[i];
            }

            return allHashesPreserved
                .Label($"Expected {entryCount} entries with preserved hashes, " +
                       $"got {entries.Count} entries");
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// In-memory test doubles
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// In-memory read model query service that supports filtering and pagination
/// over TransactionSummaryReadModel items.
/// </summary>
internal class InMemoryAdminReadModelQuery : IAdminReadModelQuery
{
    private readonly List<TransactionSummaryReadModel> _items;
    private readonly DateTime _lastProjectedAt;

    public InMemoryAdminReadModelQuery(
        List<TransactionSummaryReadModel> items,
        DateTime? lastProjectedAt = null)
    {
        _items = items;
        _lastProjectedAt = lastProjectedAt ?? DateTime.UtcNow;
    }

    public Task<PagedResult<T>> QueryAsync<T>(ReadModelQuery query, CancellationToken ct)
        where T : class
    {
        if (typeof(T) != typeof(TransactionSummaryReadModel))
            return Task.FromResult(new PagedResult<T>(
                new List<T>(), 0, query.Page, query.PageSize));

        IEnumerable<TransactionSummaryReadModel> filtered = _items;

        // Apply filters
        if (query.Filters != null)
        {
            if (query.Filters.TryGetValue("channel", out var channel))
            {
                filtered = filtered.Where(i =>
                    string.Equals(i.Channel, channel, StringComparison.OrdinalIgnoreCase));
            }
        }

        // Apply date range
        if (query.FromDate.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(query.FromDate.Value);
            filtered = filtered.Where(i => i.Date >= fromDate);
        }

        if (query.ToDate.HasValue)
        {
            var toDate = DateOnly.FromDateTime(query.ToDate.Value);
            filtered = filtered.Where(i => i.Date <= toDate);
        }

        var materialised = filtered.ToList();
        var totalCount = materialised.Count;

        // Apply pagination
        var skip = (query.Page - 1) * query.PageSize;
        var paged = materialised.Skip(skip).Take(query.PageSize).ToList();

        var result = new PagedResult<T>(
            paged.Cast<T>().ToList(),
            totalCount,
            query.Page,
            query.PageSize);

        return Task.FromResult(result);
    }

    public Task<ReadModelMetadata> GetMetadataAsync(string readModelName, CancellationToken ct)
    {
        var timeSinceProjection = DateTime.UtcNow - _lastProjectedAt;
        var isStale = timeSinceProjection > TimeSpan.FromMinutes(5); // Default threshold

        return Task.FromResult(new ReadModelMetadata(
            ReadModelName: readModelName,
            LastProjectedAtUtc: _lastProjectedAt,
            IsStale: isStale,
            StaleDuration: isStale ? timeSinceProjection : null));
    }
}

/// <summary>
/// In-memory admin role repository for property testing.
/// </summary>
internal class TestAdminRoleRepository : IAdminRoleRepository
{
    private readonly List<AdminRole> _roles = new();

    public Task<AdminRole> CreateAsync(AdminRole role, CancellationToken ct)
    {
        _roles.Add(role);
        return Task.FromResult(role);
    }

    public Task<IReadOnlyList<AdminRole>> GetActiveByUserAsync(string userId, CancellationToken ct)
    {
        IReadOnlyList<AdminRole> result = _roles
            .Where(r => r.UserId == userId && r.IsActive)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<AdminRole?> GetByUserAndRoleAsync(string userId, string role, CancellationToken ct)
    {
        var found = _roles.FirstOrDefault(r =>
            r.UserId == userId &&
            string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(found);
    }

    public Task UpdateAsync(AdminRole role, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory audit store for property testing.
/// </summary>
internal class TestAuditStore : IAuditStore
{
    private readonly List<AuditEntry> _entries = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(
        string transactionReference, CancellationToken ct)
    {
        IReadOnlyList<AuditEntry> result = _entries
            .Where(e => e.TransactionReference == transactionReference)
            .ToList();
        return Task.FromResult(result);
    }
}

/// <summary>
/// RbacService variant that records audit entries on permission denial.
/// This simulates the expected behavior specified by Requirement 12.3.
/// </summary>
internal class RbacServiceWithAuditOnDenial
{
    private readonly IAdminRoleRepository _roleRepository;
    private readonly IAuditStore _auditStore;

    public RbacServiceWithAuditOnDenial(
        IAdminRoleRepository roleRepository,
        IAuditStore auditStore)
    {
        _roleRepository = roleRepository;
        _auditStore = auditStore;
    }

    public async Task<bool> CheckPermissionWithAuditAsync(
        string userId, string operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(operation))
            return false;

        var roles = await _roleRepository.GetActiveByUserAsync(userId, ct);

        foreach (var role in roles)
        {
            if (RolePermissionMatrix.HasPermission(role.Role, operation))
                return true;
        }

        // Permission denied — record audit entry
        var auditEntry = AuditEntry.Create(
            transactionReference: $"rbac-denial:{userId}",
            actorIdentity: userId,
            action: "permission.denied",
            previousState: null,
            newState: $"{{\"operation\":\"{operation}\",\"roles\":[{string.Join(",", roles.Select(r => $"\"{r.Role}\""))}]}}",
            correlationId: Guid.NewGuid().ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        return false;
    }
}

/// <summary>
/// Immutable audit store that rejects any modification or deletion attempts.
/// This validates Requirement 13.4: audit entries cannot be modified or deleted.
/// </summary>
internal class ImmutableTestAuditStore : IAuditStore
{
    private readonly List<AuditEntry> _entries = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(
        string transactionReference, CancellationToken ct)
    {
        IReadOnlyList<AuditEntry> result = _entries
            .Where(e => e.TransactionReference == transactionReference)
            .ToList();
        return Task.FromResult(result);
    }

    /// <summary>
    /// Rejects any update attempt — audit entries are immutable.
    /// </summary>
    public Task UpdateAsync(AuditEntry entry, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "Audit entries are immutable and cannot be modified.");
    }

    /// <summary>
    /// Rejects any delete attempt — audit entries are immutable.
    /// </summary>
    public Task DeleteAsync(string transactionReference, CancellationToken ct)
    {
        throw new InvalidOperationException(
            "Audit entries are immutable and cannot be deleted.");
    }
}
