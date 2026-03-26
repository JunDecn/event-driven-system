namespace bridge_service
{
    public class KafkaSettings
    {
        public string BootstrapServers { get; set; } = "kafka:9092";
        public string Topic { get; set; } = "iot.telemetry.raw";
    }
}
