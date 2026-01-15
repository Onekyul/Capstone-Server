using GameServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GameServer.Data 
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        // 나중에 여기에 public DbSet<Player> Players { get; set; } 같은 거 추가하면 됨
        public DbSet<User> Users { get; set; }
        public DbSet<UserItem> UserItems { get; set; }
        public DbSet<UserEquipment> UserEquipments { get; set; }
        public DbSet<UserEnchant> UserEnchants { get; set; }
    }
}