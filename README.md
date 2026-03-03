# [캡스톤프로젝트 Solike] - Game Backend Server Architecture

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white) ![Docker](https://img.shields.io/badge/Docker-Enabled-2496ED?logo=docker&logoColor=white) ![Redis](https://img.shields.io/badge/Redis-Cache-DC382D?logo=redis&logoColor=white) ![MySQL](https://img.shields.io/badge/MySQL-8.0-4479A1?logo=mysql&logoColor=white) ![Photon](https://img.shields.io/badge/Network-Photon_Fusion-00B2FF)

로그라이크 멀티플레이어 게임 'Solike'의 반응성과 데이터 무결성을 보장하기 위한 하이브리드 백엔드 서버.

<br>

## System Architecture

비용 효율성과 게임의 반응성을 위해 **이원화된 네트워크 구조** 채택.

- **Lobby (마을):** `Photon Shared Mode` - 유저 간 채팅 및 가벼운 인터랙션 (비용 절감)
- **Dungeon (레이드):** `Photon Dedicated Server` - 중요 전투 로직의 서버 권한(Server Authority) 검증
- **Backend API:** `ASP.NET Core` + `MySQL/Redis` - 재화, 아이템, 계정 등 영구 데이터 검증 및 저장

<br>

## Key Engineering Highlights (핵심 설계 특징)

#### 1. Redis Cache-Aside + Write-Back 아키텍처

읽기 시 **Redis 우선 조회** 후 캐시 미스 시 MySQL에서 로드하는 **Cache-Aside** 전략 적용. 쓰기 시 Redis에 즉시 캐싱하고, .NET `BackgroundService` 워커가 Redis 큐에서 50건씩 배치로 꺼내 MySQL에 비동기 일괄 저장하는 **Write-Back** 구조.

<br>

#### 2. Redis Pub/Sub 서버 간 비동기 통신

게임서버 ↔ 데디케이티드 서버 간 세션 생성/준비 완료를 **Redis Pub/Sub 채널**로 중개. `TaskCompletionSource` + 5초 타임아웃으로 데디서버 장애 시 자동 롤백 처리.

<br>

#### 3. Redis Sorted Set 실시간 랭킹

보스 던전 클리어 타임을 **Redis Sorted Set**의 score로 저장하여 별도 정렬 없이 즉시 랭킹 조회. 기존 기록보다 빠른 경우에만 갱신하는 조건부 업데이트 적용.

<br>

#### 4. Redis Hash/Set 기반 파티 시스템

파티 메타데이터는 **Hash**, 멤버 목록은 **Set**으로 관리하여 O(1) 멤버 중복 검사 및 원자적 상태 업데이트 구현. `INCR` 명령으로 파티 ID를 동시성 안전하게 자동 생성.

<br>

#### 5. Docker Compose 인프라 컨테이너화

API Server, MySQL, Redis 등 전체 백엔드 인프라를 **Docker Compose**로 컨테이너화하여 로컬/배포 환경 간 일관된 런타임 환경 구성.

<br>

## Environment & API Settings

서버 실행 및 API 연동을 위한 필수 환경 변수 및 설정 가이드.

### 1. 환경 변수 설정 (.env)

프로젝트 루트 디렉토리(`docker-compose.yml` 위치)에 `.env` 파일을 생성하고 아래 값 설정.

```env
# Database Settings
DB_PASSWORD=your_secure_password
```

### 2. 서버 실행 (Docker)

Docker Desktop 실행 상태에서 아래 명령어를 통해 전체 서버 인프라를 백그라운드에서 구동.

```bash
docker-compose up -d --build
```

### 3. API Endpoints (Swagger)

서버 실행 후, 브라우저에서 아래 주소로 접속하여 API 명세 확인 및 테스트 가능.

- **API Docs URL:** `http://localhost:7200/swagger` _(포트는 환경에 따라 다를 수 있음)_

| Category    | Endpoint                                | Description                                                        |
| :---------- | :-------------------------------------- | :----------------------------------------------------------------- |
| **Auth**    | `POST /api/Auth/guest-login`            | 기기 ID 기반 로그인 및 유저 정보 반환                              |
|             | `POST /api/Auth/register`               | 신규 유저 가입 (닉네임 중복 방어 로직 포함)                        |
|             | `GET /api/Auth/check-nickname`          | 닉네임 사용 가능 여부 확인                                         |
| **Chat**    | `POST /api/Chat/send`                   | 로비 채팅 메시지 전송 (Redis Pub/Sub 브로드캐스팅)                 |
|             | `GET /api/Chat/receive`                 | 최근 채팅 내역 20개 조회 (Redis List)                              |
| **Game**    | `POST /api/Game/load`                   | 인게임 데이터 로드 (Redis 캐시 우선 조회 패턴 적용)                |
|             | `POST /api/Game/save`                   | 데이터 캐싱 및 DB 저장을 위한 Write-Back 큐(`task:writeback`) 적재 |
|             | `GET /api/Game/player-stats`            | 데디케이티드 서버용 플레이어 장비 데이터 조회                      |
| **Party**   | `POST /api/Party/create`                | 로비 파티(방) 생성 (리더 자동 등록, Redis Hash/Set 활용)           |
|             | `GET /api/Party/list`                   | 현재 활성화된 로비 파티 목록 전체 조회                             |
|             | `GET /api/Party/detail/{partyId}`       | 파티 상세 조회 — 멤버 닉네임 목록, 상태, 세션 정보 (폴링용)        |
|             | `POST /api/Party/join`                  | 파티 즉시 참가 (인원 초과/중복 참가 검증)                          |
|             | `POST /api/Party/leave`                 | 파티 탈퇴 (방장 탈퇴 시 파티 해산)                                 |
|             | `POST /api/Party/change-dungeon`        | 방장 전용 — 던전 종류 변경                                         |
|             | `POST /api/Party/kick`                  | 방장 전용 — 파티원 강퇴                                            |
|             | `POST /api/Party/enter`                 | 방장 전용 — 던전 입장 (데디서버 세션 생성 및 핸드오버)             |
| **Dungeon** | `POST /api/Dungeon/create-boss-session` | 보스 던전 세션 생성 (Redis Pub/Sub 데디서버 연동, 5초 타임아웃)    |
|             | `POST /api/Dungeon/result`              | 보스전 결과 처리 — 클리어 유저 랭킹 등록 (데디서버 호출)           |
| **Ranking** | `GET /api/Ranking/boss`                 | 보스 던전 클리어 타임 랭킹 조회 (Redis Sorted Set)                 |
| **Upgrade** | `POST /api/Upgrade/attempt`             | 장비 강화 시도 (확률 기반 검증)                                    |

<br>

## Architecture Diagram

<img width="800" alt="System Architecture Diagram" src="https://github.com/user-attachments/assets/e4685ae9-bc7a-4073-9453-29062c479bee" />
