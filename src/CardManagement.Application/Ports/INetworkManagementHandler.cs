using CardManagement.Application.DTOs;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for handling ISO 8583 network management messages (sign-on, echo test, key exchange).
/// Constructs proper 0810 response messages, delegates state management to the router,
/// and gates financial messages behind successful sign-on completion.
/// </summary>
public interface INetworkManagementHandler
{
    /// <summary>
    /// Handles a network management request (MTI 0800) and produces an appropriate 0810 response.
    /// Updates processor sign-on state on successful sign-on exchange.
    /// </summary>
    /// <param name="request">The inbound network management request message (MTI 0800).</param>
    /// <param name="processor">The processor that sent the request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The constructed 0810 response message.</returns>
    Task<Result<Iso8583Message>> HandleNetworkManagementAsync(
        Iso8583Message request, ProcessorType processor, CancellationToken ct = default);

    /// <summary>
    /// Validates whether a financial message (authorization or reversal) can be processed
    /// based on the sign-on state of the originating processor.
    /// </summary>
    /// <param name="request">The inbound financial message.</param>
    /// <param name="processor">The processor that sent the message.</param>
    /// <returns>
    /// A success result if the processor is signed on and the message can proceed;
    /// a failure result with an appropriate error if the processor is not signed on.
    /// </returns>
    Result ValidateFinancialMessageAllowed(Iso8583Message request, ProcessorType processor);
}
