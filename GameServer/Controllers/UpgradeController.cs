using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UpgradeController : ControllerBase
    {
        private readonly ILogger<UpgradeController> _logger;

        public UpgradeController(ILogger<UpgradeController> logger)
        {
            _logger = logger;
        }

        [HttpPost("attempt")]
        public async Task<IActionResult> TryUpgrade([FromBody] UpgradeReqDto req)
        {
            Random rand = new Random();
            int roll = rand.Next(0, 100);

            bool isSuccess = roll < (int)(req.SuccessRate * 100);

            string result = isSuccess ? "성공" : "실패";
            string timestamp = DateTime.Now.ToString("yy MM dd HH mm ss");

            _logger.LogInformation($"[강화] '{req.Nickname}' | '{req.TargetId}' | 난수:'{roll}' | 확률:'{(int)(req.SuccessRate * 100)}'% | 결과:'{result}' | [{timestamp}]");

          
            var res = new UpgradeResDto
            {
                Success = isSuccess,
                Message = isSuccess ? "성공" : "실패"
            };

            return Ok(res);
        }
    }

 

    public class UpgradeReqDto
    {
        public int UserId { get; set; }
        public string Nickname { get; set; }
        public string TargetId { get; set; }
        public string MaterialInfo { get; set; }
        public float SuccessRate { get; set; }
    }

    public class UpgradeResDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}