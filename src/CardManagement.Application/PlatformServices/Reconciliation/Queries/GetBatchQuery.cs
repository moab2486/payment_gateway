namespace CardManagement.Application.PlatformServices.Reconciliation.Queries;

/// <summary>
/// Query to retrieve a single reconciliation batch by ID.
/// </summary>
public record GetBatchQuery(Guid BatchId);
