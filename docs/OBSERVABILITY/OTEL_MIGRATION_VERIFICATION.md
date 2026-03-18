# OpenTelemetry 硫뷀듃由??듭씪 寃利?媛?대뱶

## 蹂寃??ы빆 ?붿빟

1. **prometheus-net ?쒓굅**: 紐⑤뱺 而ㅼ뒪? 硫뷀듃由?쓣 OpenTelemetry Meter 湲곕컲?쇰줈 ?꾪솚
2. **?⑥씪 ?붾뱶?ъ씤??*: `/metrics` ?붾뱶?ъ씤?몃쭔 ?ъ슜 (OTel Prometheus Exporter)
3. **硫뷀듃由??대쫫 ?좎?**: 湲곗〈 硫뷀듃由??대쫫(`signalr_*`, `kafka_*`, `pipeline_*` ??? 洹몃?濡??좎?

## 寃利?泥댄겕由ъ뒪??
### 1. ?좏뵆由ъ??댁뀡 ?ъ떆??```powershell
# Gateway API ?좏뵆由ъ??댁뀡???ъ떆?묓븯?몄슂
# Docker Compose瑜??ъ슜?섎뒗 寃쎌슦:
docker-compose restart api

# ?먮뒗 吏곸젒 ?ㅽ뻾?섎뒗 寃쎌슦:
# ?좏뵆由ъ??댁뀡??以묒??섍퀬 ?ㅼ떆 ?쒖옉
```

### 2. 硫뷀듃由??붾뱶?ъ씤???뺤씤

#### 2.1 ?⑥씪 ?붾뱶?ъ씤???뺤씤
```powershell
# /metrics ?붾뱶?ъ씤?멸? 議댁옱?섎뒗吏 ?뺤씤
Invoke-WebRequest -Uri "http://localhost:5000/metrics" -UseBasicParsing | Select-Object StatusCode

# /metrics-net ?붾뱶?ъ씤?멸? ?쒓굅?섏뿀?붿? ?뺤씤 (404媛 ?섏?????
Invoke-WebRequest -Uri "http://localhost:5000/metrics-net" -UseBasicParsing
# ?덉긽: 404 Not Found
```

#### 2.2 SignalR 硫뷀듃由??뺤씤
```powershell
# SignalR 硫뷀듃由?씠 /metrics ?붾뱶?ъ씤?몄뿉 ?몄텧?섎뒗吏 ?뺤씤
$response = Invoke-WebRequest -Uri "http://localhost:5000/metrics" -UseBasicParsing
$response.Content | Select-String -Pattern "signalr_" -CaseSensitive:$false

# ?덉긽 異쒕젰 ?덉떆:
# signalr_messages_sent_total{factory_id="ulsan",line_id="line1",tag="temp",status="success"} 1
# signalr_send_latency_seconds_bucket{factory_id="ulsan",line_id="line1",tag="temp",le="0.001"} 0
# signalr_connected_clients{factory_id="ulsan",line_id="line1",tag="temp"} 0
```

#### 2.3 紐⑤뱺 而ㅼ뒪? 硫뷀듃由??뺤씤
```powershell
# SignalR 硫뷀듃由?$response.Content | Select-String -Pattern "signalr_" -CaseSensitive:$false

# Kafka 硫뷀듃由?$response.Content | Select-String -Pattern "kafka_" -CaseSensitive:$false

# Pipeline 硫뷀듃由?$response.Content | Select-String -Pattern "pipeline_" -CaseSensitive:$false

# MQTT 硫뷀듃由?$response.Content | Select-String -Pattern "mqtt_" -CaseSensitive:$false
```

### 3. Prometheus ?ㅼ젙 ?뺤씤

#### 3.1 Prometheus ?ㅼ젙 ?뚯씪 ?뺤씤
```yaml
# prometheus/prometheus.yml ?뚯씪 ?뺤씤
# ?ㅼ쓬 ?댁슜???덉뼱????
scrape_configs:
  - job_name: 'gateway-api'
    scrape_interval: 10s
    metrics_path: '/metrics'
    static_configs:
      - targets: ['api:8080']
```

#### 3.2 Prometheus媛 ?ㅽ겕?⑺븯?붿? ?뺤씤
```powershell
# Prometheus UI?먯꽌 ?뺤씤:
# 1. http://localhost:9090 ?묒냽
# 2. Status > Targets 硫붾돱濡??대룞
# 3. gateway-api job??UP ?곹깭?몄? ?뺤씤
# 4. Last Scrape ?쒓컙??理쒓렐?몄? ?뺤씤
```

### 4. Prometheus 荑쇰━ 寃利?
#### 4.1 SignalR 硫뷀듃由?荑쇰━
```promql
# 紐⑤뱺 SignalR 硫뷀듃由?議고쉶
{__name__=~"signalr_.*"}

# 硫붿떆吏 ?꾩넚瑜?sum by(factory_id, line_id) (rate(signalr_messages_sent_total{status="success"}[5m]))

# ?꾩넚 吏?곗떆媛?(P95)
histogram_quantile(0.95, sum(rate(signalr_send_latency_seconds_bucket[5m])) by (le, factory_id, line_id))

# ?곌껐???대씪?댁뼵????sum(signalr_connected_clients)
```

#### 4.2 Kafka 硫뷀듃由?荑쇰━
```promql
# Kafka 硫붿떆吏 泥섎━??sum by(consumer_group, topic) (rate(kafka_messages_processed_messages_total{status="success"}[5m]))

# Kafka 泥섎━ 吏?곗떆媛?histogram_quantile(0.95, sum(rate(kafka_processing_duration_seconds_bucket[5m])) by (le, consumer_group, topic))
```

#### 4.3 Pipeline 硫뷀듃由?荑쇰━
```promql
# Pipeline ?대깽??泥섎━??sum(rate(pipeline_ingested_events_total[5m]))
sum(rate(pipeline_persisted_events_total[5m]))
```

### 5. Grafana ??쒕낫???뺤씤

#### 5.1 SignalR ??쒕낫??- Grafana?먯꽌 "SignalR ?ㅼ떆媛??꾩넚" ??쒕낫???닿린
- 紐⑤뱺 ?⑤꼸???곗씠?곌? ?쒖떆?섎뒗吏 ?뺤씤
- `{{factory_id}} - {{line_id}}` ?뺤떇?쇰줈 ?덉쟾?쒓? ?쒖떆?섎뒗吏 ?뺤씤

#### 5.2 荑쇰━ ?덉떆 (Grafana ?⑤꼸)
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

### 6. 臾몄젣 ?닿껐

#### 6.1 硫뷀듃由?씠 蹂댁씠吏 ?딅뒗 寃쎌슦
1. **?좏뵆由ъ??댁뀡 濡쒓렇 ?뺤씤**
   ```powershell
   # 濡쒓렇?먯꽌 ?ㅼ쓬 硫붿떆吏 ?뺤씤:
   # "OpenTelemetry Prometheus exporter available at /metrics endpoint"
   # "Custom metrics (SignalR, Kafka, Pipeline, MQTT) are registered via OTel Meter"
   ```

2. **Meter ?깅줉 ?뺤씤**
   - Program.cs?먯꽌 ?ㅼ쓬 Meter媛 ?깅줉?섏뼱 ?덈뒗吏 ?뺤씤:
     - `Gateway.SignalR`
     - `Gateway.Kafka`
     - `Gateway.Kafka.Lag`
     - `Gateway.Pipeline`
     - `Gateway.MQTT`

3. **硫뷀듃由?씠 ?ㅼ젣濡?湲곕줉?섎뒗吏 ?뺤씤**
   - SignalR 硫붿떆吏媛 ?꾩넚?섎뒗吏 ?뺤씤
   - Kafka 硫붿떆吏媛 泥섎━?섎뒗吏 ?뺤씤
   - Pipeline ?대깽?멸? 泥섎━?섎뒗吏 ?뺤씤

#### 6.2 Prometheus?먯꽌 硫뷀듃由?쓣 李얠쓣 ???녿뒗 寃쎌슦
1. **Prometheus ?寃??곹깭 ?뺤씤**
   - Prometheus UI > Status > Targets
   - `gateway-api` job??UP ?곹깭?몄? ?뺤씤
   - Last Scrape ?쒓컙??理쒓렐?몄? ?뺤씤

2. **?ㅽ겕??寃쎈줈 ?뺤씤**
   - `metrics_path: '/metrics'`媛 ?щ컮瑜몄? ?뺤씤
   - `targets: ['api:8080']`媛 ?щ컮瑜몄? ?뺤씤 (Docker ?섍꼍)

3. **Prometheus ?ъ떆??*
   ```powershell
   docker-compose restart prometheus
   ```

#### 6.3 硫뷀듃由??대쫫???ㅻⅨ 寃쎌슦
- OTel Meter濡?蹂?섑뻽吏留?硫뷀듃由??대쫫? 洹몃?濡??좎??덉뒿?덈떎
- 留뚯빟 硫뷀듃由??대쫫???ㅻⅤ?ㅻ㈃, Prometheus?먯꽌 ?ㅼ젣 硫뷀듃由??대쫫???뺤씤:
  ```promql
  {__name__=~".*signalr.*"}
  ```

## ?깃났 湲곗?

??`/metrics` ?붾뱶?ъ씤?몄뿉??SignalR 硫뷀듃由?씠 ?몄텧?? 
??`/metrics-net` ?붾뱶?ъ씤?멸? ?쒓굅??(404)  
??Prometheus?먯꽌 `{__name__=~"signalr_.*"}` 荑쇰━濡?硫뷀듃由?議고쉶 媛?? 
??Grafana ??쒕낫?쒖뿉 ?곗씠?곌? ?쒖떆?? 
??紐⑤뱺 而ㅼ뒪? 硫뷀듃由?(SignalR, Kafka, Pipeline, MQTT)??`/metrics` ?붾뱶?ъ씤?몄뿉 ?몄텧?? 

## 異붽? 李멸퀬?ы빆

### 硫뷀듃由??대쫫 留ㅽ븨

OTel Meter濡?蹂?섑뻽吏留?Prometheus 硫뷀듃由??대쫫? 洹몃?濡??좎??⑸땲??

| OTel Meter | Prometheus 硫뷀듃由??대쫫 |
|------------|----------------------|
| `Gateway.SignalR` | `signalr_messages_sent_total`, `signalr_send_latency_seconds`, `signalr_connected_clients` |
| `Gateway.Kafka` | `kafka_messages_processed_messages_total`, `kafka_processing_duration_seconds`, `kafka_producer_messages_messages_total`, `kafka_producer_duration_seconds` |
| `Gateway.Kafka.Lag` | `kafka_consumer_lag_messages`, `kafka_consumer_committed_offset`, `kafka_consumer_high_watermark` |
| `Gateway.Pipeline` | `pipeline_ingested_events_total`, `pipeline_normalized_events_total`, `pipeline_routed_events_total`, `pipeline_persisted_events_total`, `pipeline_dropped_events_total`, `pipeline_processing_duration_seconds`, `pipeline_stage_queue_length` |
| `Gateway.MQTT` | `mqtt_messages_ingested_messages_total`, `mqtt_ingest_latency_seconds` |

### Label ?대쫫

OTel?먯꽌??Tag濡? Prometheus?먯꽌??Label濡??쒖떆?⑸땲??
- `factory_id`
- `line_id`
- `tag`
- `status`
- `consumer_group`
- `topic`
- `partition`
- `stage`



