# =============================================================================
# run_all_experiments.ps1
# Orchestration script for all 70 measurements
#
# Usage:
#   ./run_all_experiments.ps1
#   ./run_all_experiments.ps1 -DryRun         # print plan only, no execution
#   ./run_all_experiments.ps1 -PlanFile ".\experiment_plan.csv"
#
# Resume after interruption:
#   Re-run after Ctrl+C; only status=pending rows are executed.
#   Rows with status=failed are not retried automatically.
#   Change status back to 'pending' manually to retry.
#
# Prerequisites:
#   - $env:MYSQL_PASSWORD set (MySQL root password)
#   - Docker running (Capstone_mysql, Capstone_redis)
#   - k6 installed and on PATH
#   - Test users 1001~1200 created (run setup_test_users.ps1 first)
# =============================================================================

param(
    [string] $PlanFile    = "$PSScriptRoot\experiment_plan.csv",
    [string] $OutputDir   = "$PSScriptRoot\results",
    [string] $TargetUrl   = "http://localhost:7200",
    [string] $ServerPath  = "C:\Users\kyul\Desktop\Capstone-Server\GameServer",
    [string] $ScenarioPath = "$PSScriptRoot\..\scenario.js",
    [int]    $UserBase    = 1001,
    [int]    $IntervalSec = 300,    # interval between measurements (5 min default)
    [switch] $DryRun                # print plan only, do not execute
)

$ErrorActionPreference = "Stop"

# =============================================================================
# Initialization
# =============================================================================
$logsDir     = "$OutputDir\logs"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $logsDir   | Out-Null

$masterLogFile = "$logsDir\orchestrator-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $ts   = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "[$ts][$Level] $Message"
    Write-Host $line
    Add-Content -Path $masterLogFile -Value $line
}

function Update-PlanStatus {
    param(
        [string]$PlanFilePath,
        [string]$ExperimentId,
        [string]$NewStatus
    )
    $rows    = Import-Csv -Path $PlanFilePath
    $updated = $rows | ForEach-Object {
        if ($_.experiment_id -eq $ExperimentId) {
            $_.status = $NewStatus
        }
        $_
    }
    $updated | Export-Csv -Path $PlanFilePath -NoTypeInformation
}

# =============================================================================
# Load experiment plan
# =============================================================================
if (-not (Test-Path $PlanFile)) {
    Write-Error "experiment_plan.csv not found: $PlanFile"
    exit 1
}

$allRows     = Import-Csv -Path $PlanFile
$pendingRows = $allRows | Where-Object { $_.status -eq "pending" }
$totalPending = $pendingRows.Count
$totalAll     = $allRows.Count

Write-Log "===== Measurement orchestration started ====="
Write-Log "Plan file   : $PlanFile"
Write-Log "Total rows  : $totalAll"
Write-Log "Pending     : $totalPending (already done: $($totalAll - $totalPending))"

if ($totalPending -eq 0) {
    Write-Log "All measurements already completed. Check status column in experiment_plan.csv."
    exit 0
}

# Estimated duration (approx. 2 min k6 + 1 min setup/teardown + interval)
$estimatedMinPerRun = 3 + [int]($IntervalSec / 60)
$estimatedTotalMin  = $totalPending * $estimatedMinPerRun
$estimatedHours     = [math]::Round($estimatedTotalMin / 60, 1)
Write-Log "Estimated duration : ${estimatedTotalMin} min (~${estimatedHours} hours)"
Write-Log "Estimated finish   : $((Get-Date).AddMinutes($estimatedTotalMin).ToString('yyyy-MM-dd HH:mm'))"

if ($DryRun) {
    Write-Log "===== DRY RUN mode -- printing plan only ====="
    $pendingRows | Format-Table priority, experiment_id, mode, vu, batch_size, repeat, status -AutoSize
    Write-Log "DryRun complete. Remove -DryRun flag to execute."
    exit 0
}

# Pre-flight checklist reminder
Write-Host ""
Write-Host "==========================================================" -ForegroundColor Yellow
Write-Host " Overnight unattended run checklist:" -ForegroundColor Yellow
Write-Host "  1. Disable sleep mode (Control Panel -> Power Options)" -ForegroundColor Yellow
Write-Host "  2. Docker Desktop is running" -ForegroundColor Yellow
Write-Host "  3. MYSQL_PASSWORD env var is set" -ForegroundColor Yellow
Write-Host "  4. Close unnecessary processes (Visual Studio, Chrome, etc.)" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Yellow
Write-Host ""

$startAll   = Get-Date
$successCount = 0
$failCount    = 0
$runIndex     = 0

# =============================================================================
# Sequential measurement execution
# =============================================================================
foreach ($row in $pendingRows) {
    $runIndex++
    $expId     = $row.experiment_id
    $mode      = $row.mode
    $vu        = [int]$row.vu
    $batchSize = [int]$row.batch_size
    $repeat    = [int]$row.repeat

    Write-Log "--------------------------------------------------------------"
    Write-Log "[$runIndex/$totalPending] Start: $expId (mode=$mode, VU=$vu, batch=$batchSize, repeat=$repeat)"

    # Mark as running
    Update-PlanStatus -PlanFilePath $PlanFile -ExperimentId $expId -NewStatus "running"

    try {
        $result = & "$PSScriptRoot\run_single_measurement.ps1" `
            -VU          $vu `
            -BatchSize   $batchSize `
            -Repeat      $repeat `
            -ExperimentId $expId `
            -Mode        $mode `
            -OutputDir   $OutputDir `
            -TargetUrl   $TargetUrl `
            -UserBase    $UserBase `
            -ServerPath  $ServerPath `
            -ScenarioPath $ScenarioPath `
            -IntervalSec  0   # interval managed here by orchestrator

        if ($result -and $result.Success) {
            Write-Log "[$runIndex/$totalPending] Done: $expId -- RPS=$($result.Rps), E2E_avg=$($result.E2eAvg)s, errors=$($result.ErrorCount)"
            Update-PlanStatus -PlanFilePath $PlanFile -ExperimentId $expId -NewStatus "completed"
            $successCount++
        } else {
            $errMsg = if ($result) { "k6 error or success condition not met (errors=$($result.ErrorCount))" } else { "run_single_measurement.ps1 returned no value" }
            Write-Log "[$runIndex/$totalPending] Failed: $expId -- $errMsg" "WARN"
            Update-PlanStatus -PlanFilePath $PlanFile -ExperimentId $expId -NewStatus "failed"
            $failCount++
        }
    } catch {
        Write-Log "[$runIndex/$totalPending] Exception: $expId -- $_" "ERROR"
        Update-PlanStatus -PlanFilePath $PlanFile -ExperimentId $expId -NewStatus "failed"
        $failCount++
    }

    # Print progress summary every 5 runs
    if ($runIndex % 5 -eq 0) {
        $elapsed   = (Get-Date) - $startAll
        $remaining = $totalPending - $runIndex
        $avgMin    = $elapsed.TotalMinutes / $runIndex
        $etaMin    = [int]($remaining * $avgMin)
        Write-Log "===== Progress: $runIndex/$totalPending done (success=$successCount / failed=$failCount) | ETA: ${etaMin} min ====="
    }

    # Wait between measurements (skip after last run)
    if ($runIndex -lt $totalPending) {
        Write-Log "Waiting interval... ($IntervalSec seconds)"
        Start-Sleep -Seconds $IntervalSec
    }
}

# =============================================================================
# Final summary
# =============================================================================
$totalElapsed  = (Get-Date) - $startAll
$totalMinutes  = [int]$totalElapsed.TotalMinutes

Write-Log "============================================================"
Write-Log "===== All measurements complete ====="
Write-Log "Total runs : $runIndex"
Write-Log "Success    : $successCount"
Write-Log "Failed     : $failCount"
Write-Log "Total time : ${totalMinutes} min"
Write-Log "Result file: $OutputDir\summary.csv"
Write-Log "============================================================"

# Quick statistics (if summary.csv exists)
$summaryAll = "$OutputDir\summary.csv"
if (Test-Path $summaryAll) {
    try {
        $summaryData = Import-Csv -Path $summaryAll
        Write-Log "===== Result statistics ====="

        $groups = $summaryData | Group-Object -Property mode, vu, batch_size
        foreach ($grp in $groups) {
            $validRps = $grp.Group | Where-Object { $_.rps -ne "N/A" -and $_.rps -ne "" } | ForEach-Object { [double]$_.rps }
            if ($validRps.Count -gt 0) {
                $avgRps = [math]::Round(($validRps | Measure-Object -Average).Average, 2)
                Write-Log "  $($grp.Name) -- avg RPS: $avgRps ($($grp.Group.Count) runs)"
            }
        }
    } catch {
        Write-Log "Error computing statistics: $_" "WARN"
    }
}

if ($failCount -gt 0) {
    Write-Log "Some measurements failed. Check status=failed rows in experiment_plan.csv."
    Write-Log "Change status to 'pending' manually and re-run to retry."
}

Write-Log "Orchestration log: $masterLogFile"
