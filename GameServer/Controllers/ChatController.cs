using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;

       
        private const string CHAT_LIST_KEY = "Chat:History"; 
        private const string CHAT_PUB_KEY = "Chat:Lobby";   

        public ChatController(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

      
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatMessageReq req)
        {
            var db = _redis.GetDatabase();
            var sub = _redis.GetSubscriber();

         
            string timeStamp = DateTime.Now.ToString("HH:mm");
            string formattedMessage = $"<color=#AAAAAA>[{timeStamp}]</color> <color=yellow>{req.Nickname}</color> : {req.Message}";

          
            await db.ListRightPushAsync(CHAT_LIST_KEY, formattedMessage);
            await db.ListTrimAsync(CHAT_LIST_KEY, -50, -1); // 최신 50개만 유지

          
            await sub.PublishAsync(CHAT_PUB_KEY, formattedMessage);

            return Ok();
        }

       
        [HttpGet("receive")]
        public async Task<IActionResult> GetMessages()
        {
            var db = _redis.GetDatabase();

            // List에 저장된 최근 메시지 20개  반환
            var redisList = await db.ListRangeAsync(CHAT_LIST_KEY, -20, -1);

            // RedisValue[] -> string[]
            var messages = redisList.Select(x => x.ToString()).ToArray();

            return Ok(messages);
        }
    }

    public class ChatMessageReq
    {
        public string Nickname { get; set; }
        public string Message { get; set; }
    }
}