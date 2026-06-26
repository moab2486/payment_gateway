namespace CardManagement.Application.PlatformServices.AdminConsole;

/// <summary>
/// Classifies admin commands as sensitive or non-sensitive based on their command type.
/// Sensitive commands require dual authorization via the maker-checker workflow.
/// Categories: adjustments, refunds, configuration changes, and role modifications.
/// </summary>
public static class SensitiveCommandClassifier
{
    /// <summary>
    /// Command type prefixes that indicate sensitive operations requiring maker-checker.
    /// </summary>
    private static readonly string[] SensitivePrefixes =
    {
        "adjustment.",     // Financial adjustments (e.g., adjustment.create, adjustment.reverse)
        "refund.",         // Refund operations (e.g., refund.execute, refund.reverse)
        "config.",         // Configuration changes (e.g., config.update, config.threshold)
        "role.",           // Role modifications (e.g., role.assign, role.revoke)
    };

    /// <summary>
    /// Specific command types that are always classified as sensitive.
    /// </summary>
    private static readonly HashSet<string> ExplicitSensitiveCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "adjustment.create",
        "adjustment.reverse",
        "refund.execute",
        "refund.reverse",
        "config.update",
        "config.threshold.update",
        "config.processor.update",
        "role.assign",
        "role.revoke",
        "user.suspend",
        "user.deactivate",
        "merchant.suspend",
        "merchant.deactivate",
    };

    /// <summary>
    /// Determines whether a command type requires the maker-checker workflow.
    /// Returns true if the command type starts with a sensitive prefix or is an explicitly sensitive command.
    /// </summary>
    public static bool IsSensitive(string commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType))
            return false;

        // Check explicit list first
        if (ExplicitSensitiveCommands.Contains(commandType))
            return true;

        // Check prefix-based classification
        foreach (var prefix in SensitivePrefixes)
        {
            if (commandType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the sensitivity category for a command type (for audit/logging purposes).
    /// </summary>
    public static string? GetCategory(string commandType)
    {
        if (string.IsNullOrWhiteSpace(commandType))
            return null;

        foreach (var prefix in SensitivePrefixes)
        {
            if (commandType.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return prefix.TrimEnd('.');
        }

        if (ExplicitSensitiveCommands.Contains(commandType))
        {
            var dotIndex = commandType.IndexOf('.');
            return dotIndex > 0 ? commandType[..dotIndex] : commandType;
        }

        return null;
    }
}
