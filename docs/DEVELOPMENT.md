# 로컬 vs Docker 개발 가이드

로컬(dotnet run)과 Docker Compose 환경에서의 설정 차이 및 Simulator MQTT publish 대상, PostgreSQL 연결 확인 방법을 정리합니다.

---

## 1. 환경별 요약

| 구분 | 로컬 개발 (dotnet run) | Docker Compose |
|------|------------------------|----------------|
| **API** | `http://localhost:5011` (또는 launchSettings) | `http://localhost:5000` |
| **UI** | `http://localhost:5270` | `http://localhost:5001` |
| **PostgreSQL** | `localhost:5433` (호스트에서 접속 시) | 컨테이너 내부: `postgres:5432` / 호스트: `localhost:5433` |
| **Kafka** | `localhost:9092` | 컨테이너 내부: `kafka:9093` / 호스트: `localhost:9092` |
| **MQTT 브로커** | 호스트의 `localhost:1883` | Compose 내 Mosquitto: 컨테이너 `mosquitto:1883` / **호스트에서 publish 시 `localhost:1884`** |

---

## 2. Simulator MQTT Publish 대상

Gateway API는 MQTT 토픽 `factory/+/+/telemetry` (와일드카드)를 구독합니다.  
Simulator는 아래 주소로 publish하면 됩니다.

### 로컬 개발 (Gateway를 dotnet run으로 실행할 때)

- **Broker 주소**: `localhost:1883`
- **토픽 예**: `factory/line-1/ulsan-line1/telemetry` (패턴: `factory/{line}/{sourceId}/telemetry`)
- Simulator 설정 예 (YAML): `broker: "localhost:1883"`, `topic_template: "factory/{line}/{source_id}/telemetry"`

→ 로컬에서 Mosquitto(또는 다른 MQTT 브로커)를 1883으로 띄워 두고, Simulator와 Gateway API 모두 `localhost:1883`을 사용합니다.

### Docker Compose (Gateway를 docker compose로 실행할 때)

- **Broker 주소**: **`localhost:1884`** (호스트 포트 1884가 Compose의 Mosquitto 1883에 매핑됨)
- **토픽 예**: 동일하게 `factory/line-1/ulsan-line1/telemetry`
- Simulator를 **호스트에서** 실행할 때: `broker: "localhost:1884"` 로 설정

> 포트 1884를 쓰는 이유: 호스트에서 이미 1883을 쓰는 MQTT 브로커(또는 다른 Simulator)가 있을 수 있어, Docker용 Mosquitto는 호스트에서는 1884로만 노출합니다. 1883이 비어 있으면 `docker-compose.yml`에서 `1883:1883`으로 바꿔도 됩니다.

### Simulator가 Docker 컨테이너로 실행될 때

- 같은 Docker 네트워크 사용 시: `broker: "mosquitto:1883"` (서비스 이름으로 접속)
- 다른 컴퓨터/컨테이너에서 접속 시: 해당 호스트 IP와 **호스트에서 매핑한 포트**(예: 1884) 사용

---

## 3. PostgreSQL 연결 확인 (Docker 환경)

### 3.1 Compose 설정

- API 컨테이너: `ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=gateway;Username=gateway;Password=gateway`
- API는 `depends_on: postgres (service_healthy)` 로 DB 준비 후 기동합니다.
- Postgres 서비스는 `5433:5432` 로 노출되므로, **호스트에서** 접속할 때는 `localhost:5433` 을 사용합니다.

### 3.2 DB 연결 확인 방법

**방법 A: 호스트에서 psql**

```bash
# Docker Postgres에 호스트 포트 5433으로 접속
psql -h localhost -p 5433 -U gateway -d gateway
# 비밀번호: gateway
```

**방법 B: 실행 중인 API 컨테이너에서**

```bash
docker exec -it gateway-api dotnet run --no-build --project /app/Gateway.Api.dll -- --urls=http://+:8080
# 또는 이미 떠 있는 API 로그에서 "Gateway API started" / DB 마이그레이션 성공 로그 확인
```

**방법 C: Health Check**

- API는 Npgsql health check를 등록하므로, 다음으로 DB 상태를 볼 수 있습니다.  
  `http://localhost:5000/health` (또는 프로젝트에서 사용하는 health 경로) 응답에서 PostgreSQL 항목이 Healthy인지 확인합니다.

로컬 개발 시에는 `appsettings.Development.json` 등에서 `Host=localhost;Port=5433` 으로 같은 DB(호스트 5433)를 바라보게 하면, 로컬 API와 Docker API 모두 동일 DB를 사용해 검증할 수 있습니다.

---

## 4. 로컬 개발 절차 (dotnet run)

1. **PostgreSQL**  
   - Docker로만 띄우기: `docker compose up -d postgres`  
   - 호스트에서 접속: `localhost:5433`

2. **Kafka (선택)**  
   - 로컬에서 Kafka 사용 시: `docker compose up -d zookeeper kafka`  
   - API 설정: `Kafka__BootstrapServers=localhost:9092`

3. **MQTT 브로커**  
   - 로컬 Mosquitto 등: `localhost:1883` 에서 수신하도록 실행

4. **API 실행**  
   ```bash
   cd src/Gateway.Api
   dotnet run
   ```  
   - DB: `Host=localhost;Port=5433` (appsettings.Development.json)  
   - MQTT: `localhost:1883`  
   - Kafka: `localhost:9092`

5. **UI 실행**  
   ```bash
   cd src/Gateway.Ui
   dotnet run
   ```  
   - API 주소: `http://localhost:5011` (appsettings.Development.json)

6. **Simulator**  
   - MQTT broker: `localhost:1883`  
   - 토픽: `factory/+/+/telemetry` 패턴 (예: `factory/line-1/ulsan-line1/telemetry`)

---

## 5. Docker Compose 개발 절차

1. **한 번에 기동**  
   ```bash
   docker compose up --build
   ```

2. **접속 주소**  
   - API: http://localhost:5000  
   - UI: http://localhost:5001  
   - PostgreSQL (호스트): `localhost:5433`  
   - MQTT (호스트에서 publish): **`localhost:1884`**

3. **Simulator (호스트에서 실행)**  
   - MQTT broker: **`localhost:1884`**  
   - 토픽: `factory/line-1/ulsan-line1/telemetry` 등 동일 패턴

4. **PostgreSQL 확인**  
   - 위 3.2 참고 (호스트: `localhost:5433`, 사용자/DB: gateway/gateway).

---

## 6. 설정 파일 정리

| 설정 | 로컬 (appsettings.Development.json) | Docker (appsettings.Docker.json + env) |
|------|-------------------------------------|----------------------------------------|
| DB | Host=localhost;Port=5433 | Host=postgres;Port=5432 |
| MQTT Server | localhost | mosquitto |
| MQTT Port | 1883 | 1883 (컨테이너 내부) |
| Kafka | localhost:9092 | kafka:9093 |
| UI → API | http://localhost:5011 | http://api:8080 |

---

## 7. 트러블슈팅

- **Docker에서 "port 1883 already allocated"**  
  → 호스트 1883 사용 중. Compose는 Mosquitto를 `1884:1883`으로 매핑해 두었으므로, Simulator는 **localhost:1884** 로 publish 하면 됩니다.

- **Docker API가 DB 연결 실패**  
  → `depends_on` 과 healthcheck로 postgres가 먼저 준비된 뒤 API가 시작합니다.  
  → DB 비밀번호/DB명이 `gateway`/`gateway` 인지, `ConnectionStrings__DefaultConnection` 이 `Host=postgres;Port=5432;...` 인지 확인하세요.

- **로컬에서는 되는데 Docker에서만 MQTT 미수신**  
  → Docker 환경에서는 반드시 **Compose 내 Mosquitto**를 쓰고, Simulator는 **localhost:1884** 로 publish 하세요 (호스트에서 실행 시).
