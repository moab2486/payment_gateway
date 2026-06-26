using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Kafka;

public sealed class KafkaHealthCheck : IHealthCheck
{
    private readonly KafkaOptions _options;

    public KafkaHealthCheck(IOptions<KafkaOptions> options)
    {
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var adminClient = new AdminClientBuilder(
                new AdminClientConfig { BootstrapServers = _options.BootstrapServers })
                .Build();

            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
            return Task.FromResult(
                HealthCheckResult.Healthy($"Connected to {metadata.Brokers.Count} broker(s)"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Kafka broker unreachable", ex));
        }
    }
}
