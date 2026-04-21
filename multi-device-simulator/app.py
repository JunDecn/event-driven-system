import paho.mqtt.client as mqtt
import json
import random
import time
from datetime import datetime

# 設定
BROKER = "emqx"
PORT = 1883
DEVICE_COUNT = 50

clients = []

def create_client(i):
    client_id = f"device-{i:03d}"
    client = mqtt.Client(client_id=client_id)
    client.connect(BROKER, PORT)
    return client

# 初始化 50 個設備連線
for i in range(DEVICE_COUNT):
    clients.append(create_client(i))

print(f"🚀 已啟動 {DEVICE_COUNT} 個模擬設備...")

try:
    while True:
        for i, client in enumerate(clients):
            device_id = f"device-{i:03d}"
            # 產生動態 Payload
            payload = {
                "temperature": round(random.uniform(20.0, 35.0), 2), # 隨機溫度 20~35
                "humidity": random.randint(50, 80),
                "timestamp": datetime.utcnow().isoformat(timespec="milliseconds") + "Z"    # 當前 ISO 時間
            }
            
            topic = f"devices/{device_id}/telemetry"
            client.publish(topic, json.dumps(payload))
            
        time.sleep(1) # 每秒發送一次
except KeyboardInterrupt:
    print("停止模擬")