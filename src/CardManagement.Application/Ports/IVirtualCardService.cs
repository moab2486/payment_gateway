using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for virtual card issuance. Orchestrates PAN generation (via HSM),
/// CVV2 computation, expiry date calculation, and secure storage of card data.
/// </summary>
public interface IVirtualCardService
{
    /// <summary>
    /// Issues a new virtual card with an HSM-secured PAN, CVV2, and calculated expiry date.
    /// Validates PAN uniqueness and Luhn check digit, with retry logic for failures.
    /// </summary>
    /// <param name="request">The card issuance request with scheme, BIN range, account, and validity period.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the issued virtual card details or an error.</returns>
    Task<Result<VirtualCard>> IssueCardAsync(CardIssuanceRequest request, CancellationToken ct);
}
