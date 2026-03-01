#  [캡스톤프로젝트 Solike] - Game Backend Server Architecture

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white) ![Docker](https://img.shields.io/badge/Docker-Enabled-2496ED?logo=docker&logoColor=white) ![Redis](https://img.shields.io/badge/Redis-Cache-DC382D?logo=redis&logoColor=white) ![MySQL](https://img.shields.io/badge/MySQL-8.0-4479A1?logo=mysql&logoColor=white) ![Photon](https://img.shields.io/badge/Network-Photon_Fusion-00B2FF)

로그라이크 멀티플레이어 게임 'Solike'의 반응성과 데이터 무결성을 보장하기 위한 하이브리드 백엔드 서버.

<br>

##  System Architecture

비용 효율성과 게임의 반응성을 위해 **이원화된 네트워크 구조** 채택.

* **Lobby (마을):** `Photon Shared Mode` - 유저 간 채팅 및 가벼운 인터랙션 (비용 절감)
* **Dungeon (레이드):** `Photon Dedicated Server` - 중요 전투 로직의 서버 권한(Server Authority) 검증
* **Backend API:** `ASP.NET Core` + `MySQL/Redis` - 재화, 아이템, 계정 등 영구 데이터 검증 및 저장

<br>

##  Key Engineering Highlights (핵심 설계 특징)

####  1. 3-Tier Layered Architecture (보안 강화)
클라이언트의 DB 직접 접근 원천 차단. 모든 아이템 획득/재화 소모 로직은 API 서버의 교차 검증을 거치며, 핵심 전투 결과는 `Secret Key` 인증을 가진 **데디케이트 서버(Dedicated Server)만이 DB에 기록할 권한**을 가지도록 설계.

<br>

####  2. Redis 기반 실시간 분산 처리 및 I/O 최적화

**Redis Pub/Sub 기반 실시간 채팅 시스템**
다중 서버 인스턴스 환경에서도 지연 없는 통신을 제공하기 위해 **Redis Pub/Sub** 아키텍처 도입. 로비 및 글로벌 채널에서 발생하는 채팅 메시지를 분산된 서버 간에 초고속으로 브로드캐스팅하여, 유저 트래픽이 증가하더라도 안정적인 실시간 채팅 경험 보장.

<br>

**Queue & Background Worker 기반 Write-Back 아키텍처**
빈번하게 발생하는 인게임 상태 변화(재화 획득, 스탯 갱신 등)를 매번 DB에 쿼리하여 생기는 I/O 병목 제거. 인게임 데이터는 메모리 기반의 **Redis에 즉시 캐싱(Hot Storage)**하여 게임의 반응성을 극대화함. 캐싱과 동시에 변경된 데이터를 내부 큐(Queue)에 적재하고, **.NET Background Worker**가 백그라운드에서 큐를 비동기적으로 소비하며 MySQL(Cold Storage)에 영구 저장하는 **Write-Back 방식** 채택.

<br>

####  3. Docker Containerization (인프라 환경 파편화 방지)
Windows 로컬 개발 환경과 AWS 배포 서버(Linux) 간의 OS 차이로 인한 파편화 방지. API Server, MySQL, Redis 등 모든 백엔드 인프라를 **Docker Compose로 컨테이너화**하여 단일 명령어로 완벽하게 동일한 런타임 환경 구축.

<br>

##  Environment & API Settings

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
* **API Docs URL:** `http://localhost:7200/swagger` *(포트는 환경에 따라 다를 수 있음)*

 | Category | Endpoint | Description |
  | :--- | :--- | :--- |
  | **Auth** | `POST /api/Auth/guest-login` | 기기 ID 기반 로그인 및 유저 정보 반환 |     | | `POST /api/Auth/register` | 신규 유저 가입 (닉네임 중복 방어 로직 포함) |
  | | `GET /api/Auth/check-nickname` | 닉네임 사용 가능 여부 확인 |                       | **Chat** | `POST /api/Chat/send` | 로비 채팅 메시지 전송 (Redis Pub/Sub 브로드캐스팅) |
  | | `GET /api/Chat/receive` | 최근 채팅 내역 20개 조회 (Redis List) |
  | **Game** | `POST /api/Game/load` | 인게임 데이터 로드 (Redis 캐시 우선 조회 패턴 적용) |
  | | `POST /api/Game/save` | 데이터 캐싱 및 DB 저장을 위한 Write-Back  큐(`task:writeback`) 적재 |
  | **Party** | `POST /api/Party/create` | 로비 파티(방) 생성 (Redis Hash/Set 활용) |
  | | `GET /api/Party/list` | 현재 활성화된 로비 파티 목록 전체 조회 |
  | | `POST /api/Party/enter` | 방장 권한 검증 및 던전(인게임) 씬 진입 처리 |
  | **Upgrade** | `POST /api/Upgrade/attempt` | 장비 강화 시도 (확률 기반 성공/실패 검증) |
  | **Dungeon** | `POST /api/Dungeon/create-boss-session` | 보스 던전 세션 생성 및 데디케이티드서버 준비 대기 (Redis Pub/Sub) |
  | | `POST /api/Dungeon/result` | 보스전 결과 처리 및 랭킹 등록 (데디케이티드서버 호출용) |
  | **Ranking** | `GET /api/Ranking/boss` | 보스 클리어 타임 기준 랭킹 조회 (Redis Sorted Set) |

<br>

##  Architecture Diagram
<img width="800" alt="System Architecture Diagram" src="https://github.com/user-attachments/assets/e4685ae9-bc7a-4073-9453-29062c479bee" />
