# Gateway Observability Architecture

## 개요

Gateway 플랫폼은 OpenTelemetry + Prometheus + Grafana를 활용한 완전한 관측성(Observability) 시스템을 제공합니다. 이 문서는 운영 환경에서 바로 사용 가능한 관측성 아키텍처를 설명합니다.

## 아키텍처 다이어그램

```
┌─────────────────────────────────────────────────────────────────┐
│                    Gateway Application                          │
│                                                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐         │
│  │ MQTT Adapter │  │   Pipeline   │  │ Kafka Prod/  │         │
│  │              │→ │   Stages     │→ │ Consumer     │         │
│  └──────────────┘  └──────────────┘  └──────────────┘         │
│         │                 │                  │                 │
│         │                 │                  │                 │
│         ▼                 ▼                  ▼                 │
│  ┌──────────────────────────────────────────────────────┐      │
│  │         OpenTelemetry SDK                           │      │
│  │  - Tracing (ActivitySource)                         │      │
│  │  - Metrics (Prometheus Exporter)                    │      │
│  │  - Logs (Serilog Integration)                       │      │
│  └──────────────────────────────────────────────────────┘      │
│         │                 │                  │                 │
└─────────┼─────────────────┼──────────────────┼─────────────────┘
          │                 │                  │
          ▼                 ▼                  ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Prometheus (Pull)                            │
│  - Scrapes /metrics endpoint every 10s                         │
│  - Stores time-series data                                     │
│  - Evaluates alert rules                                       │
└─────────────────────────────────────────────────────────────────┘
          │
          ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Grafana                                      │
│  - Visualizes Prometheus metrics                                │
│  - Pre-configured dashboards                                    │
│  - Alert notifications                                          │
└─────────────────────────────────────────────────────────────────┘
```

## 1. OpenTelemetry 적용 전략

### 1.1 Tracing (분산 추적)

**구현 방식:**
- .NET 8의 `System.Diagnostics.ActivitySource` 사용
- OpenTelemetry SDK로 자동 계측 + 수동 계측 병행

**계측 포인트:**

1. **ASP.NET Core 요청**
   - 자동 계측: `AddAspNetCoreInstrumentation()`
   - HTTP 요청/응답 추적
   - 예외 자동 기록

2. **Kafka Producer/Consumer**
   - 수동 계측: `ActivitySource.StartActivity()`
   - 메시지 발행/소비 지연 시간 측정
   - Consumer Group별 추적

3. **MQTT 수집**
   - 수동 계측: `MqttAdapter`에서 Activity 생성
   - 메시지 수신부터 파이프라인 전달까지 추적

4. **Pipeline Stages**
   - Channel 기반 각 Stage 처리 시간 측정
   - Ingest → Normalize → Route 흐름 추적

5. **SignalR 메시지 전송**
   - 실시간 스트리밍 지연 시간 계측
   - Factory/Tag별 그룹 전송 추적

6. **EF Core / PostgreSQL**
   - 자동 계측: `AddEntityFrameworkCoreInstrumentation()`
   - 쿼리 실행 시간 및 SQL 문 추적

**Trace Context 전파:**
- `TelemetryEvent.TraceId` 필드에 Activity ID 저장
- Kafka 메시지 헤더에 Trace Context 전파 (향후 확장)

### 1.2 Metrics (메트릭)

**구현 방식:**
- OpenTelemetry Metrics API + Prometheus Exporter
- `prometheus-net` 라이브러리로 커스텀 메트릭 추가

**메트릭 카테고리:**

1. **Kafka 메트릭**
   - `kafka_consumer_lag`: Consumer Group별 Lag
   - `kafka_messages_processed_total`: 처리된 메시지 수
   - `kafka_processing_duration_seconds`: 처리 지연 시간
   - `kafka_producer_messages_total`: 발행된 메시지 수

2. **SignalR 메트릭**
   - `signalr_messages_sent_total`: 전송된 메시지 수
   - `signalr_send_latency_seconds`: 전송 지연 시간
   - `signalr_connected_clients`: 연결된 클라이언트 수

3. **MQTT 메트릭**
   - `mqtt_messages_ingested_total`: 수집된 메시지 수
   - `mqtt_ingest_latency_seconds`: 수집 지연 시간

4. **Pipeline 메트릭**
   - `pipeline_stage_queue_length`: Stage별 큐 길이
   - `pipeline_processing_duration_seconds`: 처리 지연 시간
   - `pipeline_ingested_total`: 수집된 이벤트 수
   - `pipeline_persisted_total`: 저장된 이벤트 수

**라벨 설계:**
- `factory_id`: 공장 ID (Ulsan, Asan, Jeonju, Hwaseong)
- `consumer_group`: Kafka Consumer Group ID
- `topic`: Kafka Topic 이름
- `partition`: Kafka Partition 번호
- `tag`: 센서 태그 (temp, humidity, power 등)
- `stage`: Pipeline Stage 이름

**Cardinality 제어:**
- 고유 값이 많은 라벨(예: `event_id`)은 사용하지 않음
- 집계 가능한 라벨만 사용 (factory_id, tag, consumer_group 등)

### 1.3 Logs (로그)

**구조화 로깅:**
- Serilog 사용
- OpenTelemetry Log Exporter (향후 확장 가능)
- Trace ID를 로그에 포함하여 상관관계 유지

## 2. Kafka Consumer Lag 계측 방식

### 2.1 Lag 계산 원리

**공식:**
```
Lag = High Watermark (Latest Offset) - Committed Offset
```

**구현:**
1. AdminClient로 Topic Metadata 조회
2. 각 Partition의 High Watermark 조회 (`QueryWatermarkOffsets`)
3. Consumer Group의 Committed Offset 조회 (`Committed`)
4. Lag = High Watermark - Committed Offset

**주기:**
- 10초마다 업데이트
- `KafkaLagMetrics` 서비스가 백그라운드에서 실행

### 2.2 Azure Event Hubs 호환성

**고려사항:**
- Event Hubs는 Kafka 호환 API 제공
- SASL_SSL 인증 사용
- Connection String에서 Bootstrap Servers 추출

**구현:**
- `EventHubsHelper`로 Connection String 파싱
- AdminClientConfig에 Event Hubs 설정 적용
- 동일한 로직으로 Lag 계산

### 2.3 메트릭 노출

```promql
# Consumer Group별 Lag
kafka_consumer_lag{consumer_group="gateway-consumer-group", topic="telemetry-events", partition="0"}

# 전체 Lag 합계
sum(kafka_consumer_lag) by (consumer_group)
```

## 3. Prometheus & Grafana 구성

### 3.1 Prometheus 설정

**Scrape Config:**
- Gateway API: `http://api:8080/metrics` (10초 간격)
- Prometheus Metrics Server: `http://api:9090/metrics` (10초 간격)

**Storage:**
- 30일 보관 기간
- TSDB 압축 활성화

**Alert Rules:**
- `prometheus/alerts.yml`에 정의
- Kafka Lag, Pipeline Backpressure 등 모니터링

### 3.2 Grafana 설정

**Datasource:**
- Prometheus 자동 프로비저닝
- URL: `http://prometheus:9090`

**Dashboards:**
- 자동 로드: `grafana/dashboards/` 디렉토리
- 5개 주요 대시보드 제공

## 4. 추천 대시보드 및 알람 설계

### 4.1 대시보드 구성

#### 1. System Overview
- 전체 처리량 (Ingested/Persisted)
- Pipeline Stage별 처리량
- 에러율
- 시스템 가동률

#### 2. Kafka Consumer Lag
- Consumer Group별 Lag (그래프)
- Partition별 Lag 상세
- Lag 증가율
- Consumer 처리량

#### 3. Pipeline Backpressure
- Stage별 큐 길이 (게이지)
- 큐 길이 트렌드
- 처리 지연 시간 (P50, P95, P99)
- 드롭률

#### 4. SignalR 실시간 전송
- 전송 지연 시간 (P95)
- 전송량 (메시지/초)
- 연결된 클라이언트 수
- 에러율

#### 5. PostgreSQL 성능
- 쿼리 실행 시간
- 연결 풀 상태
- Insert 처리량
- 쿼리 에러율

### 4.2 알람 규칙

**Critical 알람:**
- Kafka Consumer Lag > 50,000 (2분)
- Pipeline Queue Length > 950 (1분)
- Kafka Processing Duration P95 > 5초 (5분)

**Warning 알람:**
- Kafka Consumer Lag > 10,000 (5분)
- Pipeline Queue Length > 800 (3분)
- SignalR Send Latency P95 > 0.5초 (5분)
- MQTT Ingest Latency P95 > 0.1초 (5분)

## 5. 운영 고려사항

### 5.1 성능 영향

**최소화 전략:**
- Sampling Rate: 100% (필요시 조정)
- 메트릭 수집 주기: 10초 (조정 가능)
- Lag 계산 주기: 10초 (백그라운드)

**예상 오버헤드:**
- CPU: < 2%
- Memory: < 50MB
- Network: < 1MB/min

### 5.2 확장성

**메트릭 카디널리티:**
- 현재 설계: ~1000 시계열
- 예상 증가: Consumer Group당 +10 시계열

**제한:**
- Prometheus 권장: < 10,000 시계열/인스턴스
- 현재 시스템: 충분한 여유

### 5.3 보안

**프로덕션 권장사항:**
- Prometheus/Grafana 인증 활성화
- 네트워크 격리 (VPC/Private Network)
- TLS 암호화 (HTTPS)

## 6. 메트릭 정의 이유

### 6.1 Kafka Consumer Lag
**이유:** Consumer가 Producer를 따라가지 못할 때 데이터 손실 위험
**임계값:** Lag > 10,000 (Warning), > 50,000 (Critical)

### 6.2 Pipeline Queue Length
**이유:** Backpressure 감지, 시스템 병목 식별
**임계값:** Queue Length > 800 (80% capacity)

### 6.3 SignalR Send Latency
**이유:** 실시간 스트리밍 품질 모니터링
**임계값:** P95 > 0.5초 (사용자 경험 저하)

### 6.4 MQTT Ingest Latency
**이유:** 센서 데이터 수집 지연 모니터링
**임계값:** P95 > 0.1초 (실시간 요구사항)

## 7. Trade-offs

### 7.1 Sampling Rate
**선택:** 100% Sampling
**이유:** 운영 환경에서 완전한 추적 필요
**대안:** 필요시 10% Sampling으로 CPU 사용량 감소 가능

### 7.2 Lag 계산 주기
**선택:** 10초
**이유:** 실시간 모니터링과 성능 균형
**대안:** 5초 (더 빠른 감지, 더 높은 부하)

### 7.3 메트릭 라벨
**선택:** factory_id, tag, consumer_group 등
**이유:** 집계 가능하면서도 상세 분석 가능
**대안:** event_id 추가 시 카디널리티 폭증 주의

## 8. 향후 개선 사항

1. **Jaeger/Zipkin 통합:** 분산 추적 시각화
2. **Log Exporter:** OpenTelemetry Log Exporter 추가
3. **Custom Metrics:** 비즈니스 메트릭 추가 (예: Factory별 OEE)
4. **Alertmanager:** 알람 통합 관리
5. **Tracing Context 전파:** Kafka 헤더에 Trace Context 추가

