# Deployment Guide

## GitHub Actions를 통한 Azure 배포

### 사전 준비

1. **Azure App Services 생성**
   - Gateway API용 App Service: `gateway-api`
   - Gateway UI용 App Service: `gateway-ui`
   - PostgreSQL 데이터베이스: Azure Database for PostgreSQL

2. **GitHub Secrets 설정**

   GitHub 레포지토리 Settings > Secrets and variables > Actions에서 다음 Secret 추가:

   **필수 Secrets:**
   
   - **`AZURE_CREDENTIALS`**: Azure Service Principal 자격 증명 (JSON 형식)

     Azure Service Principal 생성 방법:
     
     ```bash
     # Azure CLI로 로그인
     az login

     # Service Principal 생성 (Resource Group에 Contributor 역할 부여)
     az ad sp create-for-rbac --name "github-actions-gateway" \
       --role contributor \
       --scopes /subscriptions/{subscription-id}/resourceGroups/{resource-group-name} \
       --sdk-auth
     ```
     
     위 명령어의 출력 결과(JSON)를 그대로 복사하여 GitHub Secrets의 `AZURE_CREDENTIALS`에 추가하세요.
     
     참고:
     - `{subscription-id}`: Azure 구독 ID (`az account show --query id -o tsv`로 확인)
     - `{resource-group-name}`: App Service가 속한 Resource Group 이름

   - **`AZURE_RESOURCE_GROUP`**: App Service가 속한 Resource Group 이름
     - 예: `rg-gateway-dev`

   - **`AZURE_POSTGRESQL_CONNECTION_STRING`**: Azure PostgreSQL 연결 문자열
     
     형식:
     ```
     Host={server-name}.postgres.database.azure.com;Port=5432;Database=gateway;Username={admin-username}@{server-name};Password={password};SSL Mode=Require;Trust Server Certificate=true
     ```
     
     예시:
     ```
     Host=gateway-postgres-dev-1234.postgres.database.azure.com;Port=5432;Database=gateway;Username=gatewayadmin@gateway-postgres-dev-1234;Password=YourSecurePassword123!;SSL Mode=Require;Trust Server Certificate=true
     ```
     
     ⚠️ **중요**:
     - Username 형식: `{admin-username}@{server-name}` (서버명 포함 필수!)
     - Password에 특수문자(`@`, `;`, `=`, `%`) 포함 시 URL 인코딩 필요하거나, GitHub Secrets에 그대로 입력해도 됨
     - 연결 문자열은 GitHub Actions 워크플로우가 자동으로 App Service에 설정합니다

   - **`AZURE_MQTT_BROKER_HOST`**: Azure Container Instances에 배포된 Mosquitto MQTT broker의 FQDN (Fully Qualified Domain Name)
     
     예시: `mosquitto-gateway-12345678.koreacentral.azurecontainer.io`
     
     ⚠️ **중요**: 이 값은 아래 "MQTT Broker (Azure Container Instances) 배포" 섹션에서 얻은 FQDN을 사용합니다.

   - **`AZURE_MQTT_BROKER_PORT`**: MQTT broker 포트 (기본값: `1883`)
     
     ⚠️ **선택 사항**: 설정하지 않으면 기본값 `1883`이 사용됩니다. TLS 사용 시 `8883`으로 설정하세요.

   - **`AZURE_MQTT_USERNAME`**: MQTT broker 인증용 사용자명 (선택 사항)
     
     ⚠️ **선택 사항**: MQTT broker가 인증을 요구하지 않는 경우 빈 문자열(`""`)로 설정하거나 Secret을 추가하지 않아도 됩니다.

   - **`AZURE_MQTT_PASSWORD`**: MQTT broker 인증용 비밀번호 (선택 사항)
     
     ⚠️ **선택 사항**: MQTT broker가 인증을 요구하지 않는 경우 빈 문자열(`""`)로 설정하거나 Secret을 추가하지 않아도 됩니다.

   **참고**: 
   - Secrets는 GitHub 레포지토리 Settings > Secrets and variables > Actions > New repository secret에서 추가
   - 모든 Secrets는 환경 변수로 전달되므로 값이 로그에 노출되지 않도록 주의

### MQTT Broker (Azure Container Instances) 배포

Gateway가 Edge Gateway로부터 MQTT 메시지를 수신하려면 MQTT broker가 필요합니다. Azure Container Instances에 Mosquitto를 배포하는 방법:

#### Step 0: 리소스 프로바이더 등록 (필요한 경우)

Azure Container Instances를 처음 사용하는 경우, 리소스 프로바이더를 등록해야 합니다:

**PowerShell:**

```powershell
# Microsoft.ContainerInstance 리소스 프로바이더 등록
az provider register --namespace Microsoft.ContainerInstance

# 등록 상태 확인 (Registered가 될 때까지 대기)
az provider show --namespace Microsoft.ContainerInstance --query "registrationState"

# 등록이 완료될 때까지 대기 (보통 1-2분 소요)
Start-Sleep -Seconds 60
```

**Bash:**

```bash
# Microsoft.ContainerInstance 리소스 프로바이더 등록
az provider register --namespace Microsoft.ContainerInstance

# 등록 상태 확인
az provider show --namespace Microsoft.ContainerInstance --query "registrationState"

# 등록이 완료될 때까지 대기
sleep 60
```

⚠️ **참고**: 리소스 프로바이더 등록은 구독당 한 번만 수행하면 됩니다.

#### Step 1: Azure Container Instances에 Mosquitto 배포

**Bash (Linux/macOS/Git Bash):**

```bash
# Azure CLI로 로그인 (이미 로그인했으면 생략)
az login

# Resource Group 생성 (이미 있으면 생략)
az group create \
  --name rg-gateway-dev \
  --location koreacentral

# Azure Container Instances에 Mosquitto 배포
# 참고: DNS name label은 전역적으로 고유해야 하므로 타임스탬프를 추가합니다
az container create \
  --resource-group rg-gateway-dev \
  --name mosquitto-broker \
  --image eclipse-mosquitto:2.0 \
  --cpu 1 \
  --memory 1 \
  --ports 1883 8883 \
  --ip-address Public \
  --dns-name-label mosquitto-gateway-$(date +%s) \
  --environment-variables \
    MQTT_ALLOW_ANONYMOUS=true

# FQDN 확인 (이 값을 AZURE_MQTT_BROKER_HOST Secret에 설정)
az container show \
  --resource-group rg-gateway-dev \
  --name mosquitto-broker \
  --query ipAddress.fqdn -o tsv
```

**PowerShell (Windows):**

```powershell
# Azure CLI로 로그인 (이미 로그인했으면 생략)
az login

# Resource Group 생성 (이미 있으면 생략)
az group create `
  --name rg-gateway-dev `
  --location koreacentral

# DNS name label 생성 (고유한 값)
$dnsNameLabel = "mosquitto-gateway-$([DateTimeOffset]::Now.ToUnixTimeSeconds())"

# Azure Container Instances에 Mosquitto 배포
az container create `
  --resource-group rg-gateway-dev `
  --name mosquitto-broker `
  --image eclipse-mosquitto:2.0 `
  --os-type Linux `
  --cpu 1 `
  --memory 1 `
  --ports 1883 8883 `
  --ip-address Public `
  --dns-name-label $dnsNameLabel `
  --environment-variables MQTT_ALLOW_ANONYMOUS=true

# FQDN 확인 (이 값을 AZURE_MQTT_BROKER_HOST Secret에 설정)
az container show `
  --resource-group rg-gateway-dev `
  --name mosquitto-broker `
  --query ipAddress.fqdn -o tsv
```

**PowerShell 한 줄 실행 (더 간단):**

```powershell
az container create --resource-group rg-gateway-dev --name mosquitto-broker --image eclipse-mosquitto:2.0 --os-type Linux --cpu 1 --memory 1 --ports 1883 8883 --ip-address Public --dns-name-label "mosquitto-gateway-$([DateTimeOffset]::Now.ToUnixTimeSeconds())" --environment-variables MQTT_ALLOW_ANONYMOUS=true
```

출력 예시:
```
mosquitto-gateway-1704729600.koreacentral.azurecontainer.io
```

#### Step 2: GitHub Secrets에 MQTT 설정 추가

위에서 얻은 FQDN을 사용하여 GitHub Secrets 설정:

1. **`AZURE_MQTT_BROKER_HOST`**: 위에서 얻은 FQDN
   - 예: `mosquitto-gateway-1704729600.koreacentral.azurecontainer.io`

2. **`AZURE_MQTT_BROKER_PORT`**: `1883` (또는 TLS 사용 시 `8883`)

3. **`AZURE_MQTT_USERNAME`** (선택): 인증이 필요한 경우 사용자명
   - 위 예시에서는 `MQTT_ALLOW_ANONYMOUS=true`로 설정했으므로 빈 문자열(`""`)로 설정하거나 Secret을 추가하지 않아도 됩니다.

4. **`AZURE_MQTT_PASSWORD`** (선택): 인증이 필요한 경우 비밀번호
   - 위 예시에서는 `MQTT_ALLOW_ANONYMOUS=true`로 설정했으므로 빈 문자열(`""`)로 설정하거나 Secret을 추가하지 않아도 됩니다.

#### Step 3: MQTT Broker 상태 확인

**Bash:**

```bash
# Container 상태 확인
az container show \
  --resource-group rg-gateway-dev \
  --name mosquitto-broker \
  --query "{Status:instanceView.state, FQDN:ipAddress.fqdn, IP:ipAddress.ip}" -o table

# 로그 확인
az container logs \
  --resource-group rg-gateway-dev \
  --name mosquitto-broker
```

**PowerShell:**

```powershell
# Container 상태 확인
az container show `
  --resource-group rg-gateway-dev `
  --name mosquitto-broker `
  --query "{Status:instanceView.state, FQDN:ipAddress.fqdn, IP:ipAddress.ip}" -o table

# 로그 확인
az container logs `
  --resource-group rg-gateway-dev `
  --name mosquitto-broker
```

#### Step 4: 보안 고려 사항 (프로덕션 환경)

**개발/테스트 환경**에서는 `MQTT_ALLOW_ANONYMOUS=true`로 설정해도 되지만, **프로덕션 환경**에서는 다음을 권장합니다:

1. **TLS/SSL 활성화**: 포트 `8883` 사용
2. **사용자 인증**: `MQTT_ALLOW_ANONYMOUS=false` 및 사용자명/비밀번호 설정
3. **방화벽 규칙**: 필요한 IP만 접근 허용

TLS 및 인증을 사용하는 Mosquitto 설정 예시:

```bash
# Mosquitto 설정 파일 생성 (로컬)
cat > mosquitto.conf <<EOF
listener 1883
listener 8883
protocol mqtt
allow_anonymous false
password_file /mosquitto/config/passwd

# TLS 설정
cafile /mosquitto/config/ca.crt
certfile /mosquitto/config/server.crt
keyfile /mosquitto/config/server.key
require_certificate false
EOF

# 사용자 추가
docker run --rm -it -v $(pwd):/config eclipse-mosquitto:2.0 \
  mosquitto_passwd -c /config/passwd gateway-user

# Azure Container Instances에 파일 마운트 (Volume 마운트 지원 필요)
# 또는 Azure File Share를 사용하여 설정 파일 마운트
```

⚠️ **참고**: Azure Container Instances는 기본적으로 Volume 마운트를 지원하지 않습니다. 파일 기반 설정이 필요한 경우:
- Azure Container Apps 사용 (권장)
- Azure File Share 마운트
- 설정을 환경 변수로 전달

#### MQTT Broker 대안

Azure Container Instances 대신 다음 옵션도 고려할 수 있습니다:

1. **Azure Container Apps**: Volume 마운트, Auto-scaling 지원
2. **Azure Virtual Machines**: 완전한 제어 가능
3. **클라우드 MQTT 서비스**: HiveMQ Cloud, EMQX Cloud (완전 관리형)
4. **Azure IoT Hub**: MQTT 3.1.1 지원, 완전 관리형 (추가 구성 필요)

### 배포 프로세스

#### GitHub Actions 사용

1. **레포지토리 푸시**
   ```bash
   git remote add origin https://github.com/<username>/Multi-Protocol-Gateway.git
   git push -u origin main
   ```

2. **자동 배포**
   - `main` 브랜치에 푸시하면 자동으로 빌드 및 배포 실행
   - GitHub Actions 탭에서 진행 상황 확인

#### 수동 배포

```bash
# 빌드
dotnet publish src/Gateway.Api/Gateway.Api.csproj -c Release -o ./publish/api
dotnet publish src/Gateway.Ui/Gateway.Ui.csproj -c Release -o ./publish/ui

# Azure CLI로 배포
az webapp deploy --resource-group <resource-group> --name gateway-api --src-path ./publish/api
az webapp deploy --resource-group <resource-group> --name gateway-ui --src-path ./publish/ui
```

### Docker 이미지 배포

GitHub Container Registry (ghcr.io)에 자동으로 푸시됩니다:

```bash
# 이미지 풀
docker pull ghcr.io/<username>/Multi-Protocol-Gateway/gateway-api:main
docker pull ghcr.io/<username>/Multi-Protocol-Gateway/gateway-ui:main
```

### 환경 변수 설정

**자동 설정** (GitHub Actions 워크플로우가 자동으로 설정):
- API App Service:
  - `ASPNETCORE_ENVIRONMENT`: Production
  - `ConnectionStrings__DefaultConnection`: GitHub Secret의 `AZURE_POSTGRESQL_CONNECTION_STRING` 값
  - `Adapters__Mqtt__Enabled`: `true`
  - `Adapters__Mqtt__Server`: GitHub Secret의 `AZURE_MQTT_BROKER_HOST` 값
  - `Adapters__Mqtt__Port`: GitHub Secret의 `AZURE_MQTT_BROKER_PORT` 값 (기본값: `1883`)
  - `Adapters__Mqtt__Topic`: `factory/+/+/telemetry`
  - `Adapters__Mqtt__Username`: GitHub Secret의 `AZURE_MQTT_USERNAME` 값 (선택 사항, 기본값: 빈 문자열)
  - `Adapters__Mqtt__Password`: GitHub Secret의 `AZURE_MQTT_PASSWORD` 값 (선택 사항, 기본값: 빈 문자열)

- UI App Service:
  - `ASPNETCORE_ENVIRONMENT`: Production
  - `GatewayApi__BaseUrl`: API App Service URL (자동 설정)

**수동 설정** (필요한 경우):

Azure Portal > App Service > Configuration > Application settings에서 직접 설정할 수도 있습니다:

#### API App Service
- `ASPNETCORE_ENVIRONMENT`: Production (워크플로우에서 자동 설정)
- `ConnectionStrings__DefaultConnection`: PostgreSQL 연결 문자열 (워크플로우에서 자동 설정)
- `Adapters__Mqtt__Enabled`: `true` (워크플로우에서 자동 설정)
- `Adapters__Mqtt__Server`: MQTT broker FQDN (워크플로우에서 자동 설정)
- `Adapters__Mqtt__Port`: MQTT broker 포트 (워크플로우에서 자동 설정)
- `Adapters__Mqtt__Topic`: `factory/+/+/telemetry` (워크플로우에서 자동 설정)
- `Adapters__Mqtt__Username`: MQTT 사용자명 (워크플로우에서 자동 설정, 선택 사항)
- `Adapters__Mqtt__Password`: MQTT 비밀번호 (워크플로우에서 자동 설정, 선택 사항)

#### UI App Service
- `ASPNETCORE_ENVIRONMENT`: Production (워크플로우에서 자동 설정)
- `GatewayApi__BaseUrl`: `https://{api-app-name}.azurewebsites.net` (워크플로우에서 자동 설정)

⚠️ **참고**: GitHub Actions 워크플로우가 자동으로 설정하므로, GitHub Secrets만 올바르게 설정하면 수동 설정은 불필요합니다.

### 데이터베이스 마이그레이션

**자동 적용** (권장):
- 애플리케이션이 시작될 때 자동으로 Migration이 적용됩니다 (`Program.cs`에 구현됨)
- 배포 후 첫 실행 시 자동으로 데이터베이스 스키마가 생성/업데이트됩니다

**수동 적용** (필요한 경우):

로컬에서 Azure DB에 직접 연결하여 Migration 적용:

```bash
# Azure Cloud Shell 또는 로컬에서 (방화벽 규칙에 IP 추가 필요)
export ConnectionStrings__DefaultConnection="Host={server}.postgres.database.azure.com;Port=5432;Database=gateway;Username={user}@{server};Password={password};SSL Mode=Require;Trust Server Certificate=true"

dotnet ef database update \
  --project src/Gateway.Infrastructure/Gateway.Infrastructure.csproj \
  --startup-project src/Gateway.Api/Gateway.Api.csproj
```

또는 Azure App Service의 Kudu Console에서:
- Site Extensions > Entity Framework Core > Run Migration
- 또는 SSH/Kudu Console에서 직접 `dotnet ef database update` 실행

### 모니터링

- **Application Insights**: Azure Portal에서 설정
- **Health Checks**: `https://<api-app>.azurewebsites.net/health`
- **Metrics**: `https://<api-app>.azurewebsites.net/metrics`

## Azure Pipelines 사용 (선택사항)

Azure DevOps를 사용하는 경우 `azure-pipelines.yml` 파일을 사용합니다.

1. Azure DevOps 프로젝트 생성
2. Pipeline 생성 및 `azure-pipelines.yml` 연결
3. Azure Service Connection 설정
4. Pipeline 실행

