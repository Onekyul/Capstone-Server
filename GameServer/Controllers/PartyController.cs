using GameServer.DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;


namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PartyController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<PartyController> _logger;

        // Redis Key 규칙
        private const string KEY_ID_GEN = "Party:IdGen";      // 파티 번호 생성
        private const string KEY_LIST = "Party:List";         // Set
        private const string KEY_INFO_PREFIX = "Party:";      // 개별 방 정보 (Hash)

        public PartyController(IConnectionMultiplexer redis, ILogger<PartyController> logger)
        {
            _redis = redis;
            _logger = logger;
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
                new HashEntry("DungeonId", req.DungeonId),
                new HashEntry("Status", "Waiting"),
                new HashEntry($"Member:{req.LeaderId}", req.LeaderNickname)
            };

            await db.HashSetAsync(infoKey, entries);

            // 리더를 Members Set에 추가
            string membersKey = infoKey + ":Members";
            await db.SetAddAsync(membersKey, req.LeaderId);

            // 방 목록에 등록
            await db.SetAddAsync(KEY_LIST, newPartyId);

            return Ok(new { PartyId = newPartyId, Message = "파티 생성 완료", LeaderId = req.LeaderId });
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

            
                // Waiting 상태인 파티만 목록에 포함
                string status = dict.GetValueOrDefault("Status", "Waiting");
                if (status != "Waiting")
                    continue;

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

        [HttpGet("detail/{partyId}")]
        public async Task<IActionResult> GetPartyDetail(int partyId)
        {
            var db = _redis.GetDatabase();
            string infoKey = KEY_INFO_PREFIX + partyId;

            if (!await db.KeyExistsAsync(infoKey))
                return NotFound("존재하지 않는 파티입니다.");

            var entries = await db.HashGetAllAsync(infoKey);
            var dict = entries.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

            // Ready Set 조회
            string readyKey = infoKey + ":Ready";
            var readySet = await db.SetMembersAsync(readyKey);
            var readyUserIds = readySet.Select(r => r.ToString()).ToHashSet();

          
            var members = dict
                .Where(kv => kv.Key.StartsWith("Member:"))
                .Select(kv => new MemberDto
                {
                    UserId = int.Parse(kv.Key.Substring("Member:".Length)),
                    Nickname = kv.Value,
                    IsReady = readyUserIds.Contains(kv.Key.Substring("Member:".Length))
                })
                .ToList();

            var dto = new PartyDetailDto
            {
                PartyId = int.Parse(dict["PartyId"]),
                LeaderId = int.Parse(dict.GetValueOrDefault("LeaderId", "0")),
                DungeonId = int.Parse(dict.GetValueOrDefault("DungeonId", "0")),
                MaxCount = int.Parse(dict.GetValueOrDefault("MaxCount", "4")),
                Members = members,
                Status = dict.GetValueOrDefault("Status", "Waiting"),
                SessionName = dict.GetValueOrDefault("SessionName", "")
            };

            return Ok(dto);
        }

        [HttpPost("join")]
        public async Task<IActionResult> JoinParty([FromBody] PartyJoinReq req)
        {
            var db = _redis.GetDatabase();
            string infoKey = KEY_INFO_PREFIX + req.PartyId;

           
            if (!await db.KeyExistsAsync(infoKey))
                return NotFound("존재하지 않는 파티입니다.");

            // 현재 인원 / 최대 인원 확인
            int currentCount = (int)await db.HashGetAsync(infoKey, "CurrentCount");
            int maxCount = (int)await db.HashGetAsync(infoKey, "MaxCount");

            if (currentCount >= maxCount)
                return BadRequest("파티 인원이 가득 찼습니다.");

            // 이미 참가한 유저인지 확인
            string membersKey = infoKey + ":Members";
            if (await db.SetContainsAsync(membersKey, req.UserId))
                return BadRequest("이미 참가한 파티입니다.");

            // 멤버 추가 및 인원 증가
            await db.SetAddAsync(membersKey, req.UserId);
            await db.HashSetAsync(infoKey, new HashEntry[]
            {
                new HashEntry("CurrentCount", currentCount + 1),
                new HashEntry($"Member:{req.UserId}", req.Nickname)
            });

            int leaderId = (int)await db.HashGetAsync(infoKey, "LeaderId");
            return Ok(new { PartyId = req.PartyId, Message = "파티 참가 완료", CurrentCount = currentCount + 1, LeaderId = leaderId });
        }

        [HttpPost("ready")]
        public async Task<IActionResult> SetReady([FromBody] PartyReadyReq req)
        {
            var db = _redis.GetDatabase();
            string infoKey = KEY_INFO_PREFIX + req.PartyId;

            if (!await db.KeyExistsAsync(infoKey))
                return NotFound("존재하지 않는 파티입니다.");

            string membersKey = infoKey + ":Members";
            if (!await db.SetContainsAsync(membersKey, req.UserId))
                return BadRequest("파티에 참가하지 않은 유저입니다.");

            string readyKey = infoKey + ":Ready";
            if (req.IsReady)
            {
                await db.SetAddAsync(readyKey, req.UserId);
                return Ok(new { Message = "준비 완료" });
            }
            else
            {
                await db.SetRemoveAsync(readyKey, req.UserId);
                return Ok(new { Message = "준비 취소" });
            }
        }

        [HttpPost("leave")]
        public async Task<IActionResult> LeaveParty([FromBody] PartyLeaveReq req)
        {
            var db = _redis.GetDatabase();
            string infoKey = KEY_INFO_PREFIX + req.PartyId;

            // 파티 존재 여부 확인
            if (!await db.KeyExistsAsync(infoKey))
                return NotFound("존재하지 않는 파티입니다.");

            int leaderId = (int)await db.HashGetAsync(infoKey, "LeaderId");

            // 방장이 탈퇴하면 파티 해산
            if (req.UserId == leaderId)
            {
                string membersKey = infoKey + ":Members";
                await db.KeyDeleteAsync(membersKey);
                await db.KeyDeleteAsync(infoKey + ":Ready");
                await db.KeyDeleteAsync(infoKey);
                await db.SetRemoveAsync(KEY_LIST, req.PartyId);
                return Ok(new { Message = "방장 탈퇴로 파티가 해산되었습니다." });
            }

            // 일반 멤버 탈퇴
            string memberKey = infoKey + ":Members";
            if (!await db.SetContainsAsync(memberKey, req.UserId))
                return BadRequest("파티에 참가하지 않은 유저입니다.");

            await db.SetRemoveAsync(memberKey, req.UserId);
            await db.SetRemoveAsync(infoKey + ":Ready", req.UserId);
            await db.HashDeleteAsync(infoKey, $"Member:{req.UserId}");
            long currentCount = await db.HashDecrementAsync(infoKey, "CurrentCount");

            return Ok(new { Message = "파티 탈퇴 완료", CurrentCount = currentCount });
        }

        [HttpPost("change-dungeon")]
        public async Task<IActionResult> ChangeDungeon([FromBody] PartyChangeDungeonReq req)
        {
            var db = _redis.GetDatabase();
            string infoKey = KEY_INFO_PREFIX + req.PartyId;

            if (!await db.KeyExistsAsync(infoKey))
                return NotFound("존재하지 않는 파티입니다.");

            int leaderId = (int)await db.HashGetAsync(infoKey, "LeaderId");
            if (req.UserId != leaderId)
                return BadRequest("방장만 던전을 변경할 수 있습니다.");

            await db.HashSetAsync(infoKey, "DungeonId", req.DungeonId);

            return Ok(new { Message = "던전 변경 완료", DungeonId = req.DungeonId });
        }

        [HttpPost("kick")]
        public async Task<IActionResult> KickMember([FromBody] PartyKickReq req)
        {
            var db = _redis.GetDatabase();
            string infoKey = KEY_INFO_PREFIX + req.PartyId;

            if (!await db.KeyExistsAsync(infoKey))
                return NotFound("존재하지 않는 파티입니다.");

            int leaderId = (int)await db.HashGetAsync(infoKey, "LeaderId");
            if (req.LeaderId != leaderId)
                return BadRequest("방장만 강퇴할 수 있습니다.");

            if (req.TargetUserId == leaderId)
                return BadRequest("방장은 자기 자신을 강퇴할 수 없습니다.");

            string membersKey = infoKey + ":Members";
            if (!await db.SetContainsAsync(membersKey, req.TargetUserId))
                return BadRequest("파티에 존재하지 않는 유저입니다.");

            await db.SetRemoveAsync(membersKey, req.TargetUserId);
            await db.HashDeleteAsync(infoKey, $"Member:{req.TargetUserId}");
            long currentCount = await db.HashDecrementAsync(infoKey, "CurrentCount");

            return Ok(new { Message = "강퇴 완료", CurrentCount = currentCount });
        }

        [HttpPost("enter")]
        public async Task<IActionResult> EnterDungeon([FromBody] PartyEnterReq req)
        {
            var db = _redis.GetDatabase();
            var sub = _redis.GetSubscriber();
            string infoKey = KEY_INFO_PREFIX + req.PartyId;

            if (!await db.KeyExistsAsync(infoKey))
                return NotFound("존재하지 않는 파티입니다.");

            var storedLeaderId = await db.HashGetAsync(infoKey, "LeaderId");
            if (storedLeaderId != req.UserId)
                return BadRequest("방장만 게임을 시작할 수 있습니다.");

            // 전원 준비 완료 여부 검증
            string membersKey = infoKey + ":Members";
            var allMembers = await db.SetMembersAsync(membersKey);
            if (allMembers.Length > 1)
            {
                string readyKey = infoKey + ":Ready";
                var readySet = await db.SetMembersAsync(readyKey);
                var readyIds = readySet.Select(r => r.ToString()).ToHashSet();
                var notReady = allMembers
                    .Where(m => m.ToString() != req.UserId.ToString() && !readyIds.Contains(m.ToString()))
                    .ToList();
                if (notReady.Any())
                    return BadRequest(new { Message = "모든 파티원이 준비를 완료해야 합니다." });
            }

        
            await db.SetRemoveAsync(KEY_LIST, req.PartyId);

            // 서버 세션 생성
            string sessionName = "boss_" + Guid.NewGuid().ToString();
            int currentCount = (int)await db.HashGetAsync(infoKey, "CurrentCount");

            var sessionData = JsonSerializer.Serialize(new { status = "preparing", partyId = req.PartyId });
            await db.StringSetAsync($"dungeon_session:{sessionName}", sessionData, TimeSpan.FromSeconds(1800));

            // 서버 준비 완료 대기
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await sub.SubscribeAsync(RedisChannel.Literal("boss-dungeon:ready"), (channel, message) =>
            {
                if (message.ToString() == sessionName)
                    tcs.TrySetResult(true);
            });

            await sub.PublishAsync(RedisChannel.Literal("boss-dungeon:create"), $"{sessionName}|{currentCount}");
            _logger.LogInformation($"[Party] 던전 입장 요청: PartyId={req.PartyId}, Session={sessionName}");

            // 최대 5초 대기
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => tcs.TrySetCanceled());

            try
            {
                await tcs.Task;
                await sub.UnsubscribeAsync(RedisChannel.Literal("boss-dungeon:ready"));

                // 세션 상태 업데이트
                var readyData = JsonSerializer.Serialize(new { status = "ready", partyId = req.PartyId });
                await db.StringSetAsync($"dungeon_session:{sessionName}", readyData, TimeSpan.FromSeconds(1800));

                // 파티 상태를 InGame으로 변경
                await db.HashSetAsync(infoKey, new HashEntry[]
                {
                    new HashEntry("Status", "InGame"),
                    new HashEntry("SessionName", sessionName)
                });

                _logger.LogInformation($"[Party] 던전 입장 완료: PartyId={req.PartyId}, Session={sessionName}");
                return Ok(new { Message = "던전 입장 시작", SessionName = sessionName });
            }
            catch (TaskCanceledException)
            {
                await sub.UnsubscribeAsync(RedisChannel.Literal("boss-dungeon:ready"));
                await db.KeyDeleteAsync($"dungeon_session:{sessionName}");

                // 파티 목록에 다시 등록
                await db.SetAddAsync(KEY_LIST, req.PartyId);

                _logger.LogWarning($"[Party] 데디서버 타임아웃: PartyId={req.PartyId}");
                return StatusCode(503, new { Message = "데디케이티드 서버 준비 타임아웃" });
            }
        }
    }
}