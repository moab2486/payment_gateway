namespace CardManagement.Application.PlatformServices.Reconciliation.Queries;

/// <summary>
/// Query to list reconciliation batches with pagination.
/// </summary>
public record ListBatchesQuery(int Limit = 20, int Offset = 0);
