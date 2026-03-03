namespace GameServer.DTO
{
    public class BossRankingEntry
    {
        public int Rank { get; set; }
        public string Nickname { get; set; }
        public double ClearTime { get; set; }
    }

    public class BossRankingRes
    {
        public List<BossRankingEntry> Rankings { get; set; }
    }
}
