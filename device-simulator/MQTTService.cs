using MQTTnet;
using System.Text.Json;

namespace device_simulator
{
    public class MQTTService
    {
        private readonly MqttClientOptions _options;
        private readonly ILogger<MQTTService> _logger;
        private IMqttClient? _mqttClient;

        public MQTTService(ILogger<MQTTService> logger, IConfiguration configuration)
        {
            _logger = logger;

            // 從配置讀取 MQTT Broker 設定
            var broker = configuration["MQTT:Broker"] ?? "localhost";
            var port = int.Parse(configuration["MQTT:Port"] ?? "1883");

            _options = new MqttClientOptionsBuilder()
                .WithTcpServer(broker, port)
                .WithClientId("device-simulator-client")
                .WithCleanSession()
                .Build();
        }

        public async Task ConnectAsync()
        {
            try
            {
                var factory = new MqttClientFactory();
                _mqttClient = factory.CreateMqttClient();

                var result = await _mqttClient.ConnectAsync(_options);
                
                if (result.ResultCode == MqttClientConnectResultCode.Success)
                {
                    _logger.LogInformation("成功連接到 MQTT Broker");
                }
                else
                {
                    _logger.LogError($"連接失敗: {result.ResultCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "連接 MQTT Broker 時發生錯誤");
                throw;
            }
        }

        public async Task PublishTelemetryAsync(double temperature, int humidity)
        {
            await PublishTelemetryAsync("device-001", temperature, humidity);
        }

        public async Task PublishTelemetryAsync(string deviceId, double temperature, int humidity)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                await ConnectAsync();
            }

            var telemetryData = new
            {
                temperature = temperature,
                humidity = humidity
            };

            var payload = JsonSerializer.Serialize(telemetryData);
            
            var message = new MqttApplicationMessageBuilder()
                .WithTopic($"devices/{deviceId}/telemetry")
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce) // QoS
                .WithRetainFlag(false)
                .Build();

            try
            {
                await _mqttClient!.PublishAsync(message);
                _logger.LogInformation($"已發送遙測數據 [{deviceId}]: {payload}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "發送訊息時發生錯誤");
                throw;
            }
        }

        public async Task PublishTelemetryForAllDevicesAsync(double temperature, int humidity)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected)
            {
                await ConnectAsync();
            }

            _logger.LogInformation("開始從 device-001 到 device-1000 發送遙測數據...");

            for (int i = 1; i <= 1000; i++)
            {
                string deviceId = $"device-{i:D3}";
                await PublishTelemetryAsync(deviceId, temperature, humidity);
            }

            _logger.LogInformation("已完成發送 1000 個設備的遙測數據");
        }

        public async Task DisconnectAsync()
        {
            if (_mqttClient != null && _mqttClient.IsConnected)
            {
                await _mqttClient.DisconnectAsync();
                _logger.LogInformation("已斷開 MQTT 連接");
            }
        }
    }
}
