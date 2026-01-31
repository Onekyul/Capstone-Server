using GameServer.Data;
using GameServer.DTO;
using GameServer.Models;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GameServer.Services
{
    public class DbSyncWorker : BackgroundService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceProvider _serviceProvider; // DB 연결을 위한 도구
        private readonly ILogger<DbSyncWorker> _logger;

        private const int batchSize = 50; // 한 번

        public DbSyncWorker(IConnectionMultiplexer redis, IServiceProvider serviceProvider, ILogger<DbSyncWorker> logger)
        {
            _redis = redis; 
            _serviceProvider = serviceProvider; 
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppringToken)
        {
            _logger.LogInformation(">>> Write-Back 워커 시작");
            var db = _redis.GetDatabase();

            while (!stoppringToken.IsCancellationRequested)
            {
                try
                {
                    //Redis 큐에서 배치 만큼 꺼내기
                    //StackExchange.Redis는 pop을 한번 씩 -> 반복문 사용
                    List<string> userIds = new List<string>();

                    for(int i=0; i < batchSize; i++)
                    {
                        var redisValue = await db.ListRightPopAsync("task:writeback");
                        if (!redisValue.HasValue) break;

                        string uid = redisValue.ToString();
                        if (!userIds.Contains(uid))
                        {
                            userIds.Add(uid);
                        }
                    }

                    if (userIds.Count > 0)
                    {
                        _logger.LogInformation($"[Batch Save] {userIds.Count}명의 데이터 저장 시작");
                        await ProcessBatchSaveAsync(db, userIds);
                    }
                    else
                    {
                        //큐가 비었을때만 1초 Delay
                        await Task.Delay(1000, stoppringToken);
                    }
                  
                }
                catch (Exception ex)
                {
                    _logger.LogError($"워커 에러 발생 : {ex.Message}");
                    await Task.Delay(1000, stoppringToken); //에러 후 1초뒤 재시작
                }
            }
        }
     
        //Redis_to_DB 저장 로직
        private async Task ProcessBatchSaveAsync(IDatabase redisDb, List<string> userIds)
        {
           
            using (var scope = _serviceProvider.CreateScope())
            {
                var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                foreach(var userId in userIds)
                {
                    string json = await redisDb.StringGetAsync($"user:{userId}:data");
                    if (string.IsNullOrEmpty(json)) continue;

                    var clientData = JsonSerializer.Deserialize<GameDataDto>(json);
                    
                    // 유저 정보 조회
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                    if (user == null) return; // 유저가 없으면 스킵

                    user.MaxClearedStage = clientData.stage;
                    if (clientData.equip != null)
                    {
                        user.EquippedWeaponId = clientData.equip.weapon;
                        user.EquippedHelmetId = clientData.equip.helmet;
                        user.EquippedArmorId = clientData.equip.armor;
                        user.EquippedBootsId = clientData.equip.boots;
                    }

                    // 인벤토리 저장 
                    var oldItems = _context.UserItems.Where(i => i.UserId == user.Id);
                    _context.UserItems.RemoveRange(oldItems);
                    foreach (var itemDto in clientData.inventory)
                    {
                        _context.UserItems.Add(new UserItem
                        {
                            UserId = user.Id,
                            ItemId = itemDto.id,
                            Count = itemDto.count
                        });
                    }

                    // 장비 저장
                    var oldEquips = _context.UserEquipments.Where(e => e.UserId == user.Id);
                    _context.UserEquipments.RemoveRange(oldEquips);
                    foreach (var equipDto in clientData.equipments)
                    {
                        _context.UserEquipments.Add(new UserEquipment
                        {
                            UserId = user.Id,
                            ItemId = equipDto.id,
                            Level = equipDto.level
                        });
                    }

                    // 인챈트 저장
                    var oldEnchants = _context.UserEnchants.Where(e => e.UserId == user.Id);
                    _context.UserEnchants.RemoveRange(oldEnchants);
                    foreach (var enchantDto in clientData.enchants)
                    {
                        _context.UserEnchants.Add(new UserEnchant
                        {
                            UserId = user.Id,
                            EnchantId = enchantDto.id,
                            Level = enchantDto.level
                        });
                    }

                }
            
                await _context.SaveChangesAsync();

                _logger.LogInformation($"[Batch Complete] 유저 {userIds.Count}명 DB 저장 완료.");

            }

        }


    }
}
