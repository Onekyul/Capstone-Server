namespace GameServer.Configuration
{
    /// <summary>
    /// DbSyncWorker의 동작을 외부에서 주입하기 위한 설정 클래스.
    /// 본 연구의 독립변수인 배치 사이즈를 코드 변경 없이 변경하기 위해 도입.
    /// appsettings.json 또는 환경변수(DBSYNC_BatchSize=N)로 override 가능.
    /// </summary>
    public class DbSyncWorkerOptions
    {
        // [측정 변수 통제] 본 연구의 독립변수.
        // 컴파일 타임 상수에서 외부 주입으로 변경하여
        // 동일 바이너리로 75회 측정 자동화 가능.
        public int BatchSize { get; set; } = 50;

        // [통제변수 고정] 빈 큐 폴링 주기.
        // 기존 1초는 큐 적재 데이터를 지연시켜 측정값을 왜곡하므로
        // 10ms로 고정하여 워커 처리 속도가 배치 사이즈 효과를 그대로 반영하도록 함.
        public int EmptyQueuePollingMs { get; set; } = 10;

        // [측정 통제] Redis 큐 키 이름.
        // 기존 캡스톤 클라이언트와의 호환성을 위해 task:writeback 유지.
        public string QueueKey { get; set; } = "task:writeback";
    }
}
