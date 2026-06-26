using CardManagement.Application.DTOs;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for routing ISO 8583 messages to the correct card processor
/// based on BIN range identification. Maintains sign-on state per processor
/// and blocks financial messages until the processor has signed on.
/// </summary>
public interface ICardProcessorRouter
{
    /// <summary>
    /// Resolves which card processor handles a given PAN based on BIN range configuration.
    /// </summary>
    /// <param name="pan">The Primary Account Number to route.</param>
    /// <returns>The processor type (Interswitch for Verve, CardFi for Visa/Mastercard).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the PAN does not match any configured BIN range.</exception>
    ProcessorType ResolveProcessor(string pan);

    /// <summary>
    /// Checks whether the specified processor has completed a successful sign-on exchange.
    /// Financial transactions must not be processed until sign-on is complete.
    /// </summary>
    /// <param name="processor">The processor to check sign-on status for.</param>
    /// <returns>True if the processor is signed on and ready for financial messages.</returns>
    bool IsSignedOn(ProcessorType processor);

    /// <summary>
    /// Processes a network management message (sign-on, echo test, key exchange) for the
    /// specified processor. Updates sign-on state on successful completion.
    /// </summary>
    /// <param name="message">The network management ISO 8583 message (MTI 0800/0810).</param>
    /// <param name="processor">The target card processor.</param>
    /// <returns>A result indicating success or failure of the network management operation.</returns>
    Task<Result> ProcessNetworkManagement(Iso8583Message message, ProcessorType processor);
}
