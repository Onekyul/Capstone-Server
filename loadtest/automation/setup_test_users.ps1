# =============================================================================
# setup_test_users.ps1
# Create 200 test users for paper measurements (run only once)
#
# Prerequisites:
#   1. Set AUTO_INCREMENT in MySQL:
#      ALTER TABLE users AUTO_INCREMENT = 1001;
#   2. Capstone-Server running at http://localhost:7200
#
# Usage:
#   cd loadtest/automation
#   ./setup_test_users.ps1
# =============================================================================

param(
    [string]$TargetUrl = "http://localhost:7200",
    [int]$StartId = 1001,
    [int]$EndId   = 1200
)

$totalCount   = $EndId - $StartId + 1
$successCount = 0
$skipCount    = 0
$failCount    = 0
$createdIds   = @()

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Creating test users" -ForegroundColor Cyan
Write-Host " Target: test_device_$StartId ~ test_device_$EndId ($totalCount users)" -ForegroundColor Cyan
Write-Host " Server: $TargetUrl" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "[Prerequisites] The following SQL must have been run in MySQL:"
Write-Host "  ALTER TABLE users AUTO_INCREMENT = 1001;"
Write-Host ""

# Confirm before proceeding
$confirm = Read-Host "Proceed? (y/n)"
if ($confirm -ne 'y' -and $confirm -ne 'Y') {
    Write-Host "Cancelled." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Starting creation..." -ForegroundColor Green

for ($i = $StartId; $i -le $EndId; $i++) {
    $body = @{
        DeviceId = "test_device_$i"
        Nickname = "test_user_$i"
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod `
            -Uri "$TargetUrl/api/Auth/register" `
            -Method Post `
            -Body $body `
            -ContentType "application/json" `
            -ErrorAction Stop

        $createdIds += $response.userId
        $successCount++

        # Print dot every 10 users
        if ($successCount % 10 -eq 0) {
            Write-Host "." -NoNewline
        }
        # Print progress every 100 users
        if ($successCount % 100 -eq 0) {
            Write-Host ""
            Write-Host "  Progress: $successCount/$totalCount done" -ForegroundColor Green
        }
    }
    catch {
        $errorMsg = $_.Exception.Message
        # Duplicate nickname (user already exists) -> skip
        if ($errorMsg -match "already" -or $errorMsg -match "duplicate" -or $_.Exception.Response.StatusCode -eq 409) {
            $skipCount++
        }
        else {
            $failCount++
            Write-Host ""
            Write-Host "  [Warning] Failed to create test_device_${i}: $errorMsg" -ForegroundColor Yellow
        }
    }
}

Write-Host ""
Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Creation complete" -ForegroundColor Cyan
Write-Host "  Success : $successCount users" -ForegroundColor Green
Write-Host "  Skipped (already exists): $skipCount users" -ForegroundColor Yellow
Write-Host "  Failed  : $failCount users" -ForegroundColor Red
Write-Host "=============================================" -ForegroundColor Cyan

if ($createdIds.Count -gt 0) {
    $minId = ($createdIds | Measure-Object -Minimum).Minimum
    $maxId = ($createdIds | Measure-Object -Maximum).Maximum
    Write-Host ""
    Write-Host " userId range: $minId ~ $maxId" -ForegroundColor Green
    Write-Host " --> Set USER_BASE=$minId when running measurements." -ForegroundColor Green

    # Verify continuity
    $expectedRange = $EndId - $StartId + 1 - $skipCount
    if ($createdIds.Count -ne $expectedRange -or $minId -ne $StartId -or $maxId -ne ($StartId + $totalCount - 1 - $skipCount)) {
        Write-Host ""
        Write-Host " [Warning] userId range does not match $StartId~$EndId." -ForegroundColor Yellow
        Write-Host " Reset AUTO_INCREMENT and re-run, or verify USER_BASE manually." -ForegroundColor Yellow
    }
    else {
        Write-Host " [OK] userId range verification passed." -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "Verification SQL (run in MySQL):"
Write-Host "  SELECT MIN(id) AS min_id, MAX(id) AS max_id, COUNT(*) AS total"
Write-Host "  FROM users WHERE id BETWEEN $StartId AND $EndId;"
Write-Host "  -- Expected: min_id=$StartId, max_id=$EndId, total=$totalCount"
