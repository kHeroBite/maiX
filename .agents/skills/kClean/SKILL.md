# kClean — 프로젝트 잔여물 일괄 정리

작업 완료 후 남은 임시 파일, 캐시, 작업 복사본 등을 체계적으로 정리하는 유틸리티 스킬.

> **수동 전용**: 사용자가 명시적으로 `/kClean` 또는 "잔여물 정리" 등을 요청할 때만 실행.
> Auto-activates 없음 — 자동 발동 절대 금지.

---

## 정리 대상 (7개 카테고리)

### 1. /tmp 임시 파일

```yaml
대상: /tmp/claude_* /tmp/ntfy.json
방법: rm -f
주의: 현재 세션 활성 파일은 보존 (pipeline_state가 IDLE일 때만 전체 삭제)
확인: ls /tmp/claude_* /tmp/ntfy.json 2>/dev/null
```

### 2. ext4 작업 디렉토리

```yaml
대상: ~/work/{project}-ntfs/ 내 모든 파일
방법: rm -rf ~/work/{project}-ntfs/*
주의: 미커밋 편집이 있으면 삭제 전 경고 (ext4 파일이 NTFS보다 최신인 경우)
확인: ls ~/work/{project}-ntfs/ 2>/dev/null
```

### 3. TODO 파일

```yaml
대상: 프로젝트 루트 TODO_*.md
방법: rm -f TODO_*.md
주의: .gitignore에 이미 포함됨 (untracked)
확인: ls TODO_*.md 2>/dev/null
```

### 4. scratchpad / 분석 결과 파일

```yaml
대상: scratchpad*.md, agent_*_result.md, *_analysis.md, *_result.md, *_audit*.md, *_count.md
방법: untracked이면 rm -f, tracked이면 사용자에게 보고 (자동 git rm 금지)
확인: git ls-files --error-unmatch {파일} 로 tracked 여부 확인
```

### 5. .claude/plans 자동생성 파일

```yaml
대상: .claude/plans/ 내 에이전트 자동생성 파일 (*-agent-*.md)
방법: rm -f
주의: 사용자가 직접 만든 plan 파일은 보존 (이름에 -agent-가 없으면 보존)
확인: ls .claude/plans/ 2>/dev/null
```

### 6. .serena/memories 오래된 plan

```yaml
대상: .serena/memories/plan_*.md, *_plan.md, *Analysis*.md
방법: 삭제 전 목록 출력 → 일괄 삭제
주의: code_style_conventions.md, database_schema.md, important_patterns.md, project_overview.md 등 핵심 memory는 절대 보존
보존_키워드: code_style, database_schema, important_patterns, project_overview, suggested_commands, task_completion
확인: ls .serena/memories/ 2>/dev/null
```

### 7. Lock 파일

```yaml
대상: {프로젝트}/.claude/locks/intent_*.json
방법: rm -f
주의: 활성 Lock(TTL 미만료)은 보존
확인: ls .claude/locks/ 2>/dev/null
```

---

## 실행 절차

```yaml
1_조사:
  - 7개 카테고리 동시 탐색 (병렬 Bash 호출)
  - 각 카테고리별 파일 수 + 총 용량 집계

2_보고:
  형식: |
    🧹 **잔여물 조사 결과**
    | 카테고리 | 파일 수 | 상태 |
    |---------|---------|------|
    | /tmp 임시 파일 | {N}개 | 삭제 대상 |
    | ext4 작업 복사본 | {N}개 | 삭제 대상 |
    | TODO 파일 | {N}개 | 삭제 대상 |
    | scratchpad/분석 | {N}개 | 삭제 대상 |
    | .claude/plans | {N}개 | 삭제 대상 |
    | .serena/memories | {N}개 | 삭제 대상 |
    | Lock 파일 | {N}개 | 삭제 대상 |

3_실행:
  - untracked 파일 즉시 삭제
  - tracked 파일은 git rm + 커밋
  - 커밋 메시지: "🧹 chore: 프로젝트 잔여물 일괄 정리"

4_결과:
  형식: |
    ✅ **정리 완료**
    삭제: {N}개 파일 ({size})
    커밋: {hash} (tracked 파일이 있었을 경우)
```

---

## 보존 규칙 (절대 삭제 금지)

```yaml
프로젝트_핵심_문서:
  - CLAUDE.md, PROJECT.md, ADVANCED.md, DATABASE.md, MCP.md, RESTAPI.md, HISTORY.md, LESSONS.md
  - .gitignore, Mars.sln, *.csproj

스킬_파일:
  - .claude/skills/**/SKILL.md (모든 스킬)

Serena_핵심_메모리:
  - .serena/memories/code_style_conventions.md
  - .serena/memories/database_schema.md
  - .serena/memories/important_patterns.md
  - .serena/memories/project_overview.md
  - .serena/memories/suggested_commands.md
  - .serena/memories/task_completion_workflow.md

소스_코드:
  - Mars/**/*.cs, MarsGW/**/*.cs, Mars.Shared/**/*.cs, Mars.Mobile/**/*.cs
  - 모든 tracked 소스 파일

설정_파일:
  - .claude/settings.json, .claude/hooks/*, dashboard_layout*.json
```

---

## 프로젝트 독립성

```yaml
원칙: 이 스킬은 100% 범용 — 프로젝트 고유 경로/설정 없음
project_ntfs_dir: ~/work/{project}-ntfs/ (kInfra_{project}에서 프로젝트명 자동 감지)
하드링크: AI ↔ Mars ↔ MaiX 3개 프로젝트 공유 대상
```
