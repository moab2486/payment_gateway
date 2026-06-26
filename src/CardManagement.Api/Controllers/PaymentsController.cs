using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;

namespace CardManagement.Api.Controllers;

/// <summary>
/// REST API controller for payment operations: initiation and status inquiry.
/// </summary>
[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentOrchestrator _paymentOrchestrator;

    public PaymentsController(IPaymentOrchestrator paymentOrchestrator)
    {
        _paymentOrchestrator = paymentOrchestrator;
    }

    /// <summary>
    /// Initiates a new payment. Requires X-Idempotency-Key header.
    /// </summary>
    /// <param name="request">The payment initiation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payment result with transaction reference and status.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitiatePayment(
        [FromBody] InitiatePaymentRequest request,
        CancellationToken ct)
    {
        if (!Request.Headers.TryGetValue("X-Idempotency-Key", out var idempotencyKeyHeader)
            || string.IsNullOrWhiteSpace(idempotencyKeyHeader))
        {
            return BadRequest(new { Error = "X-Idempotency-Key header is required." });
        }

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var idempotencyKey = idempotencyKeyHeader.ToString();

        Money amount;
        try
        {
            amount = new Money(request.Amount, request.CurrencyCode);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Error = ex.Message });
        }

        var paymentRequest = PaymentRequest.Create(
            idempotencyKey: idempotencyKey,
            transactionReference: Guid.NewGuid().ToString("N"),
            transactionType: request.TransactionType,
            amount: amount,
            sourceAccount: request.SourceAccount,
            destinationAccount: request.DestinationAccount,
            channel: request.Channel);

        var result = await _paymentOrchestrator.InitiatePaymentAsync(paymentRequest, ct);

        if (!result.Success)
            return BadRequest(new { Error = result.ErrorMessage });

        return CreatedAtAction(
            nameof(GetStatus),
            new { transactionReference = result.TransactionReference },
            result);
    }

    /// <summary>
    /// Retrieves the current status of a payment by its transaction reference.
    /// </summary>
    /// <param name="transactionReference">The unique transaction reference.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payment status details.</returns>
    [HttpGet("{transactionReference}")]
    [ProducesResponseType(typeof(PaymentStatusResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus(string transactionReference, CancellationToken ct)
    {
        var result = await _paymentOrchestrator.GetStatusAsync(transactionReference, ct);

        if (!result.Found)
            return NotFound(new { Error = $"Payment with reference '{transactionReference}' not found." });

        return Ok(result);
    }

    /// <summary>
    /// Retrieves the current status of a payment by its idempotency key.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key used during payment initiation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The payment status details.</returns>
    [HttpGet("by-key/{idempotencyKey}")]
    [ProducesResponseType(typeof(PaymentStatusResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatusByKey(string idempotencyKey, CancellationToken ct)
    {
        var result = await _paymentOrchestrator.GetStatusByIdempotencyKeyAsync(idempotencyKey, ct);

        if (!result.Found)
            return NotFound(new { Error = $"Payment with idempotency key '{idempotencyKey}' not found." });

        return Ok(result);
    }
}

/// <summary>
/// API request model for initiating a payment.
/// </summary>
public class InitiatePaymentRequest
{
    /// <summary>
    /// The type of payment transaction.
    /// </summary>
    public PaymentTransactionType TransactionType { get; set; }

    /// <summary>
    /// The payment amount in the smallest currency unit (e.g., kobo, cents).
    /// </summary>
    public long Amount { get; set; }

    /// <summary>
    /// The ISO 4217 currency code (e.g., NGN, USD).
    /// </summary>
    public string CurrencyCode { get; set; } = string.Empty;

    /// <summary>
    /// The source account identifier.
    /// </summary>
    public string SourceAccount { get; set; } = string.Empty;

    /// <summary>
    /// The destination account identifier.
    /// </summary>
    public string DestinationAccount { get; set; } = string.Empty;

    /// <summary>
    /// The payment channel to route through.
    /// </summary>
    public PaymentChannel Channel { get; set; }
}
