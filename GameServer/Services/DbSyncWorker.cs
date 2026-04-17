using GameServer.Configuration;
using GameServer.Data;
using GameServer.DTO;
using GameServer.Metrics;
using GameServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GameServer.Services
{
    /// <summary>
    /// Redis Write-Back 워커.
    /// Redis 큐에 누적된 저장 요청을 배치 단위로 추출하여 MySQL에 일괄 반영함.
    /// 본 클래스는 논문 실험의 측정 대상이며, 배치 사이즈가 유일한 독립변수임.
    /// </summary>
    public class DbSyncWorker : BackgroundService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DbSyncWorker> _logger;
        private readonly DbSyncWorkerOptions _options;
        private readonly string _batchSizeLabel;

        public DbSyncWorker(
            IConnectionMultiplexer redis,
            IServiceProvider serviceProvider,
            ILogger<DbSyncWorker> logger,
            IOptions<DbSyncWorkerOptions> options)
        {
            _redis = redis;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _options = options.Value;

            // [측정 변수 통제] 배치 사이즈를 메트릭 라벨로 사용하여
            // 실험별 데이터를 Prometheus 쿼리에서 분리 가능하도록 함.
            _batchSizeLabel = _options.BatchSize.ToString();

            _logger.LogInformation(
                ">>> Write-Back 워커 초기화. BatchSize={BatchSize}, PollingMs={PollingMs}",
                _options.BatchSize, _options.EmptyQueuePollingMs);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(">>> Write-Back 워커 시작");
            var db = _redis.GetDatabase();

            while (!stoppingToken.IsCancellationRequested)
            {
                List<WriteBackQueueItem> items = new();

                try
                {
                    // [측정 변수 통제] LPOP count 단일 호출로 배치 사이즈만큼의 작업을
                    // 단일 RTT에 원자적으로 추출. Sequential pop 사용 시
                    // 배치 사이즈에 비례하는 RTT 비용이 측정 변수에 섞여 들어가는 문제를 회피.
                    // Redis 6.2+ 네이티브 LPOP count 명령어 사용 (Lua 스크립트 미사용).
                    var redisValues = await db.ListLeftPopAsync(
                        _options.QueueKey, _options.BatchSize);

                    if (redisValues != null && redisValues.Length > 0)
                    {
                        // [E2E 측정] 큐 페이로드를 WriteBackQueueItem JSON으로 역직렬화하여
                        // EnqueuedAt 타임스탬프를 추출. 이 값으로 DB commit 후 E2E 지연 계산.
                        foreach (var value in redisValues)
                        {
                            if (!value.HasValue) continue;
                            try
                            {
                                var item = JsonSerializer.Deserialize<WriteBackQueueItem>(value.ToString());
                                if (item != null) items.Add(item);
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "큐 페이로드 역직렬화 실패: {Value}", value);
                            }
                        }
                    }

                    if (items.Count > 0)
                    {
                        _logger.LogInformation(
                            "[Batch Save] {Count}명 데이터 저장 시작 (BatchSize 설정값: {BatchSize})",
                            items.Count, _options.BatchSize);

                        await ProcessBatchSaveAsync(db, items);
                    }
                    else
                    {
                        // [통제변수 고정] 빈 큐 폴링 주기를 10ms로 고정.
                        // 기존 1초는 큐 적재 데이터를 지연시켜 측정값을 왜곡함.
                        await Task.Delay(_options.EmptyQueuePollingMs, stoppingToken);
                    }

                    // [종속변수 측정] 매 루프마다 큐 backlog 측정.
                    // 부하 누적과 워커 처리 한계 확인 용도.
                    var queueLength = await db.ListLengthAsync(_options.QueueKey);
                    WriteBackMetrics.QueueLength.WithLabels(_batchSizeLabel).Set(queueLength);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "워커 에러 발생");

                    // [복구 로직] DB 저장 실패 시 추출한 아이템들을 큐에 복구.
                    // 본 연구의 측정 대상은 정상 처리 경로이므로 복구 시점도 기록.
                    if (items.Count > 0)
                    {
                        _logger.LogWarning("[Rollback] {Count}개 아이템을 큐에 복구합니다.", items.Count);
                        foreach (var item in items)
                        {
                            var json = JsonSerializer.Serialize(item);
                            await db.ListLeftPushAsync(_options.QueueKey, json);
                        }
                    }

                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        /// <summary>
        /// 추출된 배치를 MySQL에 일괄 upsert 반영.
        /// INSERT ... ON DUPLICATE KEY UPDATE 패턴으로 Unique Index 충돌 없이
        /// 동일 키를 원자적으로 처리함.
        ///
        /// 본 처리 방식은 학술대회 제출용 논문 3.2절의 명시된 구현
        /// ("MySQL에 일괄 upsert를 수행한다")과 일치함.
        /// </summary>
        private async Task ProcessBatchSaveAsync(IDatabase redisDb, List<WriteBackQueueItem> items)
        {
            // [측정 변수 통제] DB 처리 시간만 측정하기 위한 스톱워치.
            // E2E 지연에서 DB commit 시간을 분리하여 보고 가능.
            var stopwatch = Stopwatch.StartNew();

            using var scope = _serviceProvider.CreateScope();
            var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // [측정 변수 통제 - N+1 제거] 모든 유저를 단일 쿼리로 일괄 조회.
            var userIdInts = items
                .Select(i => int.TryParse(i.UserId, out var id) ? id : -1)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            var users = await _context.Users
                .Where(u => userIdInts.Contains(u.Id))
                .ToListAsync();

            var userMap = users.ToDictionary(u => u.Id);

            // [측정 변수 통제] upsert 패턴 적용으로 동일 userId race condition을
            // DB 레벨에서 원자적으로 처리. 애플리케이션 레벨 GroupBy 중복 제거는
            // 본 연구의 측정 대상인 '큐 처리 성능'에서 'DB 트랜잭션 충돌'이라는
            // 외부 변수를 제거하는 표준 방법으로 대체됨.
            var inventoryRows  = new List<object[]>();
            var equipmentRows  = new List<object[]>();
            var enchantRows    = new List<object[]>();

            foreach (var item in items)
            {
                if (!int.TryParse(item.UserId, out var userId)) continue;
                if (!userMap.TryGetValue(userId, out var user)) continue;

                // Redis에서 유저 데이터 페이로드 조회.
                string json = await redisDb.StringGetAsync($"user:{item.UserId}:data");
                if (string.IsNullOrEmpty(json)) continue;

                var clientData = JsonSerializer.Deserialize<GameDataDto>(json);
                if (clientData == null) continue;

                // users 테이블: EF 변경 추적으로 처리 (stage, equipped slot)
                user.MaxClearedStage = clientData.stage;
                if (clientData.equip != null)
                {
                    user.EquippedWeaponId = clientData.equip.weapon;
                    user.EquippedHelmetId = clientData.equip.helmet;
                    user.EquippedArmorId  = clientData.equip.armor;
                    user.EquippedBootsId  = clientData.equip.boots;
                }

                // user_items / user_equipments / user_enchants: upsert 행 수집
                foreach (var itemDto in clientData.inventory)
                    inventoryRows.Add(new object[] { userId, itemDto.id, itemDto.count });

                foreach (var equipDto in clientData.equipments)
                    equipmentRows.Add(new object[] { userId, equipDto.id, equipDto.level });

                foreach (var enchantDto in clientData.enchants)
                    enchantRows.Add(new object[] { userId, enchantDto.id, enchantDto.level });
            }

            // [단일 트랜잭션] 4개 테이블 변경을 단일 트랜잭션으로 묶어 원자성 보장.
            // 트랜잭션 실패 시 catch 블록의 큐 복구 로직이 동작함.
            using var tx = await _context.Database.BeginTransactionAsync();

            // user_items upsert — INSERT ... ON DUPLICATE KEY UPDATE
            if (inventoryRows.Count > 0)
                await BulkUpsertAsync(_context,
                    "INSERT INTO user_items (user_id, item_id, `count`) VALUES ",
                    " ON DUPLICATE KEY UPDATE `count` = VALUES(`count`)",
                    inventoryRows, 3);

            // user_equipments upsert
            if (equipmentRows.Count > 0)
                await BulkUpsertAsync(_context,
                    "INSERT INTO user_equipments (user_id, item_id, level) VALUES ",
                    " ON DUPLICATE KEY UPDATE level = VALUES(level)",
                    equipmentRows, 3);

            // user_enchants upsert
            if (enchantRows.Count > 0)
                await BulkUpsertAsync(_context,
                    "INSERT INTO user_enchants (user_id, enchant_id, level) VALUES ",
                    " ON DUPLICATE KEY UPDATE level = VALUES(level)",
                    enchantRows, 3);

            // users 테이블 업데이트 (EF 변경 추적)
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            stopwatch.Stop();

            // [종속변수 측정] DB commit 시간을 히스토그램에 기록.
            WriteBackMetrics.BatchProcessingDurationSeconds
                .WithLabels(_batchSizeLabel)
                .Observe(stopwatch.Elapsed.TotalSeconds);

            // [종속변수 측정 - E2E 지연] 각 아이템별로 (현재 시각 - 큐 push 시각)을 기록.
            // 본 논문 4장의 핵심 종속변수.
            var now = DateTime.UtcNow;
            foreach (var item in items)
            {
                var e2eSeconds = (now - item.EnqueuedAt).TotalSeconds;
                if (e2eSeconds < 0) e2eSeconds = 0; // 시계 동기화 오차 방어
                WriteBackMetrics.E2eLatencySeconds
                    .WithLabels(_batchSizeLabel)
                    .Observe(e2eSeconds);
            }

            // [종속변수 측정 - 워커 처리량] 처리 건수 누적.
            WriteBackMetrics.BatchProcessedItems
                .WithLabels(_batchSizeLabel)
                .Inc(items.Count);

            _logger.LogInformation(
                "[Batch Complete] {Count}명 저장 완료. DB commit: {DurationMs}ms",
                items.Count, stopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// 다수의 행을 단일 INSERT ... ON DUPLICATE KEY UPDATE 문으로 일괄 upsert.
        /// ExecuteSqlRawAsync의 {N} 파라미터 바인딩을 이용해 SQL Injection을 방지함.
        /// </summary>
        private static async Task BulkUpsertAsync(
            AppDbContext context,
            string insertPrefix,
            string onDuplicateClause,
            List<object[]> rows,
            int columnsPerRow)
        {
            var sb = new StringBuilder(insertPrefix);
            var allParams = new List<object>();
            int paramIndex = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append('(');
                for (int j = 0; j < columnsPerRow; j++)
                {
                    if (j > 0) sb.Append(", ");
                    sb.Append('{').Append(paramIndex++).Append('}');
                    allParams.Add(rows[i][j]);
                }
                sb.Append(')');
            }
            sb.Append(onDuplicateClause);

            await context.Database.ExecuteSqlRawAsync(sb.ToString(), allParams);
        }
    }
}
