using CardManagement.Application.PlatformServices.AdminConsole.DTOs;
using CardManagement.Domain.PlatformServices.AdminConsole;

namespace CardManagement.Application.PlatformServices.AdminConsole.Ports;

/// <summary>
/// Defines the maker-checker dual authorization workflow for sensitive admin commands.
/// A maker submits a command which requires a different checker to approve before execution.
/// </summary>
public interface IMakerCheckerWorkflow
{
    /// <summary>
    /// Submits a command for dual authorization. Persists in pending state and notifies eligible checkers.
    /// </summary>
    Task<PendingCommand> SubmitCommandAsync(AdminCommand command, string makerId, CancellationToken ct);

    /// <summary>
    /// Approves a pending command. Validates checker ≠ maker, executes command, and records audit trail.
    /// </summary>
    Task<CommandExecutionResult> ApproveAsync(Guid commandId, string checkerId, CancellationToken ct);

    /// <summary>
    /// Rejects a pending command. Records rejection reason and notifies the maker.
    /// </summary>
    Task RejectAsync(Guid commandId, string checkerId, string reason, CancellationToken ct);

    /// <summary>
    /// Marks all pending commands that have exceeded the expiry threshold as expired.
    /// </summary>
    Task ExpireStaleCommandsAsync(CancellationToken ct);
}
