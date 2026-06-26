using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// NIBSS GAPS adapter for bulk/batch payment processing.
/// Validates each batch item for required fields, submits the batch to NIBSS GAPS,
/// and rejects the entire batch if any item fails validation.
/// </summary>
public class GapsAdapter : IChannelAdapter
{
    private readonly INibssGapsClient _gapsClient;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly GapsOptions _options;
    private readonly ILogger<GapsAdapter> _logger;

    public GapsAdapter(
        INibssGapsClient gapsClient,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<GapsOptions> options,
        ILogger<GapsAdapter> logger)
    {
        _gapsClient = gapsClient ?? throw new ArgumentNullException(nameof(gapsClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PaymentChannel Channel => PaymentChannel.GAPS;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.GAPS);

        // Respect circuit breaker open state
        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "GAPS channel circuit breaker is open");
        }

        try
        {
            // Parse batch items from the payment request
            var batchItems = ParseBatchItems(request);

            // Validate batch size
            if (batchItems.Count == 0)
            {
                return new ChannelResult(false, null, "BATCH_VALIDATION_FAILED", "Batch contains no items");
            }

            if (batchItems.Count > _options.MaxBatchSize)
            {
                return new ChannelResult(false, null, "BATCH_VALIDATION_FAILED",
                    $"Batch size {batchItems.Count} exceeds maximum allowed size of {_options.MaxBatchSize}");
            }

            // Validate each item — reject entire batch if any item fails
            var validationErrors = ValidateBatchItems(batchItems);
            if (validationErrors.Count > 0)
            {
                var errorDetails = string.Join("; ", validationErrors.Select(e => $"[{e.ItemReference}] {e.ErrorMessage}"));
                _logger.LogWarning(
                    "GAPS batch validation failed for {TransactionReference}. Errors: {Errors}",
                    request.TransactionReference, errorDetails);
                return new ChannelResult(false, null, "BATCH_VALIDATION_FAILED", errorDetails);
            }

            // Build and submit batch request
            var batchReference = GenerateBatchReference();
            var batchRequest = new GapsBatchRequest(
                BatchReference: batchReference,
                Items: batchItems,
                TransactionReference: request.TransactionReference);

            var response = await _gapsClient.SubmitBatchAsync(batchRequest, ct);

            // Map NIBSS response
            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                circuitBreaker.RecordSuccess();
                _logger.LogInformation(
                    "GAPS batch submitted successfully. NIBSS Ref: {NibssRef}, BatchRef: {BatchRef}, Items: {ItemCount}",
                    response.NibssReference, batchReference, batchItems.Count);
                return new ChannelResult(true, response.NibssReference, null, null);
            }
            else
            {
                var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();

                // Include per-item errors from NIBSS if available
                var message = errorMessage;
                if (response.PerItemErrors is { Count: > 0 })
                {
                    var perItemDetails = string.Join("; ",
                        response.PerItemErrors.Select(e => $"[{e.ItemReference}] {e.ErrorCode}: {e.ErrorMessage}"));
                    message = $"{errorMessage}. Per-item errors: {perItemDetails}";
                }

                _logger.LogWarning(
                    "GAPS batch submission failed. Code: {ResponseCode}, Message: {Message}, BatchRef: {BatchRef}",
                    response.ResponseCode, message, batchReference);
                return new ChannelResult(false, response.NibssReference, errorCode, message);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "GAPS channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            var cbForError = _circuitBreakerRegistry.GetBreaker(PaymentChannel.GAPS);
            cbForError.RecordFailure();
            _logger.LogError(ex, "GAPS batch payment encountered an unexpected error for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        // GAPS batch payments are not reversible at the NIBSS level.
        _logger.LogWarning("GAPS reversal requested for {TransactionReference}. GAPS does not support batch reversals.",
            transactionReference);
        return await Task.FromResult(
            new ChannelResult(false, null, "NOT_SUPPORTED", "GAPS does not support batch payment reversals"));
    }

    public async Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.GAPS);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"GAPS circuit breaker state: {state}";

        return await Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Parses batch items from the payment request.
    /// Batch items are encoded as JSON in the DestinationAccount field.
    /// Format: JSON array of objects with recipientAccount, bankCode, amount, narration, itemReference.
    /// </summary>
    internal static IReadOnlyList<GapsBatchItem> ParseBatchItems(PaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationAccount))
            return Array.Empty<GapsBatchItem>();

        try
        {
            var items = JsonSerializer.Deserialize<List<GapsBatchItemDto>>(
                request.DestinationAccount,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (items is null || items.Count == 0)
                return Array.Empty<GapsBatchItem>();

            return items.Select(dto => new GapsBatchItem(
                RecipientAccount: dto.RecipientAccount ?? string.Empty,
                BankCode: dto.BankCode ?? string.Empty,
                Amount: dto.Amount,
                Narration: dto.Narration ?? string.Empty,
                ItemReference: dto.ItemReference ?? Guid.NewGuid().ToString("N")
            )).ToList();
        }
        catch (JsonException)
        {
            // If parsing fails, return empty list — validation will reject the batch
            return Array.Empty<GapsBatchItem>();
        }
    }

    /// <summary>
    /// Validates each batch item for required fields.
    /// Returns a list of validation errors (empty if all items are valid).
    /// </summary>
    internal static IReadOnlyList<GapsBatchItemError> ValidateBatchItems(IReadOnlyList<GapsBatchItem> items)
    {
        var errors = new List<GapsBatchItemError>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var itemRef = string.IsNullOrWhiteSpace(item.ItemReference) ? $"item[{i}]" : item.ItemReference;

            if (string.IsNullOrWhiteSpace(item.RecipientAccount))
            {
                errors.Add(new GapsBatchItemError(itemRef, "MISSING_FIELD", "RecipientAccount is required"));
            }

            if (string.IsNullOrWhiteSpace(item.BankCode))
            {
                errors.Add(new GapsBatchItemError(itemRef, "MISSING_FIELD", "BankCode is required"));
            }

            if (item.Amount <= 0)
            {
                errors.Add(new GapsBatchItemError(itemRef, "INVALID_AMOUNT", "Amount must be greater than zero"));
            }

            if (string.IsNullOrWhiteSpace(item.Narration))
            {
                errors.Add(new GapsBatchItemError(itemRef, "MISSING_FIELD", "Narration is required"));
            }
        }

        return errors;
    }

    /// <summary>
    /// Generates a unique batch reference for tracking.
    /// </summary>
    internal static string GenerateBatchReference()
    {
        return $"GAPS-{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Queries the processing status of a previously submitted GAPS batch.
    /// Returns per-item statuses (successful, failed, pending).
    /// This method is specific to GAPS and is not part of IChannelAdapter.
    /// </summary>
    /// <param name="batchReference">The batch reference returned from the original submission.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing per-item statuses or an error.</returns>
    public async Task<GapsBatchStatusResult> QueryBatchStatusAsync(string batchReference, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.GAPS);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return GapsBatchStatusResult.Failure(
                batchReference, "CHANNEL_UNAVAILABLE", "GAPS channel circuit breaker is open");
        }

        try
        {
            var response = await _gapsClient.QueryBatchStatusAsync(batchReference, ct);

            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                circuitBreaker.RecordSuccess();
                _logger.LogInformation(
                    "GAPS batch status inquiry successful. BatchRef: {BatchRef}, Items: {ItemCount}, " +
                    "Successful: {SuccessCount}, Failed: {FailedCount}, Pending: {PendingCount}",
                    batchReference,
                    response.ItemStatuses.Count,
                    response.ItemStatuses.Count(s => s.Status == "successful"),
                    response.ItemStatuses.Count(s => s.Status == "failed"),
                    response.ItemStatuses.Count(s => s.Status == "pending"));

                return GapsBatchStatusResult.Successful(batchReference, response.ItemStatuses);
            }
            else
            {
                var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();

                _logger.LogWarning(
                    "GAPS batch status inquiry failed. Code: {ResponseCode}, Message: {Message}, BatchRef: {BatchRef}",
                    response.ResponseCode, errorMessage, batchReference);

                return GapsBatchStatusResult.Failure(batchReference, errorCode, errorMessage);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return GapsBatchStatusResult.Failure(
                batchReference, "CHANNEL_UNAVAILABLE", "GAPS channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            var cbForError = _circuitBreakerRegistry.GetBreaker(PaymentChannel.GAPS);
            cbForError.RecordFailure();
            _logger.LogError(ex,
                "GAPS batch status inquiry encountered an unexpected error for BatchRef: {BatchRef}",
                batchReference);

            return GapsBatchStatusResult.Failure(
                batchReference, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal DTO for JSON deserialization of batch items from the request payload.
    /// </summary>
    private sealed class GapsBatchItemDto
    {
        public string? RecipientAccount { get; set; }
        public string? BankCode { get; set; }
        public decimal Amount { get; set; }
        public string? Narration { get; set; }
        public string? ItemReference { get; set; }
    }
}
