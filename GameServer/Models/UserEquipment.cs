using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Models
{
    [Table("user_equipments")]
    public class UserEquipment
    {
        [Key][Column("id")] public int Id { get; set; }
        [Column("user_id")] public int UserId { get; set; }
        [Column("item_id")] public string ItemId { get; set; } // "sword_wood"
        [Column("level")] public int Level { get; set; }       // 강화 수치
    }
}
