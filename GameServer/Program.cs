using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using GameServer.Data;
using GameServer.Services;
using GameServer.Configuration;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// [측정 자동화 지원] DBSYNC_ 접두사 환경변수로 설정 override 가능하게.
// 측정 자동화 스크립트가 DBSYNC_BatchSize=100 같은 환경변수로
// 동일 바이너리에서 배치 사이즈만 변경하여 실험을 반복 가능.
builder.Configuration.AddEnvironmentVariables(prefix: "DBSYNC_");

// 1. MySQL 등록
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var serverVersion = ServerVersion.AutoDetect(connectionString); // 시작 시 한 번만 감지
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

// 2. Redis 등록 
var redisString = builder.Configuration.GetConnectionString("RedisConnection");

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisString));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<DbSyncWorker>();

// [의존성 주입] DbSyncWorkerOptions를 appsettings.json의
// DbSyncWorker 섹션에서 바인딩. 환경변수가 있으면 override됨.
builder.Services.Configure<DbSyncWorkerOptions>(
    builder.Configuration.GetSection("DbSyncWorker"));

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

// [메트릭 노출] /metrics 엔드포인트로 Prometheus 형식 메트릭 노출.
// Prometheus 서버가 1초 간격으로 스크레이핑하여 시계열 데이터로 저장.
app.UseMetricServer();

// [메트릭 자동 수집] HTTP 요청별 응답 시간/상태 코드를 자동 수집.
// k6 클라이언트 측 측정과 별개로 서버 측 메트릭을 함께 보관 가능.
app.UseHttpMetrics();

app.Run();