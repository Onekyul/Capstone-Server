import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';

// 커스텀 메트릭: 실패 원인별 카운터
const errorsByStatus = new Counter('errors_by_status');
const error400 = new Counter('errors_400_bad_request');
const error500 = new Counter('errors_500_internal');
const errorTimeout = new Counter('errors_timeout');
const errorConnection = new Counter('errors_connection');
const successCount = new Counter('success_count');
const serverResponseTime = new Trend('server_response_time');

export const options = {
    vus: 50,
    duration: '30s',
    thresholds: {
        http_req_duration: ['avg<1000'],
    },
};

// VU마다 다른 userId 사용 (동시성 충돌 방지 테스트용)
export default function () {
    const userId = __VU; // 각 VU가 고유한 userId 사용

    const payload = JSON.stringify({
        userId: userId,
        nickname: `benchUser_${userId}`,
        stage: 5,
        equip: { weapon: "sword_01", helmet: "helm_01", armor: "armor_01", boots: "boots_01" },
        inventory: [
            { id: "item_001", count: 10 },
            { id: "item_002", count: 5 },
            { id: "item_003", count: 3 }
        ],
        equipments: [
            { id: "equip_001", level: 3 },
            { id: "equip_002", level: 1 }
        ],
        enchants: [
            { id: "ench_001", level: 2 }
        ]
    });

    const params = {
        headers: { 'Content-Type': 'application/json' },
        timeout: '10s',
    };

    const res = http.post('http://localhost:7000/api/game/save', payload, params);

    // 응답 상태 체크
    const isSuccess = check(res, {
        'status is 200': (r) => r.status === 200,
        'response has body': (r) => r.body && r.body.length > 0,
        'no server error': (r) => r.status < 500,
    });

    // 실패 원인 분류
    if (!isSuccess) {
        errorsByStatus.add(1, { status: String(res.status) });

        if (res.status === 0) {
            errorConnection.add(1);
            console.error(`[VU ${__VU}] 연결 실패 - error: ${res.error}`);
        } else if (res.status === 400) {
            error400.add(1);
            console.error(`[VU ${__VU}] 400 Bad Request - body: ${res.body}`);
        } else if (res.status >= 500) {
            error500.add(1);
            console.error(`[VU ${__VU}] ${res.status} Server Error - body: ${res.body}`);
        } else if (res.timings.duration > 10000) {
            errorTimeout.add(1);
            console.error(`[VU ${__VU}] Timeout - duration: ${res.timings.duration}ms`);
        } else {
            console.error(`[VU ${__VU}] Unexpected status ${res.status} - body: ${res.body}`);
        }
    } else {
        successCount.add(1);
    }

    serverResponseTime.add(res.timings.duration);
}

export function handleSummary(data) {
    // 테스트 종료 후 요약 리포트 출력
    const totalReqs = data.metrics.http_reqs ? data.metrics.http_reqs.values.count : 0;
    const failRate = data.metrics.http_req_failed ? data.metrics.http_req_failed.values.rate : 0;
    const avgDuration = data.metrics.http_req_duration ? data.metrics.http_req_duration.values.avg : 0;
    const p95Duration = data.metrics.http_req_duration ? data.metrics.http_req_duration.values['p(95)'] : 0;

    const summary = {
        '=== Write-Back 부하 테스트 분석 ===': '',
        '총 요청 수': totalReqs,
        '실패율': `${(failRate * 100).toFixed(2)}%`,
        '평균 응답시간': `${avgDuration.toFixed(2)}ms`,
        'P95 응답시간': `${p95Duration.toFixed(2)}ms`,
        '연결 실패': data.metrics.errors_connection ? data.metrics.errors_connection.values.count : 0,
        '400 에러': data.metrics.errors_400_bad_request ? data.metrics.errors_400_bad_request.values.count : 0,
        '500 에러': data.metrics.errors_500_internal ? data.metrics.errors_500_internal.values.count : 0,
        '타임아웃': data.metrics.errors_timeout ? data.metrics.errors_timeout.values.count : 0,
        '성공': data.metrics.success_count ? data.metrics.success_count.values.count : 0,
    };

    console.log('\n' + JSON.stringify(summary, null, 2));

    return {
        stdout: textSummary(data, { indent: ' ', enableColors: true }),
        'bench/results-writeback.json': JSON.stringify(data, null, 2),
    };
}

// k6 내장 텍스트 요약
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';
