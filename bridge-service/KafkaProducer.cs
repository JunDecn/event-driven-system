using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace bridge_service;

public class KafkaProducer : IProducer
{
    private readonly ILogger<KafkaProducer> _logger;
    private readonly KafkaSettings _settings;
    private Confluent.Kafka.IProducer<string, string>? _producer;
    private bool _isConnected;

    public KafkaProducer(ILogger<KafkaProducer> logger, IOptions<KafkaSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _settings.BootstrapServers,
            ClientId = "bridge-service-kafka-producer"
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
        _logger.LogInformation("Kafka producer initialized. BootstrapServers: {BootstrapServers}", _settings.BootstrapServers);
        return Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_producer is null)
        {
            throw new InvalidOperationException("Kafka producer is not initialized.");
        }

        _isConnected = true;
        _logger.LogInformation("Kafka producer connected.");
        return Task.CompletedTask;
    }

    public async Task PublishAsync(string key, string payload, CancellationToken cancellationToken)
    {
        if (!_isConnected || _producer is null)
        {
            throw new InvalidOperationException("Kafka producer is not connected.");
        }

        var result = await _producer.ProduceAsync(
            _settings.Topic,
            new Message<string, string> { Key = key, Value = payload },
            cancellationToken);

        _logger.LogInformation(
            "Published to Kafka topic {Topic}. Partition: {Partition}, Offset: {Offset}, Key: {Key}",
            _settings.Topic,
            result.Partition.Value,
            result.Offset.Value,
            key);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_producer is null)
        {
            return Task.CompletedTask;
        }

        _producer.Flush(TimeSpan.FromSeconds(10));
        _isConnected = false;
        _logger.LogInformation("Kafka producer disconnected.");
        return Task.CompletedTask;
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Reconnecting Kafka producer...");
        await DisconnectAsync(cancellationToken);

        _producer?.Dispose();
        _producer = null;

        await InitializeAsync(cancellationToken);
        await ConnectAsync(cancellationToken);
    }
}
