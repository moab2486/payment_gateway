namespace CardManagement.Application.PlatformServices.Reconciliation.Queries;

/// <summary>
/// Query to list reconciliation exceptions for a given batch with pagination.
/// </summary>
public record ListExceptionsQuery(Guid BatchId, int Limit = 20, int Offset = 0);
