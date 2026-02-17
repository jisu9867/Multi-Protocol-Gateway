# OpenTelemetry 메트릭 통일 검증 가이드

## 변경 사항 요약

1. **prometheus-net 제거**: 모든 커스텀 메트릭을 OpenTelemetry Meter 기반으로 전환
2. **단일 엔드포인트**: `/metrics` 엔드포인트만 사용 (OTel Prometheus Exporter)
3. **메트릭 이름 유지**: 기존 메트릭 이름(`signalr_*`, `kafka_*`, `pipeline_*` 등)은 그대로 유지

## 검증 체크리스트

### 1. 애플리케이션 재시작
```powershell
# Gateway API 애플리케이션을 재시작하세요
# Docker Compose를 사용하는 경우:
docker-compose restart api

# 또는 직접 실행하는 경우:
# 애플리케이션을 중지하고 다시 시작
```

### 2. 메트릭 엔드포인트 확인

#### 2.1 단일 엔드포인트 확인
```powershell
# /metrics 엔드포인트가 존재하는지 확인
Invoke-WebRequest -Uri "http://localhost:5000/metrics" -UseBasicParsing | Select-Object StatusCode

# /metrics-net 엔드포인트가 제거되었는지 확인 (404가 나와야 함)
Invoke-WebRequest -Uri "http://localhost:5000/metrics-net" -UseBasicParsing
# 예상: 404 Not Found
```

#### 2.2 SignalR 메트릭 확인
```powershell
# SignalR 메트릭이 /metrics 엔드포인트에 노출되는지 확인
$response = Invoke-WebRequest -Uri "http://localhost:5000/metrics" -UseBasicParsing
$response.Content | Select-String -Pattern "signalr_" -CaseSensitive:$false

# 예상 출력 예시:
# signalr_messages_sent_total{factory_id="ulsan",line_id="line1",tag="temp",status="success"} 1
# signalr_send_latency_seconds_bucket{factory_id="ulsan",line_id="line1",tag="temp",le="0.001"} 0
# signalr_connected_clients{factory_id="ulsan",line_id="line1",tag="temp"} 0
```

#### 2.3 모든 커스텀 메트릭 확인
```powershell
# SignalR 메트릭
$response.Content | Select-String -Pattern "signalr_" -CaseSensitive:$false

# Kafka 메트릭
$response.Content | Select-String -Pattern "kafka_" -CaseSensitive:$false

# Pipeline 메트릭
$response.Content | Select-String -Pattern "pipeline_" -CaseSensitive:$false

# MQTT 메트릭
$response.Content | Select-String -Pattern "mqtt_" -CaseSensitive:$false
```

### 3. Prometheus 설정 확인

#### 3.1 Prometheus 설정 파일 확인
```yaml
# prometheus/prometheus.yml 파일 확인
# 다음 내용이 있어야 함:
scrape_configs:
  - job_name: 'gateway-api'
    scrape_interval: 10s
    metrics_path: '/metrics'
    static_configs:
      - targets: ['api:8080']
```

#### 3.2 Prometheus가 스크랩하는지 확인
```powershell
# Prometheus UI에서 확인:
# 1. http://localhost:9090 접속
# 2. Status > Targets 메뉴로 이동
# 3. gateway-api job이 UP 상태인지 확인
# 4. Last Scrape 시간이 최근인지 확인
```

### 4. Prometheus 쿼리 검증

#### 4.1 SignalR 메트릭 쿼리
```promql
# 모든 SignalR 메트릭 조회
{__name__=~"signalr_.*"}

# 메시지 전송률
sum by(factory_id, line_id) (rate(signalr_messages_sent_total{status="success"}[5m]))

# 전송 지연시간 (P95)
histogram_quantile(0.95, sum(rate(signalr_send_latency_seconds_bucket[5m])) by (le, factory_id, line_id))

# 연결된 클라이언트 수
sum(signalr_connected_clients)
```

#### 4.2 Kafka 메트릭 쿼리
```promql
# Kafka 메시지 처리율
sum by(consumer_group, topic) (rate(kafka_messages_processed_total{status="success"}[5m]))

# Kafka 처리 지연시간
histogram_quantile(0.95, sum(rate(kafka_processing_duration_seconds_bucket[5m])) by (le, consumer_group, topic))
```

#### 4.3 Pipeline 메트릭 쿼리
```promql
# Pipeline 이벤트 처리율
sum(rate(pipeline_ingested_total[5m]))
sum(rate(pipeline_persisted_total[5m]))
```

### 5. Grafana 대시보드 확인

#### 5.1 SignalR 대시보드
- Grafana에서 "SignalR 실시간 전송" 대시보드 열기
- 모든 패널에 데이터가 표시되는지 확인
- `{{factory_id}} - {{line_id}}` 형식으로 레전드가 표시되는지 확인

#### 5.2 쿼리 예시 (Grafana 패널)
```promql
# Messages Sent Rate
sum by(factory_id, line_id) (rate(signalr_messages_sent_total{status="success"}[5m]))

# Send Latency (P95/P99)
histogram_quantile(0.95, sum(rate(signalr_send_latency_seconds_bucket[5m])) by (le, factory_id, line_id))
histogram_quantile(0.99, sum(rate(signalr_send_latency_seconds_bucket[5m])) by (le, factory_id, line_id))

# Total Messages Sent
sum(signalr_messages_sent_total{status="success"})

# Active Connections
sum(signalr_connected_clients)
```

### 6. 문제 해결

#### 6.1 메트릭이 보이지 않는 경우
1. **애플리케이션 로그 확인**
   ```powershell
   # 로그에서 다음 메시지 확인:
   # "OpenTelemetry Prometheus exporter available at /metrics endpoint"
   # "Custom metrics (SignalR, Kafka, Pipeline, MQTT) are registered via OTel Meter"
   ```

2. **Meter 등록 확인**
   - Program.cs에서 다음 Meter가 등록되어 있는지 확인:
     - `Gateway.SignalR`
     - `Gateway.Kafka`
     - `Gateway.Kafka.Lag`
     - `Gateway.Pipeline`
     - `Gateway.MQTT`

3. **메트릭이 실제로 기록되는지 확인**
   - SignalR 메시지가 전송되는지 확인
   - Kafka 메시지가 처리되는지 확인
   - Pipeline 이벤트가 처리되는지 확인

#### 6.2 Prometheus에서 메트릭을 찾을 수 없는 경우
1. **Prometheus 타겟 상태 확인**
   - Prometheus UI > Status > Targets
   - `gateway-api` job이 UP 상태인지 확인
   - Last Scrape 시간이 최근인지 확인

2. **스크랩 경로 확인**
   - `metrics_path: '/metrics'`가 올바른지 확인
   - `targets: ['api:8080']`가 올바른지 확인 (Docker 환경)

3. **Prometheus 재시작**
   ```powershell
   docker-compose restart prometheus
   ```

#### 6.3 메트릭 이름이 다른 경우
- OTel Meter로 변환했지만 메트릭 이름은 그대로 유지했습니다
- 만약 메트릭 이름이 다르다면, Prometheus에서 실제 메트릭 이름을 확인:
  ```promql
  {__name__=~".*signalr.*"}
  ```

## 성공 기준

✅ `/metrics` 엔드포인트에서 SignalR 메트릭이 노출됨  
✅ `/metrics-net` 엔드포인트가 제거됨 (404)  
✅ Prometheus에서 `{__name__=~"signalr_.*"}` 쿼리로 메트릭 조회 가능  
✅ Grafana 대시보드에 데이터가 표시됨  
✅ 모든 커스텀 메트릭 (SignalR, Kafka, Pipeline, MQTT)이 `/metrics` 엔드포인트에 노출됨  

## 추가 참고사항

### 메트릭 이름 매핑

OTel Meter로 변환했지만 Prometheus 메트릭 이름은 그대로 유지됩니다:

| OTel Meter | Prometheus 메트릭 이름 |
|------------|----------------------|
| `Gateway.SignalR` | `signalr_messages_sent_total`, `signalr_send_latency_seconds`, `signalr_connected_clients` |
| `Gateway.Kafka` | `kafka_messages_processed_total`, `kafka_processing_duration_seconds`, `kafka_producer_messages_total`, `kafka_producer_duration_seconds` |
| `Gateway.Kafka.Lag` | `kafka_consumer_lag`, `kafka_consumer_committed_offset`, `kafka_consumer_high_watermark` |
| `Gateway.Pipeline` | `pipeline_ingested_total`, `pipeline_normalized_total`, `pipeline_routed_total`, `pipeline_persisted_total`, `pipeline_dropped_total`, `pipeline_processing_duration_seconds`, `pipeline_stage_queue_length` |
| `Gateway.MQTT` | `mqtt_messages_ingested_total`, `mqtt_ingest_latency_seconds` |

### Label 이름

OTel에서는 Tag로, Prometheus에서는 Label로 표시됩니다:
- `factory_id`
- `line_id`
- `tag`
- `status`
- `consumer_group`
- `topic`
- `partition`
- `stage`

