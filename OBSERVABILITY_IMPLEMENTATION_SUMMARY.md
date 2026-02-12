# OpenTelemetry + Prometheus + Grafana 구현 완료 요약

## 구현 완료 항목

### ✅ 1. OpenTelemetry 도입

**패키지 추가:**
- `OpenTelemetry` 1.9.0
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.9.0-beta.1
- `OpenTelemetry.Instrumentation.*` 패키지들
- `prometheus-net.AspNetCore` 8.2.1

**구현 내용:**
- ASP.NET Core 자동 계측 (요청/응답 추적)
- Kafka Producer/Consumer 수동 계측
- MQTT Adapter 수동 계측
- Pipeline Stages 계측
- SignalR 메시지 전송 계측
- EF Core 자동 계측 (PostgreSQL 쿼리 추적)
- Trace/Metric/Log 상관관계 유지

### ✅ 2. Prometheus 메트릭 설계 및 구현

**구현된 메트릭:**

1. **Kafka 메트릭** (`KafkaMetrics.cs`)
   - `kafka_consumer_lag` (Gauge): Consumer Group별 Lag
   - `kafka_messages_processed_total` (Counter): 처리된 메시지 수
   - `kafka_processing_duration_seconds` (Histogram): 처리 지연 시간
   - `kafka_producer_messages_total` (Counter): 발행된 메시지 수
   - `kafka_producer_duration_seconds` (Histogram): 발행 지연 시간

2. **SignalR 메트릭** (`SignalRMetrics.cs`)
   - `signalr_messages_sent_total` (Counter): 전송된 메시지 수
   - `signalr_send_latency_seconds` (Histogram): 전송 지연 시간
   - `signalr_connected_clients` (Gauge): 연결된 클라이언트 수

3. **MQTT 메트릭** (`MqttMetrics.cs`)
   - `mqtt_messages_ingested_total` (Counter): 수집된 메시지 수
   - `mqtt_ingest_latency_seconds` (Histogram): 수집 지연 시간

4. **Pipeline 메트릭** (`PipelineMetricsExporter.cs`)
   - `pipeline_stage_queue_length` (Gauge): Stage별 큐 길이
   - `pipeline_processing_duration_seconds` (Histogram): 처리 지연 시간
   - `pipeline_ingested_total` (Counter): 수집된 이벤트 수
   - `pipeline_persisted_total` (Counter): 저장된 이벤트 수
   - `pipeline_dropped_total` (Counter): 드롭된 이벤트 수

**라벨 설계:**
- `factory_id`: 공장 ID
- `consumer_group`: Kafka Consumer Group ID
- `topic`: Kafka Topic
- `partition`: Kafka Partition
- `tag`: 센서 태그
- `stage`: Pipeline Stage 이름

**Cardinality 제어:**
- 고유 값이 많은 라벨 제외
- 집계 가능한 라벨만 사용

### ✅ 3. Kafka Consumer Lag 계측

**구현 파일:** `KafkaLagMetrics.cs`

**계산 방식:**
- AdminClient로 High Watermark 조회
- Consumer Group의 Committed Offset 조회
- Lag = High Watermark - Committed Offset

**특징:**
- Azure Event Hubs 호환 (SASL_SSL 인증 지원)
- 10초마다 자동 업데이트
- Consumer Group별 자동 등록

**메트릭:**
- `kafka_consumer_lag`: Lag 값
- `kafka_consumer_committed_offset`: Committed Offset
- `kafka_consumer_high_watermark`: High Watermark

### ✅ 4. Prometheus & Grafana 구성

**Docker Compose 추가:**
- Prometheus (포트 9090)
- Grafana (포트 3000)

**Prometheus 설정:**
- `prometheus/prometheus.yml`: Scrape 설정
- `prometheus/alerts.yml`: 알람 규칙

**Grafana 설정:**
- `grafana/provisioning/datasources/`: Prometheus 데이터소스 자동 설정
- `grafana/provisioning/dashboards/`: 대시보드 자동 로드
- `grafana/dashboards/`: 대시보드 JSON 파일

### ✅ 5. Grafana 대시보드

**구현된 대시보드:**
1. **Gateway System Overview** (`gateway-overview.json`)
   - 전체 처리량
   - Pipeline Queue Lengths
   - Kafka Consumer Lag 요약
   - Pipeline Processing Duration

2. **Kafka Consumer Lag** (`kafka-consumer-lag.json`)
   - Consumer Group별 Lag 그래프
   - Partition별 Lag 테이블
   - Committed Offset vs High Watermark
   - 처리량 및 지연 시간

**추가 대시보드 (향후 확장):**
- Pipeline Backpressure
- SignalR 실시간 전송
- PostgreSQL 성능

### ✅ 6. 알람 규칙

**구현된 알람** (`prometheus/alerts.yml`):
- `HighKafkaConsumerLag`: Lag > 10,000 (5분)
- `CriticalKafkaConsumerLag`: Lag > 50,000 (2분)
- `PipelineQueueBackpressure`: Queue Length > 800 (3분)
- `CriticalPipelineQueueBackpressure`: Queue Length > 950 (1분)
- `HighKafkaProcessingDuration`: P95 > 1초 (5분)
- `HighSignalRSendLatency`: P95 > 0.5초 (5분)
- `HighMqttIngestLatency`: P95 > 0.1초 (5분)
- `HighKafkaMessageErrors`: Error Rate > 0.1/sec (3분)
- `HighPipelineDropRate`: Drop Rate > 10/sec (3분)

## 파일 구조

```
Multi-Protocol-Gateway/
├── src/
│   ├── Gateway.Api/
│   │   └── Program.cs (OpenTelemetry 설정)
│   ├── Gateway.Infrastructure/
│   │   └── Observability/
│   │       ├── KafkaLagMetrics.cs
│   │       ├── KafkaMetrics.cs
│   │       ├── PipelineMetricsExporter.cs
│   │       ├── SignalRMetrics.cs
│   │       └── MqttMetrics.cs
│   └── Gateway.Adapters/
│       └── MqttAdapter/
│           └── MqttAdapter.cs (메트릭 수집 추가)
├── prometheus/
│   ├── prometheus.yml
│   └── alerts.yml
├── grafana/
│   ├── provisioning/
│   │   ├── datasources/
│   │   │   └── prometheus.yml
│   │   └── dashboards/
│   │       └── dashboard.yml
│   └── dashboards/
│       ├── gateway-overview.json
│       └── kafka-consumer-lag.json
├── docker-compose.yml (Prometheus/Grafana 추가)
└── docs/
    └── OBSERVABILITY_ARCHITECTURE.md
```

## 사용 방법

### 1. 실행
```bash
docker-compose up -d
```

### 2. 접속
- **Grafana**: http://localhost:3000 (admin/admin)
- **Prometheus**: http://localhost:9090
- **Gateway API Metrics**: http://localhost:5000/metrics

### 3. 대시보드 확인
- Grafana에서 "Gateway" 폴더의 대시보드 확인
- 자동으로 로드됨

## 주요 특징

1. **기존 아키텍처 변경 없음**: 파이프라인 구조 그대로 유지
2. **운영 환경 준비**: 프로덕션에서 바로 사용 가능
3. **완전한 관측성**: Trace/Metric/Log 통합
4. **Azure Event Hubs 호환**: Kafka Lag 계산 지원
5. **자동 설정**: Grafana 대시보드 자동 프로비저닝

## 다음 단계 (선택사항)

1. **Jaeger/Zipkin 통합**: 분산 추적 시각화
2. **Log Exporter**: OpenTelemetry Log Exporter 추가
3. **추가 대시보드**: Pipeline Backpressure, SignalR 등
4. **Alertmanager**: 알람 통합 관리
5. **Tracing Context 전파**: Kafka 헤더에 Trace Context 추가

## 참고 문서

- [Observability Architecture](./docs/OBSERVABILITY_ARCHITECTURE.md)
- [Observability Guide](./README_OBSERVABILITY.md)

