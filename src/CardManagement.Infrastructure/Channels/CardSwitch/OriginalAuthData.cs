namespace CardManagement.Infrastructure.Channels.CardSwitch;

/// <summary>
/// Contains original authorization data used to populate Field 90 (Original Data Elements)
/// in reversal messages for timed-out or failed authorizations.
/// </summary>
public record OriginalAuthData
{
    /// <summary>
    /// The MTI of the original authorization message (typically "0100").
    /// </summary>
    public string OriginalMti { get; init; } = "0100";

    /// <summary>
    /// The STAN (System Trace Audit Number) from the original authorization request.
    /// </summary>
    public string OriginalStan { get; init; } = string.Empty;

    /// <summary>
    /// The date and time of the original authorization in "MMddHHmmss" format.
    /// </summary>
    public string OriginalDateTime { get; init; } = string.Empty;

    /// <summary>
    /// The acquiring institution identification code.
    /// </summary>
    public string AcquiringInstitutionId { get; init; } = string.Empty;
}
