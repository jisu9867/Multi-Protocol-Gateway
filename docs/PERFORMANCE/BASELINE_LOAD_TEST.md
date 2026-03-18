# Baseline MQTT Load Test (Single Node)

This guide implements the phase-1 baseline plan:

- Goal metric: `DB Persist TPS`
- Target: stable `300 TPS`
- Environment: local Docker single node
- UI clients: excluded
- Kafka partitions: `1 -> 3 -> 6`
- Stage timing: warmup 5m, steady 10m, cooldown 5m

## 1) Start stack for performance run

From `Multi-Protocol-Gateway`:

```powershell
docker compose -f docker-compose.yml -f docker-compose.perf.override.yml up -d --build
```

Performance override does:

- disables seed data
- disables observability test endpoint seeding
- lowers noisy runtime logs

## 2) Expand Kafka partitions

```powershell
.\scripts\perf\set-kafka-partitions.ps1 -PartitionCount 1
.\scripts\perf\set-kafka-partitions.ps1 -PartitionCount 3
.\scripts\perf\set-kafka-partitions.ps1 -PartitionCount 6
```

## 3) Run one load stage manually

Example: 300 TPS (`3 processes x 100 msg/s`)

```powershell
.\scripts\perf\run-mqtt-load.ps1 -TargetTps 300 -WarmupMinutes 5 -SteadyMinutes 10 -CooldownMinutes 5
```

The script:

- starts multiple simulator processes
- enforces `rate_mode=rate`, `qos=0`, `overflow_policy=drop_oldest`
- writes run metadata and simulator logs under `artifacts/perf/<stage>`

## 4) Collect metrics for that stage

Use timestamps from `stage-run.json`:

```powershell
.\scripts\perf\collect-prometheus-metrics.ps1 `
  -StageName p3-tps300 `
  -TargetTps 300 `
  -PartitionCount 3 `
  -StartUtc "2026-03-15T01:00:00Z" `
  -SteadyStartUtc "2026-03-15T01:05:00Z" `
  -SteadyEndUtc "2026-03-15T01:15:00Z" `
  -EndUtc "2026-03-15T01:20:00Z"
```

## 5) Run full matrix end-to-end

```powershell
.\scripts\perf\run-baseline-matrix.ps1 -StartStack
```

Default matrix:

- partitions: `1,3,6`
- target TPS: `100,200,300,400`
- durations: `5m/10m/5m`

Outputs:

- per-stage JSON and CSV in `artifacts/perf/baseline-<timestamp>/<stage>/`
- aggregated CSV: `summary-all-stages.csv`
- markdown report: `RESULTS.md`

## 6) Prometheus expressions used

Primary TPS:

- `sum(rate(pipeline_persisted_events_total[1m]))`

Support:

- `sum(rate(mqtt_messages_ingested_messages_total{status="success"}[1m]))`
- `sum(rate(kafka_producer_messages_messages_total{status="success"}[1m]))`
- `sum(rate(kafka_messages_processed_messages_total{status="success"}[1m]))`
- `sum(kafka_consumer_lag_messages)`
- `sum(rate(pipeline_dropped_events_total[1m]))`
- `histogram_quantile(0.95, sum(rate(kafka_processing_duration_seconds_bucket[5m])) by (le))`
- `histogram_quantile(0.95, sum(rate(mqtt_ingest_latency_seconds_bucket[5m])) by (le))`

## 7) SLO pass criteria implemented

- avg DB persist TPS >= target TPS
- drop rate avg <= `0.01/s`
- combined error rate (ingest + produce + consume) < `0.1%`
- Kafka P95 <= `1.0s`
- MQTT P95 <= `0.1s`
- lag must not explode (`max <= avg + 5000`, coarse guardrail)

## 8) Notes

- Current gateway has one consumer instance per consumer group, so partition scaling impact may be limited.
- If 300 TPS fails, proceed to phase-2 optimization (consumer parallelism, sink batching tuning).


