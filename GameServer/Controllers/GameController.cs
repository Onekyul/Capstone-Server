using GameServer.Data;
using GameServer.DTO;
using GameServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
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


        //Save API
        [HttpPost("save")]
        public async Task<IActionResult> SaveGame([FromBody] GameDataDto clientData)
        {
            if (clientData == null) return BadRequest("데이터가 비어있습니다.");

            var db = _redis.GetDatabase();
            string key = $"user:{clientData.userId}:data";

            //Redis에 최신 상태 저장
            string jsonString = JsonSerializer.Serialize(clientData);
            await db.StringSetAsync(key, jsonString);

            //작업 큐에 User ID 등록(LPUSH)
            await db.ListLeftPushAsync("task:writeback", clientData.userId.ToString());

            return Ok(new { message = "서버 메모리에 저장됨(Async)" });
           
        }
    }
}

public class LoadGameReq
{
    public int UserId { get; set; } // 로그인 성공했을 때 받은 그 ID
}
