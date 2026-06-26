using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Handlers;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.PlatformServices.Webhooks.Queries;
using CardManagement.Domain.PlatformServices.DeveloperPortal;
using CardManagement.Domain.PlatformServices.Webhooks;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for the Developer Portal: self-service API key management,
/// webhook subscription management, request/response log viewing, and sandbox operations.
/// All endpoints enforce developer resource isolation — developers can only access their own resources.
/// </summary>
[ApiController]
[Route("api/v1/developer")]
public class DeveloperPortalController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IWebhookSubscriptionRepository _webhookSubscriptionRepository;
    private readonly IWebhookDeliveryEngine _webhookDeliveryEngine;
    private readonly IWebhookDeliveryRepository _webhookDeliveryRepository;
    private readonly IRequestLogRepository _requestLogRepository;
    private readonly ISandboxEnvironment _sandboxEnvironment;
    private readonly CreateSubscriptionCommandHandler _createSubscriptionHandler;
    private readonly UpdateSubscriptionCommandHandler _updateSubscriptionHandler;
    private readonly DeactivateSubscriptionCommandHandler _deactivateSubscriptionHandler;
    private readonly ReplayFromDlqCommandHandler _replayFromDlqHandler;

    public DeveloperPortalController(
        IApiKeyService apiKeyService,
        IApiKeyRepository apiKeyRepository,
        IWebhookSubscriptionRepository webhookSubscriptionRepository,
        IWebhookDeliveryEngine webhookDeliveryEngine,
        IWebhookDeliveryRepository webhookDeliveryRepository,
        IRequestLogRepository requestLogRepository,
        ISandboxEnvironment sandboxEnvironment,
        CreateSubscriptionCommandHandler createSubscriptionHandler,
        UpdateSubscriptionCommandHandler updateSubscriptionHandler,
        DeactivateSubscriptionCommandHandler deactivateSubscriptionHandler,
        ReplayFromDlqCommandHandler replayFromDlqHandler)
    {
        _apiKeyService = apiKeyService;
        _apiKeyRepository = apiKeyRepository;
        _webhookSubscriptionRepository = webhookSubscriptionRepository;
        _webhookDeliveryEngine = webhookDeliveryEngine;
        _webhookDeliveryRepository = webhookDeliveryRepository;
        _requestLogRepository = requestLogRepository;
        _sandboxEnvironment = sandboxEnvironment;
        _createSubscriptionHandler = createSubscriptionHandler;
        _updateSubscriptionHandler = updateSubscriptionHandler;
        _deactivateSubscriptionHandler = deactivateSubscriptionHandler;
        _replayFromDlqHandler = replayFromDlqHandler;
    }

    // ──────────────────────────────────────────────
    // API Key Management Endpoints
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a new API key for the authenticated developer.
    /// The raw key value is returned exactly once and never stored in plaintext.
    /// </summary>
    [HttpPost("keys")]
    [ProducesResponseType(typeof(ApiKeyCreateResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateApiKey(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var developerId = GetDeveloperId();

        try
        {
            var result = await _apiKeyService.CreateKeyAsync(developerId, request.Scopes, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new { Error = ex.Message, Code = "KEY_LIMIT_EXCEEDED" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Lists all API keys for the authenticated developer.
    /// Key values are masked — only the prefix and metadata are returned.
    /// </summary>
    [HttpGet("keys")]
    [ProducesResponseType(typeof(IReadOnlyList<ApiKeyListItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListApiKeys(CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        var keys = await _apiKeyRepository.GetByDeveloperAsync(developerId, ct);

        var masked = keys.Select(k => new ApiKeyListItem(
            k.Id,
            k.KeyPrefix + "****",
            k.Scopes,
            k.Status,
            k.IsSandbox,
            k.CreatedAtUtc,
            k.ExpiresAtUtc,
            k.GracePeriodEndsAtUtc)).ToList();

        return Ok(masked);
    }

    /// <summary>
    /// Rotates an API key, generating a new key with a grace period during which both keys are valid.
    /// The new raw key value is returned exactly once.
    /// </summary>
    [HttpPost("keys/{id:guid}/rotate")]
    [ProducesResponseType(typeof(ApiKeyRotateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RotateApiKey(
        Guid id,
        [FromBody] RotateApiKeyRequest request,
        CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        // Enforce resource isolation: verify key belongs to this developer
        var existingKey = await _apiKeyRepository.GetByIdAsync(id, ct);
        if (existingKey is null)
            return NotFound(new { Error = "API key not found." });

        if (existingKey.DeveloperId != developerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Access denied." });

        try
        {
            var gracePeriod = TimeSpan.FromHours(request.GracePeriodHours);
            var result = await _apiKeyService.RotateKeyAsync(id, gracePeriod, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Revokes an API key immediately. All subsequent requests using this key will be rejected.
    /// </summary>
    [HttpDelete("keys/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeApiKey(
        Guid id,
        CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        // Enforce resource isolation: verify key belongs to this developer
        var existingKey = await _apiKeyRepository.GetByIdAsync(id, ct);
        if (existingKey is null)
            return NotFound(new { Error = "API key not found." });

        if (existingKey.DeveloperId != developerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Access denied." });

        try
        {
            await _apiKeyService.RevokeKeyAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    // ──────────────────────────────────────────────
    // Webhook Subscription Management Endpoints
    // ──────────────────────────────────────────────

    /// <summary>
    /// Lists webhook subscriptions for the authenticated developer.
    /// </summary>
    [HttpGet("webhooks")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookSubscription>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWebhookSubscriptions(CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        var subscriptions = await _webhookSubscriptionRepository.GetByMerchantAsync(developerId, ct);
        return Ok(subscriptions);
    }

    /// <summary>
    /// Creates a webhook subscription for the authenticated developer.
    /// Delegates registration to the Webhook Service.
    /// </summary>
    [HttpPost("webhooks")]
    [ProducesResponseType(typeof(WebhookSubscription), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateWebhookSubscription(
        [FromBody] CreateDeveloperWebhookRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var developerId = GetDeveloperId();

        var command = new CreateSubscriptionCommand(
            developerId,
            request.DestinationUrl,
            request.EventTypes,
            request.SigningSecret);

        var result = await _createSubscriptionHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == "SUBSCRIPTION_LIMIT_EXCEEDED")
                return Conflict(new { Error = result.ErrorMessage, Code = result.ErrorCode });

            return BadRequest(new { Error = result.ErrorMessage, Code = result.ErrorCode });
        }

        return CreatedAtAction(nameof(ListWebhookSubscriptions), null, result.Value);
    }

    /// <summary>
    /// Updates a webhook subscription owned by the authenticated developer.
    /// </summary>
    [HttpPut("webhooks/{id:guid}")]
    [ProducesResponseType(typeof(WebhookSubscription), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateWebhookSubscription(
        Guid id,
        [FromBody] UpdateDeveloperWebhookRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var developerId = GetDeveloperId();

        // Enforce resource isolation: verify subscription belongs to this developer
        var subscription = await _webhookSubscriptionRepository.GetByIdAsync(id, ct);
        if (subscription is null)
            return NotFound(new { Error = "Webhook subscription not found." });

        if (subscription.MerchantId != developerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Access denied." });

        var command = new UpdateSubscriptionCommand(
            id,
            request.DestinationUrl,
            request.EventTypes);

        var result = await _updateSubscriptionHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == "SUBSCRIPTION_NOT_FOUND")
                return NotFound(new { Error = result.ErrorMessage, Code = result.ErrorCode });

            return BadRequest(new { Error = result.ErrorMessage, Code = result.ErrorCode });
        }

        return Ok(result.Value);
    }

    /// <summary>
    /// Deactivates a webhook subscription owned by the authenticated developer.
    /// </summary>
    [HttpDelete("webhooks/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateWebhookSubscription(
        Guid id,
        CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        // Enforce resource isolation: verify subscription belongs to this developer
        var subscription = await _webhookSubscriptionRepository.GetByIdAsync(id, ct);
        if (subscription is null)
            return NotFound(new { Error = "Webhook subscription not found." });

        if (subscription.MerchantId != developerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Access denied." });

        var command = new DeactivateSubscriptionCommand(id);
        var result = await _deactivateSubscriptionHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
            return BadRequest(new { Error = result.ErrorMessage, Code = result.ErrorCode });

        return NoContent();
    }

    /// <summary>
    /// Gets delivery history for a webhook subscription owned by the authenticated developer.
    /// </summary>
    [HttpGet("webhooks/{id:guid}/deliveries")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookDelivery>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWebhookDeliveryHistory(
        Guid id,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var developerId = GetDeveloperId();

        // Enforce resource isolation: verify subscription belongs to this developer
        var subscription = await _webhookSubscriptionRepository.GetByIdAsync(id, ct);
        if (subscription is null)
            return NotFound(new { Error = "Webhook subscription not found." });

        if (subscription.MerchantId != developerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Access denied." });

        var deliveries = await _webhookDeliveryRepository.GetBySubscriptionAsync(id, limit, offset, ct);
        return Ok(deliveries);
    }

    /// <summary>
    /// Gets dead-letter queue items for a webhook subscription owned by the authenticated developer.
    /// </summary>
    [HttpGet("webhooks/{id:guid}/dlq")]
    [ProducesResponseType(typeof(IReadOnlyList<DlqItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWebhookDlqItems(
        Guid id,
        CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        // Enforce resource isolation: verify subscription belongs to this developer
        var subscription = await _webhookSubscriptionRepository.GetByIdAsync(id, ct);
        if (subscription is null)
            return NotFound(new { Error = "Webhook subscription not found." });

        if (subscription.MerchantId != developerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Access denied." });

        var items = await _webhookDeliveryRepository.GetDlqItemsAsync(id, ct);
        return Ok(items);
    }

    /// <summary>
    /// Replays a dead-letter queue item. Delegates to the Webhook Service which
    /// re-attempts delivery with a fresh HMAC signature.
    /// </summary>
    [HttpPost("webhooks/dlq/{dlqItemId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayDlqItem(
        Guid dlqItemId,
        CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        // Enforce resource isolation: verify the DLQ item's subscription belongs to this developer
        var dlqItem = await _webhookDeliveryRepository.GetDlqItemByIdAsync(dlqItemId, ct);
        if (dlqItem is null)
            return NotFound(new { Error = "DLQ item not found." });

        var subscription = await _webhookSubscriptionRepository.GetByIdAsync(dlqItem.SubscriptionId, ct);
        if (subscription is null || subscription.MerchantId != developerId)
            return StatusCode(StatusCodes.Status403Forbidden, new { Error = "Access denied." });

        var command = new ReplayFromDlqCommand(dlqItemId);
        var result = await _replayFromDlqHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == "DLQ_ITEM_NOT_FOUND")
                return NotFound(new { Error = result.ErrorMessage, Code = result.ErrorCode });

            return BadRequest(new { Error = result.ErrorMessage, Code = result.ErrorCode });
        }

        return Accepted();
    }

    // ──────────────────────────────────────────────
    // Request/Response Log Endpoints
    // ──────────────────────────────────────────────

    /// <summary>
    /// Queries request/response logs for the authenticated developer.
    /// Supports filtering by endpoint, status code, and date range.
    /// </summary>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(PagedResult<RequestLogEntry>), StatusCodes.Status200OK)]
    public async Task<IActionResult> QueryRequestLogs(
        [FromQuery] string? endpoint = null,
        [FromQuery] int? statusCode = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var developerId = GetDeveloperId();

        var query = new RequestLogQuery(
            DeveloperId: developerId,
            Endpoint: endpoint,
            StatusCode: statusCode,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            Page: page,
            PageSize: pageSize);

        var result = await _requestLogRepository.QueryAsync(query, ct);
        return Ok(result);
    }

    // ──────────────────────────────────────────────
    // Sandbox Endpoints
    // ──────────────────────────────────────────────

    /// <summary>
    /// Resets sandbox data for the authenticated developer to a known baseline state.
    /// </summary>
    [HttpPost("sandbox/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetSandbox(CancellationToken ct)
    {
        var developerId = GetDeveloperId();

        await _sandboxEnvironment.ResetDataAsync(developerId, ct);
        return NoContent();
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private Guid GetDeveloperId()
    {
        var idClaim = User.FindFirst("developer_id")?.Value
            ?? User.Identity?.Name;

        if (Guid.TryParse(idClaim, out var developerId))
            return developerId;

        return Guid.Empty;
    }
}

// ──────────────────────────────────────────────
// Request / Response Models
// ──────────────────────────────────────────────

/// <summary>
/// Request model for creating an API key.
/// </summary>
public class CreateApiKeyRequest
{
    /// <summary>The permission scopes for the new key.</summary>
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Request model for rotating an API key.
/// </summary>
public class RotateApiKeyRequest
{
    /// <summary>Grace period in hours during which both old and new keys are valid (default: 24).</summary>
    public double GracePeriodHours { get; set; } = 24;
}

/// <summary>
/// Request model for creating a developer webhook subscription.
/// </summary>
public class CreateDeveloperWebhookRequest
{
    /// <summary>The destination URL to receive webhook events.</summary>
    public string DestinationUrl { get; set; } = string.Empty;

    /// <summary>The event types to subscribe to.</summary>
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>The secret used for HMAC-SHA256 payload signing.</summary>
    public string SigningSecret { get; set; } = string.Empty;
}

/// <summary>
/// Request model for updating a developer webhook subscription.
/// </summary>
public class UpdateDeveloperWebhookRequest
{
    /// <summary>The new destination URL (optional).</summary>
    public string? DestinationUrl { get; set; }

    /// <summary>The new event types (optional).</summary>
    public string[]? EventTypes { get; set; }
}

/// <summary>
/// Masked API key information for list responses.
/// </summary>
public record ApiKeyListItem(
    Guid Id,
    string MaskedKey,
    string[] Scopes,
    KeyStatus Status,
    bool IsSandbox,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? GracePeriodEndsAtUtc);
