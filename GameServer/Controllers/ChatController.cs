using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Threading.Tasks;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;

        public ChatController(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatMessageReq req)
        {
            var db = _redis.GetDatabase();
            var sub = _redis.GetSubscriber();

            string formattingMessage = $"{req.Nickname}:{req.Message}";
            await sub.PublishAsync("chat:global", formattingMessage);
            return Ok();
        }
    }
}
public class ChatMessageReq
{
    public string Nickname { get; set; }
    public string Message { get; set; }
}