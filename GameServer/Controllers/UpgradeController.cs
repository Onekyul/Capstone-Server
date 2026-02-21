using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UpgradeController : ControllerBase
    {
       
        [HttpPost("attempt")]
        public async Task<IActionResult> TryUpgrade([FromBody] UpgradeReqDto req)
        {
          
            Console.WriteLine($"[강화요청] 유저:{req.UserId}, 타겟:{req.TargetId}, 소모재료:[{req.MaterialInfo}], 성공확률:{req.SuccessRate * 100}%");

          
            Random rand = new Random();
            double roll = rand.NextDouble();

       
            bool isSuccess = roll <= req.SuccessRate;

            if (isSuccess)
            {
                Console.WriteLine($"결과: [성공] (값: {roll:F2}");
            }
            else
            {
                Console.WriteLine($" 결과: [실패] (값: {roll:F2} ");
            }

          
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