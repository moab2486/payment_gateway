using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for the end-to-end transaction processing pipeline.
/// Orchestrates the flow from message receipt through card validation,
/// balance checking, ledger posting, to response construction.
/// </summary>
public interface ITransactionPipeline
{
    /// <summary>
    /// Processes an authorization request (MTI 0100) through the full pipeline:
    /// card validation → balance check → ledger posting → response construction.
    /// On any step failure, rolls back ledger entries and returns a decline response.
    /// </summary>
    /// <param name="request">The parsed ISO 8583 authorization request message.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>The ISO 8583 response message (approval or decline).</returns>
    Task<Iso8583Message> ProcessAuthorizationAsync(Iso8583Message request, CancellationToken ct);

    /// <summary>
    /// Processes a reversal request (MTI 0420) by locating the original transaction
    /// and creating offsetting ledger entries. Returns a decline if the original is not found.
    /// </summary>
    /// <param name="request">The parsed ISO 8583 reversal request message.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>The ISO 8583 response message indicating reversal success or failure.</returns>
    Task<Iso8583Message> ProcessReversalAsync(Iso8583Message request, CancellationToken ct);
}
