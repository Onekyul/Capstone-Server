using GameServer.Data;
using GameServer.DTO;
using GameServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context; //DB 연결 객체
        private readonly IConnectionMultiplexer _redis; // Redis 연결 객체

        public GameController(AppDbContext context,IConnectionMultiplexer redis)
        {
            _context = context;
            _redis = redis;
        }





        //load API
        [HttpPost("load")]
        public async Task<IActionResult> LoadGameData([FromBody] LoadGameReq req)
        {
            var db = _redis.GetDatabase();
            string key = $"user:{req.UserId}:data";

            string cachedData = await db.StringGetAsync(key); //Redis 캐시 확인
            if (!string.IsNullOrEmpty(cachedData))
            {
                //캐시 존재시 바로 리턴
                var data = JsonSerializer.Deserialize<GameDataDto>(cachedData); 
                return Ok(data);
            }

            var user = await _context.Users
             .Include(u => u.Items)
             .Include(u => u.Equipments)
             .Include(u => u.Enchants)
             .FirstOrDefaultAsync(u => u.Id == req.UserId);

            if (user == null)
            {
                return BadRequest("유저를 찾을 수 없습니다.");
            }

            var response = new
            {
                userId = user.Id,
                stage = user.MaxClearedStage,

                equip = new
                {
                    weapon = user.EquippedWeaponId,
                    helmet = user.EquippedHelmetId,
                    armor = user.EquippedArmorId,
                    boots = user.EquippedBootsId
                },

                inventory = user.Items.Select(i => new ItemDto { id = i.ItemId, count = i.Count }).ToList(),
                equipments = user.Equipments.Select(e => new EquipItemDto { id = e.ItemId, level = e.Level }).ToList(),
                enchants = user.Enchants.Select(e => new EnchantDto { id = e.EnchantId, level = e.Level }).ToList()
            };
            return Ok(response);
        }


        // 데디서버용: 플레이어 장비 원본 데이터 반환
        [HttpGet("player-stats")]
        public async Task<IActionResult> GetPlayerStats([FromQuery] int userId)
        {
            var db = _redis.GetDatabase();
            string key = $"user:{userId}:data";

            string cachedData = await db.StringGetAsync(key);

            if (!string.IsNullOrEmpty(cachedData))
            {
                var data = JsonSerializer.Deserialize<GameDataDto>(cachedData);
                var response = new PlayerStatsRes
                {
                    userId = data.userId,
                    equippedWeapon = data.equip?.weapon,
                    equippedHelmet = data.equip?.helmet,
                    equippedArmor = data.equip?.armor,
                    equippedBoots = data.equip?.boots,
                    equipments = data.equipments ?? new List<EquipItemDto>()
                };
                return Ok(response);
            }

            // 캐시 미스 시 DB 조회
            var user = await _context.Users
                .Include(u => u.Equipments)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound("유저를 찾을 수 없습니다.");

            return Ok(new PlayerStatsRes
            {
                userId = user.Id,
                equippedWeapon = user.EquippedWeaponId,
                equippedHelmet = user.EquippedHelmetId,
                equippedArmor = user.EquippedArmorId,
                equippedBoots = user.EquippedBootsId,
                equipments = user.Equipments.Select(e => new EquipItemDto { id = e.ItemId, level = e.Level }).ToList()
            });
        }

        //Save API
        [HttpPost("save")]
        public async Task<IActionResult> SaveGame([FromBody] GameDataDto clientData)
        {
            if (clientData == null) return BadRequest("데이터가 비어있습니다.");

            try
            {
                var db = _redis.GetDatabase();
                string key = $"user:{clientData.userId}:data";

                //Redis에 최신 상태 저장
                string jsonString = JsonSerializer.Serialize(clientData);
                await db.StringSetAsync(key, jsonString);

                // [E2E 측정] 큐 push 시점을 EnqueuedAt에 기록하여
                // DbSyncWorker가 pop 시 (DateTime.UtcNow - EnqueuedAt)으로
                // E2E 지연(큐 push → DB commit)을 계산할 수 있도록 함.
                // 본 논문 4장의 핵심 종속변수 측정 인프라.
                var queueItem = new WriteBackQueueItem
                {
                    UserId = clientData.userId.ToString(),
                    EnqueuedAt = DateTime.UtcNow
                };
                var queueJson = JsonSerializer.Serialize(queueItem);
                await db.ListLeftPushAsync("task:writeback", queueJson);

                return Ok(new { message = "서버 메모리에 저장됨(Async)" });
            }
            catch (RedisConnectionException ex)
            {
                return StatusCode(503, new { error = "Redis 연결 실패", detail = ex.Message });
            }
            catch (RedisTimeoutException ex)
            {
                return StatusCode(504, new { error = "Redis 타임아웃", detail = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "서버 내부 오류", detail = ex.Message });
            }
        }

        
        // [벤치마크용] MySQL 직접 저장 — Write-Back과 성능 비교를 위한 엔드포인트.
        // upsert 패턴(INSERT ... ON DUPLICATE KEY UPDATE)을 적용하여
        // DbSyncWorker와 동일한 DB 처리 방식으로 공정한 비교가 가능하도록 함.
        [HttpPost("save-direct")]
        public async Task<IActionResult> SaveGameDirect([FromBody] GameDataDto clientData)
        {
            if (clientData == null) return BadRequest("데이터가 비어있습니다.");

            try
            {
                // child collection은 upsert로 처리하므로 Include 불필요
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == clientData.userId);

                if (user == null) return BadRequest(new { error = "유저 없음", detail = $"userId={clientData.userId}가 DB에 존재하지 않습니다." });

                // users 테이블: EF 변경 추적으로 처리
                user.MaxClearedStage = clientData.stage;
                if (clientData.equip != null)
                {
                    user.EquippedWeaponId = clientData.equip.weapon;
                    user.EquippedHelmetId = clientData.equip.helmet;
                    user.EquippedArmorId  = clientData.equip.armor;
                    user.EquippedBootsId  = clientData.equip.boots;
                }

                // upsert 행 수집
                var inventoryRows = clientData.inventory
                    .Select(i => new object[] { clientData.userId, i.id, i.count })
                    .ToList();
                var equipmentRows = clientData.equipments
                    .Select(e => new object[] { clientData.userId, e.id, e.level })
                    .ToList();
                var enchantRows = clientData.enchants
                    .Select(e => new object[] { clientData.userId, e.id, e.level })
                    .ToList();

                using var tx = await _context.Database.BeginTransactionAsync();

                if (inventoryRows.Count > 0)
                    await BulkUpsertAsync(_context,
                        "INSERT INTO user_items (user_id, item_id, `count`) VALUES ",
                        " ON DUPLICATE KEY UPDATE `count` = VALUES(`count`)",
                        inventoryRows, 3);

                if (equipmentRows.Count > 0)
                    await BulkUpsertAsync(_context,
                        "INSERT INTO user_equipments (user_id, item_id, level) VALUES ",
                        " ON DUPLICATE KEY UPDATE level = VALUES(level)",
                        equipmentRows, 3);

                if (enchantRows.Count > 0)
                    await BulkUpsertAsync(_context,
                        "INSERT INTO user_enchants (user_id, enchant_id, level) VALUES ",
                        " ON DUPLICATE KEY UPDATE level = VALUES(level)",
                        enchantRows, 3);

                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                return Ok(new { message = "DB 직접 저장 완료" });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                return StatusCode(409, new { error = "DB 동시성 충돌 (Deadlock)", detail = ex.InnerException?.Message ?? ex.Message });
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new { error = "DB 저장 실패", detail = ex.InnerException?.Message ?? ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "서버 내부 오류", detail = ex.Message });
            }
        }

        // DbSyncWorker.BulkUpsertAsync와 동일한 로직.
        // ExecuteSqlRawAsync의 {N} 바인딩으로 SQL Injection 방지.
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

public class LoadGameReq
{
    public int UserId { get; set; } 
}
