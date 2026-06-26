using CardManagement.Application.PlatformServices.AdminConsole.DTOs;

namespace CardManagement.Application.PlatformServices.AdminConsole.Ports;

/// <summary>
/// Query interface for denormalized admin read models with pagination and filtering support.
/// </summary>
public interface IAdminReadModelQuery
{
    /// <summary>
    /// Queries a read model with filtering, pagination, and date range support.
    /// </summary>
    Task<PagedResult<T>> QueryAsync<T>(ReadModelQuery query, CancellationToken ct) where T : class;

    /// <summary>
    /// Returns metadata about a read model including staleness information.
    /// </summary>
    Task<ReadModelMetadata> GetMetadataAsync(string readModelName, CancellationToken ct);
}
