using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;

namespace telemetry_processor
{
    public interface IInfluxDBService
    {
        Task WriteAsync(string measurement, Dictionary<string, string> tags, Dictionary<string, object> fields, DateTime? timestamp = null);
        Task WriteAsync(string measurement, Dictionary<string, object> fields, DateTime? timestamp = null);
        Task WriteBatchAsync(string measurement, List<(Dictionary<string, string> tags, Dictionary<string, object> fields, DateTime? timestamp)> dataPoints);
    }

    public class InfluxDBService : IInfluxDBService, IDisposable
    {
        private readonly InfluxDBClient _client;
        private readonly string _bucket;
        private readonly string _org;
        private readonly ILogger<InfluxDBService> _logger;

        public InfluxDBService(ILogger<InfluxDBService> logger, InfluxDBConfig config)
        {
            _logger = logger;
            _bucket = config.Bucket;
            _org = config.Org;
            _client = new InfluxDBClient(config.Url, config.Token);
        }

        public async Task WriteAsync(string measurement, Dictionary<string, string> tags, Dictionary<string, object> fields, DateTime? timestamp = null)
        {
            try
            {
                var writeApi = _client.GetWriteApiAsync();
                
                var point = PointData.Measurement(measurement)
                    .Timestamp(timestamp ?? DateTime.UtcNow, WritePrecision.Ns);

                foreach (var tag in tags)
                {
                    point = point.Tag(tag.Key, tag.Value);
                }

                foreach (var field in fields)
                {
                    point = point.Field(field.Key, field.Value);
                }

                await writeApi.WritePointAsync(point, _bucket, _org);
                _logger.LogDebug($"Successfully wrote data to InfluxDB: {measurement}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error writing to InfluxDB: {ex.Message}");
                throw;
            }
        }

        public async Task WriteAsync(string measurement, Dictionary<string, object> fields, DateTime? timestamp = null)
        {
            await WriteAsync(measurement, new Dictionary<string, string>(), fields, timestamp);
        }

        public async Task WriteBatchAsync(string measurement, List<(Dictionary<string, string> tags, Dictionary<string, object> fields, DateTime? timestamp)> dataPoints)
        {
            try
            {
                if (dataPoints == null || dataPoints.Count == 0)
                {
                    _logger.LogWarning("No data points to write");
                    return;
                }

                var writeApi = _client.GetWriteApiAsync();
                var points = new List<PointData>();

                foreach (var (tags, fields, timestamp) in dataPoints)
                {
                    var point = PointData.Measurement(measurement)
                        .Timestamp(timestamp ?? DateTime.UtcNow, WritePrecision.Ns);

                    foreach (var tag in tags)
                    {
                        point = point.Tag(tag.Key, tag.Value);
                    }

                    foreach (var field in fields)
                    {
                        point = point.Field(field.Key, field.Value);
                    }

                    points.Add(point);
                }

                await writeApi.WritePointsAsync(points, _bucket, _org);
                _logger.LogInformation($"Successfully wrote batch of {dataPoints.Count} data points to InfluxDB: {measurement}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error writing batch to InfluxDB: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
