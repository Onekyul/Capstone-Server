// =============================================================================
// k6 부하 테스트 시나리오
// 논문: MMORPG 서버 환경에서 Redis Write-Back 패턴의 배치 사이즈가
//       성능에 미치는 영향에 관한 실증 연구
//
// 본 시나리오는 모든 측정에서 동일하게 사용되며, 환경변수로 다음을 제어함:
//   - VU         : 동시 가상 사용자 수 (50 / 100 / 200)
//   - MODE       : 측정 대상 엔드포인트 (writeback / direct)
//   - TARGET_URL : 캡스톤 서버 URL (예: http://localhost:7200)
//   - USER_BASE  : 테스트 유저 ID 시작 번호 (사전 생성된 유저의 첫 번째 ID)
//
// 부하 패턴:
//   - Ramp-up    : 30초간 0 → VU 명까지 점진적 증가 (분석 제외)
//   - Sustained  : 60초간 VU 명 유지 (본 논문의 분석 대상 구간)
//   - Ramp-down  : 30초간 VU → 0 명까지 감소 (분석 제외)
//
// 통제 변수:
//   - 각 VU는 고유한 userId 사용하여 큐의 동일 키 충돌 회피
//   - 페이로드는 매 요청마다 동적 생성하여 캐시 효과 배제
//   - sleep 평균 1.5초로 실제 사용자 행동 모사
// =============================================================================

import http from 'k6/http';
import { sleep, check } from 'k6';
import { Trend, Counter } from 'k6/metrics';

// -----------------------------------------------------------------------------
// [측정 자동화 지원] 환경변수 파싱
// 자동화 스크립트가 매 측정마다 다른 환경변수를 주입하여 동일 시나리오로
// 75회 측정 가능하게 함.
// -----------------------------------------------------------------------------
const VU = parseInt(__ENV.VU || '50');
const MODE = (__ENV.MODE || 'writeback').toLowerCase();
const TARGET_URL = __ENV.TARGET_URL || 'http://localhost:7200';
const USER_BASE = parseInt(__ENV.USER_BASE || '1');

// 모드 검증 — 잘못된 모드는 시나리오 시작 시점에 실패
if (MODE !== 'writeback' && MODE !== 'direct') {
  throw new Error(`Invalid MODE: ${MODE}. Must be 'writeback' or 'direct'.`);
}

// 엔드포인트 결정
const ENDPOINT = MODE === 'writeback'
  ? `${TARGET_URL}/api/Game/save`
  : `${TARGET_URL}/api/Game/save-direct`;

// -----------------------------------------------------------------------------
// [통제 변수] 부하 패턴 정의
// 모든 측정에서 동일한 시간 구조를 사용하여 측정 간 비교 가능성 확보.
// Sustained 구간 60초만 분석 대상이며, Ramp 구간은 정상 상태가 아니므로
// 데이터 분석 시 제외함 (k6 결과의 timestamp로 필터링).
// -----------------------------------------------------------------------------
export const options = {
  stages: [
    { duration: '30s', target: VU },  // Ramp-up (분석 제외)
    { duration: '60s', target: VU },  // Sustained (분석 대상)
    { duration: '30s', target: 0 },   // Ramp-down (분석 제외)
  ],
  // [측정 통제] 임계값은 측정용이며 실패 조건이 아님.
  // 본 연구는 descriptive 분석이므로 SLA 임계값 없음.
  thresholds: {
    'http_req_duration': ['p(95)<10000'],   // 단순 모니터링용
    'http_req_failed': ['rate<0.10'],       // 10% 이상 실패 시 경고
  },
  // 시스템 메트릭과 함께 보고
  systemTags: ['status', 'method', 'url'],
};

// -----------------------------------------------------------------------------
// [측정 보조 메트릭]
// k6 자체 메트릭과 별개로 본 연구의 분석을 위한 커스텀 메트릭을 등록.
// 자동화 스크립트가 결과 JSON에서 추출하여 CSV로 정리.
// -----------------------------------------------------------------------------
const saveLatency = new Trend('save_latency', true);
const saveSuccess = new Counter('save_success');
const saveFailure = new Counter('save_failure');

// =============================================================================
// 캡스톤 프로젝트 실제 마스터 데이터 ID 풀
//
// 본 ID 풀은 캡스톤 프로젝트의 실제 게임 클라이언트가 사용하는
// ScriptableObject 기반 마스터 데이터에서 추출한 것임.
// 본 논문 측정의 학술적 정당성을 위해 k6 부하 테스트가 실제 게임 서버로
// 전송하는 트래픽을 정확히 모사하도록 구성됨.
//
// 슬롯별 재질 제약:
//   - 무기: wood/stone/iron/gold 4종
//   - 방어구(투구/갑옷/신발): stone/iron/gold 3종 (wood 없음)
//
// 캡스톤 DB의 외래키 제약 없음 (역정규화 전략, 최종 보고서 3.8.1 참조).
// 본 ID들은 VARCHAR 독립 저장되며 마스터 테이블 조회 없이 직접 INSERT됨.
// =============================================================================

// 장착 슬롯 전용 ID 풀 (EquipDto.weapon/helmet/armor/boots)
const WEAPON_IDS = [
  'sword_wood', 'sword_stone', 'sword_iron', 'sword_gold',
  'bow_wood',   'bow_stone',   'bow_iron',   'bow_gold',
  'spear_wood', 'spear_stone', 'spear_iron', 'spear_gold',
];
const HELMET_IDS = ['helmet_stone', 'helmet_iron', 'helmet_gold'];
const ARMOR_IDS  = ['armor_stone',  'armor_iron',  'armor_gold'];
const BOOTS_IDS  = ['boots_stone',  'boots_iron',  'boots_gold'];

// 보유 장비 목록 전용 풀 (user_equipments 테이블의 item_id)
// 장착 슬롯 ID와 동일 풀에서 선택 (보유 장비가 장착될 수 있어야 하므로)
const ALL_EQUIPMENT_IDS = [
  ...WEAPON_IDS,
  ...HELMET_IDS,
  ...ARMOR_IDS,
  ...BOOTS_IDS,
];

// 인벤토리 재료 아이템 전용 풀 (user_items 테이블의 item_id)
// 캡스톤 기획상 재료 아이템: 목재, 원소 조각(파편), 원소 정수(원석)
const MATERIAL_IDS = [
  'mat_wood',
  'mat_firepiece',    'mat_firestone',
  'mat_waterpiece',   'mat_waterstone',
  'mat_poisonpiece',  'mat_poisonstone',
  'mat_thunderpiece', 'mat_thunderstone',
];

// 인챈트 속성 전용 풀 (user_enchants 테이블의 enchant_id)
// 본 ID는 실험 시점의 캡스톤 프로젝트 상태를 반영하며 추후 변경될 수 있음
const ENCHANT_IDS = [
  'enchant_fire', 'enchant_water', 'enchant_poison', 'enchant_thunder',
];

// =============================================================================
// 페이로드 크기 상수 — 본 논문의 통제변수로 고정
//
// [측정 변수 통제] DbSyncWorker가 upsert 패턴(INSERT ... ON DUPLICATE KEY UPDATE)
// 으로 변경됨에 따라 페이로드 내 ID 중복은 DB 레벨에서 자동 처리됨.
// 따라서 셔플 기반 중복 차단 로직(shufflePick)은 불필요하며,
// 단순 랜덤 선택(pickRandom)으로 복원하여 시나리오 복잡도를 최소화.
//
// 페이로드 크기 30/15/8은 일반 MMORPG 중간 레벨 캐릭터 데이터 규모를
// 모사하기 위한 통제변수로 고정. 본 연구의 독립변수는 배치 사이즈이며,
// 페이로드 크기는 매 요청마다 동일해야 측정 변수가 통제됨.
// =============================================================================

const INVENTORY_COUNT  = 30;  // user_items: 재료/소비재 30종 보유
const EQUIPMENTS_COUNT = 15;  // user_equipments: 착용 4 + 교체용 11
const ENCHANTS_COUNT   = 8;   // user_enchants: 속성별 인챈트 8종

// =============================================================================
// 유틸: 배열에서 랜덤 요소 선택
// =============================================================================
function pickRandom(arr) {
  return arr[Math.floor(Math.random() * arr.length)];
}

// =============================================================================
// 페이로드 동적 생성 — 캡스톤 GameDataDto 구조에 정확히 매핑
//
// GameDataDto 구조 (캡스톤 DTO/GameDataDto.cs):
//   userId    (int)
//   nickname  (string)
//   stage     (int)
//   equip     (EquipDto: weapon/helmet/armor/boots, 모두 string)
//   inventory (List<ItemDto>: id string, count int)
//   equipments(List<EquipItemDto>: id string, level int)
//   enchants  (List<EnchantDto>: id string, level int)
// =============================================================================

function generatePayload(userId) {
  return {
    userId:   userId,
    nickname: `test_user_${userId}`,
    stage:    Math.floor(Math.random() * 100) + 1,

    // 장착 슬롯 — 슬롯별 허용 재질에 맞춰 ID 선택
    equip: {
      weapon: pickRandom(WEAPON_IDS),
      helmet: pickRandom(HELMET_IDS),
      armor:  pickRandom(ARMOR_IDS),
      boots:  pickRandom(BOOTS_IDS),
    },

    inventory:  generateRandomInventory(),
    equipments: generateRandomEquipments(),
    enchants:   generateRandomEnchants(),
  };
}

// -----------------------------------------------------------------------------
// 인벤토리 — user_items 테이블 매핑 (재료/소비재, 수량 누적형)
// 정확히 INVENTORY_COUNT(30)개 고정 생성 — 통제변수
// -----------------------------------------------------------------------------
function generateRandomInventory() {
  const items = [];
  for (let i = 0; i < INVENTORY_COUNT; i++) {
    items.push({
      id:    pickRandom(MATERIAL_IDS),
      count: Math.floor(Math.random() * 99) + 1,  // 수량 1~99
    });
  }
  return items;
}

// -----------------------------------------------------------------------------
// 보유 장비 — user_equipments 테이블 매핑 (개별 강화 상태 보유형)
// 정확히 EQUIPMENTS_COUNT(15)개 고정 생성 — 통제변수
// 착용 슬롯 4개 + 교체용 보유 장비 약 11개 수준
// -----------------------------------------------------------------------------
function generateRandomEquipments() {
  const items = [];
  for (let i = 0; i < EQUIPMENTS_COUNT; i++) {
    items.push({
      id:    pickRandom(ALL_EQUIPMENT_IDS),
      level: Math.floor(Math.random() * 10) + 1,  // 강화 레벨 1~10
    });
  }
  return items;
}

// -----------------------------------------------------------------------------
// 인챈트 — user_enchants 테이블 매핑 (속성별 인챈트)
// 정확히 ENCHANTS_COUNT(8)개 고정 생성 — 통제변수
// 본 ID는 실험 시점의 캡스톤 프로젝트 상태를 반영하며 추후 변경될 수 있음
// -----------------------------------------------------------------------------
function generateRandomEnchants() {
  const items = [];
  for (let i = 0; i < ENCHANTS_COUNT; i++) {
    items.push({
      id:    pickRandom(ENCHANT_IDS),
      level: Math.floor(Math.random() * 5) + 1,  // 인챈트 레벨 1~5
    });
  }
  return items;
}

// -----------------------------------------------------------------------------
// 시나리오 시작 시점 출력 (측정 조건 기록)
// 자동화 스크립트가 로그에서 어떤 조건의 측정인지 추적 가능하게 함.
// -----------------------------------------------------------------------------
export function setup() {
  console.log('===========================================');
  console.log('k6 측정 시나리오 시작');
  console.log(`  VU         : ${VU}`);
  console.log(`  MODE       : ${MODE}`);
  console.log(`  ENDPOINT   : ${ENDPOINT}`);
  console.log(`  USER_BASE  : ${USER_BASE}`);
  console.log(`  USER_RANGE : ${USER_BASE} ~ ${USER_BASE + VU - 1}`);
  console.log('===========================================');

  // 시작 시간 기록 (Sustained 구간 분석 시 활용)
  return { startTime: new Date().toISOString() };
}

// -----------------------------------------------------------------------------
// 메인 시나리오 함수
// 각 VU가 sleep 사이에 한 번씩 호출됨.
// __VU는 1부터 시작하는 가상 사용자 인덱스.
// -----------------------------------------------------------------------------
export default function () {
  // [통제 변수] 각 VU가 고유한 userId 사용.
  // VU 1 → USER_BASE, VU 2 → USER_BASE+1, ..., VU N → USER_BASE+N-1
  // 동일 userId를 여러 VU가 동시에 사용하면 Redis string 키 덮어쓰기로
  // 측정값이 의도와 달라지므로 반드시 분리.
  const userId = USER_BASE + (__VU - 1);

  const payload = JSON.stringify(generatePayload(userId));

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
    timeout: '30s',
  };

  // HTTP POST 요청 실행
  const response = http.post(ENDPOINT, payload, params);

  // [측정] 응답 상태 검증 및 커스텀 메트릭 기록
  const success = check(response, {
    'status is 200': (r) => r.status === 200,
  });

  if (success) {
    saveSuccess.add(1);
    saveLatency.add(response.timings.duration);
  } else {
    saveFailure.add(1);
    // 실패 시 로그 (자동화 스크립트가 추적 가능)
    console.warn(`[Save Failed] userId=${userId} status=${response.status} body=${response.body}`);
  }

  // [통제 변수] 평균 1.5초 sleep으로 실제 사용자 행동 모사.
  // sleep 없이 부하를 보내면 캡스톤 게임 클라이언트의 실제 저장 패턴과
  // 동떨어진 비현실적 부하가 됨.
  sleep(Math.random() * 2 + 0.5);
}

// -----------------------------------------------------------------------------
// 종료 시점 정리
// -----------------------------------------------------------------------------
export function teardown(data) {
  console.log('===========================================');
  console.log(`k6 측정 시나리오 종료 (시작: ${data.startTime})`);
  console.log('===========================================');
}
