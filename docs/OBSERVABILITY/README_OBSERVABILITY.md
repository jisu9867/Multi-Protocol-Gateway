# Gateway Observability Guide

## 鍮좊Ⅸ ?쒖옉

### 1. Docker Compose濡??꾩껜 ?ㅽ깮 ?ㅽ뻾

```bash
docker-compose up -d
```

??紐낅졊?쇰줈 ?ㅼ쓬 ?쒕퉬?ㅺ? ?쒖옉?⑸땲??
- Gateway API (?ы듃 5000)
- Gateway UI (?ы듃 5001)
- Prometheus (?ы듃 9090)
- Grafana (?ы듃 3000)
- Kafka, PostgreSQL ??
### 2. Grafana ?묒냽

1. 釉뚮씪?곗??먯꽌 `http://localhost:3000` ?묒냽
2. 濡쒓렇??
   - Username: `admin`
   - Password: `admin`
3. ??쒕낫???먮룞 濡쒕뱶??(Gateway ?대뜑)

### 3. Prometheus ?묒냽

- 釉뚮씪?곗??먯꽌 `http://localhost:9090` ?묒냽
- 硫뷀듃由?荑쇰━ ?덉떆:
  ```
  kafka_consumer_lag_messages
  pipeline_stage_queue_length
  ```

## 硫뷀듃由??붾뱶?ъ씤??
### Gateway API Metrics

- **OpenTelemetry Prometheus Exporter**: `http://localhost:5000/metrics`
- **Prometheus-net Server**: `http://localhost:9090/metrics` (蹂꾨룄 ?ы듃)

### 二쇱슂 硫뷀듃由?
#### Kafka 硫뷀듃由?- `kafka_consumer_lag_messages`: Consumer Group蹂?Lag
- `kafka_messages_processed_messages_total`: 泥섎━??硫붿떆吏 ??- `kafka_processing_duration_seconds`: 泥섎━ 吏???쒓컙

#### Pipeline 硫뷀듃由?- `pipeline_stage_queue_length`: Stage蹂???湲몄씠
- `pipeline_processing_duration_seconds`: 泥섎━ 吏???쒓컙
- `pipeline_ingested_events_total`: ?섏쭛???대깽????
#### SignalR 硫뷀듃由?- `signalr_messages_sent_total`: ?꾩넚??硫붿떆吏 ??- `signalr_send_latency_seconds`: ?꾩넚 吏???쒓컙

#### MQTT 硫뷀듃由?- `mqtt_messages_ingested_messages_total`: ?섏쭛??硫붿떆吏 ??- `mqtt_ingest_latency_seconds`: ?섏쭛 吏???쒓컙

## PromQL 荑쇰━ ?덉떆

### Consumer Lag ?⑷퀎
```promql
sum(kafka_consumer_lag_messages) by (consumer_group)
```

### Pipeline 泥섎━??(?대깽??珥?
```promql
rate(pipeline_ingested_events_total[5m])
```

### Pipeline ??諛깊봽?덉뀛 媛먯?
```promql
pipeline_stage_queue_length > 800
```

### SignalR ?꾩넚 吏??(P95)
```promql
histogram_quantile(0.95, signalr_send_latency_seconds_bucket)
```

## ?뚮엺 ?ㅼ젙

?뚮엺 洹쒖튃? `prometheus/alerts.yml`???뺤쓽?섏뼱 ?덉뒿?덈떎.

二쇱슂 ?뚮엺:
- **HighKafkaConsumerLag**: Lag > 10,000 (5遺?
- **CriticalKafkaConsumerLag**: Lag > 50,000 (2遺?
- **PipelineQueueBackpressure**: Queue Length > 800 (3遺?

## ??쒕낫??
### 1. Gateway System Overview
- ?꾩껜 ?쒖뒪??泥섎━??- Pipeline Stage蹂??곹깭
- Kafka Consumer Lag ?붿빟

### 2. Kafka Consumer Lag
- Consumer Group蹂?Lag ?곸꽭
- Partition蹂?Lag
- 泥섎━??諛?吏???쒓컙

### 3. Pipeline Backpressure
- Stage蹂???湲몄씠
- 泥섎━ 吏???쒓컙
- ?쒕∼瑜?
### 4. SignalR ?ㅼ떆媛??꾩넚
- ?꾩넚 吏???쒓컙
- ?꾩넚??- ?곌껐???대씪?댁뼵????
### 5. PostgreSQL ?깅뒫
- 荑쇰━ ?ㅽ뻾 ?쒓컙
- Insert 泥섎━??- ?곌껐 ? ?곹깭

## 臾몄젣 ?닿껐

### 硫뷀듃由?씠 ?쒖떆?섏? ?딆쓣 ??
1. **Prometheus媛 Gateway API瑜??ㅽ겕?⑺븯?붿? ?뺤씤**
   ```bash
   # Prometheus UI?먯꽌 Status > Targets ?뺤씤
   http://localhost:9090/targets
   ```

2. **Gateway API 硫뷀듃由??붾뱶?ъ씤???뺤씤**
   ```bash
   curl http://localhost:5000/metrics
   ```

3. **濡쒓렇 ?뺤씤**
   ```bash
   docker logs gateway-api
   ```

### Kafka Lag??怨꾩궛?섏? ?딆쓣 ??
1. **KafkaLagMetrics ?쒕퉬?ㅺ? ?ㅽ뻾 以묒씤吏 ?뺤씤**
   - 濡쒓렇?먯꽌 "Registered consumer group" 硫붿떆吏 ?뺤씤

2. **Kafka ?곌껐 ?뺤씤**
   - `Kafka__BootstrapServers` ?섍꼍 蹂???뺤씤
   - Azure Event Hubs ?ъ슜 ??Connection String ?뺤씤

3. **AdminClient 沅뚰븳 ?뺤씤**
   - Kafka Broker??Metadata 議고쉶 沅뚰븳 ?꾩슂

## ?곸꽭 臾몄꽌

- [Observability Architecture](./docs/OBSERVABILITY_ARCHITECTURE.md): ?꾩껜 ?꾪궎?띿쿂 ?ㅻ챸
- [Prometheus Configuration](./prometheus/prometheus.yml): Prometheus ?ㅼ젙
- [Alert Rules](./prometheus/alerts.yml): ?뚮엺 洹쒖튃



