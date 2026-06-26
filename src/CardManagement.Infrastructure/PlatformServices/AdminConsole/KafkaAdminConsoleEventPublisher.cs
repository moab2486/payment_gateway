using System.Text.Json;
using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Infrastructure.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Kafka-backed implementation of IAdminConsoleEventPublisher.
/// Publishes admin console domain events to platform.admin.* topics.
/// Used to notify checkers when a new command is pending approval.
/// </summary>
public sealed class KafkaAdminConsoleEventPublisher : IAdminConsoleEventPublisher, IDisposable
{
    private const string CommandPendingApprovalTopic = "platform.admin.command-pending-approval";

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaAdminConsoleEventPublisher> _logger;

    public KafkaAdminConsoleEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaAdminConsoleEventPublisher> logger)
    {
        var kafkaOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            ClientId = $"{kafkaOptions.ClientId}-admin-console",
            Acks = Acks.Leader,
            EnableIdempotence = false,
            MessageTimeoutMs = 5000,
            RequestTimeoutMs = 3000
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    /// <summary>
    /// Internal constructor for unit testing. Accepts a pre-built producer instance.
    /// </summary>
    internal KafkaAdminConsoleEventPublisher(
        IProducer<string, string> producer,
        ILogger<KafkaAdminConsoleEventPublisher> logger)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task PublishCommandPendingApprovalAsync(
        Guid commandId,
        string commandType,
        string makerId,
        DateTime expiresAtUtc,
        CancellationToken ct)
    {
        var payload = new
        {
            CommandId = commandId,
            CommandType = commandType,
            MakerId = makerId,
            ExpiresAtUtc = expiresAtUtc,
            PublishedAtUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(payload);
        var message = new Message<string, string>
        {
            Key = commandId.ToString(),
            Value = json
        };

        try
        {
            await _producer.ProduceAsync(CommandPendingApprovalTopic, message, ct);
            _logger.LogDebug(
                "Published command-pending-approval event for command {CommandId} (type: {CommandType}) to topic {Topic}.",
                commandId, commandType, CommandPendingApprovalTopic);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish command-pending-approval event for command {CommandId} to topic {Topic}. Continuing without blocking.",
                commandId, CommandPendingApprovalTopic);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex,
                "Kafka error while publishing command-pending-approval event to {Topic}. Continuing without blocking.",
                CommandPendingApprovalTopic);
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
