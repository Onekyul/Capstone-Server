using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Models
{
    [Table("user_items")]
    public class UserItem
    {
        [Key][Column("id")] public int Id { get; set; }
        [Column("user_id")] public int UserId { get; set; }
        [Column("item_id")] public string ItemId { get; set; } // 예: "iron_ore"
        [Column("count")] public int Count { get; set; }
    }
}
