namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the resolution decision for a dispute.
/// </summary>
public record DisputeResolution(
    DisputeDecision Decision,
    string? Notes);

/// <summary>
/// The outcome decision of a dispute resolution.
/// </summary>
public enum DisputeDecision
{
    InFavour,
    Against
}
