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

        // 파티 참가 요청
        public class PartyJoinReq
        {
            public int PartyId { get; set; }
            public int UserId { get; set; }
            public string Nickname { get; set; }
        }

        // 파티 탈퇴 요청
        public class PartyLeaveReq
        {
            public int PartyId { get; set; }
            public int UserId { get; set; }
        }

        // 파티 상세 응답 (폴링용)
        public class PartyDetailDto
        {
            public int PartyId { get; set; }
            public string Title { get; set; }
            public int DungeonId { get; set; }
            public int CurrentCount { get; set; }
            public int MaxCount { get; set; }
            public int LeaderId { get; set; }
            public string Status { get; set; }       // "Waiting" | "InGame"
            public string SessionName { get; set; }  // 데디서버 세션명 (입장 후 설정)
            public List<string> Members { get; set; } // 멤버 닉네임 리스트
        }

        // 던전 변경 요청
        public class PartyChangeDungeonReq
        {
            public int PartyId { get; set; }
            public int UserId { get; set; }
            public int DungeonId { get; set; }
        }

        // 강퇴 요청
        public class PartyKickReq
        {
            public int PartyId { get; set; }
            public int LeaderId { get; set; }
            public int TargetUserId { get; set; }
        }

        // 던전 입장 요청
        public class PartyEnterReq
        {
            public int PartyId { get; set; }
            public int UserId { get; set; }
        }

}
