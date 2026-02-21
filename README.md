# Multi-Protocol Gateway - Smart Factory Integration Gateway Platform MVP

스마트팩토리 통합 게이트웨이 플랫폼 MVP

## 프로젝트 구조

```
Gateway/
├── src/
│   ├── Gateway.Core/          # 도메인 모델, 인터페이스, 파이프라인 추상화
│   ├── Gateway.Infrastructure/ # PostgreSQL, 파일 Sink 구현
│   ├── Gateway.Adapters/      # 어댑터 구현체 (FakeAdapter)
│   ├── Gateway.Api/           # Web API 호스트, DI, Health/Metrics
│   └── Gateway.Ui/            # Blazor Server UI
├── Directory.Build.props      # 공통 빌드 설정
├── docker-compose.yml         # Docker Compose 설정
└── Gateway.sln               # 솔루션 파일
```

## 기술 스택

- .NET 8 (LTS)
- ASP.NET Core Web API
- Blazor Server
- PostgreSQL (Npgsql/EF Core)
- Serilog (구조화된 로깅)

## 주요 기능

### 파이프라인
- **Ingest**: 어댑터로부터 데이터 수집
- **Normalize**: Raw 데이터를 TelemetryEvent로 정규화
- **Route**: 정규화된 이벤트를 여러 Sink로 라우팅
- **Sink**: PostgreSQL 저장 + JSONL 파일 로깅

### 어댑터
- 플러그인 구조로 확장 가능
- 현재 구현: FakeAdapter (테스트용)

### Observability
- Health Check: `/health` (어댑터 상태 포함)
- Metrics: `/metrics` (처리량, 큐 길이, 드롭률, 지연)
- Structured Logging: Serilog

## 실행 방법

### Docker Compose 사용

```bash
docker compose up --build
```

- API: http://localhost:5000
- UI: http://localhost:5001
- PostgreSQL (호스트 접속): localhost:5433
- MQTT (Simulator가 publish할 주소): **localhost:1884** (호스트 포트 1884 → 컨테이너 Mosquitto 1883)

로컬 vs Docker 환경별 설정, Simulator MQTT 대상, PostgreSQL 확인 방법은 **[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)** 참고.

### 로컬 실행

1. PostgreSQL 실행 (로컬 설치 또는 `docker compose up -d postgres` → 호스트에서 localhost:5433)

2. 데이터베이스 마이그레이션 (선택사항)
```bash
cd src/Gateway.Api
dotnet ef migrations add InitialCreate
dotnet ef database update
```

3. API 실행
```bash
cd src/Gateway.Api
dotnet run
```

4. UI 실행
```bash
cd src/Gateway.Ui
dotnet run
```

## 엔드포인트

### API

- `GET /health` - Health Check
- `GET /metrics` - Pipeline Metrics
- `GET /adapters` - Adapter Status

### UI

- `http://localhost:5001` - Gateway Dashboard

## 환경 변수

- `ConnectionStrings__DefaultConnection`: PostgreSQL 연결 문자열
- `Sinks__JsonlFilePath`: JSONL 파일 경로 (기본값: `logs/telemetry.jsonl`)
- `ASPNETCORE_ENVIRONMENT`: 환경 (Development/Production)

## CI/CD

### GitHub Actions

이 프로젝트는 GitHub Actions를 통해 자동으로 빌드 및 배포됩니다.

#### 워크플로우

1. **CI** (`.github/workflows/ci.yml`)
   - 모든 브랜치 푸시 시 실행
   - 빌드 및 테스트 수행

2. **Azure 배포** (`.github/workflows/azure-deploy.yml`)
   - `main` 브랜치 푸시 시 자동 배포
   - API 및 UI를 Azure App Service에 배포

3. **Docker 빌드** (`.github/workflows/docker-build.yml`)
   - GitHub Container Registry에 Docker 이미지 푸시

#### 설정 방법

1. **Azure App Service 생성**
   - `gateway-api`: API용 App Service
   - `gateway-ui`: UI용 App Service

2. **GitHub Secrets 설정**
   - `AZURE_WEBAPP_PUBLISH_PROFILE_API`: API App Service Publish Profile
   - `AZURE_WEBAPP_PUBLISH_PROFILE_UI`: UI App Service Publish Profile

3. **레포지토리 연결**
   ```bash
   git remote add origin https://github.com/<username>/Multi-Protocol-Gateway.git
   git push -u origin main
   ```

자세한 배포 가이드는 [DEPLOYMENT.md](DEPLOYMENT.md)를 참조하세요.

## 문서

- [트러블슈팅 가이드](../docs/TROUBLESHOOTING.md) - 개발 및 통합 과정에서 발생한 주요 이슈와 해결 방법
- [Azure PostgreSQL 설정 가이드](../docs/AZURE_POSTGRESQL_SETUP.md) - Azure App Service와 PostgreSQL Flexible Server 연동 가이드 (최저가 기준)

## 레포지토리

GitHub: `Multi-Protocol-Gateway`

