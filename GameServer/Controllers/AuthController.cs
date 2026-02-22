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

            // 유저가 DB에 없으면 에러 반환
            if (user == null)
            {
                return NotFound("User not found");
            }

            Console.WriteLine($"[로그인] {user.Nickname} (ID: {user.Id})");

            return Ok(new
            {
                userId = user.Id,
                nickname = user.Nickname,
                stage = user.MaxClearedStage
            });
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterReq req)
        {
           
            if (req == null || string.IsNullOrEmpty(req.DeviceId) || string.IsNullOrEmpty(req.Nickname))
            {
                return BadRequest("Invalid request data");
            }

            // 닉네임 중복 검사 
            bool isDuplicate = _context.Users.Any(u => u.Nickname == req.Nickname);
            if (isDuplicate)
            {
                return BadRequest("Nickname already exists");
            }

            
            var user = new User
            {
                DeviceId = req.DeviceId,
                Nickname = req.Nickname, 
                MaxClearedStage = 0,
                CreatedAt = DateTime.Now,
            };

            _context.Users.Add(user); 
            _context.SaveChanges();   

            Console.WriteLine($"[신규 가입] {user.Nickname} (ID: {user.Id})");

            return Ok(new
            {
                userId = user.Id,
                nickname = user.Nickname,
                stage = user.MaxClearedStage
            });
        }
    }

    [HttpGet("check-nickname")]
        public IActionResult CheckNickname([FromQuery] string nickname)
        {
            if (string.IsNullOrEmpty(nickname))
            {
                return BadRequest("Nickname is required");
            }

            // DB에 해당 닉네임이 존재하는지 검사
            bool isDuplicate = _context.Users.Any(u => u.Nickname == nickname);

            // available이 true면 사용 가능, false면 중복(사용 불가)
            return Ok(new { available = !isDuplicate });
        }

    }
    public class GuestLoginReq
    {
        public string DeviceId { get; set; }
    }

public class RegisterReq
{
    public string DeviceId { get; set; }
    public string Nickname { get; set; }
}

