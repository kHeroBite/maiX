# CLAUDE.md - AI Agent 프로젝트 매뉴얼

> **역할**: Claude Code가 프로젝트에서 작업할 때 참조하는 핵심 지침서

---

## 문서/스킬 3-tier 구조 (최우선 원칙)

```
┌─────────────────────────────────────────────────────────────────┐
│  Tier 1: CLAUDE.md (이 파일) — 범용 규칙                         │
│  ● 모든 프로젝트에 공통 적용되는 규칙/정책/프로세스              │
│  ● 스킬 전체 목록 및 설명 (범용)                                  │
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

---

## 언어 정책

**필수**: 모든 대화/주석/변수명/함수명/클래스명/문서 = 한국어. Git 커밋 = 한국어 + 이모지. [COMPACT]
**커밋 모델 태그**: 제목 마지막에 `by {모델버전}` 추가 (예: `✨ 기능 추가 by claude-sonnet-4-6`). 접두어 금지. [COMPACT]
**예외**: 기술 용어(REST API, MCP 등), 라이브러리명은 영어 허용.
**중요**: /compact 후에도 한국어 유지 필수. CLAUDE.md 재읽기 필수. 새 세션도 한국어로 시작.

### 인코딩 규칙 (핵심만)

- **PowerShell UTF-8 (L-148)**: `Out-File`/`Set-Content -Encoding UTF8` 절대 금지 (BOM 강제). `[System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))` 사용. [COMPACT]
- **CRLF**: NTFS 프로젝트 파일은 CRLF 유지. `.sh` Write 후 `sed -i 's/\r//' 파일` 필수.
- **BOM**: 신규 파일 UTF-8 without BOM. 기존 파일 cp 바이너리 복사로 자동 보존.
- **Git**: `i18n.commitEncoding = utf-8` 유지.
> 상세(PowerShell/CRLF 전체 절차): domain-fileops SKILL.md 참조

### UI/Designer 규칙

- UI 컨트롤 추가/배치는 `*.Designer.cs`에서만. 코드에서 `new Control()` 직접 생성 금지.
- 이벤트 바인딩도 Designer에서 처리.


---

## 실행 환경: WSL2 + Windows

Claude Code는 WSL2에서 실행되며, 프로젝트 파일은 Windows NTFS(`/mnt/c/`)에 위치.

```yaml
기본_환경: WSL2 (Linux)

WSL에서_실행 (기본):
  - Bash 명령어, dotnet build, curl, MCP 도구
  - Read/Glob/Grep (읽기는 정상 작동)
  - Serena 코드 분석/편집

도구_정책 (WSL 최우선):
  - WSL에서 최대한 리눅스 도구 사용, Windows 도구(.exe)는 최소화
  - Windows 도구 사용 시: 반드시 사유 명시 + Fallback으로만 사용

파일시스템_변경 (fio MCP 독점 — 절대 규칙):
  - 모든 파일/디렉토리 변경은 반드시 fio MCP 경유 (유일한 변경 경로)
    - 파일: mcp__fio__file_edit / file_write / file_delete / file_rename / file_move / file_copy
    - 디렉토리: mcp__fio__dir_create / dir_delete / dir_move / dir_rename
    - 모든 경로 동일 처리 (NTFS/EXT4 구분 없음 — fio가 내부에서 자동 처리)
    - BOM/CRLF: fio 내부에서 자동 보존
    - Intent Lock: fio 내부에서 세션 간 충돌 방지 (SQLite FIFO 대기열)
    - File Lock: fio 내부에서 자동 acquire/release (try/finally)
  - C# 심볼 수정: Serena (replace_symbol_body / insert_after_symbol) — LSP 경유
  - Fallback: fio/Serena 모두 불가 시에만 Claude Code Edit
  - Bash 파일시스템 변경 명령 금지: mkdir/rmdir/rm/mv/cp/touch/sed -i/rsync 등 → fio 도구 사용
    - 예외: git, dotnet/npm 빌드, /tmp/ 경로, session-env 상태파일
  - Edit/Write 도구 직접 사용 금지 → fio 사용
  - 위반_감지: write_guard.sh가 Edit/Write + Bash 변경 명령 모두 차단

```

---

## 관련 문서

| 문서 | 내용 | 범위 |
|------|------|------|
| **[HISTORY.md](./HISTORY.md)** | 버그 수정 및 작업 이력 | 프로젝트 고유 |
| **[LESSONS.md](./LESSONS.md)** | 자기 개선 교훈 로그 | 프로젝트 고유 |

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

## k시리즈 파이프라인 프로세스 (절대 규칙)

사용자가 k시리즈 스킬(/ko, /k1, /k2, /k3, /k4, /k5, /kplan, /kplan_dual, /kdev, /ktest, /kdone 등)을 **명시적으로 요구**하면:
- 해당 스킬의 프로세스를 **예외 없이 100% 따라야** 함
- "요구사항이 명확하니 스킬 프로세스 생략" 금지
- "이미 답을 알고 있으니 바로 구현" 금지
- 스킬이 토론(kplan_dual)을 요구하면 반드시 토론 수행
- 스킬이 팀에이전트 spawn을 요구하면 반드시 spawn

**위반 시**: 사용자 신뢰 손상. 스킬 프로세스는 품질 보증 절차이며 생략 불가.

코드 수정 작업 분류 (5-tier, 7-way):
- k1 (Instant): 비코드만 또는 코드 ≤2개, ≤20줄 → kdev → kfinish
- k2 (Simple): 코드 1~3개, 20~50줄 → kplan_simple → kdev → kpop → kfinish
- k3 (Normal): 코드 4개+ 또는 50~500줄 또는 DB변경/인터페이스변경/비즈니스로직 → kplan_normal → kdev → ktest → kdone → kfinish
- k4 (Heavy): 500줄+ 또는 아키텍처 변경 → kplan_deep → kdev → ktest → kdone → kfinish
- k5 (Massive): 1500줄+ 또는 전면 변경 → kplan_dual → kdev → ktest → kdone → kfinish
- 사용자가 `/ko`로 직접 호출 시: 자동 7-way 분류 (질문/계획/k1/k2/k3/k4/k5)
- 레거시: /kq→k1, /kl→k2, /km→k3, /kf→k4 (deprecated, 3개월 후 제거)

---

## MCP 서버

### 기본 ON (3개)
| 서버 | 용도 | 비고 |
|------|------|------|
| **mysql** | DB 작업 | 한글 INSERT/UPDATE 금지 |
| **serena** | 코드 분석/편집 | claude-code context |
| **context7** | 라이브러리 문서 | 무료 |

### MCP 공통 지침

- 기본 서버는 위 표의 3개를 항상 활성화하고, 대규모 코드 분석이 필요할 때만 `serena-full`을 추가 연결합니다.
- DB 작업은 `mcp__mysql__*` 도구를 우선 사용하며, 한글이 포함된 쿼리를 실행할 때는 세션마다 `SET NAMES utf8`을 먼저 호출합니다.
- MCP 호출은 각 도구를 직접 실행하며 Serena의 `execute_shell_command`로 다른 MCP를 우회 호출하지 않습니다.
- 코드 수정 도구 선택: `.cs` 파일은 Serena, Designer/문서/JSON/YAML 파일은 Claude Code Edit/Write를 기본으로 사용합니다.

### MCP MySQL 역할 분리 규칙
> → ko SKILL.md "MCP_MySQL_역할_분리" 섹션 참조 (유일 출처)

### MCP MySQL 한글 인코딩 규칙
> → domain-database SKILL.md "UTF-8 인코딩 필수 정책" 섹션 참조 (유일 출처)

### MCP 호출 규칙
- 각 MCP 도구를 직접 호출. Serena의 `execute_shell_command`로 다른 MCP 호출 금지.

### 코드 수정 도구 선택

| 상황 | 도구 |
|------|------|
| C# 코드 심볼 수정/추가/리팩토링 | Serena |
| Designer.cs, 비코드 파일, 주석/문자열 | Claude Code Edit |
| Serena 오류 시 | Claude Code Edit (Fallback) |

