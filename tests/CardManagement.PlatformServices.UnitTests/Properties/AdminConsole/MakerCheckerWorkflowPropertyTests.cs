using CardManagement.Application.PlatformServices.AdminConsole;
using CardManagement.Domain.PlatformServices.AdminConsole;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Properties.AdminConsole;

/// <summary>
/// Property-based tests for Maker-Checker Enforcement (Property 23).
/// 
/// **Validates: Requirements 10.1**
/// 
/// Any sensitive command (adjustment, refund, config change, role mod) is correctly
/// classified as sensitive and cannot bypass the maker-checker workflow.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "23")]
public class MakerCheckerEnforcementPropertyTests
{
    /// <summary>
    /// Known sensitive command type prefixes that must always route through maker-checker.
    /// </summary>
    private static readonly string[] SensitivePrefixes =
    {
        "adjustment.",
        "refund.",
        "config.",
        "role."
    };

    /// <summary>
    /// **Validates: Requirements 10.1**
    /// 
    /// Property 23: Maker-Checker Enforcement — Any sensitive command (adjustment, refund,
    /// config change, role mod) routes through maker-checker, cannot execute directly.
    /// Commands with sensitive prefixes are always classified as sensitive.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property SensitiveCommandType_WithKnownPrefix_IsAlwaysClassifiedAsSensitive()
    {
        var prefixGen = Gen.Elements(SensitivePrefixes);
        var suffixGen = Gen.Elements(
            "create", "execute", "update", "reverse", "assign", "revoke",
            "threshold", "processor", "batch", "manual");

        return Prop.ForAll(
            prefixGen.ToArbitrary(),
            suffixGen.ToArbitrary(),
            (prefix, suffix) =>
            {
                var commandType = prefix + suffix;
                var isSensitive = SensitiveCommandClassifier.IsSensitive(commandType);

                return isSensitive
                    .Label($"Expected command '{commandType}' to be classified as sensitive, but it was not.");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.1**
    /// 
    /// Property 23: Maker-Checker Enforcement — Explicitly listed sensitive commands are
    /// always classified as sensitive regardless of casing.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExplicitSensitiveCommands_AreAlwaysClassifiedAsSensitive()
    {
        var explicitCommands = Gen.Elements(
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
            "merchant.deactivate");

        // Test with random casing variations
        var casingGen = Gen.Elements(
            new Func<string, string>(s => s),
            new Func<string, string>(s => s.ToUpperInvariant()),
            new Func<string, string>(s => char.ToUpper(s[0]) + s[1..]));

        return Prop.ForAll(
            explicitCommands.ToArbitrary(),
            casingGen.ToArbitrary(),
            (command, casingFn) =>
            {
                var commandWithCasing = casingFn(command);
                var isSensitive = SensitiveCommandClassifier.IsSensitive(commandWithCasing);

                return isSensitive
                    .Label($"Expected explicit sensitive command '{commandWithCasing}' (original: '{command}') " +
                           $"to be classified as sensitive.");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.1**
    /// 
    /// Property 23: Maker-Checker Enforcement — Non-sensitive commands (those without sensitive
    /// prefixes and not in the explicit list) are NOT classified as sensitive.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NonSensitiveCommandType_IsNotClassifiedAsSensitive()
    {
        var nonSensitiveCommands = Gen.Elements(
            "query.transactions",
            "report.generate",
            "dashboard.view",
            "notification.send",
            "webhook.create",
            "audit.view",
            "batch.status");

        return Prop.ForAll(nonSensitiveCommands.ToArbitrary(), commandType =>
        {
            var isSensitive = SensitiveCommandClassifier.IsSensitive(commandType);

            return (!isSensitive)
                .Label($"Expected command '{commandType}' to NOT be classified as sensitive, but it was.");
        });
    }

    /// <summary>
    /// **Validates: Requirements 10.1**
    /// 
    /// Property 23: Maker-Checker Enforcement — Null or empty command types are never sensitive.
    /// </summary>
    [Property(MaxTest = 10)]
    public Property NullOrEmpty_CommandType_IsNeverSensitive()
    {
        var emptyInputs = Gen.Elements("", "   ", "\t", "\n");

        return Prop.ForAll(emptyInputs.ToArbitrary(), input =>
        {
            var isSensitive = SensitiveCommandClassifier.IsSensitive(input);

            return (!isSensitive)
                .Label($"Expected null/empty/whitespace input to NOT be sensitive.");
        });
    }
}

/// <summary>
/// Property-based tests for Maker-Checker Command Lifecycle (Property 24).
/// 
/// **Validates: Requirements 10.3, 10.4, 10.5**
/// 
/// Approval executes + records both IDs; rejection marks rejected + records reason + notifies maker;
/// expiry marks expired without execution.
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "24")]
public class MakerCheckerCommandLifecyclePropertyTests
{
    /// <summary>
    /// **Validates: Requirements 10.3**
    /// 
    /// Property 24: Maker-Checker Command Lifecycle — Approval transitions command to Approved status,
    /// records the checker identity, and the command can then be marked as Executed.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Approval_TransitionsToApproved_RecordsBothIds()
    {
        var makerGen = Gen.Elements("maker_alice", "maker_bob", "maker_charlie", "maker_dave")
            .Select(m => m + "_" + Guid.NewGuid().ToString("N")[..8]);
        var checkerGen = Gen.Elements("checker_eve", "checker_frank", "checker_grace", "checker_henry")
            .Select(c => c + "_" + Guid.NewGuid().ToString("N")[..8]);
        var commandTypeGen = Gen.Elements("adjustment.create", "refund.execute", "config.update", "role.assign");

        return Prop.ForAll(
            makerGen.ToArbitrary(),
            checkerGen.ToArbitrary(),
            commandTypeGen.ToArbitrary(),
            (makerId, checkerId, commandType) =>
            {
                // Arrange: Create a pending command
                var command = PendingCommand.Create(
                    commandType,
                    $"{{\"type\":\"{commandType}\",\"amount\":5000}}",
                    makerId,
                    DateTime.UtcNow.AddHours(24));

                // Act: Approve
                command.Approve(checkerId);

                // Assert: Status is Approved, checker is recorded
                var statusApproved = command.Status == CommandStatus.Approved;
                var checkerRecorded = command.CheckerId == checkerId;
                var makerPreserved = command.MakerId == makerId;
                var resolvedTimeSet = command.ResolvedAtUtc.HasValue;

                // Now mark as executed
                command.MarkExecuted();
                var statusExecuted = command.Status == CommandStatus.Executed;

                return (statusApproved && checkerRecorded && makerPreserved && resolvedTimeSet && statusExecuted)
                    .Label($"Approval lifecycle: Approved={statusApproved}, Checker={checkerRecorded}, " +
                           $"Maker={makerPreserved}, ResolvedTime={resolvedTimeSet}, Executed={statusExecuted}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.4**
    /// 
    /// Property 24: Maker-Checker Command Lifecycle — Rejection marks command as Rejected,
    /// records the rejection reason and checker identity.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Rejection_MarksRejected_RecordsReasonAndChecker()
    {
        var makerGen = Gen.Elements("maker_alice", "maker_bob", "maker_charlie")
            .Select(m => m + "_" + Guid.NewGuid().ToString("N")[..8]);
        var checkerGen = Gen.Elements("checker_eve", "checker_frank", "checker_grace")
            .Select(c => c + "_" + Guid.NewGuid().ToString("N")[..8]);
        var reasonGen = Gen.Elements(
            "Amount exceeds threshold",
            "Insufficient justification provided",
            "Duplicate request detected",
            "Risk assessment failed",
            "Policy violation");
        var commandTypeGen = Gen.Elements("adjustment.create", "refund.execute", "config.update");

        // Combine reason and commandType into a tuple to stay within 4-arg Prop.ForAll limit
        var reasonAndCommandGen = from reason in reasonGen
                                  from cmd in commandTypeGen
                                  select (Reason: reason, CommandType: cmd);

        return Prop.ForAll(
            makerGen.ToArbitrary(),
            checkerGen.ToArbitrary(),
            reasonAndCommandGen.ToArbitrary(),
            (makerId, checkerId, reasonAndCommand) =>
            {
                var reason = reasonAndCommand.Reason;
                var commandType = reasonAndCommand.CommandType;

                // Arrange
                var command = PendingCommand.Create(
                    commandType,
                    $"{{\"type\":\"{commandType}\"}}",
                    makerId,
                    DateTime.UtcNow.AddHours(24));

                // Act: Reject
                command.Reject(checkerId, reason);

                // Assert
                var statusRejected = command.Status == CommandStatus.Rejected;
                var reasonRecorded = command.RejectionReason == reason;
                var checkerRecorded = command.CheckerId == checkerId;
                var makerPreserved = command.MakerId == makerId;
                var resolvedTimeSet = command.ResolvedAtUtc.HasValue;

                return (statusRejected && reasonRecorded && checkerRecorded && makerPreserved && resolvedTimeSet)
                    .Label($"Rejection lifecycle: Rejected={statusRejected}, Reason={reasonRecorded}, " +
                           $"Checker={checkerRecorded}, Maker={makerPreserved}, ResolvedTime={resolvedTimeSet}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.5**
    /// 
    /// Property 24: Maker-Checker Command Lifecycle — Expiry marks command as Expired
    /// without execution. Only pending commands can expire.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Expiry_MarksExpired_WithoutExecution()
    {
        var makerGen = Gen.Elements("maker_alice", "maker_bob", "maker_charlie")
            .Select(m => m + "_" + Guid.NewGuid().ToString("N")[..8]);
        var commandTypeGen = Gen.Elements("adjustment.create", "refund.execute", "config.update", "role.assign");

        return Prop.ForAll(
            makerGen.ToArbitrary(),
            commandTypeGen.ToArbitrary(),
            (makerId, commandType) =>
            {
                // Arrange: Create a pending command
                var command = PendingCommand.Create(
                    commandType,
                    $"{{\"type\":\"{commandType}\"}}",
                    makerId,
                    DateTime.UtcNow.AddHours(24));

                // Act: Mark expired
                command.MarkExpired();

                // Assert
                var statusExpired = command.Status == CommandStatus.Expired;
                var noCheckerAssigned = command.CheckerId is null;
                var resolvedTimeSet = command.ResolvedAtUtc.HasValue;
                var noRejectionReason = command.RejectionReason is null;

                // Verify cannot execute an expired command
                var cannotExecute = false;
                try
                {
                    command.MarkExecuted();
                }
                catch (InvalidOperationException)
                {
                    cannotExecute = true;
                }

                return (statusExpired && noCheckerAssigned && resolvedTimeSet && noRejectionReason && cannotExecute)
                    .Label($"Expiry lifecycle: Expired={statusExpired}, NoChecker={noCheckerAssigned}, " +
                           $"ResolvedTime={resolvedTimeSet}, NoReason={noRejectionReason}, CannotExecute={cannotExecute}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.3, 10.4, 10.5**
    /// 
    /// Property 24: Maker-Checker Command Lifecycle — Once a command transitions out of Pending
    /// (approved, rejected, or expired), it cannot be approved or rejected again.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ResolvedCommand_CannotBeApprovedOrRejectedAgain()
    {
        var makerGen = Gen.Elements("maker_alice", "maker_bob")
            .Select(m => m + "_" + Guid.NewGuid().ToString("N")[..8]);
        var checkerGen = Gen.Elements("checker_eve", "checker_frank")
            .Select(c => c + "_" + Guid.NewGuid().ToString("N")[..8]);
        var transitionGen = Gen.Elements("approve", "reject", "expire");

        return Prop.ForAll(
            makerGen.ToArbitrary(),
            checkerGen.ToArbitrary(),
            transitionGen.ToArbitrary(),
            (makerId, checkerId, transition) =>
            {
                // Arrange
                var command = PendingCommand.Create(
                    "adjustment.create",
                    "{\"amount\":1000}",
                    makerId,
                    DateTime.UtcNow.AddHours(24));

                // First transition
                switch (transition)
                {
                    case "approve":
                        command.Approve(checkerId);
                        break;
                    case "reject":
                        command.Reject(checkerId, "Test reason");
                        break;
                    case "expire":
                        command.MarkExpired();
                        break;
                }

                // Try to approve again
                var approveThrows = false;
                try { command.Approve("another_checker"); }
                catch (InvalidOperationException) { approveThrows = true; }

                // Try to reject again
                var rejectThrows = false;
                try { command.Reject("another_checker", "reason"); }
                catch (InvalidOperationException) { rejectThrows = true; }

                return (approveThrows && rejectThrows)
                    .Label($"After '{transition}', approve throws={approveThrows}, reject throws={rejectThrows}");
            });
    }
}

/// <summary>
/// Property-based tests for Maker-Checker Separation of Duties (Property 25).
/// 
/// **Validates: Requirements 10.6**
/// 
/// Approval where checker == maker is always rejected (throws InvalidOperationException).
/// </summary>
[Trait("Feature", "platform-services")]
[Trait("Property", "25")]
public class MakerCheckerSeparationOfDutiesPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 10.6**
    /// 
    /// Property 25: Maker-Checker Separation of Duties — When a user attempts to approve
    /// their own command (checker == maker), the operation is always rejected.
    /// </summary>
    [Property(MaxTest = 200)]
    public Property Approval_WithSameUserAsMakerAndChecker_AlwaysThrows()
    {
        var userIdGen = Gen.Elements(
                "user_alice", "user_bob", "user_charlie", "user_dave",
                "user_eve", "user_frank", "user_grace", "user_henry")
            .Select(u => u + "_" + Guid.NewGuid().ToString("N")[..8]);
        var commandTypeGen = Gen.Elements(
            "adjustment.create", "refund.execute", "config.update", "role.assign",
            "adjustment.reverse", "merchant.suspend");

        return Prop.ForAll(
            userIdGen.ToArbitrary(),
            commandTypeGen.ToArbitrary(),
            (userId, commandType) =>
            {
                // Arrange: Create command with userId as maker
                var command = PendingCommand.Create(
                    commandType,
                    $"{{\"type\":\"{commandType}\"}}",
                    userId,
                    DateTime.UtcNow.AddHours(24));

                // Act: Try to approve with same userId as checker
                var throwsOnSameUser = false;
                try
                {
                    command.Approve(userId);
                }
                catch (InvalidOperationException ex)
                {
                    throwsOnSameUser = ex.Message.Contains("different users", StringComparison.OrdinalIgnoreCase)
                                       || ex.Message.Contains("Maker and checker", StringComparison.OrdinalIgnoreCase);
                }

                // Assert: Must remain in Pending state
                var stillPending = command.Status == CommandStatus.Pending;
                var noCheckerAssigned = command.CheckerId is null;

                return (throwsOnSameUser && stillPending && noCheckerAssigned)
                    .Label($"Self-approval with userId='{userId}': throws={throwsOnSameUser}, " +
                           $"stillPending={stillPending}, noChecker={noCheckerAssigned}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.6**
    /// 
    /// Property 25: Maker-Checker Separation of Duties — Case-insensitive comparison ensures
    /// that maker "Alice" and checker "alice" are treated as the same user.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Approval_WithCaseInsensitiveMatchOfMakerAndChecker_AlwaysThrows()
    {
        var baseNameGen = Gen.Elements("alice", "bob", "charlie", "dave", "eve");
        var casingGen = Gen.Elements(
            new Func<string, string>(s => s.ToUpperInvariant()),
            new Func<string, string>(s => s.ToLowerInvariant()),
            new Func<string, string>(s => char.ToUpper(s[0]) + s[1..]),
            new Func<string, string>(s => s));

        return Prop.ForAll(
            baseNameGen.ToArbitrary(),
            casingGen.ToArbitrary(),
            (baseName, casingFn) =>
            {
                var makerId = baseName;
                var checkerId = casingFn(baseName);

                var command = PendingCommand.Create(
                    "adjustment.create",
                    "{\"amount\":1000}",
                    makerId,
                    DateTime.UtcNow.AddHours(24));

                // Act: Try to approve with same user (different casing)
                var throwsOnCaseInsensitiveMatch = false;
                try
                {
                    command.Approve(checkerId);
                }
                catch (InvalidOperationException)
                {
                    throwsOnCaseInsensitiveMatch = true;
                }

                // Assert
                var stillPending = command.Status == CommandStatus.Pending;

                return (throwsOnCaseInsensitiveMatch && stillPending)
                    .Label($"Case-insensitive self-approval: maker='{makerId}', checker='{checkerId}', " +
                           $"throws={throwsOnCaseInsensitiveMatch}, stillPending={stillPending}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.6**
    /// 
    /// Property 25: Maker-Checker Separation of Duties — When maker and checker are different
    /// users, approval succeeds.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Approval_WithDifferentUsers_Succeeds()
    {
        // Generate pairs of distinct user IDs
        var makerGen = Gen.Elements("maker_alice", "maker_bob", "maker_charlie")
            .Select(m => m + "_" + Guid.NewGuid().ToString("N")[..8]);
        var checkerGen = Gen.Elements("checker_dave", "checker_eve", "checker_frank")
            .Select(c => c + "_" + Guid.NewGuid().ToString("N")[..8]);

        return Prop.ForAll(
            makerGen.ToArbitrary(),
            checkerGen.ToArbitrary(),
            (makerId, checkerId) =>
            {
                // Arrange
                var command = PendingCommand.Create(
                    "adjustment.create",
                    "{\"amount\":5000}",
                    makerId,
                    DateTime.UtcNow.AddHours(24));

                // Act: Approve with different user
                var succeeded = false;
                try
                {
                    command.Approve(checkerId);
                    succeeded = true;
                }
                catch (InvalidOperationException)
                {
                    succeeded = false;
                }

                // Assert
                var statusApproved = command.Status == CommandStatus.Approved;
                var checkerRecorded = command.CheckerId == checkerId;

                return (succeeded && statusApproved && checkerRecorded)
                    .Label($"Different user approval: maker='{makerId}', checker='{checkerId}', " +
                           $"succeeded={succeeded}, approved={statusApproved}");
            });
    }

    /// <summary>
    /// **Validates: Requirements 10.6**
    /// 
    /// Property 25: Maker-Checker Separation of Duties — Rejection also enforces separation
    /// of duties: a maker cannot reject their own command.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Rejection_WithSameUserAsMakerAndChecker_AlwaysThrows()
    {
        var userIdGen = Gen.Elements("user_alpha", "user_beta", "user_gamma", "user_delta")
            .Select(u => u + "_" + Guid.NewGuid().ToString("N")[..8]);

        return Prop.ForAll(userIdGen.ToArbitrary(), userId =>
        {
            var command = PendingCommand.Create(
                "refund.execute",
                "{\"refundId\":\"ref123\"}",
                userId,
                DateTime.UtcNow.AddHours(24));

            // Act: Try to reject with same userId
            var throwsOnSameUser = false;
            try
            {
                command.Reject(userId, "Self-rejection attempt");
            }
            catch (InvalidOperationException)
            {
                throwsOnSameUser = true;
            }

            var stillPending = command.Status == CommandStatus.Pending;

            return (throwsOnSameUser && stillPending)
                .Label($"Self-rejection with userId='{userId}': throws={throwsOnSameUser}, " +
                       $"stillPending={stillPending}");
        });
    }
}
