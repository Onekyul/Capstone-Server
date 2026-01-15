using Microsoft.AspNetCore.Http;
using GameServer.Data;
using GameServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AuthController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("guest-login")]
        public IActionResult GuestLogin([FromBody] GuestLoginReq req)
        {
            if (req == null || string.IsNullOrEmpty(req.DeviceId))
            {
                return BadRequest("NO device ID");
            }

            var user = _context.Users.FirstOrDefault(u => u.DeviceId == req.DeviceId);
            if (user == null)
            {
                user = new User
                {
                    DeviceId = req.DeviceId,
                    Nickname = "모험가" + new Random().Next(1000, 9999),
                    MaxClearedStage = 0,
                    CreatedAt = DateTime.Now,
                };
                _context.Users.Add(user); // DB 추가 대기
                _context.SaveChanges();   // 실제 저장 (이때 ID가 발급됨)

                Console.WriteLine($"[신규 가입] {user.Nickname} (ID: {user.Id})");
            }
            else
            {
                Console.WriteLine($"[로그인] {user.Nickname} (ID: {user.Id})");
            }


            return Ok(new
            {
                userId = user.Id,
                nickname = user.Nickname,
                stage = user.MaxClearedStage
            });
        }

    }
    public class GuestLoginReq
    {
        public string DeviceId { get; set; }
    }
}
