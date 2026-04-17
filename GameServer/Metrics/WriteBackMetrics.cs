using Prometheus;

namespace GameServer.Metrics
{
    /// <summary>
    /// 본 연구의 종속변수를 Prometheus 메트릭으로 노출하기 위한 정의 클래스.
    /// 모든 메트릭은 batch_size 라벨을 가져 실험별 구분이 가능함.
    /// </summary>
    public static class WriteBackMetrics
    {
        // [종속변수 - E2E 지연] 큐 push 시각부터 DB commit 완료까지의 지연.
        // 본 논문 4장 분석의 핵심 지표 중 하나.
        // 히스토그램 버킷은 10ms~30s 범위를 로그 스케일로 분할.
        public static readonly Histogram E2eLatencySeconds = Prometheus.Metrics
            .CreateHistogram(
                "writeback_e2e_latency_seconds",
                "End-to-end latency from queue push to DB commit completion",
                new HistogramConfiguration
                {
                    Buckets = Histogram.ExponentialBuckets(0.01, 2, 12),
                    LabelNames = new[] { "batch_size" }
                });

        // [종속변수 - 워커 처리량] 배치당 실제 처리된 아이템 수의 누적 카운터.
        // 워커가 의도한 배치 사이즈만큼 처리하는지 검증 용도이기도 함.
        public static readonly Counter BatchProcessedItems = Prometheus.Metrics
            .CreateCounter(
                "writeback_batch_processed_items_total",
                "Total number of items processed in Write-Back batches",
                new CounterConfiguration
                {
                    LabelNames = new[] { "batch_size" }
                });

        // [종속변수 - 큐 backlog] 매 워커 루프마다 측정한 큐 길이.
        // 부하 누적 여부와 워커 처리 능력 한계를 확인하는 핵심 지표.
        public static readonly Gauge QueueLength = Prometheus.Metrics
            .CreateGauge(
                "writeback_queue_length",
                "Current length of the Write-Back queue",
                new GaugeConfiguration
                {
                    LabelNames = new[] { "batch_size" }
                });

        // [종속변수 - 배치 처리 시간] DB commit 자체에 걸린 시간.
        // E2E 지연을 분해할 때 사용 (큐 대기 시간 vs DB 처리 시간 분리).
        public static readonly Histogram BatchProcessingDurationSeconds = Prometheus.Metrics
            .CreateHistogram(
                "writeback_batch_processing_duration_seconds",
                "Time taken to process a single Write-Back batch (DB commit only)",
                new HistogramConfiguration
                {
                    Buckets = Histogram.ExponentialBuckets(0.001, 2, 14),
                    LabelNames = new[] { "batch_size" }
                });
    }
}
