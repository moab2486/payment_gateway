using FsCheck;

namespace CardManagement.PlatformServices.UnitTests.Generators;

/// <summary>
/// FsCheck generators for Developer Portal domain entities.
/// </summary>
public static class DeveloperPortalGenerators
{
    private static readonly string[] Scopes =
    {
        "payments:read", "payments:write", "disputes:read", "disputes:write",
        "webhooks:manage", "refunds:create", "reports:read", "sandbox:access"
    };

    private static readonly string[] CommandTypes =
    {
        "adjustment.create", "refund.execute", "config.update",
        "role.assign", "role.revoke", "account.suspend"
    };

    public static Arbitrary<AdminCommandData> AdminCommandArbitrary()
    {
        return (from id in Arb.Generate<Guid>()
                from commandType in Gen.Elements(CommandTypes)
                from makerId in Gen.Elements("operator-1", "operator-2", "operator-3", "compliance-1", "risk-1")
                from parameters in Gen.Elements(
                    """{"amount":5000,"reason":"Reconciliation adjustment"}""",
                    """{"transactionId":"txn-123","reason":"Customer request"}""",
                    """{"key":"max_retries","value":"5"}""",
                    """{"userId":"dev-456","role":"operations"}""")
                from isSensitive in Arb.Generate<bool>()
                select new AdminCommandData(
                    id,
                    commandType,
                    makerId,
                    parameters,
                    isSensitive))
            .ToArbitrary();
    }

    public static Arbitrary<ApiKeyData> ApiKeyArbitrary()
    {
        return (from id in Arb.Generate<Guid>()
                from developerId in Arb.Generate<Guid>()
                from keyHash in Arb.Generate<Guid>().Select(g => Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(g.ToByteArray())).ToLowerInvariant())
                from keyPrefix in Arb.Generate<Guid>().Select(g => g.ToString("N")[..8])
                from scopeCount in Gen.Choose(1, 4)
                from scopes in Gen.ArrayOf(scopeCount, Gen.Elements(Scopes))
                    .Select(arr => arr.Distinct().ToArray())
                from status in Gen.Elements(KeyStatus.Active, KeyStatus.Rotated, KeyStatus.Expired, KeyStatus.Revoked)
                from isSandbox in Arb.Generate<bool>()
                from createdDaysAgo in Gen.Choose(0, 365)
                from hasExpiry in Arb.Generate<bool>()
                from expiryDaysFromNow in Gen.Choose(1, 365)
                from hasGracePeriod in Arb.Generate<bool>()
                from graceDaysFromNow in Gen.Choose(1, 30)
                let createdAt = DateTime.UtcNow.AddDays(-createdDaysAgo)
                let expiresAt = hasExpiry ? (DateTime?)DateTime.UtcNow.AddDays(expiryDaysFromNow) : null
                let gracePeriodEnds = hasGracePeriod ? (DateTime?)DateTime.UtcNow.AddDays(graceDaysFromNow) : null
                select new ApiKeyData(
                    id,
                    developerId,
                    keyHash,
                    keyPrefix,
                    scopes,
                    status,
                    isSandbox,
                    createdAt,
                    expiresAt,
                    gracePeriodEnds))
            .ToArbitrary();
    }

    /// <summary>
    /// Registers all developer portal-related arbitraries with FsCheck.
    /// </summary>
    public class DeveloperPortalArbitraries
    {
        public static Arbitrary<AdminCommandData> AdminCommands() => AdminCommandArbitrary();
        public static Arbitrary<ApiKeyData> ApiKeys() => ApiKeyArbitrary();
    }
}

/// <summary>
/// Data record for admin command generation in property-based tests.
/// </summary>
public record AdminCommandData(
    Guid Id,
    string CommandType,
    string MakerId,
    string SerializedParameters,
    bool IsSensitive);

/// <summary>
/// Enum representing API key lifecycle status.
/// Mirrors the domain KeyStatus enum.
/// </summary>
public enum KeyStatus { Active, Rotated, Expired, Revoked }

/// <summary>
/// Data record for API key generation in property-based tests.
/// Mirrors the ApiKey domain entity structure.
/// </summary>
public record ApiKeyData(
    Guid Id,
    Guid DeveloperId,
    string KeyHash,
    string KeyPrefix,
    string[] Scopes,
    KeyStatus Status,
    bool IsSandbox,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? GracePeriodEndsAtUtc);
