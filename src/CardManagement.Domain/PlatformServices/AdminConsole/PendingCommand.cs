namespace CardManagement.Domain.PlatformServices.AdminConsole;

/// <summary>
/// Represents a sensitive admin command awaiting dual authorization via the maker-checker workflow.
/// Status lifecycle: Pending → Approved/Rejected/Expired, Approved → Executed.
/// </summary>
public class PendingCommand
{
    public Guid Id { get; private set; }
    public string CommandType { get; private set; } = string.Empty;
    public string SerializedParameters { get; private set; } = string.Empty;
    public string MakerId { get; private set; } = string.Empty;
    public string? CheckerId { get; private set; }
    public CommandStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    private PendingCommand() { }

    /// <summary>
    /// Creates a new pending command submitted by a maker.
    /// </summary>
    public static PendingCommand Create(
        string commandType,
        string serializedParameters,
        string makerId,
        DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(commandType))
            throw new ArgumentException("Command type is required.", nameof(commandType));

        if (string.IsNullOrWhiteSpace(serializedParameters))
            throw new ArgumentException("Serialized parameters are required.", nameof(serializedParameters));

        if (string.IsNullOrWhiteSpace(makerId))
            throw new ArgumentException("Maker identity is required.", nameof(makerId));

        if (expiresAtUtc <= DateTime.UtcNow)
            throw new ArgumentException("Expiry time must be in the future.", nameof(expiresAtUtc));

        return new PendingCommand
        {
            Id = Guid.NewGuid(),
            CommandType = commandType,
            SerializedParameters = serializedParameters,
            MakerId = makerId,
            CheckerId = null,
            Status = CommandStatus.Pending,
            RejectionReason = null,
            CreatedAtUtc = DateTime.UtcNow,
            ResolvedAtUtc = null,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    /// <summary>
    /// Approves the command. Enforces maker-checker separation (maker ≠ checker)
    /// and that only pending, non-expired commands can be approved.
    /// </summary>
    public void Approve(string checkerId)
    {
        if (string.IsNullOrWhiteSpace(checkerId))
            throw new ArgumentException("Checker identity is required.", nameof(checkerId));

        if (Status != CommandStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot approve command in status '{Status}'. Command must be in Pending status.");

        if (DateTime.UtcNow >= ExpiresAtUtc)
            throw new InvalidOperationException(
                "Cannot approve an expired command. The command has passed its expiry time.");

        if (string.Equals(MakerId, checkerId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Maker and checker must be different users. The same user cannot both initiate and approve a command.");

        Status = CommandStatus.Approved;
        CheckerId = checkerId;
        ResolvedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Rejects the command with a reason. Enforces maker-checker separation
    /// and that only pending commands can be rejected.
    /// </summary>
    public void Reject(string checkerId, string reason)
    {
        if (string.IsNullOrWhiteSpace(checkerId))
            throw new ArgumentException("Checker identity is required.", nameof(checkerId));

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Rejection reason is required.", nameof(reason));

        if (Status != CommandStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot reject command in status '{Status}'. Command must be in Pending status.");

        if (string.Equals(MakerId, checkerId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Maker and checker must be different users. The same user cannot both initiate and reject a command.");

        Status = CommandStatus.Rejected;
        CheckerId = checkerId;
        RejectionReason = reason;
        ResolvedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the command as expired. Only pending commands can expire.
    /// </summary>
    public void MarkExpired()
    {
        if (Status != CommandStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot expire command in status '{Status}'. Command must be in Pending status.");

        Status = CommandStatus.Expired;
        ResolvedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks an approved command as executed. Only approved commands can transition to executed.
    /// </summary>
    public void MarkExecuted()
    {
        if (Status != CommandStatus.Approved)
            throw new InvalidOperationException(
                $"Cannot execute command in status '{Status}'. Command must be in Approved status.");

        Status = CommandStatus.Executed;
    }

    /// <summary>
    /// Returns true if the command has passed its expiry time and is still pending.
    /// </summary>
    public bool IsExpired => Status == CommandStatus.Pending && DateTime.UtcNow >= ExpiresAtUtc;
}
