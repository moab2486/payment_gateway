using System.Security.Cryptography;
using CardManagement.Application.PlatformServices.Reconciliation.Commands;
using CardManagement.Application.PlatformServices.Reconciliation.Handlers;
using CardManagement.Application.PlatformServices.Reconciliation.Queries;
using CardManagement.Domain.PlatformServices.Reconciliation;
using CardManagement.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

using ReconciliationProcessorType = CardManagement.Domain.PlatformServices.Reconciliation.ProcessorType;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for reconciliation operations: uploading settlement files,
/// querying batches and exceptions, and managing adjustments.
/// </summary>
[ApiController]
[Route("api/v1/reconciliation")]
public class ReconciliationController : ControllerBase
{
    private readonly ImportSettlementFileCommandHandler _importHandler;
    private readonly GetBatchQueryHandler _getBatchHandler;
    private readonly ListBatchesQueryHandler _listBatchesHandler;
    private readonly ListExceptionsQueryHandler _listExceptionsHandler;
    private readonly CreateManualAdjustmentCommandHandler _createAdjustmentHandler;
    private readonly ListAdjustmentsQueryHandler _listAdjustmentsHandler;

    public ReconciliationController(
        ImportSettlementFileCommandHandler importHandler,
        GetBatchQueryHandler getBatchHandler,
        ListBatchesQueryHandler listBatchesHandler,
        ListExceptionsQueryHandler listExceptionsHandler,
        CreateManualAdjustmentCommandHandler createAdjustmentHandler,
        ListAdjustmentsQueryHandler listAdjustmentsHandler)
    {
        _importHandler = importHandler;
        _getBatchHandler = getBatchHandler;
        _listBatchesHandler = listBatchesHandler;
        _listExceptionsHandler = listExceptionsHandler;
        _createAdjustmentHandler = createAdjustmentHandler;
        _listAdjustmentsHandler = listAdjustmentsHandler;
    }

    /// <summary>
    /// Uploads a settlement file and creates a reconciliation batch.
    /// The file is enqueued for asynchronous parsing and matching.
    /// </summary>
    /// <param name="file">The settlement file to upload.</param>
    /// <param name="processor">The processor type (NIBSS, Interswitch, Cardify).</param>
    /// <param name="settlementDate">The settlement date for this file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Batch ID and acceptance status.</returns>
    [HttpPost("batches")]
    [ProducesResponseType(typeof(ImportSettlementFileResult), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadSettlementFile(
        IFormFile file,
        [FromForm] ReconciliationProcessorType processor,
        [FromForm] DateOnly settlementDate,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { Error = "A settlement file is required." });

        // Compute SHA-256 hash of the uploaded file
        string fileHash;
        using (var hashStream = file.OpenReadStream())
        {
            var hashBytes = await SHA256.HashDataAsync(hashStream, ct);
            fileHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // Count rows (lines) in the file for progress tracking
        int totalRows;
        using (var countStream = file.OpenReadStream())
        using (var reader = new StreamReader(countStream))
        {
            var content = await reader.ReadToEndAsync(ct);
            totalRows = content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        // Open the stream for the handler
        var fileStream = file.OpenReadStream();

        var command = new ImportSettlementFileCommand(
            FileStream: fileStream,
            Processor: processor,
            SettlementDate: settlementDate,
            FileHash: fileHash,
            TotalRows: totalRows,
            CreatedBy: User.Identity?.Name ?? "anonymous");

        var result = await _importHandler.HandleAsync(command, ct);

        if (!result.Accepted)
            return Conflict(new { Error = result.RejectionReason });

        return AcceptedAtAction(nameof(GetBatch), new { batchId = result.BatchId }, result);
    }

    /// <summary>
    /// Lists reconciliation batches with pagination.
    /// </summary>
    /// <param name="limit">Maximum number of batches to return (default: 20).</param>
    /// <param name="offset">Number of batches to skip (default: 0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of reconciliation batches.</returns>
    [HttpGet("batches")]
    [ProducesResponseType(typeof(IReadOnlyList<ReconciliationBatch>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBatches(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var query = new ListBatchesQuery(Limit: limit, Offset: offset);
        var batches = await _listBatchesHandler.HandleAsync(query, ct);
        return Ok(batches);
    }

    /// <summary>
    /// Retrieves details of a specific reconciliation batch.
    /// </summary>
    /// <param name="batchId">The unique identifier of the batch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch details.</returns>
    [HttpGet("batches/{batchId:guid}")]
    [ProducesResponseType(typeof(ReconciliationBatch), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatch(Guid batchId, CancellationToken ct)
    {
        var query = new GetBatchQuery(BatchId: batchId);
        var batch = await _getBatchHandler.HandleAsync(query, ct);

        if (batch is null)
            return NotFound(new { Error = $"Batch with ID '{batchId}' not found." });

        return Ok(batch);
    }

    /// <summary>
    /// Lists reconciliation exceptions for a specific batch with pagination.
    /// </summary>
    /// <param name="batchId">The batch ID to list exceptions for.</param>
    /// <param name="limit">Maximum number of exceptions to return (default: 20).</param>
    /// <param name="offset">Number of exceptions to skip (default: 0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of reconciliation exceptions.</returns>
    [HttpGet("batches/{batchId:guid}/exceptions")]
    [ProducesResponseType(typeof(IReadOnlyList<ReconciliationException>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListExceptions(
        Guid batchId,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var query = new ListExceptionsQuery(BatchId: batchId, Limit: limit, Offset: offset);
        var exceptions = await _listExceptionsHandler.HandleAsync(query, ct);
        return Ok(exceptions);
    }

    /// <summary>
    /// Creates a manual adjustment for a reconciliation exception.
    /// </summary>
    /// <param name="exceptionId">The exception ID to create an adjustment for.</param>
    /// <param name="request">The adjustment request containing amount and reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created adjustment details.</returns>
    [HttpPost("exceptions/{exceptionId:guid}/adjustments")]
    [ProducesResponseType(typeof(CreateManualAdjustmentResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateManualAdjustment(
        Guid exceptionId,
        [FromBody] CreateManualAdjustmentRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        Money amount;
        try
        {
            amount = new Money(request.Amount, request.CurrencyCode);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }

        var command = new CreateManualAdjustmentCommand(
            ExceptionId: exceptionId,
            Amount: amount,
            Reason: request.Reason,
            OperatorId: User.Identity?.Name ?? "anonymous");

        var result = await _createAdjustmentHandler.HandleAsync(command, ct);

        if (!result.Success)
        {
            if (result.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                return NotFound(new { Error = result.ErrorMessage });

            return BadRequest(new { Error = result.ErrorMessage });
        }

        return CreatedAtAction(nameof(ListAdjustments), new { exceptionId }, result);
    }

    /// <summary>
    /// Lists reconciliation adjustments with optional filtering by exception ID.
    /// </summary>
    /// <param name="exceptionId">Optional exception ID to filter adjustments.</param>
    /// <param name="limit">Maximum number of adjustments to return (default: 20).</param>
    /// <param name="offset">Number of adjustments to skip (default: 0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of adjustments.</returns>
    [HttpGet("adjustments")]
    [ProducesResponseType(typeof(IReadOnlyList<Adjustment>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAdjustments(
        [FromQuery] Guid? exceptionId = null,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var query = new ListAdjustmentsQuery(ExceptionId: exceptionId, Limit: limit, Offset: offset);
        var adjustments = await _listAdjustmentsHandler.HandleAsync(query, ct);
        return Ok(adjustments);
    }
}

/// <summary>
/// API request model for creating a manual adjustment.
/// </summary>
public class CreateManualAdjustmentRequest
{
    /// <summary>
    /// The adjustment amount in the smallest currency unit (e.g., kobo, cents).
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// The ISO 4217 currency code (e.g., NGN, USD).
    /// </summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>
    /// The reason for the manual adjustment.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
