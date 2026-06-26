using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for virtual card issuance operations.
/// </summary>
[ApiController]
[Route("api/cards")]
public class CardsController : ControllerBase
{
    private readonly IVirtualCardService _virtualCardService;

    public CardsController(IVirtualCardService virtualCardService)
    {
        _virtualCardService = virtualCardService;
    }

    /// <summary>
    /// Issues a new virtual card with HSM-secured PAN, CVV2, and calculated expiry date.
    /// </summary>
    /// <param name="request">The card issuance request details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The issued virtual card details.</returns>
    [HttpPost("issue")]
    [ProducesResponseType(typeof(VirtualCard), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IssueCard(
        [FromBody] CardIssuanceApiRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        BinRange binRange;
        try
        {
            binRange = new BinRange(request.BinPrefix, request.CardScheme, request.PanLength);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }

        var issuanceRequest = new CardIssuanceRequest
        {
            CardScheme = request.CardScheme,
            BinRange = binRange,
            AccountId = request.AccountId,
            ValidityMonths = request.ValidityMonths
        };

        var result = await _virtualCardService.IssueCardAsync(issuanceRequest, ct);

        if (!result.IsSuccess)
            return BadRequest(new { Error = result.ErrorMessage, Code = result.ErrorCode });

        return CreatedAtAction(nameof(IssueCard), new { id = result.Value!.Id }, result.Value);
    }
}

/// <summary>
/// API request model for card issuance. Maps to the application-layer CardIssuanceRequest.
/// </summary>
public class CardIssuanceApiRequest
{
    /// <summary>
    /// The card scheme (Verve, Visa, Mastercard).
    /// </summary>
    public CardScheme CardScheme { get; set; }

    /// <summary>
    /// The BIN prefix digits for PAN generation.
    /// </summary>
    public string BinPrefix { get; set; } = string.Empty;

    /// <summary>
    /// The desired PAN length (16-19).
    /// </summary>
    public int PanLength { get; set; }

    /// <summary>
    /// The account ID to associate the card with.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// The validity period in months (1-60).
    /// </summary>
    public int ValidityMonths { get; set; }
}
