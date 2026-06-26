namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Provides an isolated sandbox environment with synthetic test data
/// that mirrors production API behavior without routing to real payment processors.
/// </summary>
public interface ISandboxEnvironment
{
    Task<SandboxResponse> ProcessRequestAsync(SandboxRequest request, CancellationToken ct);
    Task ResetDataAsync(Guid developerId, CancellationToken ct);
}
