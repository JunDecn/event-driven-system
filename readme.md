# Event Driven System - 物聯網溫度監控系統

## 📌 項目概述

這是一個**端到端的事件驅動型 IoT 監控系統**，展示如何使用 MQTT 和事件流架構在分佈式系統中進行實時資料處理。

**核心功能：**
- 🌡️ 實時設備溫度監控（50 台設備測試情境）
- 📊 溫度異常告警（>30°C）
- 💾 時間序列資料持久化（InfluxDB）
- 📈 可視化儀表板（Grafana）

**技術重點：**
- MQTT + Kafka 事件驅動架構
- 背景服務批量消息處理
- 多節點 Kafka KRaft 集群
- 容器化部署

---

## 🏗️ 系統架構詳解

### 為什麼選擇 MQTT + Kafka？
| 選擇 | 理由 |
|------|------|
| **MQTT (邊緣層)** | 設備友好、低頻寬、輕量級協議；適合 IoT 設備 |
| **Kafka (中樞層)** | 高吞吐量、持久化、可擴展；解耦生產/消費端 |
| **多消費者群組** | 支持多種業務邏輯並行處理（告警/存儲/分析） |


### 架構圖
```
IoT Device Simulator (1 devices) or Python mqtt-bench Service (50 devices)
            │
            │ MQTT (publish every 5s)
            │
            ▼
        EMQX Broker (Port 1883)
        (Dashboard: 18083)
            │
            │ MQTT Consume (subscribe)
            │
            ▼
    ┌─────────────────────┐
    │  Bridge Service     │
    │  (.NET Worker)      │
    │  ├─ MQTT Client     │
    │  ├─ Kafka Producer  │
    │  └─ Auto Reconnect  │
    └─────────────────────┘
            │
            │  Kafka Publish (produce)
            │
            ▼
    ┌────────────────────────────────┐
    │  Kafka KRaft Cluster (3-node)  │
    │  ├─ kafka:9092                 │
    │  ├─ kafka2:9092                │
    │  ├─ kafka4:9092                │
    │  └─ Kafka UI: 8080             │
    │     Topic: iot.telemetry.raw   │
    └────────────────────────────────┘
            │
    ┌───────┴────────────────┐
    │                        │
    │ (Group: alert)         │ (Group: telemetry)
    ▼                        ▼ 
┌──────────────────┐    ┌─────────────────────┐
│ Alert Processor  │    │ Telemetry Processor │
│ (.NET Worker)    │    │ (.NET Worker)       │
│ ├─ Consume batch │    │ ├─ Consume batch    │
│ ├─ Detect >30°C  │    │ ├─ Parse JSON       │
│ └─ Log ERROR     │    │ └─ Write InfluxDB   │
└──────────────────┘    └─────────────────────┘
    │ (Log)                     │ (Data Points)
    ▼                           ▼
    Console               ┌──────────────┐
                          │  InfluxDB    │
                          │  Port: 8086  │
                          │  (Time Series│
                          │   Database)  │
                          └──────────────┘
                                │
                                ▼
                          ┌─────────────┐
                          │  Grafana    │
                          │  Port: 3000 │
                          │  (Dashboard)│
                          └─────────────┘
```

---

## 🛠️ 使用技術棧

| 層級 | 模組 | 技術 | 版本 | 用途 |
|-----|------|------|------|------|
| **IoT Edge** | Device Simulator | .NET 8 Web API | 8.0 | 設備數據模擬 |
| **MQTT Layer** | EMQX Broker | EMQX | 5.6 | 輕量級消息代理 |
| **Bridge** | Bridge Service | .NET 8 Worker | 8.0 | MQTT ↔ Kafka 轉換 |
| **Event Stream** | Kafka KRaft | Kafka | Latest | 分布式事件流 |
| **Kafka Monitor** | Kafka UI | Provectus | Latest | 集群監控 |
| **Processing** | Alert Processor | .NET 8 Worker | 8.0 | 告警檢測邏輯 |
| **Processing** | Telemetry Processor | .NET 8 Worker | 8.0 | 數據處理邏輯 |
| **Storage** | InfluxDB | InfluxDB | 2.7 | 時間序列資料庫 |
| **Visualization** | Grafana | Grafana | Latest | 數據可視化 |
| **Container** | Docker Compose | Docker | 20.10+ | 容器編排 |

備註：因為Demo時使用EMQX開源版，沒有提供kafka connector，所以選擇Bridge Service作為替代方案

---

### 各元件職責
- **Device Simulator**: 適用於單設備、單連線模擬，方便功能驗證與端到端除錯
- **Python Service (mqtt-bench)**: 用於多設備同時連線模擬與併發訊息壓測
- **Bridge Service**: MQTT 訂閱者 → Kafka 生產者，負責協議轉換
- **Alert Processor**: 批量消費告警（溫度 >30°C），記錄日誌
- **Telemetry Processor**: 批量消費資料到 InfluxDB 進行時間序列存儲
- **Kafka UI**: 監控 Kafka 集群狀態和消息流

---

## 🚀 快速開始

### 前置需求
- Docker Desktop 20.10+
- .NET 8 SDK (如本地開發)

### 一鍵啟動
```bash
# 進入項目目錄
cd event-driven-system

# 啟動所有容器
docker-compose up -d

# 驗證服務狀態
docker-compose ps
```


### 服務入口
| 服務 | URL/Port | 說明 |
|------|---------|------|
| EMQX Dashboard | http://localhost:18083 | MQTT broker 管理界面 |
| Device Simulator API | http://localhost:9000 | 測試端點 |
| Kafka UI | http://localhost:8080 | Kafka 消息監控 |
| InfluxDB | http://localhost:8086 | 時間序列資料庫 |
| Grafana | http://localhost:3000 | 可視化儀表板 |

---

## 📡 API 端點 & 數據格式

### Device Simulator 端點

**1. 發送單一設備遙測**
```bash
curl -X POST "http://localhost:9000/send-telemetry?temperature=28.5&humidity=65"
```

**2. 發送單一設備1000筆數據（測試用途）**
```bash
curl -X POST "http://localhost:9000/send-telemetry-all-devices?temperature=32&humidity=70"
```

**3. 發送多設備遙測**

正式併發連線使用 Python mqtt-bench 服務進行測試，請手動啟用 docker compose 的 mqtt-bench（loadtest profile），timestamp 為即時動態值。

---

## 📝 MQTT Topic 協議

**Topic Pattern:**
```
devices/{deviceId}/telemetry
```

**Payload Schema:**
```json
{
  "temperature": 28.5,    // 攝氏溫度
  "humidity": 65,          // 相對濕度 (%)
  "timestamp": "2026-03-30T10:30:45.123Z"
}
```

**Example Topics:**
```
devices/device-001/telemetry
devices/device-002/telemetry
...
devices/device-1000/telemetry
```

---


## 📊 Kafka Topic 協議

**Topic Name:** `iot.telemetry.raw`

**Partition:** 1 (確保消息順序)

**Consumer Groups:**
```
1. alert-processor-group
   └─ 消費和檢測溫度異常 (>30°C)
   
2. telemetry-processor-group
   └─ 消費和存儲到 InfluxDB
```

**Message Format:**
```json
{
  "deviceId": "device-001",
  "temperature": 28.5,
  "humidity": 65,
  "timestamp": "2026-03-26T10:30:45.123Z"
}
```

---

## 📊 模組深度說明

### Bridge Service (MQTT ↔ Kafka 橋接)
- **關鍵特性**：
  - MQTT 自動重連機制（5 秒間隔）
  - Kafka 同步發送確保交付
  - 協議轉換（MQTT JSON → Kafka String）
- **故障處理**：連接失敗自動重試，無限次數

### Alert Processor (告警檢測)
- **批量消費**: 一次最多 500 條消息
- **告警邏輯**：
  ```csharp
  溫度 > 30°C → 記錄 WARNING 等級日誌
  ```
- **性能優化**：批處理減少 I/O 次數

### Telemetry Processor (資料持久化)
- **寫入 InfluxDB** 時間序列資料
- **Tag 結構**：設備 ID、位置等可查詢字段
- **Field 結構**：溫度、濕度等度量值
- **時間戳**：精確到毫秒

---


## 📈 性能指標

### 系統容量
| 指標 | 數值 |
|-----|------|
| 系統測試規模 | 50 台模擬設備 |
| 目前上限吞吐 | 約 100 rps |
| !!主要瓶頸 | Bridge Service 為單點橋接 |
| 測試定位 | 架構 Demo（不進行 rps 調校） |
| MQTT 連接重試 | 無限重試 |
| Kafka 連接重試 | 無限重試 |

### 乘載補充
- 本系統現階段重點在事件驅動架構展示，不以極限吞吐優化為目標。
- 以 50 台模擬設備測試時，整體上限約為 100 rps。
- 目前可觀察到的主要瓶頸是 Bridge Service 單點處理（MQTT→Kafka 轉換集中於單一服務）。

### 延遲分析
```
IoT 設備發送 → MQTT (~50ms) → Bridge (~10ms) → Kafka (~5ms) 
→ Alert Processor (~100ms) → 總延遲: ~165ms
```

---

## 🎯 系統設計亮點

### 1. 高可用性設計
- **Kafka KRaft 模式**：3 節點集群仲裁，無需 Zookeeper
- **自動重連機制**：Bridge Service MQTT 連接失敗自動重連
- **多消費者群組**：業務邏輯解耦，互不影響

### 2. 可擴展性考慮
- **橫向擴展**：Kafka 增加分區 → 並行消費者
- **設備擴展**：設備模擬器支持編程調整設備數量
- **存儲擴展**：InfluxDB 支持分級存儲和遠程備份

### 3. 可觀測性
- **Kafka UI**：實時監控消息流量
- **Grafana 儀表板**：溫度趨勢可視化
- **構化日誌**：各服務輸出結構化日誌便於調試

---

## 🛠️ 故障排除

### Bridge Service 無法連接 MQTT
```bash
# 檢查 EMQX 狀態
docker-compose logs emqx

# 驗證連接
docker exec bridge-service ping emqx
```

### Kafka 消息未出現
```bash
# 進入 Kafka 容器檢查 broker
docker exec kafka kafka-broker-api-versions.sh \
  --bootstrap-server kafka:9092

# 查看 topic 詳情
docker exec kafka kafka-topics.sh \
  --describe --topic iot.telemetry.raw \
  --bootstrap-server kafka:9092
```

### InfluxDB 寫入失敗
```bash
# 檢查 InfluxDB 日誌
docker-compose logs influxdb

# 驗證 bucket 是否存在
docker exec influxdb influx bucket list --token $TOKEN
```

---

## 🎓 核心學習成果

### 架構層面
✅ 理解 MQTT 發布/訂閱模式  
✅ 掌握事件驅動架構設計模式  
✅ Kafka KRaft 集群配置與管理  
✅ 背景服務實現異步消息處理  

### 工程實踐
✅ 多服務協調（Docker Compose）  
✅ 協議轉換中間件開發  (Bridge Service)
✅ 批量數據處理最佳實踐  
✅ 時間序列數據模型設計  

### 遇到的挑戰 & 解決方案
| 挑戰 | 解決方案 |
|-----|---------|
| MQTT 連接不穩定 | 實現指數退避重連機制 |
| Kafka 消息順序性 | 使用單分區 + 同步確認 |
| InfluxDB 連接超時 | 增加連接池和重試邏輯 |
| 容器間通信延遲 | 優化 Docker 網絡配置 |


---

## 🐳 Docker 快速命令

```bash
# 查看所有容器狀態
docker-compose ps

# 查看特定服務日誌
docker-compose logs -f bridge-service
docker-compose logs -f alert-processor
docker-compose logs -f telemetry-processor

# 進入容器 shell
docker exec -it kafka bash
docker exec -it influxdb bash

# 重啟單個服務
docker-compose restart bridge-service

# 清理所有容器和卷
docker-compose down -v

# 重新構建鏡像
docker-compose build --no-cache
```

---

## 備註

**項目重點：**
- ✅ MQTT 發布/訂閱通信實踐
- ✅ Kafka 事件驅動架構設計
- ✅ 後台服務批量消息處理
- ✅ Docker Compose 多服務協調

**未涵蓋範圍：**
- ❌ 高併發優化 (>10K devices)
- ❌ 高可用部署 (HA failover)
- ❌ 消息加密和身份認證
- ❌ 生產環境監控告警


