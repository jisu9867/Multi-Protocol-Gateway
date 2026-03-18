# OpenTelemetry + Prometheus + Grafana 援ы쁽 ?꾨즺 ?붿빟

## 援ы쁽 ?꾨즺 ??ぉ

### ??1. OpenTelemetry ?꾩엯

**?⑦궎吏 異붽?:**
- `OpenTelemetry` 1.9.0
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.9.0-beta.1
- `OpenTelemetry.Instrumentation.*` ?⑦궎吏??- `prometheus-net.AspNetCore` 8.2.1

**援ы쁽 ?댁슜:**
- ASP.NET Core ?먮룞 怨꾩륫 (?붿껌/?묐떟 異붿쟻)
- Kafka Producer/Consumer ?섎룞 怨꾩륫
- MQTT Adapter ?섎룞 怨꾩륫
- Pipeline Stages 怨꾩륫
- SignalR 硫붿떆吏 ?꾩넚 怨꾩륫
- EF Core ?먮룞 怨꾩륫 (PostgreSQL 荑쇰━ 異붿쟻)
- Trace/Metric/Log ?곴?愿怨??좎?

### ??2. Prometheus 硫뷀듃由??ㅺ퀎 諛?援ы쁽

**援ы쁽??硫뷀듃由?**

1. **Kafka 硫뷀듃由?* (`KafkaMetrics.cs`)
   - `kafka_consumer_lag_messages` (Gauge): Consumer Group蹂?Lag
   - `kafka_messages_processed_messages_total` (Counter): 泥섎━??硫붿떆吏 ??   - `kafka_processing_duration_seconds` (Histogram): 泥섎━ 吏???쒓컙
   - `kafka_producer_messages_messages_total` (Counter): 諛쒗뻾??硫붿떆吏 ??   - `kafka_producer_duration_seconds` (Histogram): 諛쒗뻾 吏???쒓컙

2. **SignalR 硫뷀듃由?* (`SignalRMetrics.cs`)
   - `signalr_messages_sent_total` (Counter): ?꾩넚??硫붿떆吏 ??   - `signalr_send_latency_seconds` (Histogram): ?꾩넚 吏???쒓컙
   - `signalr_connected_clients` (Gauge): ?곌껐???대씪?댁뼵????
3. **MQTT 硫뷀듃由?* (`MqttMetrics.cs`)
   - `mqtt_messages_ingested_messages_total` (Counter): ?섏쭛??硫붿떆吏 ??   - `mqtt_ingest_latency_seconds` (Histogram): ?섏쭛 吏???쒓컙

4. **Pipeline 硫뷀듃由?* (`PipelineMetricsExporter.cs`)
   - `pipeline_stage_queue_length` (Gauge): Stage蹂???湲몄씠
   - `pipeline_processing_duration_seconds` (Histogram): 泥섎━ 吏???쒓컙
   - `pipeline_ingested_events_total` (Counter): ?섏쭛???대깽????   - `pipeline_persisted_events_total` (Counter): ??λ맂 ?대깽????   - `pipeline_dropped_events_total` (Counter): ?쒕∼???대깽????
**?쇰꺼 ?ㅺ퀎:**
- `factory_id`: 怨듭옣 ID
- `consumer_group`: Kafka Consumer Group ID
- `topic`: Kafka Topic
- `partition`: Kafka Partition
- `tag`: ?쇱꽌 ?쒓렇
- `stage`: Pipeline Stage ?대쫫

**Cardinality ?쒖뼱:**
- 怨좎쑀 媛믪씠 留롮? ?쇰꺼 ?쒖쇅
- 吏묎퀎 媛?ν븳 ?쇰꺼留??ъ슜

### ??3. Kafka Consumer Lag 怨꾩륫

**援ы쁽 ?뚯씪:** `KafkaLagMetrics.cs`

**怨꾩궛 諛⑹떇:**
- AdminClient濡?High Watermark 議고쉶
- Consumer Group??Committed Offset 議고쉶
- Lag = High Watermark - Committed Offset

**?뱀쭠:**
- Azure Event Hubs ?명솚 (SASL_SSL ?몄쬆 吏??
- 10珥덈쭏???먮룞 ?낅뜲?댄듃
- Consumer Group蹂??먮룞 ?깅줉

**硫뷀듃由?**
- `kafka_consumer_lag_messages`: Lag 媛?- `kafka_consumer_committed_offset`: Committed Offset
- `kafka_consumer_high_watermark`: High Watermark

### ??4. Prometheus & Grafana 援ъ꽦

**Docker Compose 異붽?:**
- Prometheus (?ы듃 9090)
- Grafana (?ы듃 3000)

**Prometheus ?ㅼ젙:**
- `prometheus/prometheus.yml`: Scrape ?ㅼ젙
- `prometheus/alerts.yml`: ?뚮엺 洹쒖튃

**Grafana ?ㅼ젙:**
- `grafana/provisioning/datasources/`: Prometheus ?곗씠?곗냼???먮룞 ?ㅼ젙
- `grafana/provisioning/dashboards/`: ??쒕낫???먮룞 濡쒕뱶
- `grafana/dashboards/`: ??쒕낫??JSON ?뚯씪

### ??5. Grafana ??쒕낫??
**援ы쁽????쒕낫??**
1. **Gateway System Overview** (`gateway-overview.json`)
   - ?꾩껜 泥섎━??   - Pipeline Queue Lengths
   - Kafka Consumer Lag ?붿빟
   - Pipeline Processing Duration

2. **Kafka Consumer Lag** (`kafka-consumer-lag.json`)
   - Consumer Group蹂?Lag 洹몃옒??   - Partition蹂?Lag ?뚯씠釉?   - Committed Offset vs High Watermark
   - 泥섎━??諛?吏???쒓컙

**異붽? ??쒕낫??(?ν썑 ?뺤옣):**
- Pipeline Backpressure
- SignalR ?ㅼ떆媛??꾩넚
- PostgreSQL ?깅뒫

### ??6. ?뚮엺 洹쒖튃

**援ы쁽???뚮엺** (`prometheus/alerts.yml`):
- `HighKafkaConsumerLag`: Lag > 10,000 (5遺?
- `CriticalKafkaConsumerLag`: Lag > 50,000 (2遺?
- `PipelineQueueBackpressure`: Queue Length > 800 (3遺?
- `CriticalPipelineQueueBackpressure`: Queue Length > 950 (1遺?
- `HighKafkaProcessingDuration`: P95 > 1珥?(5遺?
- `HighSignalRSendLatency`: P95 > 0.5珥?(5遺?
- `HighMqttIngestLatency`: P95 > 0.1珥?(5遺?
- `HighKafkaMessageErrors`: Error Rate > 0.1/sec (3遺?
- `HighPipelineDropRate`: Drop Rate > 10/sec (3遺?

## ?뚯씪 援ъ“

```
Multi-Protocol-Gateway/
?쒋?? src/
??  ?쒋?? Gateway.Api/
??  ??  ?붴?? Program.cs (OpenTelemetry ?ㅼ젙)
??  ?쒋?? Gateway.Infrastructure/
??  ??  ?붴?? Observability/
??  ??      ?쒋?? KafkaLagMetrics.cs
??  ??      ?쒋?? KafkaMetrics.cs
??  ??      ?쒋?? PipelineMetricsExporter.cs
??  ??      ?쒋?? SignalRMetrics.cs
??  ??      ?붴?? MqttMetrics.cs
??  ?붴?? Gateway.Adapters/
??      ?붴?? MqttAdapter/
??          ?붴?? MqttAdapter.cs (硫뷀듃由??섏쭛 異붽?)
?쒋?? prometheus/
??  ?쒋?? prometheus.yml
??  ?붴?? alerts.yml
?쒋?? grafana/
??  ?쒋?? provisioning/
??  ??  ?쒋?? datasources/
??  ??  ??  ?붴?? prometheus.yml
??  ??  ?붴?? dashboards/
??  ??      ?붴?? dashboard.yml
??  ?붴?? dashboards/
??      ?쒋?? gateway-overview.json
??      ?붴?? kafka-consumer-lag.json
?쒋?? docker-compose.yml (Prometheus/Grafana 異붽?)
?붴?? docs/
    ?붴?? OBSERVABILITY_ARCHITECTURE.md
```

## ?ъ슜 諛⑸쾿

### 1. ?ㅽ뻾
```bash
docker-compose up -d
```

### 2. ?묒냽
- **Grafana**: http://localhost:3000 (admin/admin)
- **Prometheus**: http://localhost:9090
- **Gateway API Metrics**: http://localhost:5000/metrics

### 3. ??쒕낫???뺤씤
- Grafana?먯꽌 "Gateway" ?대뜑????쒕낫???뺤씤
- ?먮룞?쇰줈 濡쒕뱶??
## 二쇱슂 ?뱀쭠

1. **湲곗〈 ?꾪궎?띿쿂 蹂寃??놁쓬**: ?뚯씠?꾨씪??援ъ“ 洹몃?濡??좎?
2. **?댁쁺 ?섍꼍 以鍮?*: ?꾨줈?뺤뀡?먯꽌 諛붾줈 ?ъ슜 媛??3. **?꾩쟾??愿痢≪꽦**: Trace/Metric/Log ?듯빀
4. **Azure Event Hubs ?명솚**: Kafka Lag 怨꾩궛 吏??5. **?먮룞 ?ㅼ젙**: Grafana ??쒕낫???먮룞 ?꾨줈鍮꾩???
## ?ㅼ쓬 ?④퀎 (?좏깮?ы빆)

1. **Jaeger/Zipkin ?듯빀**: 遺꾩궛 異붿쟻 ?쒓컖??2. **Log Exporter**: OpenTelemetry Log Exporter 異붽?
3. **異붽? ??쒕낫??*: Pipeline Backpressure, SignalR ??4. **Alertmanager**: ?뚮엺 ?듯빀 愿由?5. **Tracing Context ?꾪뙆**: Kafka ?ㅻ뜑??Trace Context 異붽?

## 李멸퀬 臾몄꽌

- [Observability Architecture](./docs/OBSERVABILITY_ARCHITECTURE.md)
- [Observability Guide](./README_OBSERVABILITY.md)



