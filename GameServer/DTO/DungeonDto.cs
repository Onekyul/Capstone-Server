namespace GameServer.DTO
{
    public class CreateBossSessionReq
    {
        public int UserId { get; set; }
    }

    public class CreateBossSessionRes
    {
        public string SessionName { get; set; }
    }

    public class DungeonResultReq
    {
        public string SessionName { get; set; }
        public List<DungeonPlayerResult> Results { get; set; }
    }

    public class DungeonPlayerResult
    {
        public int UserId { get; set; }
        public bool Cleared { get; set; }
        public float ClearTime { get; set; }
    }
}
