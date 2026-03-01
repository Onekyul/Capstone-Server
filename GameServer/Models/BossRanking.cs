using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Models
{
    [Table("boss_rankings")]
    public class BossRanking
    {
        [Key][Column("id")] public int Id { get; set; }
        [Column("user_id")] public int UserId { get; set; }
        [Column("nickname")] public string Nickname { get; set; }
        [Column("clear_time")] public double ClearTime { get; set; }
    }
}
