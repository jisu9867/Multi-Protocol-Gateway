param(
    [int[]]$PartitionPlan = @(1, 3, 6),
    [int[]]$TargetTpsPlan = @(100, 200, 300, 400),
    [int]$WarmupMinutes = 5,
    [int]$SteadyMinutes = 10,
    [int]$CooldownMinutes = 5,
    [switch]$StartStack,
    [string]$PrometheusBaseUrl = "http://localhost:9090",
    [string]$ArtifactsRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gatewayRoot = Resolve-Path (Join-Path $scriptDir "..\..")
if (-not $ArtifactsRoot) {
    $ArtifactsRoot = Join-Path $gatewayRoot ("artifacts\perf\baseline-{0:yyyyMMdd-HHmmss}" -f (Get-Date))
}
New-Item -ItemType Directory -Path $ArtifactsRoot -Force | Out-Null

$partitionScript = Join-Path $scriptDir "set-kafka-partitions.ps1"
$loadScript = Join-Path $scriptDir "run-mqtt-load.ps1"
$collectScript = Join-Path $scriptDir "collect-prometheus-metrics.ps1"

function Get-RunningComposeServices {
    param([string]$RootPath)
    Push-Location $RootPath
    try {
        try {
            $services = docker compose ps --services --status running 2>$null
        }
        catch {
            throw "Failed to query docker compose services. Ensure Docker Desktop is running and your account can access the Docker engine. Original error: $($_.Exception.Message)"
        }
        if (-not $services) { return @() }
        return @($services | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    finally {
        Pop-Location
    }
}

function Assert-RequiredServicesRunning {
    param(
        [string]$RootPath,
        [string[]]$RequiredServices
    )
    $running = Get-RunningComposeServices -RootPath $RootPath
    $missing = @($RequiredServices | Where-Object { $_ -notin $running })
    if ($missing.Count -gt 0) {
        throw "Required docker compose services are not running: $($missing -join ', '). Run with -StartStack or start stack manually (`"docker compose -f docker-compose.yml -f docker-compose.perf.override.yml up -d --build`")."
    }
}

if ($StartStack) {
    Write-Host "Starting gateway stack with performance override..." -ForegroundColor Cyan
    Push-Location $gatewayRoot
    try {
        docker compose -f docker-compose.yml -f docker-compose.perf.override.yml up -d --build
    }
    finally {
        Pop-Location
    }
}

$requiredServices = @("api", "kafka", "postgres", "mosquitto", "prometheus")
Assert-RequiredServicesRunning -RootPath $gatewayRoot -RequiredServices $requiredServices

$allStageSummaries = @()

foreach ($partition in $PartitionPlan) {
    Write-Host "Applying Kafka partition count: $partition" -ForegroundColor Cyan
    & $partitionScript -PartitionCount $partition

    foreach ($targetTps in $TargetTpsPlan) {
        $stageName = "p${partition}-tps${targetTps}"
        $stageDir = Join-Path $ArtifactsRoot $stageName
        New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

        Write-Host "Running load stage: $stageName" -ForegroundColor Yellow
        $runJson = & $loadScript `
            -TargetTps $targetTps `
            -WarmupMinutes $WarmupMinutes `
            -SteadyMinutes $SteadyMinutes `
            -CooldownMinutes $CooldownMinutes `
            -StageName $stageName `
            -OutputDir $stageDir

        $runInfo = $runJson | ConvertFrom-Json

        Write-Host "Collecting Prometheus metrics for $stageName" -ForegroundColor Yellow
        $summaryJson = & $collectScript `
            -StageName $stageName `
            -TargetTps $targetTps `
            -PartitionCount $partition `
            -StartUtc ([datetime]$runInfo.startUtc) `
            -SteadyStartUtc ([datetime]$runInfo.steadyStartUtc) `
            -SteadyEndUtc ([datetime]$runInfo.steadyEndUtc) `
            -EndUtc ([datetime]$runInfo.endUtc) `
            -PrometheusBaseUrl $PrometheusBaseUrl `
            -OutputDir $stageDir

        $summary = $summaryJson | ConvertFrom-Json
        $allStageSummaries += $summary
    }
}

$summaryCsv = Join-Path $ArtifactsRoot "summary-all-stages.csv"
$allStageSummaries | Export-Csv -Path $summaryCsv -NoTypeInformation

$stableTarget = $null
$limitTarget = $null
foreach ($tps in ($TargetTpsPlan | Sort-Object)) {
    $rows = @($allStageSummaries | Where-Object { [int]$_.target_tps -eq $tps })
    if ($rows.Count -eq 0) {
        continue
    }
    $allPass = ($rows | Where-Object { -not $_.slo_pass }).Count -eq 0
    if ($allPass) {
        $stableTarget = $tps
    }
    elseif (-not $limitTarget) {
        $limitTarget = $tps
    }
}

$reportPath = Join-Path $ArtifactsRoot "RESULTS.md"
$lines = @()
$lines += "# Baseline Load Test Results"
$lines += ""
$lines += "- Generated at: $(Get-Date -Format o)"
$lines += "- Stable target TPS (all partitions pass): $stableTarget"
$lines += "- Limit TPS (first failing level): $limitTarget"
$lines += ""
$lines += "| Stage | Partitions | Target TPS | Avg Persist TPS | Min Persist TPS | Avg Lag | Max Lag | Drop Rate Avg | Error Rate % | Kafka P95 (s) | MQTT P95 (s) | SLO Pass |"
$lines += "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|"
foreach ($row in $allStageSummaries) {
    $lines += "| $($row.stage_name) | $($row.partition_count) | $($row.target_tps) | $($row.db_persist_tps_avg) | $($row.db_persist_tps_min) | $($row.lag_avg) | $($row.lag_max) | $($row.drop_rate_avg) | $($row.error_rate_percent) | $($row.kafka_p95_seconds_max) | $($row.mqtt_p95_seconds_max) | $($row.slo_pass) |"
}

Set-Content -Path $reportPath -Value $lines -Encoding utf8

Write-Host "Done. Artifacts: $ArtifactsRoot" -ForegroundColor Green
