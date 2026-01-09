#  [캡스톤프로젝트 Solike] - Game Server Architecture

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Enabled-2496ED?logo=docker&logoColor=white)
![AWS](https://img.shields.io/badge/AWS-EC2-232F3E?logo=amazon-aws&logoColor=white)
![Redis](https://img.shields.io/badge/Redis-Cache-DC382D?logo=redis&logoColor=white)
![MySQL](https://img.shields.io/badge/MySQL-8.0-4479A1?logo=mysql&logoColor=white)
![Photon](https://img.shields.io/badge/Network-Photon_Fusion-00B2FF)

>**로그라이크 멀티플레이어 게임을 위한 서버 프로젝트** 

<br>

##  System Architecture

본 프로젝트는 게임의 반응성(Latency)과 데이터 무결성(Integrity)이라는 두 가지 목표를 달성하기 위해 **이원화된 네트워크 구조**를 채택했습니다.


### 🔹 Hybrid Network Topology
| 구분 | 기술 스택 | 역할 및 특징 |
| :--- | :--- | :--- |
| **Lobby & Hub** | **Photon Shared Mode** | 마을 내 유저 간 채팅 및 가벼운 인터랙션 처리 (비용 절감) |
| **Dungeon (Raid)** | **Photon Dedicated Server** | 보스 레이드 등 중요 전투 로직의 **서버 권한(Server Authority)** 검증 수행 |
| **Backend API** | **ASP.NET Core (AWS)** | 로그인, 인벤토리, 상점 등 **보안이 필요한 영구 데이터** 처리 |

<br>

##  Key Engineering Decisions (핵심 설계)

### 1. 3-Tier Layered Architecture (보안 강화)
- **Problem:** 클라이언트가 DB에 직접 접근할 경우 아이템 복사 및 데이터 변조 위험 존재.
- **Solution:** `Client` ↔ `API Server` ↔ `Database`의 3계층 구조 도입.
  - 모든 재화 소모 및 아이템 획득 로직은 API 서버 내에서 검증 후 수행.
  - 데디케이트 서버(Dungeon Host)만이 결과 저장 API(`Secret Key` 인증)를 호출할 수 있도록 함

### 2. Redis Write-Back Strategy (성능 최적화)
- **Problem:** 빈번한 전투 및 상태 변화를 매번 MySQL에 저장 시 DB I/O 병목 발생.
- **Solution:** **Redis를 인게임 'Hot Storage'로 활용.**
  - 게임 중 플레이어 상태(HP, 위치, 임시 획득 아이템)는 Redis에서 메모리 기반 캐싱으로 초고속 처리.
  - 던전 클리어 또는 세션 종료 시점에만 MySQL(Cold Storage)에 비동기로 일괄 저장(**Write-Back**)하여 DB 부하 최소화.

### 3. Docker Container Infrastructure (환경 일관성)
- **Problem:** 로컬 개발 환경(Windows)과 배포 서버(AWS Linux) 간의 환경 차이로 인한 오류.
- **Solution:** `API Server`, `MySQL`, `Redis`를 **Docker Compose**로 컨테이너화.
  - `docker-compose up` 명령어 하나로 전체 백엔드 인프라 구축 가능.
  - 데디케이트 서버가 어느 환경에서 실행되든 동일한 백엔드 환경 보장.

<br>

## 💾 Database Schema (Optimized for Roguelike)

RPG 장르 특성상 , **유일한 영구적 성장 요소인 '장비'와 '진행도'**에 집중하여 스키마를 경량화했습니다.

- **Players:** 계정 정보 및 `HighestClearStage` (진행도) 저장.
- **PlayerItems:** 획득한 장비 저장. `EnchantOptions` 컬럼에 **JSON 포맷**으로 가변적인 옵션 데이터를 저장하여 기획 변경에 유연하게 대처.

<br>

## 🛠️ Tech Stack

| Category | Technology | Usage |
| :--- | :--- | :--- |
| **Framework** | .NET 8 (ASP.NET Core) | RESTful API Server |
| **Database** | MySQL 8.0 | Persistent Data Storage (Items, Logs) |
| **Cache** | Redis | Session Management, Write-Back Buffer |
| **Infra** | AWS EC2 & Docker | Server Hosting & Orchestration |
| **Network** | Photon Fusion 2 | Real-time Gameplay Sync (UDP) |

<br>

## 아키텍처 구조도
<img width="785" height="817" alt="diagram-export-2026 -1 -7 -오후-6_30_57" src="https://github.com/user-attachments/assets/e4685ae9-bc7a-4073-9453-29062c479bee" />
