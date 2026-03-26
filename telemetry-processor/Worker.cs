
using Confluent.Kafka;
using System.Text.Json;

namespace telemetry_processor
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IInfluxDBService _influxDBService;

        public Worker(ILogger<Worker> logger, IInfluxDBService influxDBService)
        {
            _logger = logger;
            _influxDBService = influxDBService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = "kafka:9092",
                GroupId = "telemetry-processor-group",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                MaxPollIntervalMs = 300000, // 5 minutes
                FetchMinBytes = 1,
                FetchMaxBytes = 52428800, // 50MB
                MaxPartitionFetchBytes = 1048576 // 1MB per partition
            };

            using var consumer = new ConsumerBuilder<string, string>(config).Build();
            consumer.Subscribe("iot.telemetry.raw");

            _logger.LogInformation("Telemetry processor started, consuming from iot.telemetry.raw");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var messages = new List<ConsumeResult<string, string>>();
                        const int maxBatchSize = 500;

                        // 批次消費訊息，最多 500 筆
                        for (int i = 0; i < maxBatchSize && !stoppingToken.IsCancellationRequested; i++)
                        {
                            var result = consumer.Consume(TimeSpan.FromMilliseconds(100));
                            if (result == null)
                                break; // 沒有更多訊息，結束本批次

                            messages.Add(result);
                        }

                        if (messages.Count == 0)
                            continue;

                        _logger.LogInformation($"Processing batch of {messages.Count} messages");

                        // 準備批次寫入資料
                        var dataPoints = new List<(Dictionary<string, string> tags, Dictionary<string, object> fields, DateTime? timestamp)>();

                        // 處理所有訊息
                        foreach (var result in messages)
                        {
                            try
                            {
                                var orderData = result.Message.Value;
                                var key = result.Message.Key;
                                var partition = result.Partition.Value;
                                var offset = result.Offset.Value;
                                var topic = result.Topic;
                                
                                // 從 Kafka 消息中提取時間戳
                                var timestamp = result.Message.Timestamp.UtcDateTime;

                                _logger.LogDebug($"Processing telemetry data from {topic}[{partition}] at offset {offset} with timestamp {timestamp}");

                                // 準備寫入資料
                                var (tags, fields) = PrepareTelemetryData(key, orderData, topic, partition, offset);
                                dataPoints.Add((tags, fields, timestamp));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error processing individual message: {ex.Message}");
                            }
                        }

                        // 批次寫入 InfluxDB
                        if (dataPoints.Count > 0)
                        {
                            try
                            {
                                await _influxDBService.WriteBatchAsync("telemetry_data", dataPoints);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to write batch to InfluxDB: {ex.Message}");
                            }
                        }

                        // 提交最後一筆訊息的 offset
                        if (messages.Count > 0)
                        {
                            consumer.Commit(messages.Last());
                            _logger.LogInformation($"Committed batch of {messages.Count} messages");
                        }
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, $"Error consuming message: {ex.Error.Reason}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing batch: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Telemetry processor is stopping");
            }
            finally
            {
                consumer.Close();
            }
        }

        private (Dictionary<string, string> tags, Dictionary<string, object> fields) PrepareTelemetryData(string key, string data, string topic, int partition, long offset)
        {
            // 尝试解析 JSON 数据
            var fields = new Dictionary<string, object>();
            var tags = new Dictionary<string, string>
            {
                { "topic", topic },
                { "partition", partition.ToString() }
            };

            if (!string.IsNullOrEmpty(key))
            {
                tags["message_key"] = key;
            }

            try
            {
                // 尝试解析 JSON
                using var jsonDoc = JsonDocument.Parse(data);
                var root = jsonDoc.RootElement;

                foreach (var property in root.EnumerateObject())
                {
                    var value = property.Value;
                    if (value.ValueKind == JsonValueKind.Number)
                    {
                        if (value.TryGetDouble(out var doubleValue))
                        {
                            fields[property.Name] = doubleValue;
                        }
                    }
                    else if (value.ValueKind == JsonValueKind.String)
                    {
                        fields[property.Name] = value.GetString() ?? string.Empty;
                    }
                    else if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                    {
                        fields[property.Name] = value.GetBoolean();
                    }
                }
            }
            catch (JsonException)
            {
                // 如果不是 JSON，将原始数据作为字段存储
                fields["raw_data"] = data;
            }

            fields["offset"] = offset;

            return (tags, fields);
        }
    }
}
