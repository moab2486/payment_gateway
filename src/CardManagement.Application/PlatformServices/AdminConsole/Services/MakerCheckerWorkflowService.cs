using CardManagement.Application.PlatformServices.AdminConsole.DTOs;
using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.AdminConsole;

namespace CardManagement.Application.PlatformServices.AdminConsole.Services;

/// <summary>
/// Implements the maker-checker dual authorization workflow for sensitive admin commands.
/// Ensures that no single operator can unilaterally execute high-risk operations.
/// </summary>
public class MakerCheckerWorkflowService : IMakerCheckerWorkflow
{
    private readonly IAdminCommandRepository _commandRepository;
    private readonly IAuditStore _auditStore;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAdminConsoleEventPublisher? _adminEventPublisher;
    private readonly IRbacService _rbacService;
    private readonly TimeSpan _commandExpiryPeriod;

    public MakerCheckerWorkflowService(
        IAdminCommandRepository commandRepository,
        IAuditStore auditStore,
        IEventPublisher eventPublisher,
        IRbacService rbacService,
        TimeSpan? commandExpiryPeriod = null,
        IAdminConsoleEventPublisher? adminEventPublisher = null)
    {
        _commandRepository = commandRepository ?? throw new ArgumentNullException(nameof(commandRepository));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _rbacService = rbacService ?? throw new ArgumentNullException(nameof(rbacService));
        _commandExpiryPeriod = commandExpiryPeriod ?? TimeSpan.FromHours(24);
        _adminEventPublisher = adminEventPublisher;
    }

    /// <inheritdoc />
    public async Task<PendingCommand> SubmitCommandAsync(AdminCommand command, string makerId, CancellationToken ct)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("Maker identity is required.", nameof(makerId));

        // Verify maker has permission to initiate the command type
        var hasPermission = await _rbacService.HasPermissionAsync(makerId, command.CommandType, ct);
        if (!hasPermission)
            throw new UnauthorizedAccessException(
                $"User '{makerId}' does not have permission to initiate command '{command.CommandType}'.");

        // Classify command sensitivity
        var isSensitive = command.IsSensitive || SensitiveCommandClassifier.IsSensitive(command.CommandType);

        if (!isSensitive)
            throw new InvalidOperationException(
                $"Command '{command.CommandType}' is not classified as sensitive and does not require maker-checker workflow.");

        var expiresAtUtc = DateTime.UtcNow.Add(_commandExpiryPeriod);

        var pendingCommand = PendingCommand.Create(
            command.CommandType,
            command.SerializedParameters,
            makerId,
            expiresAtUtc);

        var persisted = await _commandRepository.CreateAsync(pendingCommand, ct);

        // Notify checkers via Kafka (fire-and-forget; publisher handles failures gracefully)
        if (_adminEventPublisher is not null)
        {
            await _adminEventPublisher.PublishCommandPendingApprovalAsync(
                persisted.Id,
                command.CommandType,
                makerId,
                persisted.ExpiresAtUtc,
                ct);
        }

        // Audit the submission
        await _auditStore.AppendAsync(
            AuditEntry.Create(
                transactionReference: $"cmd:{persisted.Id}",
                actorIdentity: makerId,
                action: "command.submitted",
                previousState: null,
                newState: $"{{\"commandType\":\"{command.CommandType}\",\"status\":\"Pending\"}}",
                correlationId: persisted.Id.ToString(),
                previousEntryHash: null),
            ct);

        return persisted;
    }

    /// <inheritdoc />
    public async Task<CommandExecutionResult> ApproveAsync(Guid commandId, string checkerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(checkerId))
            throw new ArgumentException("Checker identity is required.", nameof(checkerId));

        var command = await _commandRepository.GetByIdAsync(commandId, ct);
        if (command is null)
            throw new KeyNotFoundException($"Command '{commandId}' not found.");

        // Verify checker has approval permission
        var hasPermission = await _rbacService.HasPermissionAsync(
            checkerId, RolePermissionMatrix.Operations.CommandApprove, ct);
        if (!hasPermission)
            throw new UnauthorizedAccessException(
                $"User '{checkerId}' does not have permission to approve commands.");

        // Domain validates: status is Pending, not expired, maker ≠ checker
        command.Approve(checkerId);

        // Mark as executed
        command.MarkExecuted();

        await _commandRepository.UpdateAsync(command, ct);

        // Record audit with maker and checker identities
        await _auditStore.AppendAsync(
            AuditEntry.Create(
                transactionReference: $"cmd:{commandId}",
                actorIdentity: checkerId,
                action: "command.approved_and_executed",
                previousState: $"{{\"status\":\"Pending\",\"makerId\":\"{command.MakerId}\"}}",
                newState: $"{{\"status\":\"Executed\",\"checkerId\":\"{checkerId}\"}}",
                correlationId: commandId.ToString(),
                previousEntryHash: null),
            ct);

        return new CommandExecutionResult(
            CommandId: commandId,
            Success: true,
            ResultMessage: $"Command '{command.CommandType}' approved and executed successfully.",
            ExecutedAtUtc: DateTime.UtcNow);
    }

    /// <inheritdoc />
    public async Task RejectAsync(Guid commandId, string checkerId, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(checkerId))
            throw new ArgumentException("Checker identity is required.", nameof(checkerId));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required.", nameof(reason));

        var command = await _commandRepository.GetByIdAsync(commandId, ct);
        if (command is null)
            throw new KeyNotFoundException($"Command '{commandId}' not found.");

        // Verify checker has rejection permission
        var hasPermission = await _rbacService.HasPermissionAsync(
            checkerId, RolePermissionMatrix.Operations.CommandReject, ct);
        if (!hasPermission)
            throw new UnauthorizedAccessException(
                $"User '{checkerId}' does not have permission to reject commands.");

        // Domain validates: status is Pending, maker ≠ checker
        command.Reject(checkerId, reason);

        await _commandRepository.UpdateAsync(command, ct);

        // Record audit with rejection details
        await _auditStore.AppendAsync(
            AuditEntry.Create(
                transactionReference: $"cmd:{commandId}",
                actorIdentity: checkerId,
                action: "command.rejected",
                previousState: $"{{\"status\":\"Pending\",\"makerId\":\"{command.MakerId}\"}}",
                newState: $"{{\"status\":\"Rejected\",\"reason\":\"{reason}\"}}",
                correlationId: commandId.ToString(),
                previousEntryHash: null),
            ct);
    }

    /// <inheritdoc />
    public async Task ExpireStaleCommandsAsync(CancellationToken ct)
    {
        var expiredCommands = await _commandRepository.GetExpiredAsync(_commandExpiryPeriod, ct);

        foreach (var command in expiredCommands)
        {
            if (!command.IsExpired)
                continue;

            command.MarkExpired();
            await _commandRepository.UpdateAsync(command, ct);

            // Audit the expiry
            await _auditStore.AppendAsync(
                AuditEntry.Create(
                    transactionReference: $"cmd:{command.Id}",
                    actorIdentity: "system",
                    action: "command.expired",
                    previousState: $"{{\"status\":\"Pending\",\"makerId\":\"{command.MakerId}\"}}",
                    newState: $"{{\"status\":\"Expired\"}}",
                    correlationId: command.Id.ToString(),
                    previousEntryHash: null),
                ct);
        }
    }
}
