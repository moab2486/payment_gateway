using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for dispute management operations: raising, querying, and resolving disputes.
/// </summary>
[ApiController]
[Route("api/disputes")]
public class DisputesController : ControllerBase
{
    private readonly IDisputeService _disputeService;

    public DisputesController(IDisputeService disputeService)
    {
        _disputeService = disputeService;
    }

    /// <summary>
    /// Raises a new dispute against a payment transaction.
    /// </summary>
    /// <param name="request">The dispute request containing transaction reference, reason code, amount, and evidence.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dispute result with ID and status.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(DisputeResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RaiseDispute(
        [FromBody] DisputeRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _disputeService.RaiseDisputeAsync(request, ct);

        if (!result.Success)
            return BadRequest(new { Error = result.ErrorMessage });

        return CreatedAtAction(nameof(GetDispute), new { id = result.Id }, result);
    }

    /// <summary>
    /// Retrieves dispute details by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the dispute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dispute record details.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DisputeRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDispute(Guid id, CancellationToken ct)
    {
        var dispute = await _disputeService.GetDisputeAsync(id, ct);

        if (dispute is null)
            return NotFound(new { Error = $"Dispute with ID '{id}' not found." });

        return Ok(dispute);
    }

    /// <summary>
    /// Resolves a dispute with a decision (InFavour or Against) and optional notes.
    /// </summary>
    /// <param name="id">The unique identifier of the dispute to resolve.</param>
    /// <param name="resolution">The resolution decision and notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated dispute result.</returns>
    [HttpPut("{id:guid}/resolve")]
    [ProducesResponseType(typeof(DisputeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveDispute(
        Guid id,
        [FromBody] DisputeResolution resolution,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _disputeService.ResolveDisputeAsync(id, resolution, ct);

        if (!result.Success)
        {
            if (result.ErrorMessage?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true)
                return NotFound(new { Error = result.ErrorMessage });

            return BadRequest(new { Error = result.ErrorMessage });
        }

        return Ok(result);
    }
}
