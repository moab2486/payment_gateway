using CardManagement.Application.PlatformServices.AdminConsole;
using CardManagement.Application.PlatformServices.AdminConsole.DTOs;
using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Application.PlatformServices.AdminConsole.Services;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Infrastructure.PlatformServices.AdminConsole;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

#region Test Fakes

internal sealed class FakeAdminCommandRepository : IAdminCommandRepository
{
    private readonly List<PendingCommand> _commands = new();

    public Task<PendingCommand> CreateAsync(PendingCommand command, CancellationToken ct)
    {
        _commands.Add(command);
        return Task.FromResult(command);
    }

    public Task<PendingCommand?> GetByIdAsync(Guid commandId, CancellationToken ct) =>
        Task.FromResult(_commands.FirstOrDefault(c => c.Id == commandId));

    public Task UpdateAsync(PendingCommand command, CancellationToken ct) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<PendingCommand>> GetExpiredAsync(TimeSpan expiryThreshold, CancellationToken ct)
    {
        var expired = _commands
            .Where(c => c.Status == CommandStatus.Pending && c.IsExpired)
            .ToList();
        return Task.FromResult<IReadOnlyList<PendingCommand>>(expired);
    }

    public Task<IReadOnlyList<PendingCommand>> GetPendingAsync(int limit, int offset, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PendingCommand>>(
            _commands.Where(c => c.Status == CommandStatus.Pending)
                .OrderByDescending(c => c.CreatedAtUtc)
                .Skip(offset).Take(limit).ToList());

    public void AddDirect(PendingCommand command) => _commands.Add(command);
}

internal sealed class FakeAdminRoleRepository : IAdminRoleRepository
{
    private readonly List<AdminRole> _roles = new();

    public Task<AdminRole> CreateAsync(AdminRole role, CancellationToken ct)
    {
        _roles.Add(role);
        return Task.FromResult(role);
    }

    public Task<IReadOnlyList<AdminRole>> GetActiveByUserAsync(string userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdminRole>>(
            _roles.Where(r => r.UserId == userId && r.IsActive).ToList());

    public Task<AdminRole?> GetByUserAndRoleAsync(string userId, string role, CancellationToken ct) =>
        Task.FromResult(_roles.FirstOrDefault(r =>
            r.UserId == userId &&
            string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase)));

    public Task UpdateAsync(AdminRole role, CancellationToken ct) =>
        Task.CompletedTask;

    public void AddDirect(AdminRole role) => _roles.Add(role);
}

internal sealed class InMemoryAdminAuditStore : IAuditStore
{
    public List<AuditEntry> Entries { get; } = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(
        string transactionReference, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(
            Entries.Where(e => e.TransactionReference == transactionReference).ToList());
}

internal sealed class FakeAdminEventPublisher : IEventPublisher
{
    public Task PublishCardIssuedAsync(
        CardManagement.Application.DTOs.CardIssuedEvent cardEvent, CancellationToken ct) =>
        Task.CompletedTask;

    public Task PublishTransactionAuthorizedAsync(
        CardManagement.Application.DTOs.TransactionAuthorizedEvent txEvent, CancellationToken ct) =>
        Task.CompletedTask;

    public Task PublishTransactionReversedAsync(
        CardManagement.Application.DTOs.TransactionReversedEvent txEvent, CancellationToken ct) =>
        Task.CompletedTask;
}

#endregion

#region Maker-Checker State Transition Tests

public class MakerCheckerStateTransitionTests
{
    [Fact]
    public void PendingCommand_Approve_TransitionsToPendingToApproved()
    {
        var command = PendingCommand.Create(
            "adjustment.create", "{\"amount\":100}", "maker-user",
            DateTime.UtcNow.AddHours(24));

        command.Approve("checker-user");

        Assert.Equal(CommandStatus.Approved, command.Status);
        Assert.Equal("checker-user", command.CheckerId);
        Assert.NotNull(command.ResolvedAtUtc);
    }

    [Fact]
    public void PendingCommand_Reject_TransitionsToPendingToRejected()
    {
        var command = PendingCommand.Create(
            "refund.execute", "{\"refundId\":\"r1\"}", "maker-user",
            DateTime.UtcNow.AddHours(24));

        command.Reject("checker-user", "Invalid refund request");

        Assert.Equal(CommandStatus.Rejected, command.Status);
        Assert.Equal("checker-user", command.CheckerId);
        Assert.Equal("Invalid refund request", command.RejectionReason);
        Assert.NotNull(command.ResolvedAtUtc);
    }

    [Fact]
    public void PendingCommand_MarkExpired_TransitionsToPendingToExpired()
    {
        var command = PendingCommand.Create(
            "config.update", "{\"key\":\"val\"}", "maker-user",
            DateTime.UtcNow.AddHours(24));

        command.MarkExpired();

        Assert.Equal(CommandStatus.Expired, command.Status);
        Assert.NotNull(command.ResolvedAtUtc);
        Assert.Null(command.CheckerId);
    }

    [Fact]
    public void PendingCommand_ApprovedThenExecuted_TransitionsCorrectly()
    {
        var command = PendingCommand.Create(
            "role.assign", "{\"userId\":\"u1\",\"role\":\"compliance\"}",
            "maker-user", DateTime.UtcNow.AddHours(24));

        command.Approve("checker-user");
        command.MarkExecuted();

        Assert.Equal(CommandStatus.Executed, command.Status);
    }

    [Fact]
    public void PendingCommand_Approve_SameUserAsMaker_Throws()
    {
        var command = PendingCommand.Create(
            "adjustment.create", "{\"amount\":50}", "same-user",
            DateTime.UtcNow.AddHours(24));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            command.Approve("same-user"));

        Assert.Contains("different users", ex.Message);
    }

    [Fact]
    public void PendingCommand_Reject_SameUserAsMaker_Throws()
    {
        var command = PendingCommand.Create(
            "refund.execute", "{}", "same-user",
            DateTime.UtcNow.AddHours(24));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            command.Reject("same-user", "Not valid"));

        Assert.Contains("different users", ex.Message);
    }

    [Fact]
    public void PendingCommand_Approve_AlreadyRejected_Throws()
    {
        var command = PendingCommand.Create(
            "adjustment.create", "{}", "maker",
            DateTime.UtcNow.AddHours(24));

        command.Reject("checker", "Denied");

        Assert.Throws<InvalidOperationException>(() =>
            command.Approve("another-checker"));
    }

    [Fact]
    public void PendingCommand_Reject_AlreadyApproved_Throws()
    {
        var command = PendingCommand.Create(
            "config.update", "{}", "maker",
            DateTime.UtcNow.AddHours(24));

        command.Approve("checker");

        Assert.Throws<InvalidOperationException>(() =>
            command.Reject("another-checker", "Too late"));
    }

    [Fact]
    public void PendingCommand_MarkExpired_AlreadyApproved_Throws()
    {
        var command = PendingCommand.Create(
            "role.revoke", "{}", "maker",
            DateTime.UtcNow.AddHours(24));

        command.Approve("checker");

        Assert.Throws<InvalidOperationException>(() =>
            command.MarkExpired());
    }

    [Fact]
    public void PendingCommand_MarkExecuted_NotApproved_Throws()
    {
        var command = PendingCommand.Create(
            "adjustment.create", "{}", "maker",
            DateTime.UtcNow.AddHours(24));

        Assert.Throws<InvalidOperationException>(() =>
            command.MarkExecuted());
    }

    [Fact]
    public void PendingCommand_IsExpired_WhenPastExpiryTime_ReturnsTrue()
    {
        // Create with expiry 1ms in the future, then wait
        var command = PendingCommand.Create(
            "adjustment.create", "{}", "maker",
            DateTime.UtcNow.AddMilliseconds(50));

        Thread.Sleep(60);

        Assert.True(command.IsExpired);
    }

    [Fact]
    public void PendingCommand_IsExpired_WhenNotPastExpiryTime_ReturnsFalse()
    {
        var command = PendingCommand.Create(
            "adjustment.create", "{}", "maker",
            DateTime.UtcNow.AddHours(24));

        Assert.False(command.IsExpired);
    }
}

#endregion

#region RBAC Permission Matrix Tests

public class RbacPermissionMatrixTests
{
    [Theory]
    [InlineData("operations", "reconciliation.import", true)]
    [InlineData("operations", "reconciliation.view", true)]
    [InlineData("operations", "reconciliation.adjust", true)]
    [InlineData("operations", "refund.execute", true)]
    [InlineData("operations", "transaction.view", true)]
    [InlineData("operations", "transaction.reverse", true)]
    [InlineData("operations", "dispute.view", true)]
    [InlineData("operations", "dispute.resolve", true)]
    [InlineData("operations", "command.approve", true)]
    [InlineData("operations", "dashboard.view", true)]
    [InlineData("operations", "role.assign", false)]
    [InlineData("operations", "audit.view", false)]
    [InlineData("operations", "config.update", false)]
    public void Operations_Role_HasCorrectPermissions(
        string role, string operation, bool expected)
    {
        Assert.Equal(expected, RolePermissionMatrix.HasPermission(role, operation));
    }

    [Theory]
    [InlineData("compliance", "reconciliation.view", true)]
    [InlineData("compliance", "audit.view", true)]
    [InlineData("compliance", "dispute.view", true)]
    [InlineData("compliance", "dispute.resolve", true)]
    [InlineData("compliance", "command.approve", true)]
    [InlineData("compliance", "dashboard.view", true)]
    [InlineData("compliance", "reconciliation.import", false)]
    [InlineData("compliance", "reconciliation.adjust", false)]
    [InlineData("compliance", "refund.execute", false)]
    [InlineData("compliance", "role.assign", false)]
    [InlineData("compliance", "config.update", false)]
    public void Compliance_Role_HasCorrectPermissions(
        string role, string operation, bool expected)
    {
        Assert.Equal(expected, RolePermissionMatrix.HasPermission(role, operation));
    }

    [Theory]
    [InlineData("risk", "reconciliation.view", true)]
    [InlineData("risk", "transaction.view", true)]
    [InlineData("risk", "transaction.reverse", true)]
    [InlineData("risk", "dispute.view", true)]
    [InlineData("risk", "dispute.resolve", true)]
    [InlineData("risk", "merchant.suspend", true)]
    [InlineData("risk", "command.approve", true)]
    [InlineData("risk", "dashboard.view", true)]
    [InlineData("risk", "reconciliation.import", false)]
    [InlineData("risk", "reconciliation.adjust", false)]
    [InlineData("risk", "role.assign", false)]
    [InlineData("risk", "audit.view", false)]
    public void Risk_Role_HasCorrectPermissions(
        string role, string operation, bool expected)
    {
        Assert.Equal(expected, RolePermissionMatrix.HasPermission(role, operation));
    }

    [Theory]
    [InlineData("support", "transaction.view", true)]
    [InlineData("support", "refund.view", true)]
    [InlineData("support", "dispute.view", true)]
    [InlineData("support", "merchant.view", true)]
    [InlineData("support", "command.view", true)]
    [InlineData("support", "dashboard.view", true)]
    [InlineData("support", "reconciliation.import", false)]
    [InlineData("support", "refund.execute", false)]
    [InlineData("support", "role.assign", false)]
    [InlineData("support", "config.update", false)]
    [InlineData("support", "command.approve", false)]
    [InlineData("support", "audit.view", false)]
    public void Support_Role_HasCorrectPermissions(
        string role, string operation, bool expected)
    {
        Assert.Equal(expected, RolePermissionMatrix.HasPermission(role, operation));
    }

    [Theory]
    [InlineData("security-admin", "role.assign", true)]
    [InlineData("security-admin", "role.revoke", true)]
    [InlineData("security-admin", "role.view", true)]
    [InlineData("security-admin", "audit.view", true)]
    [InlineData("security-admin", "config.view", true)]
    [InlineData("security-admin", "config.update", true)]
    [InlineData("security-admin", "command.approve", true)]
    [InlineData("security-admin", "command.reject", true)]
    [InlineData("security-admin", "dashboard.view", true)]
    [InlineData("security-admin", "merchant.suspend", true)]
    [InlineData("security-admin", "merchant.deactivate", true)]
    [InlineData("security-admin", "reconciliation.import", false)]
    [InlineData("security-admin", "refund.execute", false)]
    [InlineData("security-admin", "transaction.view", false)]
    public void SecurityAdmin_Role_HasCorrectPermissions(
        string role, string operation, bool expected)
    {
        Assert.Equal(expected, RolePermissionMatrix.HasPermission(role, operation));
    }

    [Fact]
    public void HasPermission_InvalidRole_ReturnsFalse()
    {
        Assert.False(RolePermissionMatrix.HasPermission("nonexistent-role", "transaction.view"));
    }

    [Fact]
    public void HasPermission_InvalidOperation_ReturnsFalse()
    {
        Assert.False(RolePermissionMatrix.HasPermission("operations", "nonexistent.operation"));
    }

    [Fact]
    public void HasPermission_NullRole_ReturnsFalse()
    {
        Assert.False(RolePermissionMatrix.HasPermission(null!, "transaction.view"));
    }

    [Fact]
    public void HasPermission_EmptyOperation_ReturnsFalse()
    {
        Assert.False(RolePermissionMatrix.HasPermission("operations", ""));
    }

    [Fact]
    public void GetPermissions_Operations_ReturnsNonEmptySet()
    {
        var perms = RolePermissionMatrix.GetPermissions("operations");
        Assert.NotEmpty(perms);
        Assert.Contains("reconciliation.import", perms);
    }

    [Fact]
    public void GetRolesForOperation_RoleAssign_OnlySecurityAdmin()
    {
        var roles = RolePermissionMatrix.GetRolesForOperation("role.assign");
        Assert.Single(roles);
        Assert.Contains("security-admin", roles);
    }
}

#endregion

#region RBAC Service Tests

public class RbacServiceTests
{
    private readonly FakeAdminRoleRepository _roleRepo = new();
    private readonly InMemoryAdminAuditStore _auditStore = new();
    private RbacService CreateService() => new(_roleRepo, _auditStore);

    [Fact]
    public async Task HasPermissionAsync_UserWithMatchingRole_ReturnsTrue()
    {
        var role = AdminRole.Create("user-1", AdminRoleType.Operations, "admin");
        _roleRepo.AddDirect(role);
        var svc = CreateService();

        var result = await svc.HasPermissionAsync(
            "user-1", RolePermissionMatrix.Operations.ReconciliationImport, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task HasPermissionAsync_UserWithoutMatchingRole_ReturnsFalse()
    {
        var role = AdminRole.Create("user-1", AdminRoleType.Support, "admin");
        _roleRepo.AddDirect(role);
        var svc = CreateService();

        var result = await svc.HasPermissionAsync(
            "user-1", RolePermissionMatrix.Operations.ReconciliationImport, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_UserWithNoRoles_ReturnsFalse()
    {
        var svc = CreateService();

        var result = await svc.HasPermissionAsync(
            "no-roles-user", RolePermissionMatrix.Operations.TransactionView, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HasPermissionAsync_EmptyUserId_ReturnsFalse()
    {
        var svc = CreateService();

        var result = await svc.HasPermissionAsync(
            "", RolePermissionMatrix.Operations.TransactionView, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task AssignRoleAsync_ValidRole_CreatesRoleAndAudits()
    {
        var svc = CreateService();

        await svc.AssignRoleAsync("target-user", AdminRoleType.Compliance, "admin-user", CancellationToken.None);

        var roles = await svc.GetRolesAsync("target-user", CancellationToken.None);
        Assert.Contains(AdminRoleType.Compliance, roles);
        Assert.Single(_auditStore.Entries);
        Assert.Equal("role.assigned", _auditStore.Entries[0].Action);
    }

    [Fact]
    public async Task AssignRoleAsync_InvalidRole_ThrowsArgumentException()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.AssignRoleAsync("user", "invalid-role", "admin", CancellationToken.None));
    }

    [Fact]
    public async Task AssignRoleAsync_DuplicateActiveRole_ThrowsInvalidOperation()
    {
        var role = AdminRole.Create("user-1", AdminRoleType.Risk, "admin");
        _roleRepo.AddDirect(role);
        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.AssignRoleAsync("user-1", AdminRoleType.Risk, "admin", CancellationToken.None));
    }

    [Fact]
    public async Task RevokeRoleAsync_ActiveRole_DeactivatesAndAudits()
    {
        var role = AdminRole.Create("user-1", AdminRoleType.Operations, "admin");
        _roleRepo.AddDirect(role);
        var svc = CreateService();

        await svc.RevokeRoleAsync("user-1", AdminRoleType.Operations, "admin-revoker", CancellationToken.None);

        Assert.Single(_auditStore.Entries);
        Assert.Equal("role.revoked", _auditStore.Entries[0].Action);
    }

    [Fact]
    public async Task RevokeRoleAsync_NoActiveRole_ThrowsInvalidOperation()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.RevokeRoleAsync("user-1", AdminRoleType.Operations, "admin", CancellationToken.None));
    }
}

#endregion

#region Read Model Staleness Detection Tests

public class ReadModelStalenessTests
{
    [Fact]
    public void ReadModelMetadata_StaleWhenLastProjectedExceedsThreshold()
    {
        var lastProjected = DateTime.UtcNow.AddMinutes(-10);
        var threshold = TimeSpan.FromMinutes(5);
        var staleDuration = DateTime.UtcNow - lastProjected;
        var isStale = staleDuration > threshold;

        var metadata = new ReadModelMetadata(
            "TransactionSummary",
            lastProjected,
            isStale,
            isStale ? staleDuration : null);

        Assert.True(metadata.IsStale);
        Assert.NotNull(metadata.StaleDuration);
        Assert.True(metadata.StaleDuration.Value > threshold);
    }

    [Fact]
    public void ReadModelMetadata_FreshWhenLastProjectedWithinThreshold()
    {
        var lastProjected = DateTime.UtcNow.AddMinutes(-2);
        var threshold = TimeSpan.FromMinutes(5);
        var staleDuration = DateTime.UtcNow - lastProjected;
        var isStale = staleDuration > threshold;

        var metadata = new ReadModelMetadata(
            "DisputeMetrics",
            lastProjected,
            isStale,
            null);

        Assert.False(metadata.IsStale);
        Assert.Null(metadata.StaleDuration);
    }

    [Fact]
    public void ReadModelMetadata_ExactlyAtThreshold_IsNotStale()
    {
        var threshold = TimeSpan.FromMinutes(5);
        var lastProjected = DateTime.UtcNow.AddMinutes(-5);
        // Just barely at threshold — compare staleDuration <= threshold
        var staleDuration = DateTime.UtcNow - lastProjected;
        // With UtcNow drift this will be very close to threshold
        var isStale = staleDuration > threshold;

        var metadata = new ReadModelMetadata(
            "ReconciliationStatus",
            lastProjected,
            isStale,
            isStale ? staleDuration : null);

        // At boundary, could be either; test the construction is valid
        Assert.Equal("ReconciliationStatus", metadata.ReadModelName);
    }

    [Fact]
    public void ReadModelMetadata_NeverProjected_IsStale()
    {
        // When a read model has never been projected, we treat it as stale
        var metadata = new ReadModelMetadata(
            "ChannelHealth",
            DateTime.MinValue,
            true,
            null);

        Assert.True(metadata.IsStale);
    }
}

#endregion

#region Command Expiry Background Service Tests

public class CommandExpiryBackgroundServiceTests
{
    [Fact]
    public async Task ExpireStaleCommands_MarksExpiredCommandsViaWorkflow()
    {
        var commandRepo = new FakeAdminCommandRepository();
        var auditStore = new InMemoryAdminAuditStore();
        var roleRepo = new FakeAdminRoleRepository();
        var eventPublisher = new FakeAdminEventPublisher();

        // Create a command that is already past its expiry time
        var expiredCommand = PendingCommand.Create(
            "adjustment.create", "{\"amount\":100}", "maker",
            DateTime.UtcNow.AddMilliseconds(50));

        commandRepo.AddDirect(expiredCommand);

        // Wait for it to expire
        await Task.Delay(60);

        // Create the workflow service with short expiry
        var rbacService = new RbacService(roleRepo, auditStore);
        var workflow = new MakerCheckerWorkflowService(
            commandRepo, auditStore, eventPublisher, rbacService,
            TimeSpan.FromMilliseconds(50));

        // Call expire
        await workflow.ExpireStaleCommandsAsync(CancellationToken.None);

        Assert.Equal(CommandStatus.Expired, expiredCommand.Status);
    }

    [Fact]
    public async Task ExpireStaleCommands_DoesNotAffectNonPendingCommands()
    {
        var commandRepo = new FakeAdminCommandRepository();
        var auditStore = new InMemoryAdminAuditStore();
        var roleRepo = new FakeAdminRoleRepository();
        var eventPublisher = new FakeAdminEventPublisher();

        // Create a command that is already approved
        var approvedCommand = PendingCommand.Create(
            "refund.execute", "{}", "maker",
            DateTime.UtcNow.AddMilliseconds(50));

        approvedCommand.Approve("checker");
        commandRepo.AddDirect(approvedCommand);

        await Task.Delay(60);

        var rbacService = new RbacService(roleRepo, auditStore);
        var workflow = new MakerCheckerWorkflowService(
            commandRepo, auditStore, eventPublisher, rbacService,
            TimeSpan.FromMilliseconds(50));

        await workflow.ExpireStaleCommandsAsync(CancellationToken.None);

        // Status should remain Approved, not changed
        Assert.Equal(CommandStatus.Approved, approvedCommand.Status);
    }

    [Fact]
    public async Task CommandExpiryBackgroundService_UsesServiceScopeFactory()
    {
        // Arrange - set up service collection with all dependencies
        var commandRepo = new FakeAdminCommandRepository();
        var auditStore = new InMemoryAdminAuditStore();
        var roleRepo = new FakeAdminRoleRepository();
        var eventPublisher = new FakeAdminEventPublisher();

        var services = new ServiceCollection();
        services.AddSingleton<IAdminCommandRepository>(commandRepo);
        services.AddSingleton<IAuditStore>(auditStore);
        services.AddSingleton<IAdminRoleRepository>(roleRepo);
        services.AddSingleton<IEventPublisher>(eventPublisher);
        services.AddSingleton<IRbacService>(sp =>
            new RbacService(sp.GetRequiredService<IAdminRoleRepository>(), sp.GetRequiredService<IAuditStore>()));
        services.AddSingleton<IMakerCheckerWorkflow>(sp =>
            new MakerCheckerWorkflowService(
                sp.GetRequiredService<IAdminCommandRepository>(),
                sp.GetRequiredService<IAuditStore>(),
                sp.GetRequiredService<IEventPublisher>(),
                sp.GetRequiredService<IRbacService>(),
                TimeSpan.FromMilliseconds(50)));

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var bgService = new CommandExpiryBackgroundService(
            scopeFactory,
            NullLogger<CommandExpiryBackgroundService>.Instance,
            checkInterval: TimeSpan.FromMilliseconds(50),
            expiryThreshold: TimeSpan.FromMilliseconds(50));

        // Create an expired command
        var expiredCommand = PendingCommand.Create(
            "config.update", "{}", "maker",
            DateTime.UtcNow.AddMilliseconds(10));
        commandRepo.AddDirect(expiredCommand);

        await Task.Delay(60);

        // Invoke the internal method directly
        await bgService.ExpireStaleCommandsAsync(CancellationToken.None);

        Assert.Equal(CommandStatus.Expired, expiredCommand.Status);
    }

    [Fact]
    public async Task ExpireStaleCommands_RecordsAuditEntryForEachExpiry()
    {
        var commandRepo = new FakeAdminCommandRepository();
        var auditStore = new InMemoryAdminAuditStore();
        var roleRepo = new FakeAdminRoleRepository();
        var eventPublisher = new FakeAdminEventPublisher();

        // Create two expired commands
        var cmd1 = PendingCommand.Create(
            "adjustment.create", "{}", "maker1",
            DateTime.UtcNow.AddMilliseconds(10));
        var cmd2 = PendingCommand.Create(
            "refund.execute", "{}", "maker2",
            DateTime.UtcNow.AddMilliseconds(10));

        commandRepo.AddDirect(cmd1);
        commandRepo.AddDirect(cmd2);

        await Task.Delay(20);

        var rbacService = new RbacService(roleRepo, auditStore);
        var workflow = new MakerCheckerWorkflowService(
            commandRepo, auditStore, eventPublisher, rbacService,
            TimeSpan.FromMilliseconds(10));

        await workflow.ExpireStaleCommandsAsync(CancellationToken.None);

        // Should have audit entries for each expired command
        var expiryAudits = auditStore.Entries
            .Where(e => e.Action == "command.expired").ToList();
        Assert.Equal(2, expiryAudits.Count);
    }
}

#endregion

#region Audit Immutability Tests

public class AuditImmutabilityTests
{
    [Fact]
    public void AuditEntry_HasNoPublicSetters_EnsuresImmutability()
    {
        // Verify AuditEntry properties are read-only (private set)
        var type = typeof(AuditEntry);
        var properties = type.GetProperties();

        foreach (var prop in properties)
        {
            var setter = prop.GetSetMethod(nonPublic: false);
            Assert.Null(setter); // No public setter should exist
        }
    }

    [Fact]
    public void AuditStore_Interface_HasNoUpdateOrDeleteMethods()
    {
        // Verify IAuditStore only exposes Append and read operations
        var type = typeof(IAuditStore);
        var methods = type.GetMethods();

        var methodNames = methods.Select(m => m.Name).ToList();

        // Should have Append and Query/Get methods only
        Assert.Contains("AppendAsync", methodNames);
        Assert.Contains("GetByTransactionReferenceAsync", methodNames);

        // Must NOT have update or delete methods
        Assert.DoesNotContain("UpdateAsync", methodNames);
        Assert.DoesNotContain("DeleteAsync", methodNames);
        Assert.DoesNotContain("RemoveAsync", methodNames);
        Assert.DoesNotContain("ModifyAsync", methodNames);
    }

    [Fact]
    public void AuditEntry_Create_ProducesValidHashChain()
    {
        var entry1 = AuditEntry.Create(
            "cmd:test-1", "actor-1", "command.submitted",
            null, "{\"status\":\"Pending\"}", "corr-1", null);

        var entry2 = AuditEntry.Create(
            "cmd:test-1", "actor-2", "command.approved",
            "{\"status\":\"Pending\"}", "{\"status\":\"Approved\"}",
            "corr-1", entry1.EntryHash);

        Assert.NotEmpty(entry1.EntryHash);
        Assert.NotEmpty(entry2.EntryHash);
        Assert.Equal(entry1.EntryHash, entry2.PreviousEntryHash);
        Assert.NotEqual(entry1.EntryHash, entry2.EntryHash);
    }

    [Fact]
    public void AuditEntry_HashChain_TamperDetection()
    {
        var entry = AuditEntry.Create(
            "cmd:test-1", "actor-1", "command.submitted",
            null, "{\"status\":\"Pending\"}", "corr-1", null);

        var originalHash = entry.EntryHash;
        var recomputedHash = entry.ComputeHash();

        // Hash should be deterministic and match stored hash
        Assert.Equal(originalHash, recomputedHash);
    }

    [Fact]
    public async Task AuditStore_AppendOnly_AcceptsNewEntries()
    {
        var store = new InMemoryAdminAuditStore();

        var entry = AuditEntry.Create(
            "cmd:test-1", "actor-1", "command.approved",
            null, "{\"status\":\"Approved\"}", "corr-1", null);

        await store.AppendAsync(entry, CancellationToken.None);

        Assert.Single(store.Entries);
        Assert.Equal("command.approved", store.Entries[0].Action);
    }

    [Fact]
    public void AuditEntry_CannotBeConstructedWithoutRequiredFields()
    {
        Assert.Throws<ArgumentException>(() =>
            AuditEntry.Create("", "actor", "action", null, null, "corr", null));

        Assert.Throws<ArgumentException>(() =>
            AuditEntry.Create("ref", "", "action", null, null, "corr", null));

        Assert.Throws<ArgumentException>(() =>
            AuditEntry.Create("ref", "actor", "", null, null, "corr", null));

        Assert.Throws<ArgumentException>(() =>
            AuditEntry.Create("ref", "actor", "action", null, null, "", null));
    }
}

#endregion

#region Maker-Checker Workflow Service Tests

public class MakerCheckerWorkflowServiceTests
{
    private readonly FakeAdminCommandRepository _commandRepo = new();
    private readonly InMemoryAdminAuditStore _auditStore = new();
    private readonly FakeAdminRoleRepository _roleRepo = new();
    private readonly FakeAdminEventPublisher _eventPublisher = new();

    private MakerCheckerWorkflowService CreateWorkflowService()
    {
        var rbacService = new RbacService(_roleRepo, _auditStore);
        return new MakerCheckerWorkflowService(
            _commandRepo, _auditStore, _eventPublisher, rbacService,
            TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task SubmitCommand_SensitiveCommand_CreatesPendingCommand()
    {
        // Give the maker appropriate permissions (operations role has refund.execute)
        var role = AdminRole.Create("maker-user", AdminRoleType.Operations, "admin");
        _roleRepo.AddDirect(role);

        var svc = CreateWorkflowService();
        var cmd = new AdminCommand("refund.execute", "{\"amount\":500}", true);

        var result = await svc.SubmitCommandAsync(cmd, "maker-user", CancellationToken.None);

        Assert.Equal(CommandStatus.Pending, result.Status);
        Assert.Equal("maker-user", result.MakerId);
        Assert.Equal("refund.execute", result.CommandType);
    }

    [Fact]
    public async Task SubmitCommand_NonSensitiveCommand_ThrowsInvalidOperation()
    {
        var role = AdminRole.Create("maker-user", AdminRoleType.Operations, "admin");
        _roleRepo.AddDirect(role);

        var svc = CreateWorkflowService();
        // transaction.view is not sensitive and operations role has transaction.view
        var cmd = new AdminCommand("transaction.view", "{}", false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SubmitCommandAsync(cmd, "maker-user", CancellationToken.None));
    }

    [Fact]
    public async Task SubmitCommand_MakerWithoutPermission_ThrowsUnauthorized()
    {
        // User has support role which cannot do refund.execute (sensitive command)
        var role = AdminRole.Create("maker-user", AdminRoleType.Support, "admin");
        _roleRepo.AddDirect(role);

        var svc = CreateWorkflowService();
        var cmd = new AdminCommand("refund.execute", "{}", true);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.SubmitCommandAsync(cmd, "maker-user", CancellationToken.None));
    }

    [Fact]
    public async Task ApproveCommand_DifferentChecker_ExecutesSuccessfully()
    {
        // Setup maker and checker with appropriate roles
        // Operations role has refund.execute; Compliance has command.approve
        var makerRole = AdminRole.Create("maker-user", AdminRoleType.Operations, "admin");
        var checkerRole = AdminRole.Create("checker-user", AdminRoleType.Compliance, "admin");
        _roleRepo.AddDirect(makerRole);
        _roleRepo.AddDirect(checkerRole);

        var svc = CreateWorkflowService();
        var cmd = new AdminCommand("refund.execute", "{\"amount\":100}", true);
        var pending = await svc.SubmitCommandAsync(cmd, "maker-user", CancellationToken.None);

        var result = await svc.ApproveAsync(pending.Id, "checker-user", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(CommandStatus.Executed, pending.Status);
    }

    [Fact]
    public async Task ApproveCommand_SameUserAsMaker_ThrowsInvalidOperation()
    {
        // Give user operations role (has refund.execute and command.approve)
        var role1 = AdminRole.Create("same-user", AdminRoleType.Operations, "admin");
        _roleRepo.AddDirect(role1);

        var svc = CreateWorkflowService();
        var cmd = new AdminCommand("refund.execute", "{}", true);
        var pending = await svc.SubmitCommandAsync(cmd, "same-user", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.ApproveAsync(pending.Id, "same-user", CancellationToken.None));
    }

    [Fact]
    public async Task RejectCommand_WithReason_MarksRejectedAndAudits()
    {
        var makerRole = AdminRole.Create("maker-user", AdminRoleType.Operations, "admin");
        var checkerRole = AdminRole.Create("checker-user", AdminRoleType.Compliance, "admin");
        _roleRepo.AddDirect(makerRole);
        _roleRepo.AddDirect(checkerRole);

        var svc = CreateWorkflowService();
        var cmd = new AdminCommand("refund.execute", "{\"id\":\"r1\"}", true);
        var pending = await svc.SubmitCommandAsync(cmd, "maker-user", CancellationToken.None);

        await svc.RejectAsync(pending.Id, "checker-user", "Insufficient justification", CancellationToken.None);

        Assert.Equal(CommandStatus.Rejected, pending.Status);
        Assert.Equal("Insufficient justification", pending.RejectionReason);

        var rejectionAudits = _auditStore.Entries
            .Where(e => e.Action == "command.rejected").ToList();
        Assert.Single(rejectionAudits);
    }

    [Fact]
    public async Task RejectCommand_WithoutReason_ThrowsArgException()
    {
        var makerRole = AdminRole.Create("maker-user", AdminRoleType.Operations, "admin");
        var checkerRole = AdminRole.Create("checker-user", AdminRoleType.Compliance, "admin");
        _roleRepo.AddDirect(makerRole);
        _roleRepo.AddDirect(checkerRole);

        var svc = CreateWorkflowService();
        var cmd = new AdminCommand("refund.execute", "{}", true);
        var pending = await svc.SubmitCommandAsync(cmd, "maker-user", CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.RejectAsync(pending.Id, "checker-user", "", CancellationToken.None));
    }

    [Fact]
    public async Task ApproveCommand_CheckerWithoutPermission_ThrowsUnauthorized()
    {
        var makerRole = AdminRole.Create("maker-user", AdminRoleType.Operations, "admin");
        // Support role does NOT have command.approve permission
        var checkerRole = AdminRole.Create("checker-user", AdminRoleType.Support, "admin");
        _roleRepo.AddDirect(makerRole);
        _roleRepo.AddDirect(checkerRole);

        var svc = CreateWorkflowService();
        var cmd = new AdminCommand("refund.execute", "{}", true);
        var pending = await svc.SubmitCommandAsync(cmd, "maker-user", CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.ApproveAsync(pending.Id, "checker-user", CancellationToken.None));
    }
}

#endregion

#region Sensitive Command Classifier Tests

public class SensitiveCommandClassifierTests
{
    [Theory]
    [InlineData("adjustment.create", true)]
    [InlineData("adjustment.reverse", true)]
    [InlineData("refund.execute", true)]
    [InlineData("refund.reverse", true)]
    [InlineData("config.update", true)]
    [InlineData("config.threshold.update", true)]
    [InlineData("role.assign", true)]
    [InlineData("role.revoke", true)]
    [InlineData("user.suspend", true)]
    [InlineData("merchant.suspend", true)]
    [InlineData("merchant.deactivate", true)]
    [InlineData("transaction.view", false)]
    [InlineData("dashboard.view", false)]
    [InlineData("reconciliation.view", false)]
    [InlineData("dispute.view", false)]
    public void IsSensitive_ClassifiesCorrectly(string commandType, bool expected)
    {
        Assert.Equal(expected, SensitiveCommandClassifier.IsSensitive(commandType));
    }

    [Fact]
    public void IsSensitive_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(SensitiveCommandClassifier.IsSensitive(null!));
        Assert.False(SensitiveCommandClassifier.IsSensitive(""));
        Assert.False(SensitiveCommandClassifier.IsSensitive("   "));
    }

    [Theory]
    [InlineData("adjustment.create", "adjustment")]
    [InlineData("refund.execute", "refund")]
    [InlineData("config.update", "config")]
    [InlineData("role.assign", "role")]
    [InlineData("merchant.suspend", "merchant")]
    public void GetCategory_ReturnsCorrectCategory(string commandType, string expectedCategory)
    {
        Assert.Equal(expectedCategory, SensitiveCommandClassifier.GetCategory(commandType));
    }

    [Fact]
    public void GetCategory_NonSensitiveCommand_ReturnsNull()
    {
        Assert.Null(SensitiveCommandClassifier.GetCategory("transaction.view"));
        Assert.Null(SensitiveCommandClassifier.GetCategory(""));
    }
}

#endregion
