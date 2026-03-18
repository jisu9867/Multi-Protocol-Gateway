# 濡쒖뺄 vs Docker 媛쒕컻 媛?대뱶

濡쒖뺄(dotnet run)怨?Docker Compose ?섍꼍?먯꽌???ㅼ젙 李⑥씠 諛?Simulator MQTT publish ??? PostgreSQL ?곌껐 ?뺤씤 諛⑸쾿???뺣━?⑸땲??

---

## 1. ?섍꼍蹂??붿빟

| 援щ텇 | 濡쒖뺄 媛쒕컻 (dotnet run) | Docker Compose |
|------|------------------------|----------------|
| **API** | `http://localhost:5011` (?먮뒗 launchSettings) | `http://localhost:5000` |
| **UI** | `http://localhost:5270` | `http://localhost:5001` |
| **PostgreSQL** | `localhost:5433` (?몄뒪?몄뿉???묒냽 ?? | 而⑦뀒?대꼫 ?대?: `postgres:5432` / ?몄뒪?? `localhost:5433` |
| **Kafka** | `localhost:9092` | 而⑦뀒?대꼫 ?대?: `kafka:9093` / ?몄뒪?? `localhost:9092` |
| **MQTT 釉뚮줈而?* | ?몄뒪?몄쓽 `localhost:1883` | Compose ??Mosquitto: 而⑦뀒?대꼫 `mosquitto:1883` / **?몄뒪?몄뿉??publish ??`localhost:1884`** |

---

## 2. Simulator MQTT Publish ???
Gateway API??MQTT ?좏뵿 `factory/+/+/telemetry` (??쇰뱶移대뱶)瑜?援щ룆?⑸땲??  
Simulator???꾨옒 二쇱냼濡?publish?섎㈃ ?⑸땲??

### 濡쒖뺄 媛쒕컻 (Gateway瑜?dotnet run?쇰줈 ?ㅽ뻾????

- **Broker 二쇱냼**: `localhost:1883`
- **?좏뵿 ??*: `factory/line-1/ulsan-line1/telemetry` (?⑦꽩: `factory/{line}/{sourceId}/telemetry`)
- Simulator ?ㅼ젙 ??(YAML): `broker: "localhost:1883"`, `topic_template: "factory/{line}/{source_id}/telemetry"`

??濡쒖뺄?먯꽌 Mosquitto(?먮뒗 ?ㅻⅨ MQTT 釉뚮줈而?瑜?1883?쇰줈 ?꾩썙 ?먭퀬, Simulator? Gateway API 紐⑤몢 `localhost:1883`???ъ슜?⑸땲??

### Docker Compose (Gateway瑜?docker compose濡??ㅽ뻾????

- **Broker 二쇱냼**: **`localhost:1884`** (?몄뒪???ы듃 1884媛 Compose??Mosquitto 1883??留ㅽ븨??
- **?좏뵿 ??*: ?숈씪?섍쾶 `factory/line-1/ulsan-line1/telemetry`
- Simulator瑜?**?몄뒪?몄뿉??* ?ㅽ뻾???? `broker: "localhost:1884"` 濡??ㅼ젙

> ?ы듃 1884瑜??곕뒗 ?댁쑀: ?몄뒪?몄뿉???대? 1883???곕뒗 MQTT 釉뚮줈而??먮뒗 ?ㅻⅨ Simulator)媛 ?덉쓣 ???덉뼱, Docker??Mosquitto???몄뒪?몄뿉?쒕뒗 1884濡쒕쭔 ?몄텧?⑸땲?? 1883??鍮꾩뼱 ?덉쑝硫?`docker-compose.yml`?먯꽌 `1883:1883`?쇰줈 諛붽퓭???⑸땲??

### Simulator媛 Docker 而⑦뀒?대꼫濡??ㅽ뻾????
- 媛숈? Docker ?ㅽ듃?뚰겕 ?ъ슜 ?? `broker: "mosquitto:1883"` (?쒕퉬???대쫫?쇰줈 ?묒냽)
- ?ㅻⅨ 而댄벂??而⑦뀒?대꼫?먯꽌 ?묒냽 ?? ?대떦 ?몄뒪??IP? **?몄뒪?몄뿉??留ㅽ븨???ы듃**(?? 1884) ?ъ슜

---

## 3. PostgreSQL ?곌껐 ?뺤씤 (Docker ?섍꼍)

### 3.1 Compose ?ㅼ젙

- API 而⑦뀒?대꼫: `ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=gateway;Username=gateway;Password=gateway`
- API??`depends_on: postgres (service_healthy)` 濡?DB 以鍮???湲곕룞?⑸땲??
- Postgres ?쒕퉬?ㅻ뒗 `5433:5432` 濡??몄텧?섎?濡? **?몄뒪?몄뿉??* ?묒냽???뚮뒗 `localhost:5433` ???ъ슜?⑸땲??

### 3.2 DB ?곌껐 ?뺤씤 諛⑸쾿

**諛⑸쾿 A: ?몄뒪?몄뿉??psql**

```bash
# Docker Postgres???몄뒪???ы듃 5433?쇰줈 ?묒냽
psql -h localhost -p 5433 -U gateway -d gateway
# 鍮꾨?踰덊샇: gateway
```

**諛⑸쾿 B: ?ㅽ뻾 以묒씤 API 而⑦뀒?대꼫?먯꽌**

```bash
docker exec -it gateway-api dotnet run --no-build --project /app/Gateway.Api.dll -- --urls=http://+:8080
# ?먮뒗 ?대? ???덈뒗 API 濡쒓렇?먯꽌 "Gateway API started" / DB 留덉씠洹몃젅?댁뀡 ?깃났 濡쒓렇 ?뺤씤
```

**諛⑸쾿 C: Health Check**

- API??Npgsql health check瑜??깅줉?섎?濡? ?ㅼ쓬?쇰줈 DB ?곹깭瑜?蹂????덉뒿?덈떎.  
  `http://localhost:5000/health` (?먮뒗 ?꾨줈?앺듃?먯꽌 ?ъ슜?섎뒗 health 寃쎈줈) ?묐떟?먯꽌 PostgreSQL ??ぉ??Healthy?몄? ?뺤씤?⑸땲??

濡쒖뺄 媛쒕컻 ?쒖뿉??`appsettings.Development.json` ?깆뿉??`Host=localhost;Port=5433` ?쇰줈 媛숈? DB(?몄뒪??5433)瑜?諛붾씪蹂닿쾶 ?섎㈃, 濡쒖뺄 API? Docker API 紐⑤몢 ?숈씪 DB瑜??ъ슜??寃利앺븷 ???덉뒿?덈떎.

---

## 4. 濡쒖뺄 媛쒕컻 ?덉감 (dotnet run)

1. **PostgreSQL**  
   - Docker濡쒕쭔 ?꾩슦湲? `docker compose up -d postgres`  
   - ?몄뒪?몄뿉???묒냽: `localhost:5433`

2. **Kafka (?좏깮)**  
   - 濡쒖뺄?먯꽌 Kafka ?ъ슜 ?? `docker compose up -d zookeeper kafka`  
   - API ?ㅼ젙: `Kafka__BootstrapServers=localhost:9092`

3. **MQTT 釉뚮줈而?*  
   - 濡쒖뺄 Mosquitto ?? `localhost:1883` ?먯꽌 ?섏떊?섎룄濡??ㅽ뻾

4. **API ?ㅽ뻾**  
   ```bash
   cd src/Gateway.Api
   dotnet run
   ```  
   - DB: `Host=localhost;Port=5433` (appsettings.Development.json)  
   - MQTT: `localhost:1883`  
   - Kafka: `localhost:9092`

5. **UI ?ㅽ뻾**  
   ```bash
   cd src/Gateway.Ui
   dotnet run
   ```  
   - API 二쇱냼: `http://localhost:5011` (appsettings.Development.json)

6. **Simulator**  
   - MQTT broker: `localhost:1883`  
   - ?좏뵿: `factory/+/+/telemetry` ?⑦꽩 (?? `factory/line-1/ulsan-line1/telemetry`)

---

## 5. Docker Compose 媛쒕컻 ?덉감

1. **??踰덉뿉 湲곕룞**  
   ```bash
   docker compose up --build
   ```

2. **?묒냽 二쇱냼**  
   - API: http://localhost:5000  
   - UI: http://localhost:5001  
   - PostgreSQL (?몄뒪??: `localhost:5433`  
   - MQTT (?몄뒪?몄뿉??publish): **`localhost:1884`**

3. **Simulator (?몄뒪?몄뿉???ㅽ뻾)**  
   - MQTT broker: **`localhost:1884`**  
   - ?좏뵿: `factory/line-1/ulsan-line1/telemetry` ???숈씪 ?⑦꽩

4. **PostgreSQL ?뺤씤**  
   - ??3.2 李멸퀬 (?몄뒪?? `localhost:5433`, ?ъ슜??DB: gateway/gateway).

---

## 6. ?ㅼ젙 ?뚯씪 ?뺣━

| ?ㅼ젙 | 濡쒖뺄 (appsettings.Development.json) | Docker (appsettings.Docker.json + env) |
|------|-------------------------------------|----------------------------------------|
| DB | Host=localhost;Port=5433 | Host=postgres;Port=5432 |
| MQTT Server | localhost | mosquitto |
| MQTT Port | 1883 | 1883 (而⑦뀒?대꼫 ?대?) |
| Kafka | localhost:9092 | kafka:9093 |
| UI ??API | http://localhost:5011 | http://api:8080 |

---

## 7. ?몃윭釉붿뒋??
- **Docker?먯꽌 "port 1883 already allocated"**  
  ???몄뒪??1883 ?ъ슜 以? Compose??Mosquitto瑜?`1884:1883`?쇰줈 留ㅽ븨???먯뿀?쇰?濡? Simulator??**localhost:1884** 濡?publish ?섎㈃ ?⑸땲??

- **Docker API媛 DB ?곌껐 ?ㅽ뙣**  
  ??`depends_on` 怨?healthcheck濡?postgres媛 癒쇱? 以鍮꾨맂 ??API媛 ?쒖옉?⑸땲??  
  ??DB 鍮꾨?踰덊샇/DB紐낆씠 `gateway`/`gateway` ?몄?, `ConnectionStrings__DefaultConnection` ??`Host=postgres;Port=5432;...` ?몄? ?뺤씤?섏꽭??

- **濡쒖뺄?먯꽌???섎뒗??Docker?먯꽌留?MQTT 誘몄닔??*  
  ??Docker ?섍꼍?먯꽌??諛섎뱶??**Compose ??Mosquitto**瑜??곌퀬, Simulator??**localhost:1884** 濡?publish ?섏꽭??(?몄뒪?몄뿉???ㅽ뻾 ??.


