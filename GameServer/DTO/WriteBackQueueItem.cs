using System;

namespace GameServer.DTO
{
    /// <summary>
    /// Redis Write-Back 큐의 페이로드 구조.
    /// 기존에는 userId 문자열만 push되었으나,
    /// 본 연구의 종속변수인 E2E 지연(큐 push → DB commit) 측정을 위해
    /// EnqueuedAt 타임스탬프를 함께 포함하도록 변경.
    /// </summary>
    public class WriteBackQueueItem
    {
        // [E2E 측정] 저장 대상 유저의 식별자.
        public string UserId { get; set; } = string.Empty;

        // [E2E 측정] 큐 push 시점의 UTC 타임스탬프.
        // DbSyncWorker가 pop 시 (DateTime.UtcNow - EnqueuedAt)으로
        // E2E 지연을 계산하여 Prometheus 히스토그램에 기록.
        public DateTime EnqueuedAt { get; set; }
    }
}
