using Confluent.Kafka;

namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Abstraction over Kafka consumer for testability.
/// </summary>
public interface IKafkaConsumerWrapper : IDisposable
{
    void Subscribe(IEnumerable<string> topics);
    ConsumeResult<string, string>? Consume(CancellationToken ct);
    void Commit(ConsumeResult<string, string> result);
    void Close();
}
