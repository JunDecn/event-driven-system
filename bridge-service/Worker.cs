using MQTTnet;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
using System.Buffers;

namespace bridge_service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly MqttSettings _mqttSettings;
        private readonly KafkaSettings _kafkaSettings;
        private IMqttClient? _mqttClient;
        private IProducer<string, string>? _kafkaProducer;
        private int _reconnectAttempts = 0;
        private bool _isManualDisconnect = false;

        public Worker(
            ILogger<Worker> logger,
            IOptions<MqttSettings> mqttSettings,
            IOptions<KafkaSettings> kafkaSettings)
        {
            _logger = logger;
            _mqttSettings = mqttSettings.Value;
            _kafkaSettings = kafkaSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // 初始化 Kafka Producer
                await InitializeKafkaProducer();

                // 初始化並連接 MQTT Client
                await InitializeAndConnectMqttClient(stoppingToken);

                // 保持服務運行
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Worker execution");
                throw;
            }
        }

        private async Task InitializeKafkaProducer()
        {
            var config = new ProducerConfig
            {
                BootstrapServers = _kafkaSettings.BootstrapServers,
                ClientId = _mqttSettings.ClientId
            };

            _kafkaProducer = new ProducerBuilder<string, string>(config).Build();
            _logger.LogInformation("Kafka Producer initialized with bootstrap servers: {BootstrapServers}", 
                _kafkaSettings.BootstrapServers);
        }

        private async Task InitializeAndConnectMqttClient(CancellationToken stoppingToken)
        {
            var factory = new MqttClientFactory();
            _mqttClient = factory.CreateMqttClient();

            // 設置斷線事件處理器
            _mqttClient.DisconnectedAsync += async e =>
            {
                if (_isManualDisconnect || stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("MQTT client disconnected (manual or stopping)");
                    return;
                }

                _logger.LogWarning("MQTT client disconnected unexpectedly. Reason: {Reason}", e.Reason);

                if (_mqttSettings.AutoReconnect)
                {
                    await ReconnectAsync(stoppingToken);
                }
            };

            // 設置連接事件處理器
            _mqttClient.ConnectedAsync += async e =>
            {
                _reconnectAttempts = 0;
                _logger.LogInformation("MQTT client connected successfully");
                await Task.CompletedTask;
            };

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_mqttSettings.BrokerHost, _mqttSettings.BrokerPort)
                .WithClientId(_mqttSettings.ClientId);

            if (!string.IsNullOrEmpty(_mqttSettings.Username))
            {
                optionsBuilder.WithCredentials(_mqttSettings.Username, _mqttSettings.Password);
            }

            var mqttOptions = optionsBuilder.Build();

            // 設置訊息接收處理器
            _mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                try
                {
                    var topic = e.ApplicationMessage.Topic;
                    
                    // 從 ReadOnlySequence<byte> 轉換為字串
                    var payloadSequence = e.ApplicationMessage.Payload;
                    byte[] payloadBytes = new byte[payloadSequence.Length];
                    payloadSequence.CopyTo(payloadBytes);
                    var payload = System.Text.Encoding.UTF8.GetString(payloadBytes);

                    _logger.LogInformation("Received MQTT message from topic: {Topic}", topic);

                    // 提取 deviceId
                    var deviceId = ExtractDeviceId(topic);

                    // 發送到 Kafka
                    await PublishToKafka(deviceId, payload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing MQTT message");
                }
            };

            // 連接到 MQTT Broker
            var connectResult = await _mqttClient.ConnectAsync(mqttOptions, stoppingToken);
            
            if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
            {
                _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", 
                    _mqttSettings.BrokerHost, _mqttSettings.BrokerPort);
            }
            else
            {
                _logger.LogError("Failed to connect to MQTT broker. Result: {ResultCode}", connectResult.ResultCode);
                throw new Exception($"MQTT connection failed: {connectResult.ResultCode}");
            }

            // 訂閱主題
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(_mqttSettings.TopicPattern)
                .Build();
            
            var subscribeResult = await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
            _logger.LogInformation("Subscribed to MQTT topic: {Topic}", _mqttSettings.TopicPattern);
        }

        private async Task ReconnectAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested && !_isManualDisconnect)
            {
                _reconnectAttempts++;

                if (_mqttSettings.MaxReconnectAttempts > 0 && _reconnectAttempts > _mqttSettings.MaxReconnectAttempts)
                {
                    _logger.LogError("Max reconnect attempts ({MaxAttempts}) reached. Stopping reconnection.", _mqttSettings.MaxReconnectAttempts);
                    break;
                }

                _logger.LogInformation("Attempting to reconnect to MQTT broker (Attempt {Attempt})...", _reconnectAttempts);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_mqttSettings.ReconnectDelaySeconds), stoppingToken);

                    var optionsBuilder = new MqttClientOptionsBuilder()
                        .WithTcpServer(_mqttSettings.BrokerHost, _mqttSettings.BrokerPort)
                        .WithClientId(_mqttSettings.ClientId);

                    if (!string.IsNullOrEmpty(_mqttSettings.Username))
                    {
                        optionsBuilder.WithCredentials(_mqttSettings.Username, _mqttSettings.Password);
                    }

                    var mqttOptions = optionsBuilder.Build();
                    var connectResult = await _mqttClient!.ConnectAsync(mqttOptions, stoppingToken);

                    if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                    {
                        _logger.LogInformation("Reconnected to MQTT broker successfully");

                        // 重新訂閱主題
                        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                            .WithTopicFilter(_mqttSettings.TopicPattern)
                            .Build();

                        await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
                        _logger.LogInformation("Re-subscribed to MQTT topic: {Topic}", _mqttSettings.TopicPattern);

                        _reconnectAttempts = 0;
                        break;
                    }
                    else
                    {
                        _logger.LogWarning("Reconnection failed with result: {ResultCode}", connectResult.ResultCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during reconnection attempt {Attempt}", _reconnectAttempts);
                }
            }
        }

        private string ExtractDeviceId(string topic)
        {
            // topic format: devices/{deviceId}/telemetry
            var parts = topic.Split('/');
            if (parts.Length >= 2)
            {
                return parts[1];
            }
            return "unknown";
        }

        private async Task PublishToKafka(string deviceId, string payload)
        {
            try
            {
                if (_kafkaProducer == null)
                {
                    _logger.LogWarning("Kafka producer is not initialized");
                    return;
                }

                var message = new Message<string, string>
                {
                    Key = deviceId,
                    Value = payload
                };

                var result = await _kafkaProducer.ProduceAsync(_kafkaSettings.Topic, message);
                
                _logger.LogInformation(
                    "Published message to Kafka topic: {Topic}, Partition: {Partition}, Offset: {Offset}, DeviceId: {DeviceId}",
                    _kafkaSettings.Topic, result.Partition.Value, result.Offset.Value, deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing message to Kafka for device: {DeviceId}", deviceId);
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bridge Service is stopping...");

            // 標記為手動斷線，避免觸發重連
            _isManualDisconnect = true;

            // 斷開 MQTT 連接
            if (_mqttClient != null)
            {
                var disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                    .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                    .Build();
                    
                await _mqttClient.DisconnectAsync(disconnectOptions, stoppingToken);
                _mqttClient.Dispose();
                _logger.LogInformation("Disconnected from MQTT broker");
            }

            // 清理 Kafka Producer
            if (_kafkaProducer != null)
            {
                _kafkaProducer.Flush(TimeSpan.FromSeconds(10));
                _kafkaProducer.Dispose();
                _logger.LogInformation("Kafka producer disposed");
            }

            await base.StopAsync(stoppingToken);
        }
    }
}
