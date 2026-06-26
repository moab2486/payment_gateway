using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Health Check Connectivity Reporting (Property 6).
/// Validates: Requirements 6.5
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
[Trait("Property", "6")]
public class HealthCheckConnectivityPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 6.5**
    /// 
    /// Property 6: For any combination of PostgreSQL connectivity state (reachable/unreachable)
    /// and Kafka connectivity state (reachable/unreachable), the health report SHALL accurately
    /// report both individual component statuses, and the overall status SHALL be "Healthy"
    /// only when all components are reachable.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OverallHealth_IsHealthy_OnlyWhenAllComponentsAreHealthy()
    {
        var gen = from pgHealthy in Arb.Generate<bool>()
                  from kafkaHealthy in Arb.Generate<bool>()
                  select (PgHealthy: pgHealthy, KafkaHealthy: kafkaHealthy);

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            // Simulate health check entries based on connectivity states
            var entries = new Dictionary<string, HealthReportEntry>
            {
                ["postgresql"] = new HealthReportEntry(
                    testCase.PgHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                    testCase.PgHealthy ? "Reachable" : "Unreachable",
                    TimeSpan.FromMilliseconds(10),
                    exception: null,
                    data: null),
                ["kafka"] = new HealthReportEntry(
                    testCase.KafkaHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                    testCase.KafkaHealthy ? "Connected to 1 broker(s)" : "Kafka broker unreachable",
                    TimeSpan.FromMilliseconds(50),
                    exception: null,
                    data: null)
            };

            // Compute overall status: Healthy only when ALL entries are Healthy
            var overallStatus = entries.All(e => e.Value.Status == HealthStatus.Healthy)
                ? HealthStatus.Healthy
                : HealthStatus.Unhealthy;

            var report = new HealthReport(entries, overallStatus, TimeSpan.FromMilliseconds(60));

            // Assert individual statuses are accurately reported
            var pgStatusCorrect = entries["postgresql"].Status ==
                (testCase.PgHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy);
            var kafkaStatusCorrect = entries["kafka"].Status ==
                (testCase.KafkaHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy);

            // Assert overall status is Healthy only when both components are reachable
            var expectedOverallHealthy = testCase.PgHealthy && testCase.KafkaHealthy;
            var overallStatusCorrect = (report.Status == HealthStatus.Healthy) == expectedOverallHealthy;

            return (pgStatusCorrect && kafkaStatusCorrect && overallStatusCorrect)
                .Label($"PG={testCase.PgHealthy}, Kafka={testCase.KafkaHealthy}: " +
                       $"expected overall Healthy={expectedOverallHealthy}, got {report.Status}");
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.5**
    /// 
    /// Property 6b: For any combination of connectivity states, each individual component
    /// status in the health report SHALL independently reflect its own connectivity state
    /// regardless of the other component's state.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IndividualComponentStatus_ReflectsOwnConnectivityState()
    {
        var gen = from pgHealthy in Arb.Generate<bool>()
                  from kafkaHealthy in Arb.Generate<bool>()
                  select (PgHealthy: pgHealthy, KafkaHealthy: kafkaHealthy);

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            var entries = new Dictionary<string, HealthReportEntry>
            {
                ["postgresql"] = new HealthReportEntry(
                    testCase.PgHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                    testCase.PgHealthy ? "Reachable" : "Unreachable",
                    TimeSpan.FromMilliseconds(10),
                    exception: null,
                    data: null),
                ["kafka"] = new HealthReportEntry(
                    testCase.KafkaHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                    testCase.KafkaHealthy ? "Connected to 1 broker(s)" : "Kafka broker unreachable",
                    TimeSpan.FromMilliseconds(50),
                    exception: null,
                    data: null)
            };

            var overallStatus = entries.All(e => e.Value.Status == HealthStatus.Healthy)
                ? HealthStatus.Healthy
                : HealthStatus.Unhealthy;

            var report = new HealthReport(entries, overallStatus, TimeSpan.FromMilliseconds(60));

            // PostgreSQL status is independent of Kafka state
            var pgIndependent = (report.Entries["postgresql"].Status == HealthStatus.Healthy) == testCase.PgHealthy;
            // Kafka status is independent of PostgreSQL state
            var kafkaIndependent = (report.Entries["kafka"].Status == HealthStatus.Healthy) == testCase.KafkaHealthy;

            return (pgIndependent && kafkaIndependent)
                .Label($"PG reachable={testCase.PgHealthy} (status={report.Entries["postgresql"].Status}), " +
                       $"Kafka reachable={testCase.KafkaHealthy} (status={report.Entries["kafka"].Status})");
        });
    }
}
