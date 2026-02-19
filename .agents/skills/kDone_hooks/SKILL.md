---
name: kDone_hooks
description: "반복 위반 hook 강제화. 물리 차단으로 위반율 0% 달성."
---
# kDone_hooks — 반복 위반 hook 강제화

## 역할

Review 단계에서 발견된 반복 위반을 **hook 스크립트로 물리적 차단**으로 승격합니다.
"텍스트 규칙으로 N회 위반" → "hook으로 0회 위반"으로 전환하는 자기 개선 루프.

## 입력: /tmp/claude_review_actions.json

```yaml
소비_대상: actions 배열에서 target == "hook" 인 항목만
소비_절차:
  1. /tmp/claude_review_actions.json 읽기
  2. target == "hook" 항목 필터링
  3. 항목 없으면 "✅ kDone_hooks: hook 대상 없음" 출력 후 종료
  4. 항목 있으면 아래 구현 절차 실행

판단_기준 (review에서 이미 결정됨):
  - hook으로 물리 차단 가능 (도구 호출 시점에서 감지 가능)
  - 반복 횟수 무관: hook 가능하면 1회 위반이라도 즉시 hook 생성
```

## Hook 설계 원칙

```yaml
3-Layer 준수:
  - Layer 1 (Hooks): 물리적 차단. AI 재량 0%
  - Hook은 "규칙을 강제"하는 것이지 "가이드"하는 것이 아님
  - 차단 시 명확한 에러 메시지 필수 (왜 차단되었는지 + 어떻게 해결하는지)

Hook 유형:
  PreToolUse: 도구 호출 전 차단 (가장 효과적)
    - matcher: 차단할 도구명 (Edit|Write|Bash|Skill 등)
    - stdin: JSON (tool_input 포함)
    - block: stderr에 {"decision":"block","reason":"..."} + exit 2
  PostToolUse: 도구 호출 후 감시 (증거 수집용)
    - matcher: 감시할 도구명
    - stdin: JSON (stdout, tool_input 포함)

차단 출력 형식:
  echo '{"decision":"block","reason":"❌ [사유]"}' >&2
  exit 2
```

## 구현 절차

```yaml
1_review_JSON_읽기:
  - /tmp/claude_review_actions.json에서 target == "hook" 항목 추출
  - 각 항목의 target_file, proposed_rule 참조

2_기존_Hook_확인:
  - ls ~/.claude/hooks/ 로 현재 hook 목록 확인
  - 기존 hook에 조건 추가로 해결 가능한지 판단
  - 가능하면 기존 hook 수정 (신규 hook 최소화)

3_Hook_스크립트_작성:
  - 경로: ~/.claude/hooks/{descriptive_name}.sh
  - 실행 권한: chmod +x 필수
  - 🚨 CRLF 제거 필수: sed -i 's/\r$//' (L-020)
  - 테스트: echo '{"tool_input":{...}}' | bash hook.sh 2>&1

4_settings.json_등록:
  - ~/.claude/settings.json의 hooks 섹션에 추가
  - matcher 패턴 정확히 설정
  - timeout: 5 (기본)

5_검증:
  - 의도적 위반 시도 → 차단 확인
  - 정상 동작 시도 → 통과 확인
  - 유틸리티 스킬 등 예외 케이스 확인
```

## 기존 Hook 목록 (참조)

```yaml
pipeline_gate.sh: 파이프라인 상태 전이 강제 (PreToolUse:Skill)
ko_check.sh: kO 미발동 시 파일 수정 차단 (PreToolUse:Edit|Write|Serena)
evidence_gate.sh: 테스트 증거 자동 수집 (PostToolUse:Bash)
phase_guard.sh: 다단계 계획 자동 계속 (PostToolUse:Skill)
mysql_korean_guard.sh: MCP MySQL SQL 한글 차단 (PreToolUse:mcp__mysql__query|execute) (L-024)
ko_reset.sh: 사용자 메시지 시 상태 리셋 (UserPromptSubmit)
compact_save.sh: compact 전 상태 저장 (PreCompact)
compact_restore.sh: compact 후 상태 복원 (SessionStart:compact)
```

## 사례

```yaml
사례_1_NTFS_직접_Edit:
  위반: /mnt/c/ 경로에 Edit 도구 직접 사용 (3회 반복)
  hook: ko_check.sh에 경로 검사 추가
  차단: "❌ NTFS 직접 Edit 금지. rsync 방식 사용하세요"

사례_2_kTest_누락:
  위반: kTest_build만 완료 후 kDone 진입 (5회 반복)
  hook: pipeline_gate.sh에 증거 파일 4개 검증 추가
  차단: "❌ kTest 증거 미완료: deploy run quality"

사례_3_파이프라인_점프:
  위반: IDLE에서 kDev 직접 호출 (5회 반복)
  hook: pipeline_gate.sh 상태 전이 규칙
  차단: "❌ kDev는 PLAN/TEST 상태에서만 호출 가능"

사례_4_MCP_MySQL_한글 (L-024):
  위반: SELECT alias에 한글 사용 → latin1에서 구문 오류 (1회)
  hook: mysql_korean_guard.sh — SQL 텍스트에서 한글 감지
  차단: "BLOCKED: MCP MySQL SQL에 한글이 포함되어 있습니다"
  판단: hook 가능 → 스킬 중복 기재 불필요
```

## 절대 금지

- Hook에서 복잡한 비즈니스 로직 구현 (hook은 단순 gate만)
- Hook timeout 5초 초과 (차단 지연 → UX 저하)
- 기존 hook 삭제 (비활성화만 허용 — settings.json에서 제거)
- CRLF 미제거 상태로 배포 (L-020)
