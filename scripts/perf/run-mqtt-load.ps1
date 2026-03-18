param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 100000)]
    [int]$TargetTps,

    [int]$RatePerProcess = 100,
    [int]$WarmupMinutes = 5,
    [int]$SteadyMinutes = 10,
    [int]$CooldownMinutes = 5,

    [string]$Broker = "localhost:1884",
    [string]$TopicTemplate = "factory/{line}/{source_id}/telemetry",

    [string]$SimulatorRoot = "",
    [string]$OutputDir = "",
    [string]$StageName = ""
)

$ErrorActionPreference = "Stop"

if ($RatePerProcess -le 0) {
    throw "RatePerProcess must be > 0."
}

$effectiveRatePerProcess = $RatePerProcess
if ($TargetTps -lt $effectiveRatePerProcess) {
    $effectiveRatePerProcess = $TargetTps
}

if ($TargetTps % $effectiveRatePerProcess -ne 0) {
    $divisors = 1..$effectiveRatePerProcess | Where-Object { $TargetTps % $_ -eq 0 }
    if ($divisors.Count -gt 0) {
        $effectiveRatePerProcess = ($divisors | Sort-Object -Descending | Select-Object -First 1)
    }
}

if ($TargetTps % $effectiveRatePerProcess -ne 0) {
    throw "TargetTps must be divisible by RatePerProcess. TargetTps=$TargetTps RatePerProcess=$RatePerProcess EffectiveRatePerProcess=$effectiveRatePerProcess"
}

$processCount = [int]($TargetTps / $effectiveRatePerProcess)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gatewayRoot = Resolve-Path (Join-Path $scriptDir "..\..")
if (-not $SimulatorRoot) {
    $SimulatorRoot = Resolve-Path (Join-Path $gatewayRoot "..\Multi-Protocol-Simulator")
}
else {
    $SimulatorRoot = Resolve-Path $SimulatorRoot
}

$simulatorExe = Join-Path $SimulatorRoot "simulator.exe"
function Get-LatestSimulatorSourceTime {
    param([string]$Root)
    $sourceDirs = @("cmd", "internal")
    $times = @()
    foreach ($dir in $sourceDirs) {
        $full = Join-Path $Root $dir
        if (Test-Path $full) {
            $times += Get-ChildItem -Path $full -Recurse -File -Include *.go | Select-Object -ExpandProperty LastWriteTimeUtc
        }
    }
    if ($times.Count -eq 0) {
        return [datetime]::MinValue
    }
    return ($times | Sort-Object -Descending | Select-Object -First 1)
}

function Ensure-SimulatorBinary {
    param(
        [string]$Root,
        [string]$BinaryPath
    )
    $needBuild = -not (Test-Path $BinaryPath)
    if (-not $needBuild) {
        $exeTime = (Get-Item $BinaryPath).LastWriteTimeUtc
        $srcTime = Get-LatestSimulatorSourceTime -Root $Root
        if ($srcTime -gt $exeTime) {
            $needBuild = $true
        }
    }

    if ($needBuild) {
        Write-Host "Building simulator.exe from latest sources..." -ForegroundColor Cyan
        Push-Location $Root
        try {
            go build -o simulator.exe ./cmd/simulator
            if ($LASTEXITCODE -ne 0) {
                throw "go build failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }
}
Ensure-SimulatorBinary -Root $SimulatorRoot -BinaryPath $simulatorExe

if (-not $StageName) {
    $StageName = "tps-$TargetTps"
}

$runId = "{0}-{1:yyyyMMdd-HHmmss}" -f $StageName, (Get-Date)
if (-not $OutputDir) {
    $OutputDir = Join-Path $gatewayRoot "artifacts\perf\$runId"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$configDir = Join-Path $OutputDir "configs"
$logDir = Join-Path $OutputDir "simulator-logs"
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

$brokerParts = $Broker.Split(":")
$brokerHost = $brokerParts[0]
$brokerPort = if ($brokerParts.Count -gt 1) { [int]$brokerParts[1] } else { 1883 }

function Get-ProcessFailureDetail {
    param(
        [Parameter(Mandatory = $true)]$ProcessInfo
    )
    $stderr = ""
    if (Test-Path $ProcessInfo.stderr) {
        $stderr = (Get-Content $ProcessInfo.stderr -Tail 40 -ErrorAction SilentlyContinue) -join [Environment]::NewLine
    }
    $stdout = ""
    if (Test-Path $ProcessInfo.stdout) {
        $stdout = (Get-Content $ProcessInfo.stdout -Tail 20 -ErrorAction SilentlyContinue) -join [Environment]::NewLine
    }
    return "pid=$($ProcessInfo.pid), exitCode=$($ProcessInfo.proc.ExitCode), config=$($ProcessInfo.config)`n--- stderr ---`n$stderr`n--- stdout ---`n$stdout"
}

function Assert-SimulatorsHealthy {
    param(
        [Parameter(Mandatory = $true)]$ProcessInfos,
        [string]$Phase = "runtime"
    )
    $failed = @($ProcessInfos | Where-Object { $_.proc.HasExited })
    if ($failed.Count -gt 0) {
        $detailLines = @()
        foreach ($f in $failed) {
            $detailLines += Get-ProcessFailureDetail -ProcessInfo $f
        }
        $detail = $detailLines -join ("`n`n")
        throw "Simulator process exited during $Phase. $detail"
    }
}

function New-SimulatorConfigContent {
    param(
        [string]$ClientId,
        [string]$SourceId,
        [string]$LineId,
        [int]$Rate
    )
@"
adapter: mqtt

generator:
  source_id: "$SourceId"
  factory_id: "Ulsan"
  equipment_type: "Sensor"
  equipment_name: "Perf Sensor $SourceId"
  random_factory: false
  random_source: false
  tags:
    - tag: "temp"
      pattern: "uniform"
      min: 20.0
      max: 70.0
      unit: "C"
      quality: "Good"
    - tag: "humidity"
      pattern: "uniform"
      min: 30.0
      max: 80.0
      unit: "%"
      quality: "Good"
    - tag: "pressure"
      pattern: "uniform"
      min: 900.0
      max: 1100.0
      unit: "hPa"
      quality: "Good"

engine:
  rate_mode: "rate"
  rate: $Rate
  jitter_percent: 5.0
  queue_size: 5000
  overflow_policy: "drop_oldest"
  retry_count: 3
  metrics_interval: 5s

mqtt:
  broker: "$brokerHost`:$brokerPort"
  client_id: "$ClientId"
  username: ""
  password: ""
  tls: false
  keepalive: 60
  qos: 0
  retain: false
  topic_template: "$TopicTemplate"
  line: "$LineId"
  reconnect_max_retries: 10
  reconnect_max_wait: 60s
  reconnect_initial_wait: 1s
"@
}

Write-Host "Starting MQTT load stage '$StageName' (TargetTps=$TargetTps, ProcessCount=$processCount, RatePerProcess=$effectiveRatePerProcess)" -ForegroundColor Cyan

$started = @()
try {
    for ($i = 1; $i -le $processCount; $i++) {
        $clientId = "sim-perf-$runId-p$i"
        $sourceId = "perf-line$i"
        $lineId = "line-$i"
        $cfgPath = Join-Path $configDir ("sim-$i.yaml")
        $cfgContent = New-SimulatorConfigContent -ClientId $clientId -SourceId $sourceId -LineId $lineId -Rate $effectiveRatePerProcess
        Set-Content -Path $cfgPath -Value $cfgContent -Encoding utf8

        $stdoutPath = Join-Path $logDir ("sim-$i.out.log")
        $stderrPath = Join-Path $logDir ("sim-$i.err.log")
        $proc = Start-Process -FilePath $simulatorExe -ArgumentList @("run", "--config", $cfgPath) -PassThru -NoNewWindow -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $started += [pscustomobject]@{
            index = $i
            pid = $proc.Id
            proc = $proc
            config = $cfgPath
            stdout = $stdoutPath
            stderr = $stderrPath
        }
    }

    Start-Sleep -Seconds 3
    Assert-SimulatorsHealthy -ProcessInfos $started -Phase "startup"

    $startUtc = (Get-Date).ToUniversalTime()
    $steadyStartUtc = $startUtc.AddMinutes($WarmupMinutes)
    $steadyEndUtc = $steadyStartUtc.AddMinutes($SteadyMinutes)
    $endUtc = $steadyEndUtc.AddMinutes($CooldownMinutes)

    Write-Host "Warmup phase: $WarmupMinutes min" -ForegroundColor Yellow
    Start-Sleep -Seconds ($WarmupMinutes * 60)
    Assert-SimulatorsHealthy -ProcessInfos $started -Phase "warmup"
    Write-Host "Steady phase: $SteadyMinutes min" -ForegroundColor Yellow
    Start-Sleep -Seconds ($SteadyMinutes * 60)
    Assert-SimulatorsHealthy -ProcessInfos $started -Phase "steady"
    Write-Host "Cooldown phase: $CooldownMinutes min" -ForegroundColor Yellow
    Start-Sleep -Seconds ($CooldownMinutes * 60)
    Assert-SimulatorsHealthy -ProcessInfos $started -Phase "cooldown"
}
finally {
    foreach ($p in $started) {
        try {
            Stop-Process -Id $p.pid -Force -ErrorAction SilentlyContinue
        }
        catch {
        }
    }
}

$result = [pscustomobject]@{
    stageName = $StageName
    runId = $runId
    targetTps = $TargetTps
    processCount = $processCount
    ratePerProcess = $effectiveRatePerProcess
    startUtc = $startUtc.ToString("o")
    steadyStartUtc = $steadyStartUtc.ToString("o")
    steadyEndUtc = $steadyEndUtc.ToString("o")
    endUtc = $endUtc.ToString("o")
    outputDir = $OutputDir
    simulators = @($started | ForEach-Object {
        [pscustomobject]@{
            index = $_.index
            pid = $_.pid
            config = $_.config
            stdout = $_.stdout
            stderr = $_.stderr
        }
    })
}

$resultPath = Join-Path $OutputDir "stage-run.json"
$result | ConvertTo-Json -Depth 5 | Set-Content -Path $resultPath -Encoding utf8
$result | ConvertTo-Json -Depth 5
