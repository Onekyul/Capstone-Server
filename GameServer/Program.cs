using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using GameServer.Data;
using GameServer.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. MySQL 등록
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 2. Redis 등록 
var redisString = builder.Configuration.GetConnectionString("RedisConnection");

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisString));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<DbSyncWorker>();

var app = builder.Build();

// 3. 서버 시작 시 DB 연결 테스트 
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        // 1. MySQL 연결 정밀 검사
        var dbContext = services.GetRequiredService<AppDbContext>();

       
        dbContext.Database.OpenConnection();
        dbContext.Database.CloseConnection();

        logger.LogInformation(" [MySQL] DB 연결 성공! (Port: 3306)");

        // 2. Redis 연결 검사
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        if (redis.IsConnected)
            logger.LogInformation(" [Redis] 캐시 서버 연결 성공!");
    }
    catch (Exception ex)
    {
       
        logger.LogError($" 서버 시작 실패! 원인: {ex.Message}");

        if (ex.InnerException != null)
        {
            logger.LogError($" 상세 원인: {ex.InnerException.Message}");
        }
    }
}


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();