using GameServer.DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PartyController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;

        // Redis Key 규칙
        private const string KEY_ID_GEN = "Party:IdGen";      // 파티 번호 생성
        private const string KEY_LIST = "Party:List";         // Set
        private const string KEY_INFO_PREFIX = "Party:";      // 개별 방 정보 (Hash)

        public PartyController(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

     
        [HttpPost("create")]
        public async Task<IActionResult> CreateParty([FromBody] PartyCreateReq req)
        {
            var db = _redis.GetDatabase();

            
            long newPartyId = await db.StringIncrementAsync(KEY_ID_GEN);

            
            string infoKey = KEY_INFO_PREFIX + newPartyId;

            var entries = new HashEntry[]
            {
                new HashEntry("PartyId", newPartyId),
                new HashEntry("Title", req.Title),
                new HashEntry("LeaderId", req.LeaderId),
                new HashEntry("LeaderName", req.LeaderNickname),
                new HashEntry("CurrentCount", 1), 
                new HashEntry("MaxCount", req.MaxCount),
                new HashEntry("DungeonId", req.DungeonId)
            };

            await db.HashSetAsync(infoKey, entries);

            //방 목록(Set)에 등록
            await db.SetAddAsync(KEY_LIST, newPartyId);

            return Ok(new { PartyId = newPartyId, Message = "파티 생성 완료" });
        }

        
        [HttpGet("list")]
        public async Task<IActionResult> GetPartyList()
        {
            var db = _redis.GetDatabase();

            
            var partyIds = await db.SetMembersAsync(KEY_LIST);

            if (partyIds.Length == 0)
                return Ok(new List<PartyDto>());

            var list = new List<PartyDto>();

            
            foreach (var id in partyIds)
            {
                string infoKey = KEY_INFO_PREFIX + id;

            
                if (!await db.KeyExistsAsync(infoKey))
                {
            
                    await db.SetRemoveAsync(KEY_LIST, id);
                    continue;
                }

            
                var entries = await db.HashGetAllAsync(infoKey);
                var dict = entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

            
                var dto = new PartyDto
                {
                    PartyId = int.Parse(dict["PartyId"]),
                    Title = dict.GetValueOrDefault("Title", "Unknown"),
                    LeaderName = dict.GetValueOrDefault("LeaderName", "Unknown"),
                    CurrentCount = int.Parse(dict.GetValueOrDefault("CurrentCount", "0")),
                    MaxCount = int.Parse(dict.GetValueOrDefault("MaxCount", "4")),
                    DungeonId = int.Parse(dict.GetValueOrDefault("DungeonId", "0"))
                };
                list.Add(dto);
            }

            
            return Ok(list.OrderByDescending(p => p.PartyId));
        }

        [HttpPost("enter")]
        public async Task<IActionResult> EnterDungeon([FromBody] PartyEnterReq req)
        {
            var db = _redis.GetDatabase();
            string infoKey = KEY_INFO_PREFIX + req.PartyId;

            
            var storedLeaderId = await db.HashGetAsync(infoKey, "LeaderId");
            if (storedLeaderId != req.UserId)
            {
                return BadRequest("방장만 게임을 시작할 수 있습니다.");
            }

            
            await db.SetRemoveAsync(KEY_LIST, req.PartyId);

                        await db.HashSetAsync(infoKey, "Status", "InGame");

            return Ok(new { Message = "던전 입장 시작", TargetScene = "Dungeon_1" });
        }
    }

    
}