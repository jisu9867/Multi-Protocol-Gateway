param(
    [Parameter(Mandatory = $true)]
    [string]$StageName,
    [Parameter(Mandatory = $true)]
    [int]$TargetTps,
    [Parameter(Mandatory = $true)]
    [int]$PartitionCount,
    [Parameter(Mandatory = $true)]
    [datetime]$StartUtc,
    [Parameter(Mandatory = $true)]
    [datetime]$SteadyStartUtc,
    [Parameter(Mandatory = $true)]
    [datetime]$SteadyEndUtc,
    [Parameter(Mandatory = $true)]
    [datetime]$EndUtc,
    [string]$PrometheusBaseUrl = "http://localhost:9090",
    [string]$OutputDir = "",
    [double]$ErrorRateThresholdPercent = 0.1,
    [double]$KafkaP95ThresholdSeconds = 1.0,
    [double]$MqttP95ThresholdSeconds = 0.1,
    [double]$LagRecoveryThreshold = 100
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gatewayRoot = Resolve-Path (Join-Path $scriptDir "..\..")
if (-not $OutputDir) {
    $OutputDir = Join-Path $gatewayRoot "artifacts\perf\$StageName"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

function Convert-ToUnix {
    param([datetime]$Dt)
    if ($Dt.Kind -eq [DateTimeKind]::Unspecified) {
        $Dt = [DateTime]::SpecifyKind($Dt, [DateTimeKind]::Utc)
    }
    $dto = [DateTimeOffset]$Dt
    return $dto.ToUnixTimeSeconds()
}

function Invoke-PromQueryRange {
    param(
        [string]$Query,
        [datetime]$RangeStartUtc,
        [datetime]$RangeEndUtc,
        [int]$StepSeconds = 30
    )
    $qs = [uri]::EscapeDataString($Query)
    $start = Convert-ToUnix $RangeStartUtc
    $end = Convert-ToUnix $RangeEndUtc
    $url = "$PrometheusBaseUrl/api/v1/query_range?query=$qs&start=$start&end=$end&step=$StepSeconds"
    $resp = Invoke-RestMethod -Uri $url -Method Get
    if ($resp.status -ne "success") {
        throw "Prometheus query_range failed: $Query"
    }
    return $resp.data.result
}

function Invoke-PromQueryInstant {
    param(
        [string]$Query,
        [datetime]$AtUtc
    )
    $qs = [uri]::EscapeDataString($Query)
    $time = Convert-ToUnix $AtUtc
    $url = "$PrometheusBaseUrl/api/v1/query?query=$qs&time=$time"
    $resp = Invoke-RestMethod -Uri $url -Method Get
    if ($resp.status -ne "success") {
        throw "Prometheus query failed: $Query"
    }
    return $resp.data.result
}

function Get-SeriesValues {
    param($Series)
    $values = @()
    foreach ($row in $Series.values) {
        $values += [double]$row[1]
    }
    return $values
}

function Get-Stats {
    param([double[]]$Values)
    if (-not $Values -or $Values.Count -eq 0) {
        return [pscustomobject]@{ avg = 0.0; min = 0.0; max = 0.0 }
    }
    return [pscustomobject]@{
        avg = [double](($Values | Measure-Object -Average).Average)
        min = [double](($Values | Measure-Object -Minimum).Minimum)
        max = [double](($Values | Measure-Object -Maximum).Maximum)
    }
}

function Get-FirstSeriesStats {
    param(
        [string]$Query,
        [datetime]$RangeStartUtc,
        [datetime]$RangeEndUtc
    )
    $result = Invoke-PromQueryRange -Query $Query -RangeStartUtc $RangeStartUtc -RangeEndUtc $RangeEndUtc
    if (-not $result -or $result.Count -eq 0) {
        return Get-Stats -Values @()
    }
    $values = Get-SeriesValues -Series $result[0]
    return Get-Stats -Values $values
}

function Get-ErrorRatePercent {
    param(
        [datetime]$AtUtc,
        [int]$WindowMinutes
    )
    $win = "${WindowMinutes}m"
    $errorExpr = @(
        "sum(increase(mqtt_messages_ingested_messages_total{status=`"error`"}[$win]))",
        "sum(increase(kafka_producer_messages_messages_total{status=`"error`"}[$win]))",
        "sum(increase(kafka_messages_processed_messages_total{status=`"error`"}[$win]))"
    )
    $successExpr = @(
        "sum(increase(mqtt_messages_ingested_messages_total{status=`"success`"}[$win]))",
        "sum(increase(kafka_producer_messages_messages_total{status=`"success`"}[$win]))",
        "sum(increase(kafka_messages_processed_messages_total{status=`"success`"}[$win]))"
    )

    $err = 0.0
    foreach ($expr in $errorExpr) {
        $r = Invoke-PromQueryInstant -Query $expr -AtUtc $AtUtc
        if ($r.Count -gt 0) { $err += [double]$r[0].value[1] }
    }

    $ok = 0.0
    foreach ($expr in $successExpr) {
        $r = Invoke-PromQueryInstant -Query $expr -AtUtc $AtUtc
        if ($r.Count -gt 0) { $ok += [double]$r[0].value[1] }
    }

    $total = $err + $ok
    if ($total -le 0) { return 0.0 }
    return ($err / $total) * 100.0
}

function Get-LagRecoverySeconds {
    param(
        [double[]]$LagValues,
        [datetime]$RangeStartUtc,
        [int]$StepSeconds,
        [datetime]$SteadyEndUtc,
        [double]$Threshold
    )
    if (-not $LagValues -or $LagValues.Count -eq 0) {
        return -1
    }
    $steadyEndOffset = [int][Math]::Floor(($SteadyEndUtc - $RangeStartUtc).TotalSeconds / $StepSeconds)
    if ($steadyEndOffset -lt 0) { $steadyEndOffset = 0 }
    if ($steadyEndOffset -ge $LagValues.Count) { $steadyEndOffset = $LagValues.Count - 1 }

    $baseline = $LagValues[$steadyEndOffset]
    for ($i = $steadyEndOffset; $i -lt $LagValues.Count; $i++) {
        if ($LagValues[$i] -le ($baseline + $Threshold)) {
            return ($i - $steadyEndOffset) * $StepSeconds
        }
    }
    return -1
}

$steadyMinutes = [int][Math]::Max(1, [Math]::Round(($SteadyEndUtc - $SteadyStartUtc).TotalMinutes))

$dbPersistStats = Get-FirstSeriesStats -Query "sum(rate(pipeline_persisted_events_total[1m]))" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $SteadyEndUtc
$dropStats = Get-FirstSeriesStats -Query "sum(rate(pipeline_dropped_events_total[1m]))" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $SteadyEndUtc
$ingestStats = Get-FirstSeriesStats -Query "sum(rate(mqtt_messages_ingested_messages_total{status=`"success`"}[1m]))" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $SteadyEndUtc
$producerStats = Get-FirstSeriesStats -Query "sum(rate(kafka_producer_messages_messages_total{status=`"success`"}[1m]))" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $SteadyEndUtc
$consumerStats = Get-FirstSeriesStats -Query "sum(rate(kafka_messages_processed_messages_total{status=`"success`"}[1m]))" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $SteadyEndUtc
$kafkaP95Stats = Get-FirstSeriesStats -Query "histogram_quantile(0.95, sum(rate(kafka_processing_duration_seconds_bucket[5m])) by (le))" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $SteadyEndUtc
$mqttP95Stats = Get-FirstSeriesStats -Query "histogram_quantile(0.95, sum(rate(mqtt_ingest_latency_seconds_bucket[5m])) by (le))" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $SteadyEndUtc

$lagSeries = Invoke-PromQueryRange -Query "sum(kafka_consumer_lag_messages)" -RangeStartUtc $SteadyStartUtc -RangeEndUtc $EndUtc -StepSeconds 30
$lagValues = @()
if ($lagSeries.Count -gt 0) {
    $lagValues = Get-SeriesValues -Series $lagSeries[0]
}
$steadyLagPointCount = [int][Math]::Floor(($SteadyEndUtc - $SteadyStartUtc).TotalSeconds / 30) + 1
if ($steadyLagPointCount -gt $lagValues.Count) { $steadyLagPointCount = $lagValues.Count }
$steadyLagValues = @()
if ($steadyLagPointCount -gt 0) {
    $steadyLagValues = $lagValues[0..($steadyLagPointCount - 1)]
}
$lagStats = Get-Stats -Values $steadyLagValues
$lagRecoverySeconds = Get-LagRecoverySeconds -LagValues $lagValues -RangeStartUtc $SteadyStartUtc -StepSeconds 30 -SteadyEndUtc $SteadyEndUtc -Threshold $LagRecoveryThreshold

$errorRatePercent = Get-ErrorRatePercent -AtUtc $SteadyEndUtc -WindowMinutes $steadyMinutes

$passDb = ($dbPersistStats.avg -ge $TargetTps)
$passDrop = ($dropStats.avg -le 0.01)
$passErr = ($errorRatePercent -lt $ErrorRateThresholdPercent)
$passKafkaP95 = ($kafkaP95Stats.max -le $KafkaP95ThresholdSeconds)
$passMqttP95 = ($mqttP95Stats.max -le $MqttP95ThresholdSeconds)
$passLag = ($lagStats.max -le ($lagStats.avg + 5000))

$sloPass = $passDb -and $passDrop -and $passErr -and $passKafkaP95 -and $passMqttP95 -and $passLag

$summary = [pscustomobject]@{
    stage_name = $StageName
    partition_count = $PartitionCount
    target_tps = $TargetTps
    db_persist_tps_avg = [Math]::Round($dbPersistStats.avg, 2)
    db_persist_tps_min = [Math]::Round($dbPersistStats.min, 2)
    db_persist_tps_max = [Math]::Round($dbPersistStats.max, 2)
    mqtt_ingest_tps_avg = [Math]::Round($ingestStats.avg, 2)
    kafka_produce_tps_avg = [Math]::Round($producerStats.avg, 2)
    kafka_consume_tps_avg = [Math]::Round($consumerStats.avg, 2)
    lag_avg = [Math]::Round($lagStats.avg, 2)
    lag_max = [Math]::Round($lagStats.max, 2)
    lag_recovery_seconds = $lagRecoverySeconds
    drop_rate_avg = [Math]::Round($dropStats.avg, 5)
    error_rate_percent = [Math]::Round($errorRatePercent, 5)
    kafka_p95_seconds_max = [Math]::Round($kafkaP95Stats.max, 5)
    mqtt_p95_seconds_max = [Math]::Round($mqttP95Stats.max, 5)
    pass_db_tps = $passDb
    pass_drop = $passDrop
    pass_error_rate = $passErr
    pass_kafka_p95 = $passKafkaP95
    pass_mqtt_p95 = $passMqttP95
    pass_lag = $passLag
    slo_pass = $sloPass
    start_utc = $StartUtc.ToString("o")
    steady_start_utc = $SteadyStartUtc.ToString("o")
    steady_end_utc = $SteadyEndUtc.ToString("o")
    end_utc = $EndUtc.ToString("o")
}

$jsonPath = Join-Path $OutputDir "stage-summary.json"
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $jsonPath -Encoding utf8

$csvPath = Join-Path $OutputDir "stage-summary.csv"
$csvObj = @($summary)
if (Test-Path $csvPath) {
    $csvObj | Export-Csv -Path $csvPath -NoTypeInformation -Append
}
else {
    $csvObj | Export-Csv -Path $csvPath -NoTypeInformation
}

$summary | ConvertTo-Json -Depth 5

