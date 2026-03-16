using GameServer.DTO;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServerController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<ServerController> _logger;

        public ServerController(IConnectionMultiplexer redis, ILogger<ServerController> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        // 데디서버 시작 시 호출 — 유휴 풀에 등록
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] ServerRegisterReq req)
        {
            var db = _redis.GetDatabase();

            // 서버 메타 정보 저장
            await db.HashSetAsync($"server:{req.ServerId}:info", new HashEntry[]
            {
                new HashEntry("port",         req.Port),
                new HashEntry("registeredAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            });

            // 혹시 이전 세션에서 busy에 남아 있으면 제거
            await db.HashDeleteAsync("server:pool:busy", req.ServerId);

            // 유휴 풀에 추가
            await db.SetAddAsync("server:pool:idle", req.ServerId);

            _logger.LogInformation($"[ServerPool] 서버 등록: {req.ServerId} (port: {req.Port})");
            return Ok(new { success = true });
        }

        // 세션 종료 후 유휴 복귀 시 호출
        [HttpPost("idle")]
        public async Task<IActionResult> Idle([FromBody] ServerIdleReq req)
        {
            var db = _redis.GetDatabase();

            // busy → idle 이동
            await db.HashDeleteAsync("server:pool:busy", req.ServerId);
            await db.SetAddAsync("server:pool:idle", req.ServerId);

            _logger.LogInformation($"[ServerPool] 서버 유휴 복귀: {req.ServerId}");
            return Ok(new { success = true });
        }
    }
}
