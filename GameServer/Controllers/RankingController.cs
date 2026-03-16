using GameServer.Data;
using GameServer.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class RankingController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly AppDbContext _context;

        public RankingController(IConnectionMultiplexer redis, AppDbContext context)
        {
            _redis = redis;
            _context = context;
        }

        // 보스 랭킹 조회 (클리어 시간 기준 오름차순)
        [HttpGet("boss")]
        public async Task<IActionResult> GetBossRanking([FromQuery] int top = 10)
        {
            var db = _redis.GetDatabase();

            // 낮은 시간이 상위
            var entries = await db.SortedSetRangeByRankWithScoresAsync("ranking:boss", 0, top - 1);

            var rankings = new List<BossRankingEntry>();
            int rank = 1;

            foreach (var entry in entries)
            {
           
                string member = entry.Element.ToString();
                string nickname = member.Contains(':') ? member.Substring(member.IndexOf(':') + 1) : member;

                rankings.Add(new BossRankingEntry
                {
                    Rank = rank++,
                    Nickname = nickname,
                    ClearTime = entry.Score
                });
            }

            return Ok(new BossRankingRes { Rankings = rankings });
        }

   
    }
}
