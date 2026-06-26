namespace CardManagement.Application.PlatformServices.Reconciliation.Queries;

/// <summary>
/// Query to list reconciliation adjustments with pagination and optional filtering.
/// </summary>
public record ListAdjustmentsQuery(
    Guid? ExceptionId = null,
    int Limit = 20,
    int Offset = 0);
