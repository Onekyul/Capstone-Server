# Measurement Automation Scripts

This directory contains the automation infrastructure for 70 measurements of the Redis Write-Back batch size experiment.

---

## File Overview

| File | Purpose |
|---|---|
| `setup_test_users.ps1` | Create 200 test users (run once before first measurement) |
| `reset_test_users.sql` | DB reset SQL run before every measurement |
| `run_single_measurement.ps1` | Run a single measurement (VU, batch size as parameters) |
| `run_all_experiments.ps1` | Orchestrate all 70 measurements sequentially |
| `experiment_plan.csv` | Measurement plan (70 rows, tracks status per run) |
| `results/` | Directory for CSV results and k6 JSON output |

---

## 1. Prerequisites

### Set environment variable

```powershell
# Set MySQL root password (required every new session)
$env:MYSQL_PASSWORD = "your_password_here"
```

### Verify tools are installed

```powershell
# Check k6 version
k6 version

# Check Docker containers are running
docker ps | grep -E "Capstone_mysql|Capstone_redis"
```

### Set DB auto_increment (one time only)

Run in MySQL before creating test users so that userId 1001~1200 is assigned:

```sql
ALTER TABLE users AUTO_INCREMENT = 1001;
```

Via Docker:

```powershell
docker exec Capstone_mysql mysql -u root -p"$env:MYSQL_PASSWORD" GameDB `
    -e "ALTER TABLE users AUTO_INCREMENT = 1001;"
```

---

## 2. One-time setup: Create test users

```powershell
cd loadtest/automation

# Server must be running before executing this
./setup_test_users.ps1
```

Verify in MySQL after completion:

```sql
SELECT MIN(id) AS min_id, MAX(id) AS max_id, COUNT(*) AS total
FROM users
WHERE id BETWEEN 1001 AND 1200;
-- Expected: min_id=1001, max_id=1200, total=200
```

---

## 3. Running measurements

### Single measurement test (for validation before full run)

```powershell
./run_single_measurement.ps1 `
    -VU 10 `
    -BatchSize 50 `
    -Repeat 1 `
    -ExperimentId "test-single"
```

Expected results:
- `results/test-single-summary.csv` created
- One row appended to `results/summary.csv`
- `results/logs/test-single.log` created
- No errors

### Full 70-measurement run

```powershell
# Preview execution plan (dry run)
./run_all_experiments.ps1 -DryRun

# Execute all measurements
./run_all_experiments.ps1
```

---

## 4. Interruption and resume

Press Ctrl+C to stop at any time.

On re-run, only rows with `status=pending` in `experiment_plan.csv` are executed, so the run resumes from where it stopped.

Rows with `status=failed` are not retried automatically. Edit the CSV to change status back to `pending` and re-run:

```csv
# Edit experiment_plan.csv:
P1,B3-r2,writeback,100,50,2,pending   # change failed -> pending
```

---

## 5. Reviewing results

```
results/
  summary.csv                  -- consolidated results (one row per measurement)
  {ExperimentId}-raw.json      -- raw k6 JSON output
  {ExperimentId}-summary.csv   -- per-measurement summary
  logs/
    {ExperimentId}.log         -- step-by-step measurement log
    {ExperimentId}-server.log  -- GameServer stdout
    {ExperimentId}-k6.log      -- k6 stdout
```

### summary.csv columns

```
experiment_id, mode, vu, batch_size, repeat, timestamp,
rps, http_req_p50, http_req_p90, http_req_p95, http_req_p99,
save_latency_avg, save_latency_p95,
e2e_latency_avg, e2e_latency_count,
queue_backlog_last, batch_items_processed,
error_count, error_rate, duration_seconds, k6_exit_code
```

---

## 6. Overnight unattended run checklist

The full 70-measurement run takes approximately 9-10 hours.

- [ ] Disable sleep mode (Control Panel -> Power Options -> Never sleep)
- [ ] `$env:MYSQL_PASSWORD` is set
- [ ] Docker Desktop is running (Capstone_mysql, Capstone_redis)
- [ ] k6 is on PATH (`k6 version` works)
- [ ] Unnecessary processes closed (Visual Studio, Chrome, etc.)
- [ ] Single measurement validation passed (`run_single_measurement.ps1 -VU 10 ...`)

---

## 7. Troubleshooting

### Server does not start for the next measurement

A previous dotnet process may still be running. Kill it manually:

```powershell
Get-Process -Name "dotnet" | Stop-Process -Force
Start-Sleep -Seconds 5
```

### Redis queue reset fails

Verify redis-cli access directly:

```powershell
docker exec Capstone_redis redis-cli DEL task:writeback
```

### BatchSize env var not taking effect

Confirm `$env:DbSyncWorker__BatchSize` is set before server starts, then restart:

```powershell
$env:DbSyncWorker__BatchSize = "100"
dotnet run --launch-profile http
```

Check server startup log for `BatchSize=100` initialization message.

### k6 result parsing fails

Scripts are written for k6 v1.6.1. Check your version:

```powershell
k6 version
# Other versions may produce different JSON output format
```

---

## 8. Porting to AWS

After local validation, the following changes are needed to run on an AWS EC2 instance:

- `Start-Process dotnet` in `run_single_measurement.ps1` -> replace with SSH remote execution
- `Stop-Server` function -> replace with SSH `pkill dotnet`
- `TargetUrl` parameter -> EC2 public IP or internal IP
- DB/Redis reset -> access Docker containers via SSH on the EC2 instance

Detailed instructions will be provided in prompt 2 (AWS infrastructure setup).
