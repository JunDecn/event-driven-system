using Confluent.Kafka;
using System.Text.Json;

namespace alert_processor
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = "kafka:9092",
                GroupId = "alert-processor-group",
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

                                // 解析 JSON 並提取 tags 和 fields
                                var jsonDoc = JsonDocument.Parse(orderData);
                                var tags = new Dictionary<string, string>();
                                // 獲取溫度，檢測是否超過標準
                                if (jsonDoc.RootElement.TryGetProperty("temperature", out var tempProp))
                                {
                                    if (tempProp.TryGetDouble(out var temperature))
                                    {
                                        tags["temperature"] = temperature.ToString();
                                        if (temperature >= 30) // 假設 30 是溫度標準
                                        {
                                            _logger.LogWarning($"Temperature alert: deviceId={key}, temperature={temperature} at {timestamp}");
                                        }
                                        else
                                        {
                                            
                                        }
                                    }
                                }


                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Error processing individual message: {ex.Message}");
                            }
                        }


                        // 提交最後一筆訊息的 offset
                        if (messages.Count > 0)
                        {
                            //consumer.Commit(messages.Last());
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
    }
}
