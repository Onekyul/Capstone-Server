# =============================================================================
# run_single_measurement.ps1
# Run a single measurement
#
# Usage:
#   ./run_single_measurement.ps1 -VU 100 -BatchSize 50 -Repeat 1 `
#       -ExperimentId "E4-r1" -Mode writeback
#
# Prerequisites:
#   - $env:MYSQL_PASSWORD set (MySQL root password)
#   - Docker running (Capstone_mysql, Capstone_redis)
#   - k6 installed and on PATH
# =============================================================================

param(
    [Parameter(Mandatory)][int]    $VU,
    [Parameter(Mandatory)][int]    $BatchSize,
    [Parameter(Mandatory)][int]    $Repeat,
    [Parameter(Mandatory)][string] $ExperimentId,
    [string] $Mode       = "writeback",
    [string] $OutputDir  = "$PSScriptRoot\results",
    [string] $TargetUrl  = "http://localhost:7200",
    [int]    $UserBase   = 1001,
    [string] $ServerPath = "C:\Users\kyul\Desktop\Capstone-Server\GameServer",
    [string] $ScenarioPath = "$PSScriptRoot\..\scenario.js",
    [int]    $ServerWarmupSec = 30,
    [int]    $IntervalSec     = 300   # interval between measurements (5 min default)
)

# =============================================================================
# Initialization
# =============================================================================
$ErrorActionPreference = "Stop"

$logsDir = "$OutputDir\logs"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $logsDir   | Out-Null

$logFile     = "$logsDir\$ExperimentId.log"
$k6JsonFile  = "$OutputDir\$ExperimentId-raw.json"
$summaryFile = "$OutputDir\$ExperimentId-summary.csv"
$summaryAll  = "$OutputDir\summary.csv"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $ts  = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$ts][$Level] $Message"
    Write-Host $line
    Add-Content -Path $logFile -Value $line
}

function Stop-Server {
    $procs = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
    if ($procs) {
        Write-Log "Stopping existing dotnet process(es)..."
        $procs | Stop-Process -Force
        Start-Sleep -Seconds 3
        Write-Log "dotnet process(es) stopped."
    }
}

Write-Log "===== Measurement start: $ExperimentId (VU=$VU, BatchSize=$BatchSize, Mode=$Mode, Repeat=$Repeat) ====="

# =============================================================================
# [Variable control] Triple initialization -- ensures every measurement starts
# from an identical baseline. Residual Redis queue items, DB data, and server
# state from a prior run must not bleed into the next measurement.
# This satisfies the reproducibility requirement stated in paper Chapter 3.
# =============================================================================

# Step 1: Stop server
Write-Log "Step 1: Stop existing server"
Stop-Server
Write-Log "[OK] Step 1 complete"

# Step 2: Reset DB
Write-Log "Step 2: Reset DB (clear test user data)"
$mysqlPwd = $env:MYSQL_PASSWORD
if (-not $mysqlPwd) {
    Write-Log "MYSQL_PASSWORD env var not set. Using default 'PASSWORD'." "WARN"
    $mysqlPwd = "PASSWORD"
}
$resetSql = "$PSScriptRoot\reset_test_users.sql"

# Copy SQL file into container
Write-Log "Step 2a: Copying SQL file to container..."
docker cp $resetSql Capstone_mysql:/tmp/reset_test_users.sql 2>&1 | Out-Null
Write-Log "Step 2a: Copy complete"

# Execute SQL with timeout via a background job
Write-Log "Step 2b: Executing reset SQL (timeout=30s)..."
$dbJob = Start-Job -ScriptBlock {
    param($pwd, $sqlFile)
    # Redirect stderr to stdout so warnings don't block; pipe SQL file directly
    docker exec -i Capstone_mysql `
        mysql -u root -p"$pwd" --batch --silent GameDB `
        -e "$(Get-Content $sqlFile -Raw)" 2>&1
} -ArgumentList $mysqlPwd, $resetSql

$dbCompleted = Wait-Job -Job $dbJob -Timeout 30
if ($dbCompleted) {
    $dbResult = Receive-Job -Job $dbJob
    Remove-Job -Job $dbJob -Force
    Write-Log "DB reset result: $dbResult"
    Write-Log "[OK] Step 2 complete"
} else {
    Stop-Job  -Job $dbJob
    Remove-Job -Job $dbJob -Force
    Write-Log "DB reset timed out after 30s. Proceeding anyway." "WARN"
}

# Step 3: Clear Redis queue
Write-Log "Step 3: Clear Redis queue (DEL task:writeback)"
$redisDel = docker exec Capstone_redis redis-cli DEL task:writeback
Write-Log "Redis DEL result: $redisDel"
Write-Log "[OK] Step 3 complete"

# Step 4: Set BatchSize env var
Write-Log "Step 4: Set BatchSize env var (DbSyncWorker__BatchSize=$BatchSize)"
$env:DbSyncWorker__BatchSize = "$BatchSize"
Write-Log "[OK] Step 4 complete"

# Step 5: Start server in background
Write-Log "Step 5: Start server (BatchSize=$BatchSize)"
$serverLogFile = "$logsDir\$ExperimentId-server.log"
$serverProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--launch-profile", "http" `
    -WorkingDirectory $ServerPath `
    -PassThru `
    -RedirectStandardOutput $serverLogFile `
    -RedirectStandardError "$logsDir\$ExperimentId-server-err.log"

Write-Log "Server process started (PID: $($serverProcess.Id))"
Write-Log "[OK] Step 5 complete"

# Step 6: Wait for server readiness (poll up to 30 seconds)
Write-Log "Step 6: Waiting for server readiness (max $ServerWarmupSec seconds)"
$ready    = $false
$elapsed  = 0
while ($elapsed -lt $ServerWarmupSec) {
    Start-Sleep -Seconds 2
    $elapsed += 2
    try {
        $probe = Invoke-WebRequest -Uri "$TargetUrl/metrics" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($probe.StatusCode -eq 200) {
            $ready = $true
            Write-Log "Server ready ($elapsed seconds elapsed)"
            break
        }
    } catch { }
}
if (-not $ready) {
    Write-Log "Server did not respond within ${ServerWarmupSec}s. Aborting measurement." "ERROR"
    Stop-Server
    throw "Server readiness check failed: $ExperimentId"
}
Write-Log "[OK] Step 6 complete"

# Step 7: Record measurement start time
$startTime = Get-Date
Write-Log "Step 7: Measurement start time: $startTime"
Write-Log "[OK] Step 7 complete"

# Step 8: Run k6
Write-Log "Step 8: Running k6 (VU=$VU, MODE=$Mode)"
$k6SummaryFile = "$OutputDir\$ExperimentId-k6summary.json"
$k6Args = @(
    "run",
    "--env", "VU=$VU",
    "--env", "MODE=$Mode",
    "--env", "TARGET_URL=$TargetUrl",
    "--env", "USER_BASE=$UserBase",
    "--out",            "json=$k6JsonFile",       # raw JSONL (for archival)
    "--summary-export", $k6SummaryFile,           # end-of-test summary JSON (for parsing)
    $ScenarioPath
)
$k6LogFile = "$logsDir\$ExperimentId-k6.log"
$k6Process = Start-Process -FilePath "k6" `
    -ArgumentList $k6Args `
    -PassThru `
    -Wait `
    -RedirectStandardOutput $k6LogFile `
    -RedirectStandardError "$logsDir\$ExperimentId-k6-err.log"

$k6ExitCode = $k6Process.ExitCode
Write-Log "k6 finished. Exit code: $k6ExitCode"
Write-Log "[OK] Step 8 complete"

# Step 9: Record measurement end time
$endTime = Get-Date
$durationSec = ($endTime - $startTime).TotalSeconds
Write-Log "Step 9: Measurement end time: $endTime (elapsed: $([int]$durationSec)s)"
Write-Log "[OK] Step 9 complete"

# Step 10: Extract Prometheus metrics
Write-Log "Step 10: Extracting Prometheus metrics"
$metricsRaw    = ""
$e2eAvg        = "N/A"
$e2eCount      = 0
$queueBacklog  = "N/A"
$batchItems    = 0
try {
    $metricsRaw = (Invoke-WebRequest -Uri "$TargetUrl/metrics" -UseBasicParsing).Content

    # E2E latency average = sum / count
    $e2eSumMatch   = $metricsRaw | Select-String 'writeback_e2e_latency_seconds_sum\{batch_size="(\d+)"\}\s+([\d.]+)'
    $e2eCountMatch = $metricsRaw | Select-String 'writeback_e2e_latency_seconds_count\{batch_size="(\d+)"\}\s+([\d.]+)'
    if ($e2eSumMatch -and $e2eCountMatch) {
        $e2eSum   = [double]$e2eSumMatch.Matches[0].Groups[2].Value
        $e2eCount = [int]$e2eCountMatch.Matches[0].Groups[2].Value
        if ($e2eCount -gt 0) {
            $e2eAvg = [math]::Round($e2eSum / $e2eCount, 4)
        }
    }

    # Queue backlog last value
    $queueMatch = $metricsRaw | Select-String 'writeback_queue_length\{batch_size="(\d+)"\}\s+([\d.]+)'
    if ($queueMatch) {
        $queueBacklog = $queueMatch.Matches[0].Groups[2].Value
    }

    # Total batch items processed
    $batchMatch = $metricsRaw | Select-String 'writeback_batch_processed_items_total\{batch_size="(\d+)"\}\s+([\d.]+)'
    if ($batchMatch) {
        $batchItems = [int]$batchMatch.Matches[0].Groups[2].Value
    }

    Write-Log "Prometheus - e2e_avg=${e2eAvg}s, e2e_count=${e2eCount}, queue=${queueBacklog}, batch_items=${batchItems}"
} catch {
    Write-Log "Failed to extract Prometheus metrics: $_" "WARN"
}
Write-Log "[OK] Step 10 complete"

# Step 11: Parse k6 end-of-test summary JSON
# k6 --summary-export produces a single well-formed JSON file, unlike the
# streaming JSONL of --out json. All aggregated metrics are available here.
Write-Log "Step 11: Parsing k6 summary JSON ($k6SummaryFile)"
$rps        = "N/A"
$httpP50    = "N/A"
$httpP90    = "N/A"
$httpP95    = "N/A"
$httpP99    = "N/A"
$saveAvg    = "N/A"
$saveP95    = "N/A"
$errorCount = 0
$errorRate  = "N/A"

try {
    if (-not (Test-Path $k6SummaryFile)) {
        Write-Log "k6 summary file not found: $k6SummaryFile" "WARN"
    } else {
        $k6Summary = Get-Content $k6SummaryFile -Raw | ConvertFrom-Json
        $m = $k6Summary.metrics

        # RPS — http_reqs.rate (Counter metric: values are direct properties)
        if ($null -ne $m.http_reqs) {
            $rps = [math]::Round($m.http_reqs.rate, 3)
        }

        # http_req_duration percentiles (Trend metric, ms)
        # Values are direct properties — no .values wrapper in --summary-export format.
        # p(99) is not computed by default in k6; remains N/A.
        if ($null -ne $m.http_req_duration) {
            $d       = $m.http_req_duration
            $httpP50 = [math]::Round($d.med,      2)
            $httpP90 = [math]::Round($d.'p(90)',  2)
            $httpP95 = [math]::Round($d.'p(95)',  2)
            # $httpP99 stays N/A — not in default k6 output
        }

        # Custom metric: save_latency (Trend, ms)
        if ($null -ne $m.save_latency) {
            $sl      = $m.save_latency
            $saveAvg = [math]::Round($sl.avg,     2)
            $saveP95 = [math]::Round($sl.'p(95)', 2)
        }

        # Custom metric: save_failure (Counter) — key absent when count is 0
        if ($null -ne $m.save_failure) {
            $errorCount = [int]$m.save_failure.count
        }

        # http_req_failed.value = failure rate (0.0~1.0); convert to %
        # Note: .fails/.passes are threshold evaluation counts, NOT request counts
        if ($null -ne $m.http_req_failed) {
            $errorRate = [math]::Round($m.http_req_failed.value * 100, 3)
        }

        Write-Log "k6 summary parsed - rps=$rps, http_p50=${httpP50}ms, http_p90=${httpP90}ms, http_p95=${httpP95}ms, save_avg=${saveAvg}ms, save_p95=${saveP95}ms, errors=$errorCount, error_rate=${errorRate}%"
    }
} catch {
    Write-Log "Failed to parse k6 summary JSON: $_" "WARN"
}
Write-Log "[OK] Step 11 complete"

# Step 12: Append CSV row
Write-Log "Step 12: Writing CSV result"
$csvHeader = "experiment_id,mode,vu,batch_size,repeat,timestamp,rps,http_req_p50,http_req_p90,http_req_p95,http_req_p99,save_latency_avg,save_latency_p95,e2e_latency_avg,e2e_latency_count,queue_backlog_last,batch_items_processed,error_count,error_rate,duration_seconds,k6_exit_code"
$csvLine   = "$ExperimentId,$Mode,$VU,$BatchSize,$Repeat,$(Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'),$rps,$httpP50,$httpP90,$httpP95,$httpP99,$saveAvg,$saveP95,$e2eAvg,$e2eCount,$queueBacklog,$batchItems,$errorCount,$errorRate,$([int]$durationSec),$k6ExitCode"

# Individual measurement CSV
Set-Content  -Path $summaryFile -Value $csvHeader
Add-Content  -Path $summaryFile -Value $csvLine

# Consolidated CSV (add header if file does not exist)
if (-not (Test-Path $summaryAll)) {
    Set-Content -Path $summaryAll -Value $csvHeader
}
Add-Content -Path $summaryAll -Value $csvLine
Write-Log "CSV written: $summaryAll"
Write-Log "[OK] Step 12 complete"

# Step 13: Stop server
Write-Log "Step 13: Stopping server"
Stop-Server
Write-Log "[OK] Step 13 complete"

# Clean up env var
$env:DbSyncWorker__BatchSize = $null

# Step 14: Interval wait is managed by the orchestrator if called from run_all_experiments.ps1
Write-Log "===== Measurement complete: $ExperimentId ====="
Write-Log "Result: RPS=$rps, E2E_avg=${e2eAvg}s, errors=$errorCount"

# Return value (used by orchestrator)
return @{
    ExperimentId = $ExperimentId
    Success      = ($k6ExitCode -eq 0 -and $errorCount -eq 0)
    Rps          = $rps
    E2eAvg       = $e2eAvg
    ErrorCount   = $errorCount
    Duration     = $durationSec
}
