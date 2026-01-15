using GameServer.Data;
using GameServer.DTO;
using GameServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; 
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
        [HttpPost("save")]
        public IActionResult SaveGame([FromBody] GameDataDto clientData)
        {
            if (clientData == null) return BadRequest("데이터가 비어있습니다.");

            // 1. 유저 존재 확인 및 기본 정보(스테이지, 장착 슬롯) 업데이트
            var user = _context.Users.FirstOrDefault(u => u.Id == clientData.userId);
            if (user == null) return NotFound("유저를 찾을 수 없습니다.");

            // 스테이지 저장 (DTO에 stage가 있다면)
            user.MaxClearedStage = clientData.stage;

            // 장착 중인 아이템 ID 저장
            if (clientData.equip != null)
            {
                user.EquippedWeaponId = clientData.equip.weapon;
                user.EquippedHelmetId = clientData.equip.helmet;
                user.EquippedArmorId = clientData.equip.armor;
                user.EquippedBootsId = clientData.equip.boots;
            }

            //2.인벤토리 저장(user_items 테이블)
            var oldItems = _context.UserItems.Where(i => i.UserId == clientData.userId);
            _context.UserItems.RemoveRange(oldItems); // 기존 템 삭제

            foreach (var itemDto in clientData.inventory)
            {
                _context.UserItems.Add(new UserItem
                {
                    UserId = clientData.userId,
                    ItemId = itemDto.id,
                    Count = itemDto.count
                });
            }

            
            // 3. 장비 저장 (user_equipments 테이블)
            var oldEquips = _context.UserEquipments.Where(e => e.UserId == clientData.userId);
            _context.UserEquipments.RemoveRange(oldEquips);

            foreach (var equipDto in clientData.equipments)
            {
                _context.UserEquipments.Add(new UserEquipment
                {
                    UserId = clientData.userId,
                    ItemId = equipDto.id,
                    Level = equipDto.level
                });
            }

            
            // 4. 인챈트 저장 (user_enchants 테이블)
            var oldEnchants = _context.UserEnchants.Where(e => e.UserId == clientData.userId);
            _context.UserEnchants.RemoveRange(oldEnchants);

            foreach (var enchantDto in clientData.enchants)
            {
                _context.UserEnchants.Add(new UserEnchant
                {
                    UserId = clientData.userId,
                    EnchantId = enchantDto.id,
                    Level = enchantDto.level
                });
            }

            // 5. 최종 DB 반영 (Commit)
            _context.SaveChanges();

            return Ok(new { message = "저장 완료!" });
        }
    }
}

public class LoadGameReq
{
    public int UserId { get; set; } // 로그인 성공했을 때 받은 그 ID
}
