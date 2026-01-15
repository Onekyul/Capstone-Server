using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Models
{
    [Table("user_enchants")]
    public class UserEnchant
    {
        [Key][Column("id")] public int Id { get; set; }
        [Column("user_id")] public int UserId { get; set; }
        [Column("enchant_id")] public string EnchantId { get; set; } // "fire"
        [Column("level")] public int Level { get; set; }
    }
}
