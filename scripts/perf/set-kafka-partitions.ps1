param(
    [string]$ContainerName = "gateway-kafka",
    [string]$BootstrapServer = "localhost:9092",
    [string]$Topic = "telemetry-events",
    [Parameter(Mandatory = $true)]
    [int]$PartitionCount
)

$ErrorActionPreference = "Stop"

if ($PartitionCount -lt 1) {
    throw "PartitionCount must be >= 1."
}

Write-Host "Ensuring topic '$Topic' exists..." -ForegroundColor Cyan
docker exec $ContainerName kafka-topics --bootstrap-server $BootstrapServer --create --if-not-exists --topic $Topic --partitions 1 --replication-factor 1 | Out-Null

$describe = docker exec $ContainerName kafka-topics --bootstrap-server $BootstrapServer --describe --topic $Topic
$current = 1
foreach ($line in $describe) {
    if ($line -match "PartitionCount:\s*(\d+)") {
        $current = [int]$matches[1]
        break
    }
}

if ($PartitionCount -lt $current) {
    throw "Kafka topic partitions cannot be reduced (current=$current, requested=$PartitionCount)."
}

if ($PartitionCount -eq $current) {
    Write-Host "Topic '$Topic' already has $PartitionCount partitions." -ForegroundColor Green
}
else {
    Write-Host "Updating topic '$Topic' partitions: $current -> $PartitionCount" -ForegroundColor Yellow
    docker exec $ContainerName kafka-topics --bootstrap-server $BootstrapServer --alter --topic $Topic --partitions $PartitionCount | Out-Null
}

Write-Host "Topic partition state:" -ForegroundColor Cyan
docker exec $ContainerName kafka-topics --bootstrap-server $BootstrapServer --describe --topic $Topic
