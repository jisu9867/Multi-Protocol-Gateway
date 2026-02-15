# Prometheus Metrics Design Guide
## Gateway API - Smart Factory Monitoring

### 목차
1. [NoData 원인 분석 및 해결](#nodata-원인-분석-및-해결)
2. [메트릭 설계 원칙](#메트릭-설계-원칙)
3. [Label 설계 가이드](#label-설계-가이드)
4. [PromQL 예시](#promql-예시)
5. [Cardinality 폭발 방지](#cardinality-폭발-방지)

---

## NoData 원인 분석 및 해결

### 원인 A: `sum by(...)` 때문에 label이 사라지는 경우

**문제:**
```promql
# 잘못된 쿼리
sum by(factory_id) (rate(pipeline_persisted_total[5m]))
```
- `pipeline_persisted_total`에 `factory_id` 레이블이 없으면 결과가 비어있음
- `factory_id="unknown"`만 있는 경우, `sum by(factory_id)`는 작동하지만 다른 레이블이 사라짐

**해결:**
```promql
# 올바른 쿼리 - 모든 레이블 유지
sum(rate(pipeline_persisted_total[5m]))

# 또는 factory_id별로 그룹핑하려면
sum by(factory_id) (rate(pipeline_persisted_total[5m])) or vector(0)
```

**실제 적용:**
- `pipeline_persisted_total`는 현재 `factory_id="unknown"`만 있음
- 따라서 `sum()`을 사용하여 모든 레이블 집계
- 향후 `factory_id`별 추적이 추가되면 `sum by(factory_id)` 사용 가능

---

### 원인 B: `rate()` 구간이 너무 짧은 경우

**문제:**
```promql
# 잘못된 쿼리 - scrape interval보다 짧음
rate(pipeline_persisted_total[10s])  # scrape_interval=15s인 경우
```
- Prometheus scrape interval (15s)보다 짧은 구간 사용
- 데이터 포인트가 부족하여 rate 계산 불가

**해결:**
```promql
# 올바른 쿼리 - scrape interval의 2-4배 사용
rate(pipeline_persisted_total[1m])   # 최소 1분
rate(pipeline_persisted_total[5m])   # 권장: 5분
```

**실제 적용:**
- 현재 scrape_interval: 10s (gateway-api-net)
- 최소 구간: 30s (3배)
- 권장 구간: 5m (안정적인 rate 계산)

---

### 원인 C: Scrape Interval 문제

**문제:**
- Prometheus가 메트릭을 수집하지 못함
- `/metrics-net` 엔드포인트 접근 불가
- 네트워크 문제 또는 서비스 다운

**해결:**
1. Prometheus Targets 확인:
   ```
   http://localhost:9090/targets
   ```
   - `gateway-api-net` job이 UP 상태인지 확인

2. 메트릭 엔드포인트 직접 확인:
   ```bash
   curl http://localhost:5000/metrics-net | grep pipeline_persisted_total
   ```

3. Prometheus 설정 확인:
   ```yaml
   scrape_configs:
     - job_name: 'gateway-api-net'
       scrape_interval: 10s
       metrics_path: '/metrics-net'
       static_configs:
         - targets: ['api:8080']
   ```

---

### 원인 D: Metric이 조건부로만 생성되는 경우

**문제:**
```csharp
// 조건부로만 메트릭 생성
if (persistedDelta > 0)
{
    PipelinePersistedTotal.WithLabels("unknown").Inc(persistedDelta);
}
```
- `persistedDelta`가 0이면 메트릭이 생성되지 않음
- Prometheus는 존재하지 않는 메트릭을 쿼리할 수 없음

**해결:**
```promql
# or vector(0)을 사용하여 메트릭이 없을 때 0 반환
sum(rate(pipeline_persisted_total[5m])) or vector(0)

# 또는 clamp_min을 사용
clamp_min(sum(rate(pipeline_persisted_total[5m])), 0)
```

**실제 적용:**
- `PipelineMetricsExporter`는 5초마다 업데이트
- 데이터가 없으면 메트릭이 생성되지 않을 수 있음
- Grafana 쿼리에 `or vector(0)` 추가 권장

---

### 원인 E: Label Mismatch (factory_id만 있는데 line_id로 group하는 경우)

**문제:**
```promql
# 잘못된 쿼리 - line_id 레이블이 없음
sum by(factory_id, line_id) (rate(pipeline_persisted_total[5m]))
```
- `pipeline_persisted_total`에 `line_id` 레이블이 없으면 결과가 비어있음

**해결:**
```promql
# 현재 메트릭 구조에 맞는 쿼리
sum by(factory_id) (rate(pipeline_persisted_total[5m]))

# line_id가 추가된 메트릭 (SignalR)의 경우
sum by(factory_id, line_id) (rate(signalr_messages_sent_total[5m]))
```

**실제 적용:**
- `pipeline_*` 메트릭: `factory_id`만 지원 (향후 `line_id` 추가 예정)
- `signalr_*` 메트릭: `factory_id` + `line_id` 지원

---

## 메트릭 설계 원칙

### 1. Counter 설계

**원칙:**
- 항상 증가하는 값만 사용
- `rate()` 또는 `increase()`로 사용
- 레이블은 제한적으로 사용 (cardinality 방지)

**예시:**
```csharp
// Counter 생성
private static readonly Counter PipelinePersistedTotal = Metrics.CreateCounter(
    "pipeline_persisted_total",
    "Total number of events persisted to database",
    new[] { "factory_id", "line_id" }); // 최대 2-3개 레이블 권장

// 사용
PipelinePersistedTotal.WithLabels("ulsan", "line1").Inc();
```

**PromQL:**
```promql
# 초당 insert 수
sum(rate(pipeline_persisted_total[5m]))

# factory_id별
sum by(factory_id) (rate(pipeline_persisted_total[5m]))

# factory_id + line_id별
sum by(factory_id, line_id) (rate(pipeline_persisted_total[5m]))
```

---

### 2. Histogram 설계

**원칙:**
- 분포를 측정할 때 사용 (latency, duration)
- Bucket 설정은 도메인에 맞게 조정
- 레이블은 제한적으로 사용

**예시:**
```csharp
// Histogram 생성
private static readonly Histogram SignalRSendLatency = Metrics.CreateHistogram(
    "signalr_send_latency_seconds",
    "SignalR message send latency in seconds",
    new[] { "factory_id", "line_id", "tag" },
    new HistogramConfiguration
    {
        Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0 }
    });

// 사용
SignalRSendLatency.WithLabels("ulsan", "line1", "temp").Observe(0.023);
```

**PromQL:**
```promql
# P95 latency
histogram_quantile(0.95, 
    sum(rate(signalr_send_latency_seconds_bucket[5m])) by (le, factory_id, line_id)
)

# P99 latency by factory + line
histogram_quantile(0.99,
    sum(rate(signalr_send_latency_seconds_bucket[5m])) by (le, factory_id, line_id)
)
```

---

### 3. Gauge 설계

**원칙:**
- 현재 상태를 나타내는 값 (queue length, connected clients)
- 증가/감소 가능
- 레이블은 제한적으로 사용

**예시:**
```csharp
// Gauge 생성
private static readonly Gauge PipelineStageQueueLength = Metrics.CreateGauge(
    "pipeline_stage_queue_length",
    "Current queue length for each pipeline stage",
    new[] { "stage" });

// 사용
PipelineStageQueueLength.WithLabels("ingest").Set(42);
```

**PromQL:**
```promql
# 현재 queue length
pipeline_stage_queue_length

# stage별 평균
avg by(stage) (pipeline_stage_queue_length)
```

---

## Label 설계 가이드

### 권장 레이블 조합

**SignalR 메트릭:**
```csharp
new[] { "factory_id", "line_id", "tag", "status" }
```
- `factory_id`: 공장 식별자 (3-5개 값)
- `line_id`: 라인 식별자 (3-10개 값)
- `tag`: 센서 타입 (5-10개 값)
- `status`: success/error (2개 값)
- **예상 cardinality: 3 × 10 × 10 × 2 = 600** (안전)

**Pipeline 메트릭:**
```csharp
new[] { "factory_id", "line_id" }
```
- `factory_id`: 공장 식별자 (3-5개 값)
- `line_id`: 라인 식별자 (3-10개 값)
- **예상 cardinality: 3 × 10 = 30** (매우 안전)

### 레이블 값 검증

**안전한 레이블 값:**
- 알파벳, 숫자, 언더스코어, 하이픈만 사용
- 최대 길이: 50자
- 소문자로 정규화

**위험한 레이블 값:**
- 사용자 입력 (cardinality 폭발)
- 타임스탬프 (매우 높은 cardinality)
- UUID (매우 높은 cardinality)

---

## PromQL 예시

### Factory + Line 단위 그룹핑

```promql
# SignalR 메시지 전송률 (factory + line별)
sum by(factory_id, line_id) (
    rate(signalr_messages_sent_total{status="success"}[5m])
)

# Legend 포맷: {{factory_id}} - {{line_id}}
# Grafana에서 설정: Legend → Custom → {{factory_id}} - {{line_id}}
```

### Component별 그룹핑

```promql
# 모든 pipeline 메트릭을 component별로 그룹핑
sum by(factory_id, line_id, component) (
    rate(pipeline_persisted_total[5m])
) * on(component) group_left() 
  {component="api"}
```

### Multi-metric 집계

```promql
# Pipeline throughput (factory + line별)
sum by(factory_id, line_id) (
    rate(pipeline_ingested_total[5m]) or vector(0)
) + 
sum by(factory_id, line_id) (
    rate(pipeline_persisted_total[5m]) or vector(0)
)
```

---

## Cardinality 폭발 방지

### 1. 레이블 개수 제한
- **최대 레이블 개수: 4-5개**
- 각 레이블의 고유 값 개수 제한

### 2. 레이블 값 검증
```csharp
// MetricLabelHelper.SanitizeLabelValue() 사용
var safeFactoryId = MetricLabelHelper.SanitizeLabelValue(factoryId);
var safeLineId = MetricLabelHelper.SanitizeLabelValue(lineId);
```

### 3. Unknown 값 사용
```csharp
// 패턴 매칭 실패 시 "unknown" 반환
var lineId = MetricLabelHelper.ExtractLineId(sourceId);
// "unknown"이 반환되면 cardinality가 제한됨
```

### 4. 메트릭 생성 제한
- 조건부 메트릭 생성 최소화
- 항상 동일한 레이블 조합 사용

### 5. 모니터링
```promql
# Prometheus에서 cardinality 모니터링
count by(__name__) ({__name__=~"signalr_.*"})
```

---

## 실제 적용 예시

### SignalR 메트릭 (수정 완료)

```csharp
// SignalRMetrics.cs
private static readonly Counter SignalRMessagesSentTotal = Metrics.CreateCounter(
    "signalr_messages_sent_total",
    "Total number of SignalR messages sent",
    new[] { "factory_id", "line_id", "tag", "status" });

// 사용
var factoryId = telemetryEvent.FactoryId.ToString();
var lineId = MetricLabelHelper.ExtractLineId(telemetryEvent.SourceId);
SignalRMetrics.RecordMessageSent(factoryId, lineId, telemetryEvent.Tag, duration);
```

### Grafana 쿼리

```promql
# Insert Throughput (factory + line별)
sum by(factory_id, line_id) (
    rate(pipeline_persisted_total[5m])
) or vector(0)
```

**Legend 설정:**
- Format: `{{factory_id}} - {{line_id}}`
- 또는: `${__field.labels.factory_id} - ${__field.labels.line_id}`

---

## 체크리스트

### NoData 문제 해결 체크리스트

- [ ] Prometheus Targets에서 `gateway-api-net` job이 UP인지 확인
- [ ] `/metrics-net` 엔드포인트에서 메트릭이 노출되는지 확인
- [ ] `rate()` 구간이 scrape_interval의 2-4배인지 확인 (최소 1분)
- [ ] `sum()` 또는 `sum by(...)` 사용 시 레이블이 존재하는지 확인
- [ ] 메트릭이 조건부로만 생성되는 경우 `or vector(0)` 추가
- [ ] Label mismatch 확인 (쿼리의 레이블이 메트릭에 존재하는지)

### 메트릭 설계 체크리스트

- [ ] 레이블 개수가 4-5개 이하인지 확인
- [ ] 각 레이블의 고유 값 개수가 제한되어 있는지 확인
- [ ] 레이블 값이 검증/정규화되는지 확인
- [ ] Unknown 값이 적절히 사용되는지 확인
- [ ] Cardinality 예상치가 1000 이하인지 확인

---

## 참고 자료

- [Prometheus Best Practices](https://prometheus.io/docs/practices/naming/)
- [Prometheus Label Best Practices](https://prometheus.io/docs/practices/instrumentation/#use-labels)
- [Cardinality Explosion](https://prometheus.io/docs/practices/instrumentation/#cardinality)

