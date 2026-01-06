using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using GameServer; // AppDbContext가 있는 네임스페이스 (에러나면 Alt+Enter로 수정)

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

var app = builder.Build();

// 3. 서버 시작 시 DB 연결 테스트 
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        // MySQL 연결 확인
        var dbContext = services.GetRequiredService<AppDbContext>();
        // 데이터베이스가 없으면 생성 (처음 실행 시 유용)
        // dbContext.Database.EnsureCreated(); 

        if (dbContext.Database.CanConnect())
        {
            logger.LogInformation(" [MySQL] DB 연결 성공! (Port: 3306)");
        }
        else
        {
            logger.LogError(" [MySQL] 연결 실패...");
        }

        // Redis 연결 확인
        var redis = services.GetRequiredService<IConnectionMultiplexer>();
        if (redis.IsConnected)
        {
            logger.LogInformation(" [Redis] 캐시 서버 연결 성공! (Port: 6379)");
        }
        else
        {
            logger.LogError(" [Redis] 연결 실패...");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "서버 시작 도중 치명적인 에러 발생!");
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