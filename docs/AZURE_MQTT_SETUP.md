# Azure Container Instances에 Mosquitto MQTT Broker 배포 가이드

이 가이드는 Gateway가 클라우드에서 동작할 때 MQTT broker를 Azure Container Instances에 배포하는 방법을 설명합니다.

## 빠른 시작

### Step 0: 리소스 프로바이더 등록 (필요한 경우)

Azure Container Instances를 처음 사용하는 경우, 리소스 프로바이더를 등록해야 합니다.

**PowerShell:**

```powershell
# 현재 구독 확인
az account show

# Microsoft.ContainerInstance 리소스 프로바이더 등록
az provider register --namespace Microsoft.ContainerInstance

# 등록 상태 확인 (Registered가 될 때까지 대기)
az provider show --namespace Microsoft.ContainerInstance --query "registrationState"

# 등록이 완료될 때까지 대기 (보통 1-2분 소요)
Start-Sleep -Seconds 60
```

**Bash:**

```bash
# 현재 구독 확인
az account show

# Microsoft.ContainerInstance 리소스 프로바이더 등록
az provider register --namespace Microsoft.ContainerInstance

# 등록 상태 확인 (Registered가 될 때까지 대기)
az provider show --namespace Microsoft.ContainerInstance --query "registrationState"

# 등록이 완료될 때까지 대기
sleep 60
```

⚠️ **참고**: 리소스 프로바이더 등록은 구독당 한 번만 수행하면 됩니다. 이미 등록된 경우 이 단계를 건너뛰어도 됩니다.

### Step 1: Azure Container Instances에 Mosquitto 배포

#### Bash (Linux/macOS/Git Bash)

```bash
# Azure CLI로 로그인
az login

# Resource Group 생성 (이미 있으면 생략)
az group create \
  --name rg-gateway-dev \
  --location koreacentral

# Azure Container Instances에 Mosquitto 배포
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

#### PowerShell (Windows)

```powershell
# Azure CLI로 로그인
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

또는 한 줄로 실행 (PowerShell):

```powershell
az container create --resource-group rg-gateway-dev --name mosquitto-broker --image eclipse-mosquitto:2.0 --cpu 1 --memory 1 --ports 1883 8883 --ip-address Public --dns-name-label "mosquitto-gateway-$([DateTimeOffset]::Now.ToUnixTimeSeconds())" --environment-variables MQTT_ALLOW_ANONYMOUS=true
```

출력 예시:
```
mosquitto-gateway-1704729600.koreacentral.azurecontainer.io
```

### Step 2: GitHub Secrets 설정

GitHub 레포지토리 Settings > Secrets and variables > Actions에서 다음 Secret 추가:

#### 필수 Secrets

1. **`AZURE_MQTT_BROKER_HOST`**
   - 값: 위에서 얻은 FQDN
   - 예: `mosquitto-gateway-1704729600.koreacentral.azurecontainer.io`

2. **`AZURE_MQTT_BROKER_PORT`** (선택 사항)
   - 값: `1883` (또는 TLS 사용 시 `8883`)
   - 설정하지 않으면 기본값 `1883`이 사용됩니다 (appsettings.Production.json에서)

#### 선택적 Secrets (인증이 필요한 경우)

3. **`AZURE_MQTT_USERNAME`** (선택 사항)
   - 값: MQTT broker 인증용 사용자명
   - 인증이 필요하지 않은 경우 Secret을 추가하지 않아도 됩니다

4. **`AZURE_MQTT_PASSWORD`** (선택 사항)
   - 값: MQTT broker 인증용 비밀번호
   - 인증이 필요하지 않은 경우 Secret을 추가하지 않아도 됩니다

### Step 3: Gateway 배포

`main` 브랜치에 푸시하면 자동으로 배포되며, MQTT 설정이 자동으로 적용됩니다:

```bash
git add .
git commit -m "Configure MQTT broker for Azure deployment"
git push origin main
```

### Step 4: MQTT Broker 상태 확인

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

### Step 5: Gateway Health Check

배포가 완료된 후 Gateway API의 Health Check를 확인합니다:

```bash
# Health Check 확인
curl https://gateway-api-wltn9.azurewebsites.net/health

# Adapter 상태 확인
curl https://gateway-api-wltn9.azurewebsites.net/adapters
```

## 프로덕션 환경 보안 설정 (권장)

개발/테스트 환경에서는 `MQTT_ALLOW_ANONYMOUS=true`로 설정해도 되지만, 프로덕션 환경에서는 다음을 권장합니다:

### 1. TLS/SSL 활성화

TLS/SSL을 사용하려면:
1. 인증서 생성 (Let's Encrypt 등)
2. Azure File Share에 인증서 저장
3. Container에 Volume 마운트 (Azure Container Apps 사용 권장)

### 2. 사용자 인증 활성화

```bash
# Mosquitto 사용자 추가
docker run --rm -it -v $(pwd):/config eclipse-mosquitto:2.0 \
  mosquitto_passwd -c /config/passwd gateway-user

# 암호 입력 후 생성된 passwd 파일을 Azure File Share에 업로드
```

### 3. 방화벽 규칙 설정

Azure Portal에서:
1. Container Instances 리소스 선택
2. Networking > Access > IP address restrictions 설정
3. 필요한 IP 주소만 허용

## 비용 최적화

- **Container Instances**: 실행 시간 기준 과금 (중지 시 과금 없음)
- **예상 비용**: 약 $0.001/시간 (1 CPU, 1GB RAM 기준)
- **월 예상 비용**: 약 $7 (24/7 실행 기준)

비용 절감 방법:
- 필요할 때만 실행 (자동 시작/중지 스크립트)
- Azure Container Apps 사용 (자동 스케일링, 비용 절감)
- 다른 Azure 서비스와 통합 (Azure IoT Hub 등)

## 문제 해결

### Mosquitto가 원격 연결을 거부함 ("Connection forcibly closed by remote host")

**증상:**
```
Connection error: network Error : read tcp ...->...:1883: wsarecv: An existing connection was forcibly closed by the remote host.
```

**원인:**
Mosquitto가 "local only mode"로 실행되어 원격 연결을 허용하지 않습니다. 로그를 확인하면:
```
Starting in local only mode. Connections will only be possible from clients running on this machine.
```

**해결 방법:**

Mosquitto 컨테이너를 설정 파일과 함께 다시 생성해야 합니다:

1. **기존 컨테이너 삭제:**
   ```powershell
   az container delete --resource-group rg-gateway-dev-korea-01 --name mosquitto-broker --yes
   ```

2. **설정 파일을 포함한 컨테이너 재생성:**

   **PowerShell:**
   ```powershell
   $RESOURCE_GROUP = "rg-gateway-dev-korea-01"
   $CONTAINER_NAME = "mosquitto-broker"
   $DNS_NAME_LABEL = "mosquitto-gateway-$([DateTimeOffset]::Now.ToUnixTimeSeconds())"
   
   # 명령어를 올바르게 이스케이프
   $command = '/bin/sh -c "echo listener 1883 0.0.0.0 > /mosquitto/config/mosquitto.conf && echo allow_anonymous true >> /mosquitto/config/mosquitto.conf && echo log_dest stdout >> /mosquitto/config/mosquitto.conf && echo log_type all >> /mosquitto/config/mosquitto.conf && mosquitto -c /mosquitto/config/mosquitto.conf"'
   
   az container create `
     --resource-group $RESOURCE_GROUP `
     --name $CONTAINER_NAME `
     --image eclipse-mosquitto:2.0 `
     --os-type Linux `
     --cpu 1 `
     --memory 1 `
     --ports 1883 8883 `
     --ip-address Public `
     --dns-name-label $DNS_NAME_LABEL `
     --command-line $command
   ```

   **또는 Azure Portal 사용:**
   1. Azure Portal > Container Instances > Create
   2. Basic 탭: 이름, 리소스 그룹, 이미지(`eclipse-mosquitto:2.0`) 설정
   3. Networking 탭: Public IP, 포트 1883, 8883 추가
   4. **Advanced 탭 > Command override**: 아래 명령어 입력
      ```
      /bin/sh -c "echo listener 1883 0.0.0.0 > /mosquitto/config/mosquitto.conf && echo allow_anonymous true >> /mosquitto/config/mosquitto.conf && echo log_dest stdout >> /mosquitto/config/mosquitto.conf && echo log_type all >> /mosquitto/config/mosquitto.conf && mosquitto -c /mosquitto/config/mosquitto.conf"
      ```

3. **연결 확인:**
   ```powershell
   # 로그 확인 (리스너가 0.0.0.0에 바인딩되었는지 확인)
   az container logs --resource-group rg-gateway-dev-korea-01 --name mosquitto-broker
   ```
   
   성공 시 로그에 다음이 표시됩니다:
   ```
   Opening ipv4 listen socket on 0.0.0.0:1883.
   ```

4. **Simulator 연결 테스트:**
   ```powershell
   cd Multi-Protocol-Simulator
   .\simulator.exe run --config .\configs\azure.yaml --adapter mqtt
   ```

**참고:** Azure Container Instances는 파일 마운트를 지원하지 않으므로, 설정 파일을 command를 통해 동적으로 생성해야 합니다.

### 리소스 프로바이더가 등록되지 않음

**오류 메시지:**
```
MissingSubscriptionRegistration: The subscription is not registered to use namespace 'Microsoft.ContainerInstance'
```

**해결 방법:**

1. **PowerShell에서 리소스 프로바이더 등록:**
   ```powershell
   az provider register --namespace Microsoft.ContainerInstance
   ```

2. **등록 상태 확인:**
   ```powershell
   az provider show --namespace Microsoft.ContainerInstance --query "registrationState"
   ```
   
   출력이 `"Registered"`가 될 때까지 대기하세요 (보통 1-2분 소요).

3. **등록이 완료된 후 다시 시도:**
   ```powershell
   # 1-2분 대기 후
   Start-Sleep -Seconds 60
   
   # Mosquitto 배포 다시 시도 (--os-type Linux 추가)
   az container create --resource-group rg-gateway-dev-korea-01 --name mosquitto-broker --image eclipse-mosquitto:2.0 --os-type Linux --cpu 1 --memory 1 --ports 1883 8883 --ip-address Public --dns-name-label "mosquitto-gateway-$([DateTimeOffset]::Now.ToUnixTimeSeconds())" --environment-variables MQTT_ALLOW_ANONYMOUS=true
   ```

### OS 타입 오류

**오류 메시지:**
```
InvalidOsType: The 'osType' for container group '<null>' is invalid. The value must be one of 'Windows,Linux'.
```

**해결 방법:**

PowerShell에서 Azure Container Instances를 생성할 때는 `--os-type Linux` 옵션을 명시적으로 지정해야 합니다:

```powershell
# 올바른 명령어 (--os-type Linux 추가)
az container create `
  --resource-group rg-gateway-dev-korea-01 `
  --name mosquitto-broker `
  --image eclipse-mosquitto:2.0 `
  --os-type Linux `
  --cpu 1 `
  --memory 1 `
  --ports 1883 8883 `
  --ip-address Public `
  --dns-name-label "mosquitto-gateway-$([DateTimeOffset]::Now.ToUnixTimeSeconds())" `
  --environment-variables MQTT_ALLOW_ANONYMOUS=true
```

### MQTT Broker에 연결할 수 없음

1. **FQDN 확인**:
   
   **Bash:**
   ```bash
   az container show \
     --resource-group rg-gateway-dev \
     --name mosquitto-broker \
     --query ipAddress.fqdn -o tsv
   ```
   
   **PowerShell:**
   ```powershell
   az container show `
     --resource-group rg-gateway-dev `
     --name mosquitto-broker `
     --query ipAddress.fqdn -o tsv
   ```

2. **Container 상태 확인**:
   
   **Bash:**
   ```bash
   az container show \
     --resource-group rg-gateway-dev \
     --name mosquitto-broker \
     --query instanceView.state -o tsv
   ```
   
   **PowerShell:**
   ```powershell
   az container show `
     --resource-group rg-gateway-dev `
     --name mosquitto-broker `
     --query instanceView.state -o tsv
   ```
   
   상태가 `Running`이어야 합니다.

3. **포트 확인**:
   
   **Bash:**
   ```bash
   # 1883 포트가 열려있는지 확인
   telnet mosquitto-gateway-xxx.koreacentral.azurecontainer.io 1883
   ```
   
   **PowerShell:**
   ```powershell
   # Test-NetConnection 사용 (PowerShell 4.0+)
   Test-NetConnection -ComputerName mosquitto-gateway-xxx.koreacentral.azurecontainer.io -Port 1883
   ```

### Gateway가 MQTT 메시지를 수신하지 않음

1. **Gateway 로그 확인**:
   - Azure Portal > App Service > Log stream
   
   **Bash/PowerShell:**
   ```bash
   az webapp log tail --name gateway-api-wltn9 --resource-group rg-gateway-dev
   ```

2. **Adapter Health Check 확인**:
   ```bash
   curl https://gateway-api-wltn9.azurewebsites.net/adapters
   ```

3. **환경 변수 확인**:
   - Azure Portal > App Service > Configuration > Application settings
   - `Adapters__Mqtt__Server`가 올바른 FQDN인지 확인
   - `Adapters__Mqtt__Enabled`가 `true`인지 확인

### Container가 계속 재시작됨

1. **로그 확인**:
   ```bash
   az container logs \
     --resource-group rg-gateway-dev \
     --name mosquitto-broker
   ```

2. **리소스 부족 확인**:
   - CPU/Memory 할당량 확인
   - 필요시 CPU/Memory 증가

## 대안 옵션

### Azure Container Apps (권장)

Azure Container Apps는 더 많은 기능을 제공합니다:
- Volume 마운트 지원
- Auto-scaling
- 더 나은 모니터링
- 더 낮은 비용 (사용량 기반)

```bash
# Azure Container Apps 환경 생성
az containerapp env create \
  --name gateway-env \
  --resource-group rg-gateway-dev \
  --location koreacentral

# Mosquitto 배포
az containerapp create \
  --name mosquitto-broker \
  --resource-group rg-gateway-dev \
  --environment gateway-env \
  --image eclipse-mosquitto:2.0 \
  --target-port 1883 \
  --ingress external \
  --min-replicas 1 \
  --max-replicas 1 \
  --cpu 1.0 \
  --memory 1.0Gi \
  --env-vars MQTT_ALLOW_ANONYMOUS=true
```

### 클라우드 MQTT 서비스

완전 관리형 MQTT 서비스:
- **HiveMQ Cloud**: https://www.hivemq.com/cloud/
- **EMQX Cloud**: https://www.emqx.com/en/cloud
- **Azure IoT Hub**: MQTT 3.1.1 지원 (추가 구성 필요)

## 참고 자료

- [Azure Container Instances 문서](https://docs.microsoft.com/azure/container-instances/)
- [Mosquitto 문서](https://mosquitto.org/documentation/)
- [MQTT 프로토콜 스펙](https://mqtt.org/)

