# Metrics Implementation Summary
## SignalR Metrics with line_id Label - Implementation Complete

### 구현 완료 사항

#### 1. ✅ line_id 레이블 추가

**파일:**
- `Gateway.Infrastructure/Observability/MetricLabelHelper.cs` (신규)
- `Gateway.Infrastructure/Observability/SignalRMetrics.cs` (수정)
- `Gateway.Api/Services/SignalRTelemetryService.cs` (수정)

**변경 내용:**
- `SignalRMetrics`의 모든 메트릭에 `line_id` 레이블 추가
- `MetricLabelHelper.ExtractLineId()`로 SourceId에서 line_id 추출
- 레이블 값 검증 및 정규화로 cardinality 폭발 방지

**메트릭 구조:**
```csharp
// Before
new[] { "factory_id", "tag", "status" }

// After
new[] { "factory_id", "line_id", "tag", "status" }
```

**예상 Cardinality:**
- factory_id: 3-5개 (Ulsan, Asan, Jeonju)
- line_id: 3-10개 (line1, line2, line3, ...)
- tag: 5-10개 (temp, humidity, pressure, ...)
- status: 2개 (success, error)
- **총 Cardinality: 3 × 10 × 10 × 2 = 600** (안전 범위)

---

#### 2. ✅ NoData 원인 분석 및 해결

**문서:** `docs/METRICS_DESIGN_GUIDE.md`

**주요 원인 및 해결:**

| 원인 | 문제 | 해결 방법 |
|------|------|----------|
| A. `sum by(...)` 레이블 불일치 | 레이블이 없으면 결과 비어있음 | `sum()` 사용 또는 `or vector(0)` 추가 |
| B. `rate()` 구간 너무 짧음 | scrape interval보다 짧으면 계산 불가 | 최소 1분, 권장 5분 구간 사용 |
| C. Scrape Interval 문제 | Prometheus가 메트릭 수집 실패 | Targets 확인, 엔드포인트 확인 |
| D. 조건부 메트릭 생성 | 데이터 없으면 메트릭 생성 안됨 | `or vector(0)` 추가 |
| E. Label Mismatch | 쿼리 레이블이 메트릭에 없음 | 메트릭 구조에 맞는 쿼리 사용 |

**적용된 수정:**
- `pipeline-backpressure.json`: 모든 `rate()` 쿼리에 `sum()` 추가
- `postgresql-performance.json`: 이미 `sum()` 적용됨

---

#### 3. ✅ Grafana 대시보드 수정

**파일:**
- `grafana/dashboards/signalr-realtime.json`
- `grafana/dashboards/pipeline-backpressure.json`

**변경 내용:**

**SignalR 대시보드:**
```promql
# Before
rate(signalr_messages_sent_total[5m])

# After - factory_id + line_id 그룹핑
sum by(factory_id, line_id) (
    rate(signalr_messages_sent_total{status="success"}[5m])
)
# Legend: {{factory_id}} - {{line_id}}
```

**Pipeline Backpressure 대시보드:**
```promql
# Before
rate(pipeline_persisted_total[5m])

# After - sum() 추가로 NoData 방지
sum(rate(pipeline_persisted_total[5m]))
```

---

#### 4. ✅ 메트릭 설계 개선 가이드

**문서:** `docs/METRICS_DESIGN_GUIDE.md`

**주요 내용:**
- Counter/Histogram/Gauge 각각의 설계 원칙
- Label 설계 가이드 (최대 4-5개 레이블)
- Cardinality 폭발 방지 전략
- PromQL 예시 (factory_id + line_id 그룹핑)
- NoData 문제 해결 체크리스트

---

### 코드 예시

#### 1. MetricLabelHelper 사용

```csharp
// SourceId에서 line_id 추출
var lineId = MetricLabelHelper.ExtractLineId("ulsan-line1");
// Returns: "line1"

var lineId2 = MetricLabelHelper.ExtractLineId("asan-line-2");
// Returns: "line-2"

var lineId3 = MetricLabelHelper.ExtractLineId("invalid");
// Returns: "unknown" (cardinality 방지)
```

#### 2. SignalRMetrics 사용

```csharp
// Before
SignalRMetrics.RecordMessageSent(factoryId, tag, duration);

// After
var factoryId = telemetryEvent.FactoryId.ToString();
var lineId = MetricLabelHelper.ExtractLineId(telemetryEvent.SourceId);
SignalRMetrics.RecordMessageSent(factoryId, lineId, tag, duration);
```

#### 3. Prometheus 메트릭 생성 (prometheus-net)

```csharp
// Counter with labels
private static readonly Counter SignalRMessagesSentTotal = Metrics.CreateCounter(
    "signalr_messages_sent_total",
    "Total number of SignalR messages sent",
    new[] { "factory_id", "line_id", "tag", "status" });

// Histogram with labels
private static readonly Histogram SignalRSendLatency = Metrics.CreateHistogram(
    "signalr_send_latency_seconds",
    "SignalR message send latency in seconds",
    new[] { "factory_id", "line_id", "tag" },
    new HistogramConfiguration
    {
        Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0 }
    });

// Gauge with labels
private static readonly Gauge SignalRConnectedClients = Metrics.CreateGauge(
    "signalr_connected_clients",
    "Number of connected SignalR clients",
    new[] { "factory_id", "line_id", "tag" });

// 사용
SignalRMessagesSentTotal.WithLabels("ulsan", "line1", "temp", "success").Inc();
SignalRSendLatency.WithLabels("ulsan", "line1", "temp").Observe(0.023);
SignalRConnectedClients.WithLabels("ulsan", "line1", "temp").Set(5);
```

---

### PromQL 예시

#### Factory + Line 단위 그룹핑

```promql
# SignalR 메시지 전송률 (factory + line별)
sum by(factory_id, line_id) (
    rate(signalr_messages_sent_total{status="success"}[5m])
)

# Legend 포맷: {{factory_id}} - {{line_id}}
# Grafana에서 자동으로 적용됨
```

#### Component별 그룹핑

```promql
# 모든 메트릭을 component별로 그룹핑
sum by(factory_id, line_id, component) (
    rate(signalr_messages_sent_total[5m])
) * on(component) group_left() 
  {component="api"}
```

#### Histogram P95/P99

```promql
# P95 latency by factory + line
histogram_quantile(0.95,
    sum(rate(signalr_send_latency_seconds_bucket[5m])) 
    by (le, factory_id, line_id)
)

# P99 latency by factory + line
histogram_quantile(0.99,
    sum(rate(signalr_send_latency_seconds_bucket[5m])) 
    by (le, factory_id, line_id)
)
```

---

### 테스트 방법

#### 1. 메트릭 노출 확인

```bash
# 메트릭 엔드포인트 확인
curl http://localhost:5000/metrics-net | grep signalr_messages_sent_total

# 예상 출력:
# signalr_messages_sent_total{factory_id="ulsan",line_id="line1",tag="temp",status="success"} 42
```

#### 2. Prometheus Targets 확인

```
http://localhost:9090/targets
- gateway-api-net job이 UP 상태인지 확인
```

#### 3. Grafana 대시보드 확인

```
http://localhost:3000/dashboards
- SignalR 실시간 전송 대시보드
- Legend에 "ulsan - line1" 형태로 표시되는지 확인
```

---

### 다음 단계 (선택사항)

#### 1. Pipeline 메트릭에도 line_id 추가

현재 `pipeline_*` 메트릭은 `factory_id`만 지원합니다.
향후 `line_id`를 추가하려면:

```csharp
// PipelineMetricsExporter.cs 수정 필요
// IPipelineMetrics 인터페이스 확장 필요
// TelemetryEvent에서 line_id 추출하여 기록
```

#### 2. 메트릭 알림 설정

```promql
# SignalR 메시지 전송 실패율 알림
(
    sum(rate(signalr_messages_sent_total{status="error"}[5m])) 
    / 
    sum(rate(signalr_messages_sent_total[5m]))
) > 0.1  # 10% 이상 실패 시 알림
```

---

### 참고 파일

- `docs/METRICS_DESIGN_GUIDE.md` - 상세 설계 가이드
- `Gateway.Infrastructure/Observability/MetricLabelHelper.cs` - 레이블 헬퍼
- `Gateway.Infrastructure/Observability/SignalRMetrics.cs` - SignalR 메트릭
- `grafana/dashboards/signalr-realtime.json` - SignalR 대시보드
- `grafana/dashboards/pipeline-backpressure.json` - Pipeline 대시보드

---

### 체크리스트

- [x] SignalRMetrics에 line_id 레이블 추가
- [x] MetricLabelHelper 구현 (line_id 추출)
- [x] SignalRTelemetryService에서 line_id 사용
- [x] Grafana 대시보드 쿼리 수정 (factory_id + line_id 그룹핑)
- [x] Legend 포맷 수정 ({{factory_id}} - {{line_id}})
- [x] NoData 원인 분석 문서 작성
- [x] 메트릭 설계 가이드 작성
- [x] Pipeline Backpressure 대시보드 NoData 수정

---

**구현 완료일:** 2025-01-XX
**작성자:** AI Assistant
**버전:** 1.0

