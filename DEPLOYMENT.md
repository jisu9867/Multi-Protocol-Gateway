# Deployment Guide

## GitHub Actions를 통한 Azure 배포

### 사전 준비

1. **Azure App Services 생성**
   - Gateway API용 App Service: `gateway-api`
   - Gateway UI용 App Service: `gateway-ui`
   - PostgreSQL 데이터베이스: Azure Database for PostgreSQL

2. **GitHub Secrets 설정**

   GitHub 레포지토리 Settings > Secrets and variables > Actions에서 다음 Secret 추가:

   - `AZURE_CREDENTIALS`: Azure Service Principal 자격 증명 (JSON 형식)

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
   - `{subscription-id}`: Azure 구독 ID
   - `{resource-group-name}`: App Service가 속한 Resource Group 이름

3. **Azure Database for PostgreSQL 연결 문자열**

   App Service의 Configuration에서 다음 연결 문자열 추가:
   ```
   ConnectionStrings__DefaultConnection
   Host=<server>.postgres.database.azure.com;Port=5432;Database=gateway;Username=<username>;Password=<password>;Ssl Mode=Require;
   ```

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

Azure App Service Configuration에서 설정:

#### API App Service
- `ASPNETCORE_ENVIRONMENT`: Production
- `ConnectionStrings__DefaultConnection`: PostgreSQL 연결 문자열
- `Sinks__JsonlFilePath`: `/home/logs/telemetry.jsonl`

#### UI App Service
- `ASPNETCORE_ENVIRONMENT`: Production
- `ApiBaseUrl`: API App Service URL

### 데이터베이스 마이그레이션

Azure에서 실행:

```bash
# Azure Cloud Shell 또는 로컬에서
export ConnectionStrings__DefaultConnection="Host=...;..."

dotnet ef database update \
  --project src/Gateway.Infrastructure/Gateway.Infrastructure.csproj \
  --startup-project src/Gateway.Api/Gateway.Api.csproj
```

또는 Azure App Service의 Kudu Console에서:
- Site Extensions > Entity Framework Core > Run Migration

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

