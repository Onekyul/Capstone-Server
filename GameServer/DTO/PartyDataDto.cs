namespace GameServer.DTO
{
          // 파티 생성 요청
        public class PartyCreateReq
        {
            public string Title { get; set; }
            public int LeaderId { get; set; }
            public string LeaderNickname { get; set; }
            public int MaxCount { get; set; } = 4;
            public int DungeonId { get; set; } = 1;
        }

        // 파티 정보 응답
        public class PartyDto
        {
            public int PartyId { get; set; }
            public string Title { get; set; }
            public string LeaderName { get; set; }
            public int CurrentCount { get; set; }
            public int MaxCount { get; set; }
            public int DungeonId { get; set; }
        }

        // 던전 입장 요청 (아까 추가한 것)
        public class PartyEnterReq
        {
            public int PartyId { get; set; }
            public int UserId { get; set; }
        }
   
}
