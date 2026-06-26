namespace CardManagement.Application.PlatformServices.Notifications;

/// <summary>
/// Exception thrown when template rendering fails due to missing required variables.
/// Identifies the specific variable that was not provided in the variable map.
/// </summary>
public class TemplateRenderingException : Exception
{
    /// <summary>
    /// The name of the missing template variable.
    /// </summary>
    public string MissingVariable { get; }

    public TemplateRenderingException(string missingVariable)
        : base($"Missing required template variable: '{missingVariable}'")
    {
        MissingVariable = missingVariable;
    }

    public TemplateRenderingException(string missingVariable, Exception innerException)
        : base($"Missing required template variable: '{missingVariable}'", innerException)
    {
        MissingVariable = missingVariable;
    }
}
