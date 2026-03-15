# CLAUDE.md - AI Agent 프로젝트 매뉴얼

> **역할**: Claude Code가 프로젝트에서 작업할 때 참조하는 핵심 지침서

---

## 문서/스킬 3-tier 구조 (최우선 원칙)

```
┌─────────────────────────────────────────────────────────────────┐
│  Tier 1: CLAUDE.md (이 파일) — 범용 규칙                         │
│  ● 모든 프로젝트에 공통 적용되는 규칙/정책/프로세스              │
│  ● 스킬 전체 목록 및 설명 (범용 47개)                             │
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
- Skill('kO') 로딩 시 **현재 프로젝트를 감지**하여 `kInfra_{project}` 자동 호출 → 프로젝트별 인프라 설정 로딩

---

## 언어 정책

**필수**: 모든 대화/주석/변수명/함수명/클래스명/문서 = 한국어. Git 커밋 = 한국어 + 이모지. [COMPACT]
**커밋 모델 태그**: 커밋 메시지 제목 마지막에 `by {모델버전}` 추가 (예: `✨ 기능 추가 by claude-sonnet-4-6`, `✨ 기능 추가 by codex-5.3-pro`). 접두어([클로드], [코덱스] 등) 사용 금지.
**예외**: 기술 용어(REST API, MCP 등), 라이브러리명은 영어 허용.
**중요**: Context 압축(/compact) 후에도 한국어 유지 필수. Compact 후 system-reminder의 규칙 복원 안내 준수 + CLAUDE.md 재읽기 필수. 새 세션도 한국어로 시작.

### 인코딩 및 텍스트 파일 정책

- **PowerShell UTF-8 파일 쓰기 (L-148)**: `Out-File -Encoding UTF8` / `Set-Content -Encoding UTF8` 사용 **절대 금지** (PowerShell 5.x에서 BOM 강제 포함 → Python/Linux 호환 불가). 대신:
  ```powershell
  # 문자열 → 파일
  [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
  # 배열 → 파일
  [System.IO.File]::WriteAllLines($path, $lines, [System.Text.UTF8Encoding]::new($false))
  ```
  - 위반 시 BOM 오염 발생 (주의)
- 콘솔 세션은 `chcp 65001` 및 `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8` 상태를 유지합니다.
- Python 스크립트는 파일 I/O 시 `encoding='utf-8'`을 사용하고, 대량 치환 후 `git diff`로 한글 깨짐 여부를 확인합니다.
- **CRLF 정책**: NTFS 프로젝트 파일(`.cs`, `.md`, `.json`, `.xml` 등)은 CRLF 줄 끝 유지.
  - rsync로 동기화 후 CRLF가 손상된 경우 `unix2dos 파일명`으로 복구.
  - git 설정: `core.autocrlf = true` (Windows) / `core.autocrlf = input` (WSL) 유지.
  - 신규 파일 Write 시 **CRLF로 생성됨** (Claude Code 기본 동작 — L-057). `.sh` 파일 Write 후 반드시 `sed -i 's/\r//' 파일` 실행 필수.
  - `.gitattributes`에 `* text=auto eol=crlf` 설정 시 git checkout 시 자동 CRLF 변환 (unix2dos 불필요).
- **BOM 정책**: 신규 파일은 UTF-8 without BOM으로 생성. 기존 파일 수정 시 BOM 유무 그대로 유지 (cp 바이너리 복사로 자동 보존).
- **Git 인코딩**: `i18n.commitEncoding = utf-8`, `i18n.logOutputEncoding = utf-8` 설정 유지.
- **Git 커밋 모델 태그**: Codex CLI 포함 모든 AI 도구는 커밋 메시지 제목 마지막에 `by {모델버전}` 형식으로 모델을 명시. 접두어([클로드], [코덱스] 등) 사용 금지.

### UI/Designer 규칙

- UI 컨트롤 추가/배치는 `*.Designer.cs`에서만 수행.
- 코드에서 `new Control()`로 직접 생성 후 연결 금지.
- 이벤트 바인딩도 Designer에서 처리.


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
  - NTFS(/mnt/c/)에서 Edit/Write 도구 직접 사용 절대 금지 [COMPACT]
    - drvfs 캐시 불일치로 ENOENT statx 발생 → 파일 손상 (복구 불가)
    - "한 번만 괜찮겠지" 예외 없음 — .cs, .md, .json, .xml 모든 파일 유형 해당
  - 필수 절차 (rsync 방식 — 유일한 안전 경로):
      1. cp "/mnt/c/.../파일" /home/rioky/work/{project}-ntfs/파일  (ext4로 복사)
      2. Read /home/rioky/work/{project}-ntfs/파일  (ext4 파일 읽기)
      3. Edit /home/rioky/work/{project}-ntfs/파일  (ext4에서 부분 수정)
      4. rsync -a --inplace /home/rioky/work/{project}-ntfs/파일 "/mnt/c/.../파일"  (NTFS에 동기화)
  - --inplace 필수 (없으면 임시파일 rename 시 NTFS metadata 오류)
  - BOM 처리: cp가 바이너리 복사이므로 기존 BOM 자동 보존 (신규 파일은 BOM 없이 생성)
  - 작업 디렉토리: /home/rioky/work/{project}-ntfs/ (세션 공통 — ~/는 Edit/Write/Read 도구에서 확장 불가, 절대경로 필수)
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

## 스킬 시스템 (범용 47개 + 프로젝트스킬 10개)

**모든 사용자 메시지에 메인이 6-way 판단** (판단만 수행. kO 로딩은 수정 작업 시만 — 질문/계획은 kO 미경유) → 질문/계획/퀵/라이트/미디엄/풀

> **v3.2 아키텍처**: 메인 = 6-way 판단 + 수정 시 Skill('kO') 로딩 → 메인이 직접 팀에이전트 spawn+오케스트레이션. kFinish = 메인 직접 실행.
> **팀에이전트 spawn 방식**: Claude Code가 자동으로 pane 생성 + 팀 join. Task 도구 team_name 필수.
> **kO 팀에이전트 spawn 절대 금지 (L-214)**: Claude Code는 flat team 구조. kO는 스킬로만 로딩.

**spawn 규칙 (절대):**
- `mode: "bypassPermissions"` 필수 (L-181). `mode: "plan"` 절대 금지 [COMPACT]
- 메인 직접 파일 수정 절대 금지 (L-171). 파일 종류 무관, 예외 없음 [COMPACT]
- kO를 팀에이전트(Agent)로 spawn 절대 금지 (L-214) — kO는 스킬로만 로딩

```
사용자 메시지 → [메인] (6-way 판단, 1턴 이내)
  ├─ 질문 → 메인 직접 응답 (Read/Grep/Glob 0~2회)
  ├─ 계획 → TeamCreate + kPlan spawn + 대기 → kFinish (상세: "계획 분류 절차" 참조)
  │           └─ kPlan: 코드 탐색 + 분석/설계 결과만 반환 (파일 수정 금지)
  ├─ 퀵(비코드) → Skill('kO') → kDev → kDone → kFinish
  │                 비코드(.md/.json/.yaml)만 수정. 코드 수정 1건이라도 있으면 라이트 격상.
  └─ 라이트/미디엄/풀(코드) → Skill('kO') → kPlan → kDev×N → kTest → kDone → kFinish
                                코드(.cs 등) 수정 포함. kPlan+kTest 필수.

⚠️ kFinish 자동 발동 (L-200 — 절대 규칙):
  원칙: 팀에이전트 사용 시(계획/퀵/라이트/미디엄/풀) 완료 후 반드시 kFinish 실행 [COMPACT]
  순서: 마지막 에이전트 완료 → Skill('kFinish') 즉시 실행 (shutdown은 kFinish_cleanup이 잔류 멤버에 보충 발송)
  금지:
    - 완료 후 kFinish 없이 사용자에게 응답만 하고 종료
    - "정리가 필요하면 말씀해주세요" 식의 선택적 kFinish (kFinish는 필수, 선택 아님)
    - 다른 작업으로 전환하며 kFinish 누락 (인터럽트 발생해도 기존 팀 kFinish 먼저)
  조기_종료 (L-216):
    - 팀에이전트 1개라도 spawn한 후 에러/중단 발생 시에도 kFinish 필수
    - kDone 미도달이어도 kFinish 실행 (팀 정리 + IDLE 전환)
    - 적용: 계획 단독 완료, kPlan 에스컬레이션 실패, kDev/kTest 실패 중단
  커밋_필수 (L-205): kFinish 완료 후 미커밋 변경이 있으면 반드시 git add + commit + push 수행

6-way 분류 기준:
  질문: 코드 탐색 0~2회로 답변 가능 (아키텍처 질문, 파일 위치, 설정 확인 등)
  계획: 파일 수정 없이 분석/설계만 필요한 경우 — 코드 탐색 3회+ 예상되거나, 새 기능/신규 구현 요청처럼 사전 설계가 필요한 경우.
        ⚡ 즉시 kPlan spawn 트리거 (메인 직접 탐색 금지):
          - "~~ 기능 추가해줘" / "~~ 구현해줘" 형태의 새 기능 요청
          - 여러 파일에 걸친 변경이 예상되는 요청
          - DB/파일/UI 등 여러 레이어가 연관된 요청
          - 메인이 코드 탐색을 1회라도 수행한 시점에서 3회+ 예상되면 즉시 kPlan spawn
  퀵: 비코드 파일(.md/.json/.yaml 등)만 수정 — 코드 파일 수정 0건
  라이트: 코드 파일 1~2개 AND 단위작업 1~2개 AND 복잡도 낮음
  미디엄: 코드 파일 2~4개 OR 단위작업 3~5개 OR 복잡도 중간
  풀: 코드 파일 5개+ OR 단위작업 6개+ OR 복잡도 높음

다중 작업 (L-178):
  원칙: 사용자 메시지에 수정 작업이 2개 이상이면 Skill('kO') 로딩 후 오케스트레이션
  예외: 질문만 2개+ → 메인 직접 응답 (kO 미경유). 질문+수정 혼합 → 질문 즉시 응답 + 수정은 kO 경유

메인 역할 경계 (L-162):
  원칙: 메인 = 디스패처 + 오케스트레이터. 6-way 판단 + spawn + 대기 + kFinish
  질문: Read/Grep/Glob 0~2회만. 초과 시 계획으로 격상하여 kPlan spawn
  계획: 사전 코드 탐색 없이 즉시 kPlan spawn (코드 탐색은 kPlan 역할)
        금지 패턴: "먼저 현황을 파악하겠습니다" + 탐색 시작 → 이미 위반
        새 기능 요청은 코드를 보기 전에 계획으로 분류하고 즉시 kPlan spawn
  퀵/라이트+: Skill('kO') 로딩 → kO 지침에 따라 팀에이전트 spawn
  금지: "먼저 현황을 파악하겠습니다" 식의 메인 직접 탐색 (3회+ = 계획 = kPlan 위임)

인터럽트 처리 (L-162):
  원칙: 매 사용자 메시지마다 독립 6-way 재판단 수행
  "결이 같은 요청": 진행 중 에이전트에 SendMessage로 추가 지시
  "결이 다른 요청": 같은 팀에 새 에이전트 spawn 또는 새 TeamCreate
  질문: 팀 진행 중이어도 메인 직접 응답 (0~2회 탐색)

계획 분류 절차 (L-219 — kO 미경유, 메인 직접 수행):
  판정_기준: 사용자 요청의 의도가 분석/설계이고 파일 수정 없음 → 계획 분류 (메인 사전 탐색 불필요)
  절차:
    1. SID 결정: OWNER_SID=$(awk '{print $2}' /tmp/claude_pipeline_state 2>/dev/null | cut -c1-8); SHORT_SID="${OWNER_SID:-$(echo $CLAUDE_SESSION_ID | cut -c1-8)}"
    2. TeamCreate (team_name 자동 생성)
    3. BEFORE 스냅샷: tmux list-panes -a -F '#{pane_id}' | sort > /tmp/claude_panes_before_${SHORT_SID}.txt
    4. pipeline_state 설정: echo "PLAN $CLAUDE_SESSION_ID" > /tmp/claude_pipeline_state
    5. kPlan spawn: Agent(team_name, name="kPlan-1", mode="bypassPermissions", prompt="Skill('kPlan') + 사용자 요구사항 + Skill('kInfra_{project}')")
    6. kPlan 완료 대기 → 결과 수신 → 사용자에게 결과 전달
    7. shutdown_request 발송 (fire-and-forget)
    8. kFinish 실행 (팀 정리 + IDLE 전환 — kDev/kTest/kDone 미수행이므로 경량 모드)
  금지:
    - kO 스킬 로딩 (계획은 kO 미경유)
    - 메인 직접 코드 탐색 후 계획 (코드 탐색은 kPlan 역할)
    - kPlan 결과를 기반으로 메인이 직접 수정 시작 (수정 필요 시 사용자에게 결과 공유 후 새 메시지 대기)
```

### 오케스트레이션 (1개)

| 스킬 | 설명 |
|------|------|
| **kO** | 오케스트레이션 실행 지침 — 메인이 Skill('kO')로 로딩. 퀵/코드수정 분류 + 파이프라인 순서(kPlan→kDev→kTest→kDone) + 팀에이전트 spawn 지침 제공. **팀에이전트로 spawn 절대 금지 (L-214)** |

### 메인 + 서브스킬 (27개)

> **파이프라인 순서 엄수 (L-008)**: 메인(kO 지침)이 kPlan(1) → kDev(2) → kTest(3) → kDone(4) 순서로 팀에이전트 spawn. 이전 단계 완료 전 다음 진입 금지. **진입 기반 상태 전환**: spawn 직전에 해당 단계 상태 설정 (비정상종료 시 실패 단계 식별 가능). **실패 시 역라우팅 허용**: DEV→PLAN, TEST→DEV, DONE→TEST (1단계 역방향만, 최대 2회 — `/tmp/claude_reroute_counter_${SHORT_SID}` 카운터 파일로 강제).

#### kPlan 계열 (4개) — 순수 계획 수립
kPlan(메인) → kPlan_deep(심층설계) / kPlan_sim(시뮬레이션) / kPlan_review(검증)

#### kDev 계열 (6개) — 코드 구현
kDev(메인) → kDev_parallel(병렬디스패치) / kDev_review(코드리뷰) / kDev_simplify(단순화) / kDev_impact(영향도) / kDev_lock(파일Lock)

#### kTest 계열 (7개) — 빌드/테스트
kTest(메인) → kTest_build(빌드) / kTest_deploy(배포) / kTest_run(테스트실행) / kTest_quality(품질검증) / kTestUI(UI테스트) / kTestUIWinforms(WinForms자동화)

#### kDone 계열 (10개) — 작업 마무리
kDone(메인) → kDone_review(프로세스개선) / kDone_trans(우회감지) / kDone_hooks(Hook강제화) / kDone_skills(스킬업데이트) / kDone_cleanup(코드정리) / kDone_docs(문서) / kDone_git(커밋) / kFinish(파이프라인마무리) / kFinish_cleanup(팀정리)

### 유틸리티 (9개)

| 스킬 | 설명 |
|------|------|
| **kCopy** | 새 프로젝트 기본 환경 구축 — AI 원본의 공용 파일을 하드링크, commands/skills 폴더를 심볼릭링크로 연결 |
| **kDebug** | 체계적 디버깅 — 4 Phases 근본원인 분석 (모든 단계에서 호출 가능) |
| **kVerify** | 완료 검증 — Gate Function 5단계, 증거 기반 확인 (모든 단계에서 호출 가능) |
| **kThink** | 관점 전환 — 문제 재정의, 대안 매트릭스, 레버리지 포인트 (교착 시 호출) |
| **kSurl** | 최신 스크린샷 조회 — Windows 스크린샷 폴더에서 최신 PNG 탐색 + 클립보드 복사 |
| **kClean** | 프로젝트 잔여물 정리 유틸리티 |
| **kInsights** | 세션 메타/facets/트랜스크립트/LESSONS 분석 → 패턴 감지 → 사용자 선택 → 자동 개선 (hooks/skills/CLAUDE.md/MEMORY) |
| **kMemOpt** | 컨텍스트 토큰 통합 최적화 — MEMORY→CLAUDE 이관, CLAUDE→스킬 흡수, SKILL.md 최적화, LESSONS→CLAUDE/스킬/hook 이관 (수동 전용) |
| **stress-agent** | 스트레스 테스트 더미 에이전트 — echo/sleep/SendMessage 프로토콜 (Bash+SendMessage만 허용) |

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

### 검색 (1개)

| 스킬 | 설명 |
|------|------|
| **find-skills** | 스킬 검색 — 사용자 요청에 맞는 설치 가능 스킬 탐색 |

### 라이브러리 스킬 (1개)

> **네이밍 규칙**: 공통 라이브러리 스킬은 `kSkill_{라이브러리명}` 형식.

| 스킬 | 설명 |
|------|------|
| **kSkill_livecharts2** | LiveCharts2 차트 구현 — kPlan/kDev/kTest 파이프라인 통합 |

### 외부 스킬 (2개)

| 스킬 | 설명 |
|------|------|
| **skill-creator** | 스킬 생성 가이드 — 새 스킬 작성/수정 시 구조화된 워크플로우 |
| **mcp-builder** | MCP 서버 생성 가이드 — FastMCP/MCP SDK 기반 외부 서비스 통합 |

### 프로젝트스킬 시스템

```yaml
구조: 메인스킬 > 서브스킬 (범용) + 프로젝트스킬 (고유, _{project} 접미사)
로딩: 팀에이전트가 각자 Skill('kInfra_{project}') 호출
코딩_규칙: /kRules_{project} — on-demand 로드
금지: 범용 스킬에 프로젝트 고유 경로/설정 추가
```

> 상세: [PROJECT.md](./PROJECT.md) 프로젝트스킬 섹션 참조

### 스킬 공유 (다중 프로젝트)

```yaml
공유_방식: skills 폴더 자체를 심볼릭링크 — AI/.claude/skills/ → Mars/MaiX에서 폴더 심볼릭링크.
공유_대상: AI ↔ Mars ↔ MaiX (3개 프로젝트)
공유_범위: 범용 스킬 + 프로젝트스킬 모두 포함 (AI 원본에서 통합 관리)
원본_소스: /mnt/c/DATA/Project/AI/.claude/skills/
심볼릭링크: {프로젝트}/.claude/skills → AI/.claude/skills/ (폴더 자체)

프로젝트스킬: AI 원본 skills/ 안에 _{project} 접미사로 포함 (별도 디렉토리 불필요)
  예: AI/.claude/skills/kInfra_mars/, AI/.claude/skills/kRules_mars/ 등

외부_스킬_설치:
  도구: npx skills add {owner/repo} --skill {skill-name} -a claude-code -y
  설치_후: skills 폴더 심볼릭링크이므로 AI에 설치하면 전체 프로젝트 자동 반영
  .agents/skills: skills CLI 원본 디렉토리 — 동기화 필요

금지:
  - 범용 스킬에 프로젝트 고유 경로/설정 추가
  - 한 프로젝트에만 범용 스킬 수정 후 다른 프로젝트 미반영 (AI 원본 수정 시 심볼릭링크로 자동 반영)
```

### Hooks 공유
기타 hook 운영/동기화/금지 규칙은 [AI/hooks.md](./AI/hooks.md) 참조.

---

## 병렬화 정책

> 상세: kDev_parallel SKILL.md "병렬화 분리 기준" 섹션 참조 (유일 출처)

---

# Bash 명령어 실행 규칙

> 상세: kTest SKILL.md "Bash 명령어 실행 규칙" 섹션 참조 (유일 출처) [COMPACT]

---

## 에이전트 운영 규칙

```yaml
팀에이전트_공통:
  - shutdown_request 수신 시 즉시 응답. SendMessage hook 차단 절대 금지 (L-172)
  - 에이전트 반환 메시지: 요약 5줄 이내 + 파일 경로만 (컨텍스트 보호)
  - 에이전트 1개당 담당 파일 최대 3개. DB SELECT는 상위 5행만
  - 스킬 호출 선언 필수: 스킬 로딩 시 "Skill('{스킬명}') 로딩 — {목적}" 형식으로 선언
  - kDev는 구현(파일 수정)만 담당. git commit/push 절대 금지 — 커밋은 kDone 전용 (L-228)
  - 팀에이전트는 정상완료/스킵/이미완료 등 모든 종료 경로에서 SendMessage 완료 보고 필수 (L-228)
중단_조건: 동일 오류 10회 반복 / 외부 의존성 문제 → ntfy 알림
```

> 상세: kO SKILL.md (오케스트레이션 지침, 분류별 처리 흐름, 제약 사항)

---

## 스킬/문서 동기화 규칙

- 스킬 수정 시 CLAUDE.md 테이블 설명 즉시 동기화 (L-119)
- 스킬 추가/삭제 시 개수 카운터 업데이트
- 수정 필요 발견 즉시 자동 수정. "수정할까요?" 질문 금지 (L-116, Bypass 원칙)

---

## 도구 호출 절제 원칙

```yaml
단순_질문_직접_응답:
  - 단순 질문/의견/판단 요청에는 도구 호출 없이 바로 텍스트로 답하라
  - sequential-thinking, vibe-check 등 분석 도구를 불필요하게 호출하면 스트리밍이 멈추고 응답 지연 발생
  - 도구 호출이 필요한 경우: 파일 읽기/수정, 코드 검색, DB 쿼리 등 실제 데이터가 필요할 때만
```

## 스킬 회피 안티패턴 (절대 금지)

아래 사고방식은 스킬 적용을 회피하는 합리화 패턴이다. 감지 즉시 중단하고 스킬을 먼저 확인하라.

- "단순한 질문일 뿐이라 스킬이 필요 없다"
- "먼저 맥락/코드를 파악한 후 스킬을 결정하겠다"
- "이 작업엔 스킬이 과도하다"
- "지금 진행 중인 작업이 있어서 스킬 확인을 나중에 하겠다"
- "이미 방법을 알고 있으니 스킬 없이 진행하겠다"

**원칙**: 1%라도 스킬 적용 가능성이 있으면 먼저 스킬을 확인하라. 스킬이 작업에 해당하면 반드시 사용해야 하며 예외는 없다. (우선순위: 사용자 명시 지시 > 스킬 > 기본 동작)

## kO 경로 강제화 (프로젝트 훅)

`/.claude/settings.json`의 `PreToolUse`에서 `/.claude/hooks/ko_route_guard.sh`를 실행하여 아래를 하드 차단한다.

- 분류(`question|plan|quick|light|medium|full`)가 기록되지 않은 상태의 수정/팀명령
- `question`/`plan` 분류에서 `kO` 호출 또는 파일 수정 시도
- `plan` 분류에서 `kPlan` 이외 팀에이전트 단계 진입
- `quick|light|medium|full` 분류에서 `kO` 미경유 수정/팀명령

즉, 질문/답변만 직접 처리하고, 수정 분류는 반드시 `kO` 선행 후 파이프라인으로만 진행된다.

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

### MCP 공통 지침

- 기본 서버는 위 표의 6개를 항상 활성화하고, 대규모 코드 분석이 필요할 때만 `serena-full`을 추가 연결합니다.
- DB 작업은 `mcp__mysql__*` 도구를 우선 사용하며, 한글이 포함된 쿼리를 실행할 때는 세션마다 `SET NAMES utf8`을 먼저 호출합니다.
- MCP 호출은 각 도구를 직접 실행하며 Serena의 `execute_shell_command`로 다른 MCP를 우회 호출하지 않습니다.
- 코드 수정 도구 선택: `.cs` 파일은 Serena, Designer/문서/JSON/YAML 파일은 Claude Code Edit/Write를 기본으로 사용합니다.

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

### 도메인 스킬 자동 트리거 기준

| 상황 | 자동 로딩 스킬 |
|------|---------------|
| C# 코드 리뷰/리팩토링/품질 분석 | `domain-csharp` |
| WinForms UI 설계/폼 생성/MDI/레이아웃 | `domain-winforms` |
| DB 스키마 변경/테이블 생성/마이그레이션 | `domain-database` |
| 새 라이브러리 도입/버전 업그레이드 | `domain-context7` |
| NTFS 파일 수정/파일 잠금/다중 세션 충돌 | `domain-fileops` |

**규칙**: 위 상황에 해당하면 작업 시작 전 해당 스킬을 반드시 로딩하라. "나중에 필요하면 보겠다" 패턴 금지.

---

## 프로젝트 정보

- **구조**: [PROJECT.md](./PROJECT.md) - 파일 추가/삭제 시 즉시 업데이트, **프로젝트스킬 상세 포함**
- **DB**: [DATABASE.md](./DATABASE.md) - 테이블 변경 시 즉시 업데이트

---

## 중요 참고사항

### 프로젝트별 코딩 규칙
- SQL, Gateway, 권한, UI, GDI+, 한글 인코딩 등 프로젝트 고유 규칙은 `/kRules_{project}`에서 on-demand 로드
- Skill('kO') 로딩 시 자동 안내됨

### TODO 파일 생명주기
생성 → 계획 → 추적 → 삭제 (.gitignore 포함)
