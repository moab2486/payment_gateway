namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Represents a request to be processed in the sandbox environment.
/// Mirrors production API request structure without routing to real processors.
/// </summary>
public record SandboxRequest(
    Guid DeveloperId,
    string Endpoint,
    string Method,
    string? RequestBody,
    Dictionary<string, string>? Headers);
