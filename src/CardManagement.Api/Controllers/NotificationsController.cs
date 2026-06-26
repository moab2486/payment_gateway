using CardManagement.Application.PlatformServices.Notifications.Commands;
using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Application.PlatformServices.Notifications.Queries;
using CardManagement.Domain.PlatformServices.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for notification operations: dispatching notifications,
/// managing templates, querying delivery logs and statistics, and managing recipient preferences.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly DispatchNotificationCommandHandler _dispatchHandler;
    private readonly CreateTemplateCommandHandler _createTemplateHandler;
    private readonly UpdateTemplateCommandHandler _updateTemplateHandler;
    private readonly GetDeliveryLogQueryHandler _getDeliveryLogHandler;
    private readonly GetDeliveryStatisticsQueryHandler _getDeliveryStatisticsHandler;
    private readonly GetPreferencesQueryHandler _getPreferencesHandler;
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly INotificationPreferenceRepository _preferenceRepository;

    public NotificationsController(
        DispatchNotificationCommandHandler dispatchHandler,
        CreateTemplateCommandHandler createTemplateHandler,
        UpdateTemplateCommandHandler updateTemplateHandler,
        GetDeliveryLogQueryHandler getDeliveryLogHandler,
        GetDeliveryStatisticsQueryHandler getDeliveryStatisticsHandler,
        GetPreferencesQueryHandler getPreferencesHandler,
        INotificationTemplateRepository templateRepository,
        INotificationPreferenceRepository preferenceRepository)
    {
        _dispatchHandler = dispatchHandler;
        _createTemplateHandler = createTemplateHandler;
        _updateTemplateHandler = updateTemplateHandler;
        _getDeliveryLogHandler = getDeliveryLogHandler;
        _getDeliveryStatisticsHandler = getDeliveryStatisticsHandler;
        _getPreferencesHandler = getPreferencesHandler;
        _templateRepository = templateRepository;
        _preferenceRepository = preferenceRepository;
    }

    /// <summary>
    /// Dispatches a notification using the specified template and variables.
    /// The recipient's preferred channel is resolved automatically.
    /// </summary>
    /// <param name="request">The notification dispatch request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Accepted status on successful dispatch.</returns>
    [HttpPost("dispatch")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DispatchNotification(
        [FromBody] DispatchNotificationRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var command = new DispatchNotificationCommand(
            request.RecipientId,
            request.RecipientAddress,
            request.TemplateId,
            request.Variables);

        try
        {
            await _dispatchHandler.HandleAsync(command, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }

        return Accepted();
    }

    /// <summary>
    /// Lists notification templates with pagination.
    /// </summary>
    /// <param name="limit">Maximum number of templates to return (default: 20).</param>
    /// <param name="offset">Number of templates to skip (default: 0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of notification templates.</returns>
    [HttpGet("templates")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationTemplate>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTemplates(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var templates = await _templateRepository.ListAsync(limit, offset, ct);
        return Ok(templates);
    }

    /// <summary>
    /// Creates a new notification template with channel-specific content.
    /// </summary>
    /// <param name="request">The template creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created template ID.</returns>
    [HttpPost("templates")]
    [ProducesResponseType(typeof(CreateTemplateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTemplate(
        [FromBody] CreateTemplateRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var command = new CreateTemplateCommand(
            request.Name,
            request.Category,
            request.RequiredVariables,
            request.EmailSubjectTemplate,
            request.EmailBodyTemplate,
            request.SmsBodyTemplate,
            request.WhatsAppBodyTemplate);

        try
        {
            var templateId = await _createTemplateHandler.HandleAsync(command, ct);
            var response = new CreateTemplateResponse(templateId);
            return CreatedAtAction(nameof(ListTemplates), null, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Updates an existing notification template's content and required variables.
    /// </summary>
    /// <param name="id">The template identifier.</param>
    /// <param name="request">The template update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success.</returns>
    [HttpPut("templates/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTemplate(
        Guid id,
        [FromBody] UpdateTemplateRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var command = new UpdateTemplateCommand(
            id,
            request.EmailSubjectTemplate,
            request.EmailBodyTemplate,
            request.SmsBodyTemplate,
            request.WhatsAppBodyTemplate,
            request.RequiredVariables);

        try
        {
            await _updateTemplateHandler.HandleAsync(command, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { Error = ex.Message });
        }

        return NoContent();
    }

    /// <summary>
    /// Queries delivery log entries for a specific recipient.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="limit">Maximum number of log entries to return (default: 50).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of delivery log entries.</returns>
    [HttpGet("delivery-log")]
    [ProducesResponseType(typeof(IReadOnlyList<DeliveryLogEntry>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDeliveryLog(
        [FromQuery] string recipientId,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(recipientId))
            return BadRequest(new { Error = "recipientId query parameter is required." });

        var query = new GetDeliveryLogQuery(recipientId, limit);

        try
        {
            var entries = await _getDeliveryLogHandler.HandleAsync(query, ct);
            return Ok(entries);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Gets delivery statistics aggregated per channel and/or template within a date range.
    /// </summary>
    /// <param name="channel">Optional channel filter (Email, Sms, WhatsApp).</param>
    /// <param name="templateId">Optional template ID filter.</param>
    /// <param name="startUtc">Start of the date range (UTC).</param>
    /// <param name="endUtc">End of the date range (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated delivery statistics.</returns>
    [HttpGet("statistics")]
    [ProducesResponseType(typeof(DeliveryStatistics), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] NotificationChannel? channel,
        [FromQuery] Guid? templateId,
        [FromQuery] DateTime startUtc,
        [FromQuery] DateTime endUtc,
        CancellationToken ct = default)
    {
        if (startUtc == default || endUtc == default)
            return BadRequest(new { Error = "startUtc and endUtc query parameters are required." });

        DateRange range;
        try
        {
            range = new DateRange(startUtc, endUtc);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }

        var query = new GetDeliveryStatisticsQuery(channel, templateId, range);
        var statistics = await _getDeliveryStatisticsHandler.HandleAsync(query, ct);
        return Ok(statistics);
    }

    /// <summary>
    /// Gets notification preferences for a specific recipient.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recipient's notification preferences, or 404 if not found.</returns>
    [HttpGet("preferences/{recipientId}")]
    [ProducesResponseType(typeof(NotificationPreference), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPreferences(
        string recipientId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recipientId))
            return BadRequest(new { Error = "Recipient ID is required." });

        var query = new GetPreferencesQuery(recipientId);
        var preferences = await _getPreferencesHandler.HandleAsync(query, ct);

        if (preferences is null)
            return NotFound(new { Error = $"Preferences for recipient '{recipientId}' not found." });

        return Ok(preferences);
    }

    /// <summary>
    /// Updates or creates notification preferences for a recipient.
    /// </summary>
    /// <param name="recipientId">The recipient identifier.</param>
    /// <param name="request">The preference update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated preferences.</returns>
    [HttpPut("preferences/{recipientId}")]
    [ProducesResponseType(typeof(NotificationPreference), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePreferences(
        string recipientId,
        [FromBody] UpdatePreferencesRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (string.IsNullOrWhiteSpace(recipientId))
            return BadRequest(new { Error = "Recipient ID is required." });

        try
        {
            var existing = await _preferenceRepository.GetByRecipientAsync(recipientId, ct);

            if (existing is null)
            {
                // Create new preference
                existing = NotificationPreference.Create(
                    recipientId,
                    request.PrimaryChannel,
                    request.FallbackChannel);
            }
            else
            {
                // Update existing preference
                existing.UpdateChannels(request.PrimaryChannel, request.FallbackChannel);
            }

            // Apply category opt-in settings
            if (request.CategoryOptIn is not null)
            {
                foreach (var (category, optedIn) in request.CategoryOptIn)
                {
                    existing.SetCategoryOptIn(category, optedIn);
                }
            }

            await _preferenceRepository.SaveAsync(existing, ct);
            return Ok(existing);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }
}

/// <summary>
/// Request model for dispatching a notification.
/// </summary>
public class DispatchNotificationRequest
{
    /// <summary>The recipient identifier.</summary>
    public string RecipientId { get; set; } = string.Empty;

    /// <summary>The recipient's delivery address (email, phone number, etc.).</summary>
    public string RecipientAddress { get; set; } = string.Empty;

    /// <summary>The template ID to use for rendering.</summary>
    public Guid TemplateId { get; set; }

    /// <summary>The template variable values for substitution.</summary>
    public Dictionary<string, string> Variables { get; set; } = new();
}

/// <summary>
/// Request model for creating a notification template.
/// </summary>
public class CreateTemplateRequest
{
    /// <summary>Unique template name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Template category (e.g., "payment", "dispute", "account").</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Variables that must be provided at dispatch time.</summary>
    public string[] RequiredVariables { get; set; } = Array.Empty<string>();

    /// <summary>Email subject template with {{variable}} placeholders.</summary>
    public string? EmailSubjectTemplate { get; set; }

    /// <summary>Email body template with {{variable}} placeholders.</summary>
    public string? EmailBodyTemplate { get; set; }

    /// <summary>SMS body template with {{variable}} placeholders.</summary>
    public string? SmsBodyTemplate { get; set; }

    /// <summary>WhatsApp body template with {{variable}} placeholders.</summary>
    public string? WhatsAppBodyTemplate { get; set; }
}

/// <summary>
/// Request model for updating a notification template.
/// </summary>
public class UpdateTemplateRequest
{
    /// <summary>Updated email subject template.</summary>
    public string? EmailSubjectTemplate { get; set; }

    /// <summary>Updated email body template.</summary>
    public string? EmailBodyTemplate { get; set; }

    /// <summary>Updated SMS body template.</summary>
    public string? SmsBodyTemplate { get; set; }

    /// <summary>Updated WhatsApp body template.</summary>
    public string? WhatsAppBodyTemplate { get; set; }

    /// <summary>Updated required variables list.</summary>
    public string[] RequiredVariables { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Response model for template creation.
/// </summary>
public record CreateTemplateResponse(Guid TemplateId);

/// <summary>
/// Request model for updating recipient notification preferences.
/// </summary>
public class UpdatePreferencesRequest
{
    /// <summary>The preferred primary notification channel.</summary>
    public NotificationChannel PrimaryChannel { get; set; }

    /// <summary>The fallback channel if primary fails (optional, must differ from primary).</summary>
    public NotificationChannel? FallbackChannel { get; set; }

    /// <summary>Per-category opt-in/opt-out settings.</summary>
    public Dictionary<string, bool>? CategoryOptIn { get; set; }
}
