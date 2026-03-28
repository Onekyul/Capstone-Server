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
        public int PartyId { get; set; }
        public List<DungeonPlayerResult> Results { get; set; }
    }

    public class DungeonPlayerResult
    {
        public int UserId { get; set; }
        public bool Cleared { get; set; }
        public float ClearTime { get; set; }
    }

    public class DungeonEnterReq
    {
        public int PartyId { get; set; }
        public int PartyLeaderUserId { get; set; }
        public List<int> MemberUserIds { get; set; }
    }

    public class DungeonEnterRes
    {
        public string Status { get; set; }
        public string? SessionName { get; set; }
        public string? Message { get; set; }
    }

    public class DungeonClearReq
    {
        public string Nickname { get; set; }
        public string DungeonName { get; set; }
        public bool IsClear { get; set; }
        public List<RewardItem> Rewards { get; set; }
    }

    public class RewardItem
    {
        public string ItemName { get; set; }
        public int Count { get; set; }
    }
}
