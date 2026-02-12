# Gateway Observability Guide

## 빠른 시작

### 1. Docker Compose로 전체 스택 실행

```bash
docker-compose up -d
```

이 명령으로 다음 서비스가 시작됩니다:
- Gateway API (포트 5000)
- Gateway UI (포트 5001)
- Prometheus (포트 9090)
- Grafana (포트 3000)
- Kafka, PostgreSQL 등

### 2. Grafana 접속

1. 브라우저에서 `http://localhost:3000` 접속
2. 로그인:
   - Username: `admin`
   - Password: `admin`
3. 대시보드 자동 로드됨 (Gateway 폴더)

### 3. Prometheus 접속

- 브라우저에서 `http://localhost:9090` 접속
- 메트릭 쿼리 예시:
  ```
  kafka_consumer_lag
  pipeline_stage_queue_length
  ```

## 메트릭 엔드포인트

### Gateway API Metrics

- **OpenTelemetry Prometheus Exporter**: `http://localhost:5000/metrics`
- **Prometheus-net Server**: `http://localhost:9090/metrics` (별도 포트)

### 주요 메트릭

#### Kafka 메트릭
- `kafka_consumer_lag`: Consumer Group별 Lag
- `kafka_messages_processed_total`: 처리된 메시지 수
- `kafka_processing_duration_seconds`: 처리 지연 시간

#### Pipeline 메트릭
- `pipeline_stage_queue_length`: Stage별 큐 길이
- `pipeline_processing_duration_seconds`: 처리 지연 시간
- `pipeline_ingested_total`: 수집된 이벤트 수

#### SignalR 메트릭
- `signalr_messages_sent_total`: 전송된 메시지 수
- `signalr_send_latency_seconds`: 전송 지연 시간

#### MQTT 메트릭
- `mqtt_messages_ingested_total`: 수집된 메시지 수
- `mqtt_ingest_latency_seconds`: 수집 지연 시간

## PromQL 쿼리 예시

### Consumer Lag 합계
```promql
sum(kafka_consumer_lag) by (consumer_group)
```

### Pipeline 처리량 (이벤트/초)
```promql
rate(pipeline_ingested_total[5m])
```

### Pipeline 큐 백프레셔 감지
```promql
pipeline_stage_queue_length > 800
```

### SignalR 전송 지연 (P95)
```promql
histogram_quantile(0.95, signalr_send_latency_seconds_bucket)
```

## 알람 설정

알람 규칙은 `prometheus/alerts.yml`에 정의되어 있습니다.

주요 알람:
- **HighKafkaConsumerLag**: Lag > 10,000 (5분)
- **CriticalKafkaConsumerLag**: Lag > 50,000 (2분)
- **PipelineQueueBackpressure**: Queue Length > 800 (3분)

## 대시보드

### 1. Gateway System Overview
- 전체 시스템 처리량
- Pipeline Stage별 상태
- Kafka Consumer Lag 요약

### 2. Kafka Consumer Lag
- Consumer Group별 Lag 상세
- Partition별 Lag
- 처리량 및 지연 시간

### 3. Pipeline Backpressure
- Stage별 큐 길이
- 처리 지연 시간
- 드롭률

### 4. SignalR 실시간 전송
- 전송 지연 시간
- 전송량
- 연결된 클라이언트 수

### 5. PostgreSQL 성능
- 쿼리 실행 시간
- Insert 처리량
- 연결 풀 상태

## 문제 해결

### 메트릭이 표시되지 않을 때

1. **Prometheus가 Gateway API를 스크랩하는지 확인**
   ```bash
   # Prometheus UI에서 Status > Targets 확인
   http://localhost:9090/targets
   ```

2. **Gateway API 메트릭 엔드포인트 확인**
   ```bash
   curl http://localhost:5000/metrics
   ```

3. **로그 확인**
   ```bash
   docker logs gateway-api
   ```

### Kafka Lag이 계산되지 않을 때

1. **KafkaLagMetrics 서비스가 실행 중인지 확인**
   - 로그에서 "Registered consumer group" 메시지 확인

2. **Kafka 연결 확인**
   - `Kafka__BootstrapServers` 환경 변수 확인
   - Azure Event Hubs 사용 시 Connection String 확인

3. **AdminClient 권한 확인**
   - Kafka Broker에 Metadata 조회 권한 필요

## 상세 문서

- [Observability Architecture](./docs/OBSERVABILITY_ARCHITECTURE.md): 전체 아키텍처 설명
- [Prometheus Configuration](./prometheus/prometheus.yml): Prometheus 설정
- [Alert Rules](./prometheus/alerts.yml): 알람 규칙

