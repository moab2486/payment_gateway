using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Kafka;

/// <summary>
/// No-op implementation of <see cref="IEventPublisher"/> used when Kafka is not configured.
/// Logs a warning on construction indicating event publishing is disabled.
/// </summary>
public sealed class NullEventPublisher : IEventPublisher
{
    private readonly ILogger<NullEventPublisher> _logger;

    public NullEventPublisher(ILogger<NullEventPublisher> logger)
    {
        _logger = logger;
        _logger.LogWarning("Kafka event publishing is disabled. KAFKA__BOOTSTRAP_SERVERS not configured.");
    }

    public Task PublishCardIssuedAsync(CardIssuedEvent cardEvent, CancellationToken ct) => Task.CompletedTask;

    public Task PublishTransactionAuthorizedAsync(TransactionAuthorizedEvent txEvent, CancellationToken ct) => Task.CompletedTask;

    public Task PublishTransactionReversedAsync(TransactionReversedEvent txEvent, CancellationToken ct) => Task.CompletedTask;
}
