# Azure PostgreSQL with TimescaleDB 설정 가이드

## 연결 문자열 형식

Azure Database for PostgreSQL Flexible Server에서 TimescaleDB를 사용할 때의 연결 문자열 형식:

```
Host={server-name}.postgres.database.azure.com;Port=5432;Database=gateway;Username={admin-username}@{server-name};Password={password};SSL Mode=Require;Trust Server Certificate=true
```

### 예시

```
Host=gateway-postgres-dev.postgres.database.azure.com;Port=5432;Database=gateway;Username=gatewayadmin@gateway-postgres-dev;Password=YourPassword123!;SSL Mode=Require;Trust Server Certificate=true
```

## 중요 사항

### 1. Username 형식
- Azure PostgreSQL에서는 Username에 서버명을 포함해야 합니다
- 형식: `{admin-username}@{server-name}`
- 예: `gatewayadmin@gateway-postgres-dev`

### 2. SSL 연결
- Azure PostgreSQL은 기본적으로 SSL 연결을 요구합니다
- 연결 문자열에 `SSL Mode=Require` 또는 `SslMode=Require` 필수
- 자체 서명 인증서 사용 시 `Trust Server Certificate=true` 추가

### 3. TimescaleDB 확장
- Azure Database for PostgreSQL Flexible Server에서는 TimescaleDB 확장이 지원됩니다
- Migration 실행 시 자동으로 `CREATE EXTENSION IF NOT EXISTS timescaledb;` 실행
- 확장이 이미 존재하면 오류 없이 건너뜀

### 4. Migration 실행
- 애플리케이션 시작 시 자동으로 Migration이 실행됩니다 (`Program.cs`)
- TimescaleDB hypertable 생성도 자동으로 처리됩니다
- 첫 배포 시 데이터베이스가 비어있어도 정상 작동합니다

## 환경 변수 설정 (Azure App Service)

Azure Portal > App Service > Configuration > Application settings:

```
ConnectionStrings__DefaultConnection
Host=gateway-postgres-dev.postgres.database.azure.com;Port=5432;Database=gateway;Username=gatewayadmin@gateway-postgres-dev;Password=YourPassword123!;SSL Mode=Require;Trust Server Certificate=true
```

또는 GitHub Secrets를 사용하는 경우:

```
AZURE_POSTGRESQL_CONNECTION_STRING
Host=gateway-postgres-dev.postgres.database.azure.com;Port=5432;Database=gateway;Username=gatewayadmin@gateway-postgres-dev;Password=YourPassword123!;SSL Mode=Require;Trust Server Certificate=true
```

## TimescaleDB 기능

현재 구현된 TimescaleDB 기능:

1. **Hypertable**: `telemetry_events` 테이블이 TimescaleDB hypertable로 변환됨
2. **파티셔닝**: `timestamp` 컬럼 기준으로 일별 파티셔닝 (chunk_time_interval: 1 day)
3. **복합 Primary Key**: `(event_id, timestamp)` - TimescaleDB 요구사항 충족
4. **인덱스**: 시간 기반 쿼리 최적화를 위한 인덱스

## 문제 해결

### TimescaleDB 확장 생성 실패
- Azure Database for PostgreSQL Flexible Server에서 TimescaleDB 확장이 지원되는지 확인
- 서버 관리자 권한이 있는지 확인
- Migration 로그 확인

### SSL 연결 오류
- 연결 문자열에 `SSL Mode=Require` 확인
- `Trust Server Certificate=true` 추가 시도
- 방화벽 규칙 확인

### 연결 문자열 형식 오류
- Username에 `@` 기호가 올바르게 포함되었는지 확인
- Password에 특수문자(`@`, `;`, `=`, `%`)가 포함된 경우 URL 인코딩 고려

