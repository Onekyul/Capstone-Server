using GameServer.DTO;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Text.Json;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DungeonController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<DungeonController> _logger;

        public DungeonController(IConnectionMultiplexer redis, ILogger<DungeonController> logger)
        {
            _redis = redis;
            _logger = logger;
        }

      
        [HttpPost("create-boss-session")]
        public async Task<IActionResult> CreateBossSession([FromBody] CreateBossSessionReq req)
        {
            var db = _redis.GetDatabase();
            var sub = _redis.GetSubscriber();

            // 세션 이름 생성
            string sessionName = "boss_" + Guid.NewGuid().ToString();

            // Redis에 정보 저장 (TTL 30분)
            var sessionData = JsonSerializer.Serialize(new { status = "preparing", userId = req.UserId });
            await db.StringSetAsync($"dungeon_session:{sessionName}", sessionData, TimeSpan.FromSeconds(1800));

            // 데디서버 준비 완료 대기를 위한 Pub/Sub 구독
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await sub.SubscribeAsync(RedisChannel.Literal("boss-dungeon:ready"), (channel, message) =>
            {
                if (message.ToString() == sessionName)
                {
                    tcs.TrySetResult(true);
                }
            });

            // 서버에 세션 생성 요청 발행
            await sub.PublishAsync(RedisChannel.Literal("boss-dungeon:create"), $"{sessionName}|4");

            _logger.LogInformation($"[Dungeon] 보스 세션 생성 요청: {sessionName}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => tcs.TrySetCanceled());

            try
            {
                await tcs.Task;

                await sub.UnsubscribeAsync(RedisChannel.Literal("boss-dungeon:ready"));

                // 세션 상태 업데이트
                var readyData = JsonSerializer.Serialize(new { status = "ready", userId = req.UserId });
                await db.StringSetAsync($"dungeon_session:{sessionName}", readyData, TimeSpan.FromSeconds(1800));

                _logger.LogInformation($"[Dungeon] 보스 세션 준비 완료: {sessionName}");
                return Ok(new CreateBossSessionRes { SessionName = sessionName });
            }
            catch (TaskCanceledException)
            {
                await sub.UnsubscribeAsync(RedisChannel.Literal("boss-dungeon:ready"));

                // 타임아웃 시 세션 정리
                await db.KeyDeleteAsync($"dungeon_session:{sessionName}");

                _logger.LogWarning($"[Dungeon] 보스 세션 타임아웃: {sessionName}");
                return StatusCode(503, new { message = "데디케이티드 서버 준비 타임아웃" });
            }
        }

        // 파티 보스던전 입장
        [HttpPost("enter")]
        public async Task<IActionResult> Enter([FromBody] DungeonEnterReq req)
        {
            var db = _redis.GetDatabase();
            var sub = _redis.GetSubscriber();

            // 유휴 서버 하나 꺼내기 
            var serverId = await db.SetPopAsync("server:pool:idle");
            if (serverId.IsNullOrEmpty)
            {
                _logger.LogWarning("[ServerPool] 유휴 서버 없음 — full 반환");
                return Ok(new DungeonEnterRes { Status = "full", Message = "서버가 혼잡합니다" });
            }

            // 세션명 생성
            string sessionName = "boss-" + Guid.NewGuid().ToString("N")[..8];

            // busy 상태로 등록
            await db.HashSetAsync("server:pool:busy", serverId.ToString(), sessionName);

            // 세션 정보 저장 (TTL 30분)
            var sessionData = JsonSerializer.Serialize(new
            {
                serverId        = serverId.ToString(),
                partyLeaderUserId = req.PartyLeaderUserId,
                memberUserIds   = req.MemberUserIds
            });
            await db.StringSetAsync($"dungeon_session:{sessionName}", sessionData, TimeSpan.FromMinutes(30));

            //  해당 서버 전용 채널로 세션 할당 알림
            var assignPayload = JsonSerializer.Serialize(new
            {
                sessionName,
                memberUserIds = req.MemberUserIds,
                partyId = req.PartyId
            });
            await sub.PublishAsync(RedisChannel.Literal($"boss-dungeon:assign:{serverId}"), assignPayload);

            // 파티 상태를 InGame으로 변경하고 SessionName 저장
            if (req.PartyId > 0)
            {
                string partyKey = $"Party:{req.PartyId}";
                if (await db.KeyExistsAsync(partyKey))
                {
                    await db.HashSetAsync(partyKey, new HashEntry[]
                    {
                        new HashEntry("Status", "InGame"),
                        new HashEntry("SessionName", sessionName)
                    });
                    _logger.LogInformation($"[ServerPool] 파티 상태 InGame 변경: PartyId={req.PartyId}, Session={sessionName}");
                }
            }

            _logger.LogInformation($"[ServerPool] 던전 입장: {sessionName} → {serverId}");
            return Ok(new DungeonEnterRes { Status = "ok", SessionName = sessionName });
        }

        // 보스전 결과 처리 
        [HttpPost("result")]
        public async Task<IActionResult> DungeonResult([FromBody] DungeonResultReq req)
        {
            var db = _redis.GetDatabase();

            // 1클리어한 유저들 랭킹 등록
            foreach (var result in req.Results)
            {
                if (result.Cleared)
                {
                    // Redis 캐시에서 닉네임 조회
                    string cacheKey = $"user:{result.UserId}:data";
                    var cachedJson = await db.StringGetAsync(cacheKey);
                    if (cachedJson.IsNullOrEmpty) continue;

                    var gameData = JsonSerializer.Deserialize<GameDataDto>(cachedJson);
                    if (gameData == null) continue;

                    string nickname = gameData.nickname;
                    string member = $"{result.UserId}:{nickname}";

                    // 동일 유저가 이미 있으면 더 빠른 기록일 때만 갱신
                    var existingScore = await db.SortedSetScoreAsync("ranking:boss", member);
                    if (existingScore.HasValue && existingScore.Value <= result.ClearTime)
                    {
                        _logger.LogInformation($"[Ranking] 기존 기록이 더 빠름: {nickname} (기존 {existingScore.Value}초 vs 신규 {result.ClearTime}초)");
                        continue;
                    }

                    await db.SortedSetAddAsync("ranking:boss", member, result.ClearTime);
                    _logger.LogInformation($"[Ranking] 보스 랭킹 등록: {nickname} - {result.ClearTime}초");
                }
            }

            // 세션 정보 삭제
            await db.KeyDeleteAsync($"dungeon_session:{req.SessionName}");

            // 파티 키 삭제 (partyId == 0이면 솔로 입장이므로 스킵)
            if (req.PartyId > 0)
            {
                string partyKey = $"Party:{req.PartyId}";
                await db.KeyDeleteAsync(partyKey);
                await db.KeyDeleteAsync(partyKey + ":Members");
                await db.KeyDeleteAsync(partyKey + ":Ready");
                await db.SetRemoveAsync("Party:List", req.PartyId);
                _logger.LogInformation($"[Dungeon] 파티 삭제: PartyId={req.PartyId}");
            }

            _logger.LogInformation($"[Dungeon] 보스 세션 결과 처리 완료: {req.SessionName}");
            return Ok(new { success = true });
        }
    }
}
