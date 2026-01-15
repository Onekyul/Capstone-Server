using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GameServer.Models
{
    [Table("users")]
    public class User
    {
        [Key][Column("id")] public int Id { get; set; }
        [Column("device_id")] public string DeviceId { get; set; }
        [Column("nickname")] public string Nickname { get; set; }
        [Column("max_cleared_stage")] public int MaxClearedStage { get; set; }

        // 장착 정보 
        [Column("equipped_weapon_id")] public string EquippedWeaponId { get; set; }
        [Column("equipped_helmet_id")] public string EquippedHelmetId { get; set; }
        [Column("equipped_armor_id")] public string EquippedArmorId { get; set; }
        [Column("equipped_boots_id")] public string EquippedBootsId { get; set; }

        [Column("created_at")] public DateTime CreatedAt { get; set; }

        // 하위 테이블 연결 
        public List<UserItem> Items { get; set; }
        public List<UserEquipment> Equipments { get; set; }
        public List<UserEnchant> Enchants { get; set; }
    }
}
