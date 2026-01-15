using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; 
using GameServer.Data;
using GameServer.Models;
using System.Linq;
namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;

        public GameController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("load")]
        public IActionResult LoadGameData([FromBody] LoadGameReq req)
        {
            var user = _context.Users
             .Include(u => u.Items)       // user_items 테이블 조인
             .Include(u => u.Equipments)  // user_equipments 테이블 조인
             .Include(u => u.Enchants)    // user_enchants 테이블 조인
             .FirstOrDefault(u => u.Id == req.UserId);

            if (user == null)
            {
                return BadRequest("유저를 찾을 수 없습니다.");
            }

            var response = new
            {
                nickname = user.Nickname,
                stage = user.MaxClearedStage,

                equip = new
                {
                    weapon = user.EquippedWeaponId,
                    helmet = user.EquippedHelmetId,
                    armor = user.EquippedArmorId,
                    boots = user.EquippedBootsId
                },

                inventory = user.Items.Select(i => new { id = i.ItemId, count = i.Count }).ToList(),
                equipments = user.Equipments.Select(e => new { id = e.ItemId, level = e.Level }).ToList(),
                enchants = user.Enchants.Select(e => new { id = e.EnchantId, level = e.Level }).ToList()
            };
            return Ok(response);
        }
    }
}

public class LoadGameReq
{
    public int UserId { get; set; } // 로그인 성공했을 때 받은 그 ID
}
