namespace CardManagement.Application.PlatformServices.DeveloperPortal;

/// <summary>
/// Represents a response from the sandbox environment.
/// Uses the same format as production responses with synthetic data.
/// </summary>
public record SandboxResponse(
    int StatusCode,
    string? ResponseBody,
    Dictionary<string, string>? Headers);
