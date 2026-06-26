using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for account balance query operations.
/// </summary>
[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly ILedgerService _ledgerService;

    public AccountsController(ILedgerService ledgerService)
    {
        _ledgerService = ledgerService;
    }

    /// <summary>
    /// Retrieves the current balance for the specified account.
    /// </summary>
    /// <param name="accountId">The unique identifier of the account.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account balance details.</returns>
    [HttpGet("{accountId:guid}/balance")]
    [ProducesResponseType(typeof(AccountBalance), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalance(Guid accountId, CancellationToken ct)
    {
        var result = await _ledgerService.GetBalanceAsync(accountId, ct);

        if (!result.IsSuccess)
            return NotFound(new { Error = result.ErrorMessage, Code = result.ErrorCode });

        return Ok(result.Value);
    }
}
