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

   **참고**: 
   - Secrets는 GitHub 레포지토리 Settings > Secrets and variables > Actions > New repository secret에서 추가
   - 모든 Secrets는 환경 변수로 전달되므로 값이 로그에 노출되지 않도록 주의

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

- UI App Service:
  - `ASPNETCORE_ENVIRONMENT`: Production
  - `GatewayApi__BaseUrl`: API App Service URL (자동 설정)

**수동 설정** (필요한 경우):

Azure Portal > App Service > Configuration > Application settings에서 직접 설정할 수도 있습니다:

#### API App Service
- `ASPNETCORE_ENVIRONMENT`: Production (워크플로우에서 자동 설정)
- `ConnectionStrings__DefaultConnection`: PostgreSQL 연결 문자열 (워크플로우에서 자동 설정)

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

