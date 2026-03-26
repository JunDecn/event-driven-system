namespace bridge_service
{
    public class MqttSettings
    {
        public string BrokerHost { get; set; } = "emqx1";
        public int BrokerPort { get; set; } = 1883;
        public string ClientId { get; set; } = "bridge-service";
        public string TopicPattern { get; set; } = "devices/+/telemetry";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool AutoReconnect { get; set; } = true;
        public int ReconnectDelaySeconds { get; set; } = 5;
        public int MaxReconnectAttempts { get; set; } = 0; // 0 = µL­­­«¸Õ
    }
}
