namespace CardManagement.Domain.PlatformServices.AdminConsole;

/// <summary>
/// Represents the lifecycle status of an admin command in the maker-checker workflow.
/// </summary>
public enum CommandStatus
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Executed
}
