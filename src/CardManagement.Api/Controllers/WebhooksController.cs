using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Handlers;
using CardManagement.Application.PlatformServices.Webhooks.Queries;
using CardManagement.Domain.PlatformServices.Webhooks;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for webhook subscription management, delivery history, and dead-letter queue operations.
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly CreateSubscriptionCommandHandler _createSubscriptionHandler;
    private readonly UpdateSubscriptionCommandHandler _updateSubscriptionHandler;
    private readonly DeactivateSubscriptionCommandHandler _deactivateSubscriptionHandler;
    private readonly GetDeliveryHistoryQueryHandler _getDeliveryHistoryHandler;
    private readonly GetDlqItemsQueryHandler _getDlqItemsHandler;
    private readonly ReplayFromDlqCommandHandler _replayFromDlqHandler;
    private readonly Application.PlatformServices.Webhooks.Ports.IWebhookSubscriptionRepository _subscriptionRepository;

    public WebhooksController(
        CreateSubscriptionCommandHandler createSubscriptionHandler,
        UpdateSubscriptionCommandHandler updateSubscriptionHandler,
        DeactivateSubscriptionCommandHandler deactivateSubscriptionHandler,
        GetDeliveryHistoryQueryHandler getDeliveryHistoryHandler,
        GetDlqItemsQueryHandler getDlqItemsHandler,
        ReplayFromDlqCommandHandler replayFromDlqHandler,
        Application.PlatformServices.Webhooks.Ports.IWebhookSubscriptionRepository subscriptionRepository)
    {
        _createSubscriptionHandler = createSubscriptionHandler;
        _updateSubscriptionHandler = updateSubscriptionHandler;
        _deactivateSubscriptionHandler = deactivateSubscriptionHandler;
        _getDeliveryHistoryHandler = getDeliveryHistoryHandler;
        _getDlqItemsHandler = getDlqItemsHandler;
        _replayFromDlqHandler = replayFromDlqHandler;
        _subscriptionRepository = subscriptionRepository;
    }

    /// <summary>
    /// Creates a new webhook subscription with URL verification.
    /// </summary>
    /// <param name="request">The subscription creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created subscription.</returns>
    [HttpPost("subscriptions")]
    [ProducesResponseType(typeof(WebhookSubscription), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateSubscription(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var command = new CreateSubscriptionCommand(
            request.MerchantId,
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

        return CreatedAtAction(
            nameof(ListSubscriptions),
            new { merchantId = request.MerchantId },
            result.Value);
    }

    /// <summary>
    /// Lists all webhook subscriptions for a merchant.
    /// </summary>
    /// <param name="merchantId">The merchant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of subscriptions for the merchant.</returns>
    [HttpGet("subscriptions")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookSubscription>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSubscriptions(
        [FromQuery] Guid merchantId,
        CancellationToken ct)
    {
        if (merchantId == Guid.Empty)
            return BadRequest(new { Error = "merchantId query parameter is required." });

        var subscriptions = await _subscriptionRepository.GetByMerchantAsync(merchantId, ct);
        return Ok(subscriptions);
    }

    /// <summary>
    /// Updates an existing webhook subscription's destination URL and/or event types.
    /// </summary>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="request">The update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated subscription.</returns>
    [HttpPut("subscriptions/{id:guid}")]
    [ProducesResponseType(typeof(WebhookSubscription), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSubscription(
        Guid id,
        [FromBody] UpdateSubscriptionRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

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
    /// Deactivates a webhook subscription. Stops all future deliveries.
    /// </summary>
    /// <param name="id">The subscription identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpDelete("subscriptions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateSubscription(
        Guid id,
        CancellationToken ct)
    {
        var command = new DeactivateSubscriptionCommand(id);
        var result = await _deactivateSubscriptionHandler.HandleAsync(command, ct);

        if (!result.IsSuccess)
        {
            if (result.ErrorCode == "SUBSCRIPTION_NOT_FOUND")
                return NotFound(new { Error = result.ErrorMessage, Code = result.ErrorCode });

            return BadRequest(new { Error = result.ErrorMessage, Code = result.ErrorCode });
        }

        return NoContent();
    }

    /// <summary>
    /// Gets delivery history with attempt details for a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription identifier.</param>
    /// <param name="limit">Maximum number of deliveries to return.</param>
    /// <param name="offset">Number of deliveries to skip for pagination.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of webhook deliveries with attempt details.</returns>
    [HttpGet("deliveries/{subscriptionId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookDelivery>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeliveryHistory(
        Guid subscriptionId,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var query = new GetDeliveryHistoryQuery(subscriptionId, limit, offset);
        var deliveries = await _getDeliveryHistoryHandler.HandleAsync(query, ct);
        return Ok(deliveries);
    }

    /// <summary>
    /// Gets dead-letter queue items for a subscription.
    /// </summary>
    /// <param name="subscriptionId">The subscription identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of DLQ items for the subscription.</returns>
    [HttpGet("dlq/{subscriptionId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<DlqItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDlqItems(
        Guid subscriptionId,
        CancellationToken ct)
    {
        var query = new GetDlqItemsQuery(subscriptionId);
        var items = await _getDlqItemsHandler.HandleAsync(query, ct);
        return Ok(items);
    }

    /// <summary>
    /// Replays a dead-lettered webhook delivery with a fresh HMAC signature.
    /// </summary>
    /// <param name="dlqItemId">The DLQ item identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Accepted status on success.</returns>
    [HttpPost("dlq/{dlqItemId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReplayDlqItem(
        Guid dlqItemId,
        CancellationToken ct)
    {
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
}

/// <summary>
/// Request model for creating a webhook subscription.
/// </summary>
public class CreateSubscriptionRequest
{
    /// <summary>The merchant identifier.</summary>
    public Guid MerchantId { get; set; }

    /// <summary>The destination URL to receive webhook events.</summary>
    public string DestinationUrl { get; set; } = string.Empty;

    /// <summary>The event types to subscribe to.</summary>
    public string[] EventTypes { get; set; } = Array.Empty<string>();

    /// <summary>The secret used for HMAC-SHA256 payload signing.</summary>
    public string SigningSecret { get; set; } = string.Empty;
}

/// <summary>
/// Request model for updating a webhook subscription.
/// </summary>
public class UpdateSubscriptionRequest
{
    /// <summary>The new destination URL (optional).</summary>
    public string? DestinationUrl { get; set; }

    /// <summary>The new event types (optional).</summary>
    public string[]? EventTypes { get; set; }
}
