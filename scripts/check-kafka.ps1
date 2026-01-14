# Kafka 연결 상태 확인 스크립트

Write-Host "=== Kafka 연결 상태 확인 ===" -ForegroundColor Cyan

# 1. Kafka 컨테이너 상태 확인
Write-Host "`n1. Kafka 컨테이너 상태:" -ForegroundColor Yellow
docker ps --filter "name=gateway-kafka" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

# 2. Zookeeper 컨테이너 상태 확인
Write-Host "`n2. Zookeeper 컨테이너 상태:" -ForegroundColor Yellow
docker ps --filter "name=gateway-zookeeper" --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

# 3. Kafka 토픽 목록 확인
Write-Host "`n3. Kafka 토픽 목록:" -ForegroundColor Yellow
docker exec gateway-kafka kafka-topics --bootstrap-server localhost:9092 --list 2>&1

# 4. telemetry-events 토픽 상세 정보 확인
Write-Host "`n4. telemetry-events 토픽 상세 정보:" -ForegroundColor Yellow
docker exec gateway-kafka kafka-topics --bootstrap-server localhost:9092 --describe --topic telemetry-events 2>&1

# 5. Consumer Group 상태 확인
Write-Host "`n5. Consumer Group 상태:" -ForegroundColor Yellow
docker exec gateway-kafka kafka-consumer-groups --bootstrap-server localhost:9092 --list 2>&1

Write-Host "`n=== 확인 완료 ===" -ForegroundColor Green

