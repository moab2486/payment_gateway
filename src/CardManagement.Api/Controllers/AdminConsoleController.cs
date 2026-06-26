using CardManagement.Application.PlatformServices.AdminConsole;
using CardManagement.Application.PlatformServices.AdminConsole.DTOs;
using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.PlatformServices.AdminConsole;
using CardManagement.Domain.PlatformServices.AdminConsole.ReadModels;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for the Admin Console: maker-checker command workflow,
/// RBAC-gated operations, CQRS dashboard read models, role management, and audit trail queries.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
public class AdminConsoleController : ControllerBase
{
    private readonly IMakerCheckerWorkflow _makerCheckerWorkflow;
    private readonly IRbacService _rbacService;
    private readonly IAdminReadModelQuery _readModelQuery;
    private readonly IAdminCommandRepository _commandRepository;
    private readonly IAuditStore _auditStore;

    public AdminConsoleController(
        IMakerCheckerWorkflow makerCheckerWorkflow,
        IRbacService rbacService,
        IAdminReadModelQuery readModelQuery,
        IAdminCommandRepository commandRepository,
        IAuditStore auditStore)
    {
        _makerCheckerWorkflow = makerCheckerWorkflow;
        _rbacService = rbacService;
        _readModelQuery = readModelQuery;
        _commandRepository = commandRepository;
        _auditStore = auditStore;
    }

    // ──────────────────────────────────────────────
    // Maker-Checker Command Endpoints
    // ──────────────────────────────────────────────

    /// <summary>
    /// Submits an admin command (maker role). Sensitive commands enter the maker-checker workflow.
    /// RBAC-gated: the user must have permission for the command's operation type.
    /// </summary>
    [HttpPost("commands")]
    [ProducesResponseType(typeof(PendingCommand), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitCommand(
        [FromBody] SubmitCommandRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = GetUserId();

        // RBAC gate: verify user has permission for this command type
        var hasPermission = await _rbacService.HasPermissionAsync(userId, request.CommandType, ct);
        if (!hasPermission)
            return Forbid();

        var command = new AdminCommand(
            request.CommandType,
            request.SerializedParameters,
            request.IsSensitive);

        try
        {
            var pendingCommand = await _makerCheckerWorkflow.SubmitCommandAsync(command, userId, ct);
            return AcceptedAtAction(nameof(GetPendingCommands), null, pendingCommand);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Lists pending commands awaiting checker approval.
    /// RBAC-gated: user must have command.view permission.
    /// </summary>
    [HttpGet("commands/pending")]
    [ProducesResponseType(typeof(IReadOnlyList<PendingCommand>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingCommands(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.CommandView, ct);
        if (!hasPermission)
            return Forbid();

        var commands = await _commandRepository.GetPendingAsync(limit, offset, ct);
        return Ok(commands);
    }

    /// <summary>
    /// Approves a pending command (checker role). Enforces maker ≠ checker separation.
    /// RBAC-gated: user must have command.approve permission.
    /// </summary>
    [HttpPost("commands/{id:guid}/approve")]
    [ProducesResponseType(typeof(CommandExecutionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveCommand(
        Guid id,
        CancellationToken ct)
    {
        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.CommandApprove, ct);
        if (!hasPermission)
            return Forbid();

        try
        {
            var result = await _makerCheckerWorkflow.ApproveAsync(id, userId, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("same user", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("maker and checker", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { Error = ex.Message, Code = "MAKER_CHECKER_SAME_USER" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { Error = ex.Message, Code = "COMMAND_EXPIRED" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Rejects a pending command (checker role) with a reason.
    /// RBAC-gated: user must have command.reject permission.
    /// </summary>
    [HttpPost("commands/{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectCommand(
        Guid id,
        [FromBody] RejectCommandRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.CommandReject, ct);
        if (!hasPermission)
            return Forbid();

        try
        {
            await _makerCheckerWorkflow.RejectAsync(id, userId, request.Reason, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("same user", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("maker and checker", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { Error = ex.Message, Code = "MAKER_CHECKER_SAME_USER" });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    // ──────────────────────────────────────────────
    // Dashboard Read Model Endpoints
    // ──────────────────────────────────────────────

    /// <summary>
    /// Queries the transaction summary read model with filtering and pagination.
    /// Includes staleness metadata in the response.
    /// </summary>
    [HttpGet("dashboard/transactions")]
    [ProducesResponseType(typeof(ReadModelResponse<TransactionSummaryReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTransactionSummary(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? channel = null,
        CancellationToken ct = default)
    {
        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.DashboardView, ct);
        if (!hasPermission)
            return Forbid();

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(channel))
            filters["channel"] = channel;

        var query = new ReadModelQuery("TransactionSummary", page, pageSize, fromDate, toDate, filters);
        var result = await _readModelQuery.QueryAsync<TransactionSummaryReadModel>(query, ct);
        var metadata = await _readModelQuery.GetMetadataAsync("TransactionSummary", ct);

        return Ok(new ReadModelResponse<TransactionSummaryReadModel>(result, metadata));
    }

    /// <summary>
    /// Queries the reconciliation status read model with filtering and pagination.
    /// Includes staleness metadata in the response.
    /// </summary>
    [HttpGet("dashboard/reconciliation")]
    [ProducesResponseType(typeof(ReadModelResponse<ReconciliationStatusReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetReconciliationStatus(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? processor = null,
        CancellationToken ct = default)
    {
        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.DashboardView, ct);
        if (!hasPermission)
            return Forbid();

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(processor))
            filters["processor"] = processor;

        var query = new ReadModelQuery("ReconciliationStatus", page, pageSize, fromDate, toDate, filters);
        var result = await _readModelQuery.QueryAsync<ReconciliationStatusReadModel>(query, ct);
        var metadata = await _readModelQuery.GetMetadataAsync("ReconciliationStatus", ct);

        return Ok(new ReadModelResponse<ReconciliationStatusReadModel>(result, metadata));
    }

    /// <summary>
    /// Queries the dispute metrics read model with filtering and pagination.
    /// Includes staleness metadata in the response.
    /// </summary>
    [HttpGet("dashboard/disputes")]
    [ProducesResponseType(typeof(ReadModelResponse<DisputeMetricsReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDisputeMetrics(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? disputeType = null,
        CancellationToken ct = default)
    {
        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.DashboardView, ct);
        if (!hasPermission)
            return Forbid();

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(disputeType))
            filters["disputeType"] = disputeType;

        var query = new ReadModelQuery("DisputeMetrics", page, pageSize, fromDate, toDate, filters);
        var result = await _readModelQuery.QueryAsync<DisputeMetricsReadModel>(query, ct);
        var metadata = await _readModelQuery.GetMetadataAsync("DisputeMetrics", ct);

        return Ok(new ReadModelResponse<DisputeMetricsReadModel>(result, metadata));
    }

    /// <summary>
    /// Queries the channel health read model with filtering and pagination.
    /// Includes staleness metadata in the response.
    /// </summary>
    [HttpGet("dashboard/channels")]
    [ProducesResponseType(typeof(ReadModelResponse<ChannelHealthReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetChannelHealth(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] string? channelName = null,
        CancellationToken ct = default)
    {
        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.DashboardView, ct);
        if (!hasPermission)
            return Forbid();

        var filters = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(channelName))
            filters["channelName"] = channelName;

        var query = new ReadModelQuery("ChannelHealth", page, pageSize, fromDate, toDate, filters);
        var result = await _readModelQuery.QueryAsync<ChannelHealthReadModel>(query, ct);
        var metadata = await _readModelQuery.GetMetadataAsync("ChannelHealth", ct);

        return Ok(new ReadModelResponse<ChannelHealthReadModel>(result, metadata));
    }

    // ──────────────────────────────────────────────
    // Audit Trail Endpoint
    // ──────────────────────────────────────────────

    /// <summary>
    /// Queries the immutable audit trail. Read-only; no modification or deletion is permitted.
    /// RBAC-gated: user must have audit.view permission.
    /// </summary>
    [HttpGet("audit")]
    [ProducesResponseType(typeof(IReadOnlyList<Domain.Entities.AuditEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> QueryAuditTrail(
        [FromQuery] string? actor = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.AuditView, ct);
        if (!hasPermission)
            return Forbid();

        var entries = await _auditStore.QueryAsync(actor, action, fromUtc, toUtc, limit, offset, ct);
        return Ok(entries);
    }

    // ──────────────────────────────────────────────
    // Role Management Endpoints
    // ──────────────────────────────────────────────

    /// <summary>
    /// Assigns a role to a user. Subject to maker-checker workflow (role assignment is a sensitive operation).
    /// RBAC-gated: user must have role.assign permission (security-admin only).
    /// </summary>
    [HttpPost("roles")]
    [ProducesResponseType(typeof(PendingCommand), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignRole(
        [FromBody] AssignRoleRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            userId, RolePermissionMatrix.Operations.RoleAssign, ct);
        if (!hasPermission)
            return Forbid();

        // Role modifications are sensitive — they go through maker-checker
        var serializedParams = System.Text.Json.JsonSerializer.Serialize(new
        {
            request.TargetUserId,
            request.Role
        });

        var command = new AdminCommand(
            RolePermissionMatrix.Operations.RoleAssign,
            serializedParams,
            IsSensitive: true);

        try
        {
            var pendingCommand = await _makerCheckerWorkflow.SubmitCommandAsync(command, userId, ct);
            return AcceptedAtAction(nameof(GetPendingCommands), null, pendingCommand);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Revokes a role from a user. Subject to maker-checker workflow (role revocation is a sensitive operation).
    /// RBAC-gated: user must have role.revoke permission (security-admin only).
    /// </summary>
    [HttpDelete("roles/{userId}/{role}")]
    [ProducesResponseType(typeof(PendingCommand), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevokeRole(
        string userId,
        string role,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { Error = "User ID is required." });

        if (string.IsNullOrWhiteSpace(role))
            return BadRequest(new { Error = "Role is required." });

        var currentUserId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            currentUserId, RolePermissionMatrix.Operations.RoleRevoke, ct);
        if (!hasPermission)
            return Forbid();

        // Role modifications are sensitive — they go through maker-checker
        var serializedParams = System.Text.Json.JsonSerializer.Serialize(new
        {
            TargetUserId = userId,
            Role = role
        });

        var command = new AdminCommand(
            RolePermissionMatrix.Operations.RoleRevoke,
            serializedParams,
            IsSensitive: true);

        try
        {
            var pendingCommand = await _makerCheckerWorkflow.SubmitCommandAsync(command, currentUserId, ct);
            return AcceptedAtAction(nameof(GetPendingCommands), null, pendingCommand);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Gets the active roles for a specific user.
    /// RBAC-gated: user must have role.view permission.
    /// </summary>
    [HttpGet("roles/{userId}")]
    [ProducesResponseType(typeof(UserRolesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUserRoles(
        string userId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { Error = "User ID is required." });

        var currentUserId = GetUserId();

        var hasPermission = await _rbacService.HasPermissionAsync(
            currentUserId, RolePermissionMatrix.Operations.RoleView, ct);
        if (!hasPermission)
            return Forbid();

        var roles = await _rbacService.GetRolesAsync(userId, ct);
        return Ok(new UserRolesResponse(userId, roles));
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private string GetUserId()
    {
        return User.Identity?.Name ?? "anonymous";
    }
}

// ──────────────────────────────────────────────
// Request / Response Models
// ──────────────────────────────────────────────

/// <summary>
/// Request model for submitting an admin command.
/// </summary>
public class SubmitCommandRequest
{
    /// <summary>The type/category of command (e.g., "reconciliation.adjust", "refund.execute").</summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>JSON-serialized command parameters (sensitive data excluded).</summary>
    public string SerializedParameters { get; set; } = string.Empty;

    /// <summary>Whether this command is classified as sensitive and requires dual authorization.</summary>
    public bool IsSensitive { get; set; }
}

/// <summary>
/// Request model for rejecting a pending command.
/// </summary>
public class RejectCommandRequest
{
    /// <summary>The reason for rejecting the command.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Request model for assigning a role to a user.
/// </summary>
public class AssignRoleRequest
{
    /// <summary>The user ID to assign the role to.</summary>
    public string TargetUserId { get; set; } = string.Empty;

    /// <summary>The role to assign (operations, compliance, risk, support, security-admin).</summary>
    public string Role { get; set; } = string.Empty;
}

/// <summary>
/// Response model for user roles query.
/// </summary>
public record UserRolesResponse(string UserId, IReadOnlyList<string> Roles);

/// <summary>
/// Wrapper response for read model queries that includes staleness metadata.
/// </summary>
public record ReadModelResponse<T>(
    PagedResult<T> Data,
    ReadModelMetadata Metadata) where T : class;
