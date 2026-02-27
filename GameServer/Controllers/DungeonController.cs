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

        // 보스 던전 세션 생성 + 데디서버 준비 대기
        [HttpPost("create-boss-session")]
        public async Task<IActionResult> CreateBossSession([FromBody] CreateBossSessionReq req)
        {
            var db = _redis.GetDatabase();
            var sub = _redis.GetSubscriber();

            // 1. 고유 세션 이름 생성
            string sessionName = "boss_" + Guid.NewGuid().ToString();

            // 2. Redis에 세션 정보 저장 (TTL 30분)
            var sessionData = JsonSerializer.Serialize(new { status = "preparing", userId = req.UserId });
            await db.StringSetAsync($"dungeon_session:{sessionName}", sessionData, TimeSpan.FromSeconds(1800));

            // 3. 데디서버 준비 완료 대기를 위한 Pub/Sub 구독
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await sub.SubscribeAsync(RedisChannel.Literal("boss-dungeon:ready"), (channel, message) =>
            {
                if (message.ToString() == sessionName)
                {
                    tcs.TrySetResult(true);
                }
            });

            // 4. 데디서버에 세션 생성 요청 발행
            await sub.PublishAsync(RedisChannel.Literal("boss-dungeon:create"), $"{sessionName}|4");

            _logger.LogInformation($"[Dungeon] 보스 세션 생성 요청: {sessionName}");

            // 5. 최대 5초 대기
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

        // 보스전 결과 처리 (데디서버가 호출)
        [HttpPost("result")]
        public async Task<IActionResult> DungeonResult([FromBody] DungeonResultReq req)
        {
            var db = _redis.GetDatabase();

            // 1. 클리어한 유저들 랭킹 등록
            foreach (var result in req.Results)
            {
                if (result.Cleared)
                {
                    string member = $"{result.UserId}:{result.Nickname}";
                    await db.SortedSetAddAsync("ranking:boss", member, result.ClearTime);
                    _logger.LogInformation($"[Ranking] 보스 랭킹 등록: {result.Nickname} - {result.ClearTime}초");
                }
            }

            // 2. 세션 정보 삭제
            await db.KeyDeleteAsync($"dungeon_session:{req.SessionName}");

            _logger.LogInformation($"[Dungeon] 보스 세션 결과 처리 완료: {req.SessionName}");
            return Ok(new { success = true });
        }
    }
}
