<<<<<<< Updated upstream

namespace GameServer
=======
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using GameServer.Data; // AppDbContext가 있는 네임스페이스 (에러나면 Alt+Enter로 수정)

var builder = WebApplication.CreateBuilder(args);

// 1. MySQL 등록
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 2. Redis 등록 
var redisString = builder.Configuration.GetConnectionString("RedisConnection")!;

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisString));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 3. 서버 시작 시 DB 연결 테스트 
using (var scope = app.Services.CreateScope())
>>>>>>> Stashed changes
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
