using Microsoft.EntityFrameworkCore;

namespace GameServer 
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        // 나중에 여기에 public DbSet<Player> Players { get; set; } 같은 거 추가하면 됨
    }
}