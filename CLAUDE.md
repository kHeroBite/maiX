# CLAUDE.md - AI Agent 프로젝트 매뉴얼

> **역할**: Claude Code가 프로젝트에서 작업할 때 참조하는 핵심 지침서

---

## 문서/스킬 3-tier 구조 (최우선 원칙)

```
┌─────────────────────────────────────────────────────────────────┐
│  Tier 1: CLAUDE.md (이 파일) — 범용 규칙                         │
│  ● 모든 프로젝트에 공통 적용되는 규칙/정책/프로세스              │
│  ● 스킬 전체 목록 및 설명 (17개)                                 │
│  ● 실행 환경, 병렬화 정책, MCP 설정 등 범용 인프라               │
├─────────────────────────────────────────────────────────────────┤
│  Tier 2: PROJECT.md — 프로젝트 고유 정보                         │
│  ● 프로젝트 메타데이터, 파일 구조, 아키텍처                      │
│  ● 프로젝트스킬(_{project}) 목록 및 설명                         │
│  ● 해당 프로젝트에만 해당하는 설정/경로/도구                     │
├─────────────────────────────────────────────────────────────────┤
│  Tier 3: 스킬 파일 (.claude/skills/*/SKILL.md)                   │
│  ● 가이드/유틸리티 = 100% 범용 (프로젝트명/경로 금지)           │
│  ● 프로젝트스킬(_{project}) = 100% 프로젝트 고유                │
│  ● 프로젝트 문서(ADVANCED/DATABASE/MCP 등) = 프로젝트 고유       │
└─────────────────────────────────────────────────────────────────┘
```

**분리 원칙 (절대 위반 금지):**
- CLAUDE.md, 가이드/유틸리티 스킬에 **특정 프로젝트명/경로/설정 금지**
- 프로젝트 고유 정보는 **PROJECT.md 또는 프로젝트스킬(_{project})에만** 기재
- kO 진입 시 **현재 프로젝트를 감지**하여 `kInfra_{project}` 자동 호출 → 프로젝트별 인프라 설정 로딩

---

## 언어 정책

**필수**: 모든 대화/주석/변수명/함수명/클래스명/문서 = 한국어. Git 커밋 = 한국어 + 이모지.
**예외**: 기술 용어(REST API, MCP 등), 라이브러리명은 영어 허용.
**중요**: Context 압축(/compact) 후에도 한국어 유지 필수. 새 세션도 한국어로 시작.

---

## 실행 환경: WSL2 + Windows

Claude Code는 WSL2에서 실행되며, 프로젝트 파일은 Windows NTFS(`/mnt/c/`)에 위치.

```yaml
기본_환경: WSL2 (Linux)
프로젝트_경로: kInfra_{project} 프로젝트스킬의 솔루션_경로 참조

WSL에서_실행 (기본):
  - Bash 명령어, dotnet build, curl, MCP 도구
  - Read/Glob/Grep (읽기는 정상 작동)
  - Serena 코드 분석/편집

도구_정책 (WSL 최우선):
  - WSL에서 최대한 리눅스 도구 사용, Windows 도구(.exe)는 최소화
  - git push → kInfra_{project} 프로젝트스킬 참조
  - Windows 도구 사용 시: 반드시 사유 명시 + Fallback으로만 사용

파일_수정 (/mnt/c/ 경로 — 절대 규칙):
  - NTFS(/mnt/c/)에서 Edit/Write 도구 직접 사용 절대 금지
    - drvfs 캐시 불일치로 ENOENT statx 발생 → 파일 손상 (복구 불가)
    - "한 번만 괜찮겠지" 예외 없음 — .cs, .md, .json, .xml 모든 파일 유형 해당
  - 필수 절차 (rsync 방식 — 유일한 안전 경로):
      1. cp "/mnt/c/.../파일" ~/work/{project}-ntfs/파일  (ext4로 복사)
      2. Read ~/work/{project}-ntfs/파일  (ext4 파일 읽기)
      3. Edit ~/work/{project}-ntfs/파일  (ext4에서 부분 수정)
      4. rsync -a --inplace ~/work/{project}-ntfs/파일 "/mnt/c/.../파일"  (NTFS에 동기화)
  - --inplace 필수 (없으면 임시파일 rename 시 NTFS metadata 오류)
  - BOM 보존: cp가 바이너리 복사이므로 utf-8-sig BOM 자동 보존
  - 작업 디렉토리: ~/work/{project}-ntfs/ (세션 공통)
  - 예외_Serena_심볼편집만: LSP 경유이므로 rsync 불필요 (유일한 예외)
  - 위반_감지: /mnt/c/ 경로에 Edit/Write 도구 직접 호출 시 즉시 중단 + 경고

스크린샷:
  - kInfra_{project} 프로젝트스킬에 경로/API 정의됨
  - 사용자가 스크린샷 언급 시: 프로젝트스킬의 경로에서 최신 파일 자동 확인 (묻지 않고 직접 탐색)
```

---

## 관련 문서

| 문서 | 내용 | 범위 |
|------|------|------|
| **[PROJECT.md](./PROJECT.md)** | 프로젝트 구조, 파일 인벤토리, **프로젝트스킬 상세** | 프로젝트 고유 |
| **[ADVANCED.md](./ADVANCED.md)** | 새 폼, 메뉴 등록, 권한, 람다식, 리팩토링 | 프로젝트 고유 |
| **[DATABASE.md](./DATABASE.md)** | 테이블, 스키마, FK, MCP 설정 | 프로젝트 고유 |
| **[MCP.md](./MCP.md)** | MCP 서버 통합 문서 | 프로젝트 고유 |
| **[RESTAPI.md](./restapi.md)** | REST API 엔드포인트 | 프로젝트 고유 |
| **[HISTORY.md](./HISTORY.md)** | 버그 수정 및 작업 이력 | 프로젝트 고유 |
| **[LESSONS.md](./LESSONS.md)** | 자기 개선 교훈 로그 | 프로젝트 고유 |

> 프로젝트스킬(`_{project}`) 상세 설명은 **PROJECT.md** 참조.

---

## 스킬 시스템 (45개)

**모든 사용자 메시지에 kO 자동 발동** → 분류 + 오케스트레이션/spawn

```
사용자 메시지 → [kO] 디스패처 + 직접 오케스트레이터
                 ├─ 질문/탐색 → 팀에이전트(kQ) spawn → kO idle
                 ├─ 퀵 작업 → 팀에이전트(kQ 퀵모드) spawn → kO idle
                 │              └─ kQ 내부: kDev→kTest→kDone 직접 수행
                 └─ 코드 수정 → kO 직접 5단계 오케스트레이션:
                                ├─ 1단계: kPlan 팀에이전트 spawn → 계획 수립
                                ├─ 2단계: 분류(라이트/미디엄/풀) → kDev×N 팀에이전트 spawn
                                ├─ 3단계: kTest×N 팀에이전트 spawn
                                ├─ 4단계: kDone×N 팀에이전트 spawn
                                └─ 5단계: kFinish — kO(메인)가 직접 실행
                 (추가 작업 시: kO-worker×1 spawn → 독립 파이프라인)
```

### 오케스트레이션 (1개)

| 스킬 | 설명 |
|------|------|
| **kO** | 디스패처 + 직접 오케스트레이터 — 모든 메시지에 자동 발동, 3-way 분류(질문/퀵/코드수정). 코드 수정 시 kO가 직접 kPlan→kDev→kTest→kDone→kFinish 팀에이전트 5단계 오케스트레이션. 추가 작업 시 kO-worker 위임 |

### 에이전트 전용 (1개)

| 스킬 | 설명 |
|------|------|
| **kQ** | 질문/탐색 + 퀵 처리 — 팀에이전트가 실행, 퀵 시 kDev→kTest→kDone 직접 수행 |

### 메인 + 서브스킬 (22개)

> **파이프라인 순서 엄수 (L-008)**: kO 직접 오케스트레이션으로 kPlan → 분류 → kDev → kTest → kDone → kFinish. 이전 단계 완료 전 다음 진입 금지.

#### kPlan 계열 (4개) — 순수 계획 수립

| 스킬 | 설명 | 파이프라인 |
|------|------|-----------|
| **kPlan** | 순수 계획 — kO가 팀에이전트로 spawn, Quick/Deep 자체 판정, 파일 목록 + 단위작업 출력 | 1 메인 |
| **kPlan_deep** | 심층 설계 — Scout 에이전트 + 대안 탐색 + 정밀 분석 | 1-1 |
| **kPlan_sim** | 계획 시뮬레이션 — 정상/예외/엣지케이스 사전 발견 | 1-2 |
| **kPlan_review** | 계획 검증 — 요구사항 + 기술 타당성 Two-step | 1-3 |

#### kDev 계열 (5개) — 코드 구현

| 스킬 | 설명 | 파이프라인 |
|------|------|-----------|
| **kDev** | 구현 라우터 — Lock 획득, 도구 선택, 배치 빌드 | 2 메인 |
| **kDev_parallel** | 병렬 디스패치 — 서브/팀 에이전트 생성, 파일 할당 | 2-1 |
| **kDev_review** | 코드 리뷰 — 구현 결과물 검증, 에이전트 간 일관성 | 2-2 |
| **kDev_impact** | 영향도 분석 — 심볼/인터페이스 참조 추적 | 2-3 |
| **kDev_lock** | 파일 Lock — 세션 간 충돌 방지, Stale Lock 감지 | 2-4 |

#### kTest 계열 (5개) — 빌드/테스트

| 스킬 | 설명 | 파이프라인 |
|------|------|-----------|
| **kTest** | 테스트 라우터 — 3 Phase 순서 + 대상 자동 감지 | 3 메인 |
| **kTest_build** | 빌드 — 프로세스 종료 + 병렬 빌드 + 증거 수집 | 3-1 |
| **kTest_deploy** | 배포 — 로컬/모바일/원격 배포 실행 | 3-2 |
| **kTest_run** | 테스트 실행 — 로그/API/스크린샷 + 스마트 라우팅 | 3-3 |
| **kTest_quality** | 품질 검증 — 요청-결과 대조 + DB 정합성 + UI/UX | 3-4 |

#### kDone 계열 (9개) — 작업 마무리

| 스킬 | 설명 | 파이프라인 |
|------|------|-----------|
| **kDone** | 마무리 라우터 — Fast/Parallel Path + 서브스킬 디스패치 | 4 메인 |
| **kDone_review** | 프로세스 개선 — 분석 + 조치방향 JSON 출력 (반영은 후속 스킬) | 4-1 |
| **kDone_hooks** | Hook 강제화 — review JSON 소비, 물리 차단 설계/구현 | 4-2 |
| **kDone_skills** | 스킬 업데이트 — review JSON 소비, 규칙 강화/추가 | 4-3 |
| **kDone_cleanup** | 코드 정리 — 디버그 변환, 테스트 코드 제거, Lock 해제 | 4-4 |
| **kDone_docs** | 문서 업데이트 — 교훈 기록(LESSONS/MEMORY) + HISTORY/PROJECT/DATABASE | 4-5 |
| **kDone_git** | Git 커밋 & 푸시 — 한국어+이모지 메시지, 다중 세션 주의 | 4-6 |
| **kDone_notify** | ntfy 알림 — 발송 + 잔여 Phase 체크 + 타이머 정지 | 4-7 |
| **kFinish** | 파이프라인 마무리 — 팀에이전트 종료, tmux 정리, 종료 배너. 메인 직접 실행 | 4-8 |

### 유틸리티 (4개)

| 스킬 | 설명 |
|------|------|
| **kDebug** | 체계적 디버깅 — 4 Phases 근본원인 분석 (모든 단계에서 호출 가능) |
| **kVerify** | 완료 검증 — Gate Function 5단계, 증거 기반 확인 (모든 단계에서 호출 가능) |
| **kThink** | 관점 전환 — 문제 재정의, 대안 매트릭스, 레버리지 포인트 (교착 시 호출) |
| **kSurl** | URL 단축 — 커밋 메시지/알림에 긴 URL 단축 |

### 에이전트 (1개)

| 스킬 | 설명 |
|------|------|
| **agent_profiles** | 에이전트 프로필 프리셋 — 프롬프트 조립 시 참조 (impl-form/backend/mobile, scout, analyst, reviewer) |

### 도메인 (5개)

| 스킬 | 설명 |
|------|------|
| **domain-fileops** | 파일 수정 Low-level 드라이버 — 환경 감지(NTFS/EXT4), NTFS 안전 절차, 원자적 파일 Lock |
| **domain-csharp** | C# 코드 품질 분석 + 리팩토링 (통합) |
| **domain-winforms** | WinForms UI 디자인 + MDI 폼 템플릿 (통합) |
| **domain-database** | DB 스키마 마이그레이션 |
| **domain-context7** | Context7 MCP 실시간 라이브러리 문서 |

### 라이브러리 스킬 (1개)

> **네이밍 규칙**: 공통 라이브러리 스킬은 `kSkill_{라이브러리명}` 형식. pipeline_gate.sh에서 `kSkill_*` 패턴으로 자동 허용.

| 스킬 | 설명 |
|------|------|
| **kSkill_livecharts2** | LiveCharts2 차트 구현 — kPlan/kDev/kTest 파이프라인 통합 |

### 외부 스킬 (2개)

| 스킬 | 설명 |
|------|------|
| **skill-creator** | 스킬 생성 가이드 — 새 스킬 작성/수정 시 구조화된 워크플로우 |
| **mcp-builder** | MCP 서버 생성 가이드 — FastMCP/MCP SDK 기반 외부 서비스 통합 |

### 프로젝트스킬 시스템

프로젝트 고유 설정은 `_{project}` 접미사 프로젝트스킬로 분리. 서브스킬은 100% 범용.

```yaml
구조: 메인스킬 > 서브스킬 (범용) + 프로젝트스킬 (고유)
로딩: 서브스킬 진입 시 해당 프로젝트스킬 자동 로딩
코딩_규칙: /kRules_{project} — 프로젝트별 코딩 규칙 on-demand 로드

프로젝트스킬_목록 (_{project} 접미사):
  - kInfra_{project}: 프로젝트 메타, API, 로그, MCP 설정
  - kRules_{project}: 프로젝트 코딩 규칙
  - kTest_build_{project}: 프로젝트 빌드 명령/설정
  - kTest_deploy_{project}: 프로젝트 배포 명령/설정
  - kTest_run_{project}: 프로젝트 테스트 명령/설정

자동_감지_흐름:
  1. 사용자 메시지 수신
  2. kO 자동 발동 (모든 메시지)
  3. kO가 현재 프로젝트 감지 (CLAUDE.md 경로, .sln 파일 등)
  4. kInfra_{project} 프로젝트스킬 자동 호출
  5. 3-way 분류 (질문/퀵/코드수정)
  6. 질문/퀵: 팀에이전트(kQ) spawn → kO idle / 코드 수정: kO 직접 오케스트레이션
  7. 팀에이전트 내부에서 해당 _{project} 자동 로딩

프로젝트스킬_상세: PROJECT.md 참조
```

### 스킬 하드링크 공유 (다중 프로젝트)

```yaml
공유_방식: NTFS 하드링크 (ln) — 동일 inode, 어느 프로젝트에서 수정해도 전체 반영
공유_대상: AI ↔ Mars ↔ MaiX (3개 프로젝트)
공유_범위: 범용 스킬 34개 (프로젝트스킬 제외)
원본_소스: /mnt/c/DATA/Project/AI/.claude/skills/

범용_스킬: 오케스트레이션(kO), 에이전트전용(kQ),
           kPlan 4개, kDev 5개, kDone 8개, kTest 5개,
           유틸리티(kDebug, kVerify, kThink, kSurl), agent_profiles, domain 4개, find-skills
프로젝트스킬: kInfra_{project}, kRules_{project}, kTest_build_{project} 등 (하드링크 아님)

외부_스킬_설치:
  도구: npx skills add {owner/repo} --skill {skill-name} -a claude-code -y
  설치_후:
    1. symlink→디렉토리+하드링크 교체 (skills CLI가 .claude/skills에 symlink 생성 → Claude Code 인식 불가)
    2. 다른 프로젝트에 하드링크 연결 필수 (상세: MEMORY/skills-hardlink.md)
  .agents/skills: skills CLI 원본 디렉토리 — 하드링크 동기화 필요
  강제화: PostToolUse Hook (skills_hardlink_guard.sh)이 npx skills add 감지 → symlink 교체 + 하드링크 안내

금지:
  - 범용 스킬에 프로젝트 고유 경로/설정 추가
  - 심볼릭 링크 사용 (Claude Code가 symlink 디렉토리의 SKILL.md를 슬래시 커맨드로 인식 못함)
  - 한 프로젝트에만 범용 스킬 수정 후 다른 프로젝트 미반영 (하드링크이므로 자동 반영됨)
```

---

## 병렬화 정책 (핵심 원칙: 병렬이 기본값)

> **유일 출처 (Single Source of Truth)**: 이 섹션이 병렬화 정책의 유일한 정의. 다른 문서에서 참조만 허용.

```yaml
기본_원칙: 병렬이 기본값, 순차가 예외
순차_선택_시: 반드시 사유 명시 (미명시 = Gate 위반)

필수_조건 (OR — 하나라도 해당하면 병렬화):
  A. 수정 파일 2개+ AND 독립 작업 2개+
  B. 시간: 예상 소요 시간 10분+ AND 독립 작업 2개+
  C. 복잡도: 복잡도 높음 AND 독립 작업 2개+
  공통: 코드 파일뿐 아니라 .md, .json 등 비코드 파일도 해당
  강제: kO에서 판정 (kPlan 결과 수신 후) → 이후 변경 절대 금지

에이전트_수_정책 (참조 — 유일 출처: kO 코드 수정 오케스트레이션 Step 2):
  기준값: max(수정_파일수, 단위작업수)
  | 분류       | 빠른 확장 상한 | 신중 확장     |
  |-----------|--------------|--------------|
  | 질문/탐색  | kQ ×1        | -            |
  | 퀵        | kQ ×1        | -            |
  | 라이트     | 2개까지       | 4개+ 그룹화   |
  | 미디엄     | 4개까지       | 6개+ 그룹화   |
  | 풀        | 6개까지       | 8개+ 그룹화   |
  빠른_확장_이내: 기준값 = 에이전트 수 (1:1, 그룹화 없음)
  빠른_확장_초과: 에이전트 수 = 상한 고정, 초과분 그룹화
  원칙: 퀵 이외 코드 수정 작업은 팀에이전트 위임 (메인 직접 수정 금지)
  분류_주체: 질문/퀵 = kO 직접, 라이트/미디엄/풀 = kO 직접 (kPlan 결과 기반)
  서브단계_자율: kPlan/kTest/kDone 각 서브단계는 필요 시 자율적으로 추가 에이전트 spawn 가능

병렬_극대화:
  - 서브에이전트/팀에이전트 개수 상한 없음
  - 빠른 확장 이내: 기준값만큼 에이전트 (1:1 단순 할당)
  - 빠른 확장 초과: 상한까지 에이전트 + 초과분 그룹화
  - 순차 처리 최소화, 병렬 처리 최대화

충돌_방지:
  - 파일 할당 매트릭스: kO에서 사전 분배 (수정 파일 중복 = 0)
  - 에이전트 프롬프트에 "수정 허용 파일" 목록 필수 포함
  - 다중 세션: git status로 사전 확인 + kDev_lock으로 실시간 보호
  - 커밋: 팀원은 커밋 금지, 리더만 통합 커밋

금지:
  - 병렬화 조건 충족 시 순차 처리
  - 빠른 확장 이내에서 그룹화로 에이전트 수 축소 (1:1 단순 할당 필수)
  - 문제 해결 중 병렬화 판단 포기
  - 에이전트 수를 인위적으로 제한
  - 파일 할당 매트릭스 없이 병렬 실행

에이전트_네이밍: agent_profiles "에이전트 네이밍 표준" 섹션 참조 (유일 출처)
```

---

# Bash 명령어 단일 실행 원칙

> **유일 출처**: 이 섹션이 Bash 명령어 규칙의 유일한 정의.

**절대 금지**: `&&`, `||`, `;`, `|` 연산자 사용한 명령어 체이닝

```yaml
규칙:
  - 각 명령어를 별도 Bash 호출로 분리
  - 독립 작업은 동일 메시지 내 병렬 호출
  - 순차 작업은 별도 메시지로 분리
  - 1분+ 작업: run_in_background: true
```

---

## 에이전트 Context 관리 (Context Limit 방지)

```yaml
환경변수_설정:
  CLAUDE_CODE_MAX_OUTPUT_TOKENS: "64000"        # 에이전트 결과 잘림 방지

에이전트_프롬프트_필수규칙:
  결과_반환:
    - 에이전트는 분석 결과를 scratchpad 파일로 저장
    - 반환 메시지는 요약 5줄 이내 + 파일 경로만
    - 전체 코드/쿼리 본문을 반환 메시지에 포함 금지
  범위_제한:
    - 에이전트 1개당 담당 파일 최대 3개
    - "전체 분석" 금지 → 반드시 파일/범위 지정
    - 반복 작업("사라질때까지")은 "1회차: 상위 N개"로 제한
  DB_결과:
    - SELECT 결과는 COUNT(*) 또는 상위 5행만
    - 전체 결과 출력 금지 (컨텍스트 폭발 원인)

대규모_작업_분할:
  - 분석 → 구현 → 검증을 별도 세션으로 분리
  - 1세션 = 1단계 원칙
  - 세션 간 결과는 scratchpad .md 파일로 전달
  - 시뮬레이션/DB테스트는 구현 완료 후 별도 세션

에이전트_수_제한:
  - 분석 작업: 최대 4개 (리더 컨텍스트 보호)
  - 구현 작업: 파일 수에 따라 제한 없음 (기존 병렬화 정책 적용)
  - sequential-thinking: 분석 범위 파일 2개 이내로 제한
```

---

## 예외 및 중단 처리

```yaml
중단_조건: Bypass 모드 중단 / 동일 오류 10회 반복 / 외부 의존성 문제
중단_시: ntfy 알림 (priority:5, warning 태그)
```

---

## MCP 서버

### 기본 ON (6개)
| 서버 | 용도 | 비고 |
|------|------|------|
| **mysql** | DB 작업 | 한글 INSERT/UPDATE 금지 |
| **serena** | 코드 분석/편집 | claude-code context |
| **sequential-thinking** | 문제 분석 | |
| **context7** | 라이브러리 문서 | 무료 |
| **ref** | 문서 검색 | |
| **vibe-check-mcp** | 메타인지 검증 | |

### MCP MySQL 한글 인코딩 규칙

> **유일 출처**: 한글 INSERT/UPDATE 금지 규칙은 여기서만 정의. 프로젝트스킬은 참조만.
> 한글 데이터 수정 방법 상세: /kRules_{project} 참조.

**MCP MySQL로 한글 INSERT/UPDATE 절대 금지** (latin1 연결). SELECT/영문만 허용.

### MCP 호출 규칙
- 각 MCP 도구를 직접 호출. Serena의 `execute_shell_command`로 다른 MCP 호출 금지.

### 코드 수정 도구 선택

| 상황 | 도구 |
|------|------|
| C# 코드 심볼 수정/추가/리팩토링 | Serena |
| Designer.cs, 비코드 파일, 주석/문자열 | Claude Code Edit |
| Serena 오류 시 | Claude Code Edit (Fallback) |

---

## 프로젝트 정보

- **구조**: [PROJECT.md](./PROJECT.md) - 파일 추가/삭제 시 즉시 업데이트, **프로젝트스킬 상세 포함**
- **DB**: [DATABASE.md](./DATABASE.md) - 테이블 변경 시 즉시 업데이트

---

## 중요 참고사항

### 프로젝트별 코딩 규칙
- SQL, Gateway, 권한, UI, GDI+, 한글 인코딩 등 프로젝트 고유 규칙은 `/kRules_{project}`에서 on-demand 로드
- kO 진입 시 자동 안내됨

### TODO 파일 생명주기
생성 → 계획 → 추적 → 삭제 (.gitignore 포함)
