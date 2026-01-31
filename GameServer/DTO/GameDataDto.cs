namespace GameServer.DTO
{
    public class GameDataDto
    {
        public int userId { get; set; }
        public string nickname { get; set; }
        public int stage { get; set; }

        public EquipDto equip { get; set; }
        public List<ItemDto> inventory { get; set; }
        public List<EquipItemDto> equipments { get; set; }
        public List<EnchantDto> enchants { get; set; }
    }

    public class EquipDto
    {
        public string weapon { get; set; }
        public string helmet { get; set; }
        public string armor { get; set; }
        public string boots { get; set; }
    }

    public class ItemDto
    {
        public string id { get; set; }
        public int count { get; set; }
    }

    public class EquipItemDto
    {
        public string id { get; set; }
        public int level { get; set; }
    }

    public class EnchantDto
    {
        public string id { get; set; }
        public int level { get; set; }
    }
}
