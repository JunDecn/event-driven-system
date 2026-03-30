using bridge_service;

namespace bridge_service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMqttSubscriber _mqttSubscriber;
        private readonly IProducer _kafkaProducer;
        private static readonly TimeSpan StartupRetryDelay = TimeSpan.FromSeconds(5);

        public Worker(
            ILogger<Worker> logger,
            IMqttSubscriber mqttSubscriber,
            IProducer kafkaProducer)
        {
            _logger = logger;
            _mqttSubscriber = mqttSubscriber;
            _kafkaProducer = kafkaProducer;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _mqttSubscriber.MessageReceived += OnMqttMessageReceivedAsync;

            try
            {
                await RetryUntilConnectedAsync(
                    "Kafka",
                    async ct =>
                    {
                        await _kafkaProducer.InitializeAsync(ct);
                        await _kafkaProducer.ConnectAsync(ct);
                    },
                    stoppingToken);

                await RetryUntilConnectedAsync(
                    "MQTT",
                    async ct =>
                    {
                        await _mqttSubscriber.InitializeAsync(ct);
                        await _mqttSubscriber.ConnectAsync(ct);
                    },
                    stoppingToken);

                _logger.LogInformation("Bridge worker started.");

                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker cancellation requested.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected fatal error in Worker execution");
                throw;
            }
        }

        private async Task RetryUntilConnectedAsync(
            string componentName,
            Func<CancellationToken, Task> connectAction,
            CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await connectAction(stoppingToken);
                    _logger.LogInformation("{Component} initialized and connected.", componentName);
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "{Component} initialize/connect failed. Retry after {DelaySeconds} seconds.",
                        componentName,
                        StartupRetryDelay.TotalSeconds);
                }

                await Task.Delay(StartupRetryDelay, stoppingToken);
            }
        }

        private async Task OnMqttMessageReceivedAsync(string topic, string payload)
        {
            try
            {
                var deviceId = ExtractDeviceId(topic);
                await _kafkaProducer.PublishAsync(deviceId, payload, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to bridge MQTT message from topic {Topic}", topic);
            }
        }

        private static string ExtractDeviceId(string topic)
        {
            var parts = topic.Split('/');
            return parts.Length >= 2 ? parts[1] : "unknown";
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bridge Service is stopping...");

            _mqttSubscriber.MessageReceived -= OnMqttMessageReceivedAsync;

            await _mqttSubscriber.DisconnectAsync(stoppingToken);
            await _kafkaProducer.DisconnectAsync(stoppingToken);

            await base.StopAsync(stoppingToken);
        }
    }
}
