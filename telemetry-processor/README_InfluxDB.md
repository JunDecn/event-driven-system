# Telemetry Processor - InfluxDB 集成

## 概述
此服?? Kafka 消????据并?入 InfluxDB ?行存?和分析。

## 配置

### appsettings.json 配置
?在 `appsettings.json` 中配置以下 InfluxDB ?接信息：

```json
{
  "InfluxDB": {
    "Url": "http://influxdb:8086",
    "Token": "your-influxdb-token",
    "Org": "your-org",
    "Bucket": "telemetry"
  }
}
```

#### 配置?明：
- **Url**: InfluxDB 服?器地址
- **Token**: InfluxDB ??令牌（在 InfluxDB 管理界面生成）
- **Org**: InfluxDB ??名?
- **Bucket**: 存??据的 Bucket 名?

## 功能?明

### ?据流程
1. ? Kafka 主? `iot.telemetry.raw` 消?消息
2. 解析消息?容（支持 JSON 格式）
3. ??据?入 InfluxDB
4. 提交 Kafka offset

### ?据?构

#### Measurement
- 名?：`telemetry_data`

#### Tags（??）
- `topic`: Kafka 主?名?
- `partition`: Kafka 分??
- `message_key`: Kafka 消息?（如果存在）

#### Fields（字段）
- 自?解析 JSON 中的所有字段：
  - ?字?型保持??字
  - 字符串?型保持?字符串
  - 布??型保持?布?值
- `offset`: Kafka offset
- `raw_data`: 如果消息不是 JSON 格式，?原始?据存?在此字段

### 示例?据

假?? Kafka 接收到如下 JSON 消息：
```json
{
  "deviceId": "sensor-001",
  "temperature": 25.5,
  "humidity": 60.2,
  "timestamp": "2024-01-01T10:00:00Z"
}
```

??入 InfluxDB 的?据?：
```
telemetry_data,topic=iot.telemetry.raw,partition=0,message_key=sensor-001 deviceId="sensor-001",temperature=25.5,humidity=60.2,timestamp="2024-01-01T10:00:00Z",offset=12345 1704103200000000000
```

## 使用方法

### 本地?行
```bash
dotnet run
```

### Docker ?行
```bash
docker build -t telemetry-processor .
docker run -e InfluxDB__Url=http://influxdb:8086 \
           -e InfluxDB__Token=your-token \
           -e InfluxDB__Org=your-org \
           -e InfluxDB__Bucket=telemetry \
           telemetry-processor
```

## 查??据

在 InfluxDB 中查??据示例：

```flux
from(bucket: "telemetry")
  |> range(start: -1h)
  |> filter(fn: (r) => r._measurement == "telemetry_data")
```

## ???理

- Kafka 消????被??但不?中?服?
- InfluxDB ?入???被??并?出异常
- 服??在取消令牌触??优雅??

## 日志??

可在 `appsettings.json` 中?整日志??：
```json
{
  "Logging": {
    "LogLevel": {
      "telemetry_processor": "Debug"
    }
  }
}
```
