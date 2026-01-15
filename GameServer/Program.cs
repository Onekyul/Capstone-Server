<<<<<<< Updated upstream

namespace GameServer
=======
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using GameServer.Data; // AppDbContext�� �ִ� ���ӽ����̽� (�������� Alt+Enter�� ����)

var builder = WebApplication.CreateBuilder(args);

// 1. MySQL ���
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
// 2. Redis ��� 
var redisString = builder.Configuration.GetConnectionString("RedisConnection")!;

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisString));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 3. ���� ���� �� DB ���� �׽�Ʈ 
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
