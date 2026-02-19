---
name: kDone_review
description: "프로세스 개선 점검. 교훈 분석 + 조치 방향 결정 → /tmp/claude_review_actions.json 출력."
---
# kDone_review — 프로세스 개선 (분석 전담)

매 작업 완료 시 프로세스 개선점을 점검하고, 조치 방향을 JSON으로 출력.
**review는 분석만 담당** — 실제 반영은 후속 스킬(hooks → skills → docs)이 수행.

## 🚨 핵심 원칙: review = 분석 + 분류만

```yaml
역할: 교훈 발견 → 5W1H 분석 → Level/조치방향 결정 → JSON 출력
금지:
  - LESSONS.md/MEMORY.md/CLAUDE.md 직접 수정
  - 스킬 파일 직접 수정
  - Hook 스크립트 직접 생성/수정
이유: 실제 반영은 교훈 반영 파이프라인(hooks → skills → docs)이 전담
```

## Step 0: 이전 교훈 미반영 점검 (Step 1 전)

```yaml
점검_절차:
  1. LESSONS.md 하단 반영 추적 테이블 확인
  2. ⬜(미확인) 항목 존재 시:
     - 해당 항목을 /tmp/claude_review_actions.json의 pending_items에 포함
  3. 미확인 항목 없으면 즉시 Step 1로 진행

소요_시간: 최대 30초 (테이블 존재 여부 확인만)
```

## Step 0.5: 오류 추적 (3-소스 수집)

```yaml
목적: 세션 중 발생한 모든 오류를 빠짐없이 수집하여 Step 1 점검의 입력으로 활용

오류_3분류:
  소스_A (자동감지): hook 차단 로그 — 시스템이 자동 기록
  소스_B (자동감지): Bash 실패 — 트랜스크립트에서 exit code 스캔
  소스_C (사용자지적): 사용자 피드백 — 모델이 스스로 감지 못한 오류 (가장 중요)

⚠️ 알려진_제약 (L-040):
  - Claude Code PostToolUse hook은 Bash 도구 실패(exit code != 0) 시 호출되지 않음
  - 따라서 error_tracker.sh는 Bash 에러(cp 실패, ls 실패 등)를 감지할 수 없음
  - 보완: 소스 B(트랜스크립트 스캔)로 누락된 Bash 에러를 반드시 수집

소스_A: error_tracker.sh 로그 (PostToolUse hook — 성공한 도구에서 감지한 오류)
  파일: /tmp/claude_errors_{kOID}.md (kOID는 /tmp/claude_current_koid에서 읽기)
  절차:
    1. kOID 읽기: cat /tmp/claude_current_koid
    2. error md 파일 존재 여부 확인: /tmp/claude_errors_{kOID}.md
    3. 존재 시: 전체 내용 읽기 → 미해결/해결됨 항목 분류
    4. 미해결 항목: Step 1의 "1_도구_오류" 점검에 자동 포함 (YES 처리)
    5. 해결됨 항목: 오류→해결 쌍으로 묶어 Step 2 분석 대상에 포함

소스_B: 트랜스크립트 에러 스캔 (Bash 실패 보완)
  → kDone_trans로 분리됨. kDone_trans가 트랜스크립트 스캔 수행.

소스_C: 사용자 피드백 (모델 미감지 오류 — 최우선, 2중 메커니즘)
  중요도: 소스 A/B보다 높음 — 모델이 스스로 감지 못한 오류이므로 교훈 가치 최대
  원칙: 사용자의 모든 지적/수정 요청은 반드시 error md에 기록되어야 함

  메커니즘_1_실시간_기록 (kO 시점 — 발생 즉시):
    시점: kO가 사용자 메시지를 분류할 때
    조건: 사용자 메시지가 아래 피드백 패턴에 해당
    동작: error_log_lib.sh의 log_hook_error 호출하여 error md에 즉시 기록
      category: USER_FEEDBACK
      tool_name: 해당 작업 단계 (kO/kDev/kTest 등)
      reason: 사용자 원문 + 모델이 한 행동 요약
    ID_형식: UFBK-N (User FeedBacK)
    효과: error md에 실시간 누적 → kDone_review에서 자동 수집됨

  메커니즘_2_사후_보완_스캔 (kDone_review 시점 — 누락 방지):
    시점: kDone_review Step 0.5
    파일: 현재 세션의 transcript_path (.jsonl)
    목적: 메커니즘 1에서 누락된 피드백 보완 수집
    절차:
      1. 트랜스크립트에서 사용자 메시지(type: "human") 추출
      2. 피드백 패턴 매칭으로 지적 항목 식별
      3. error md에 이미 UFBK로 기록된 항목은 스킵 (중복 제거)
      4. 누락 항목만 UFBK-N으로 추가

  감지_패턴:
    불만/지적: "다시", "또", "잘못", "왜 안", "아니", "그게 아니", "안 됐", "빠졌", "누락"
    수정_요청: "~하지 말고", "~로 바꿔", "이미 말했", "방금 말한", "아까"
    반복_지시: 동일 요청 2회+ (첫 번째 실행이 불충분했다는 의미)
  제외_패턴:
    - 순수 추가 요청: "추가로 ~도 해라" (불만이 아닌 새 요구)
    - 질문: "이거 뭐야?", "~인가?" (정보 요청)
    - 긍정 피드백: "좋다", "됐다", "오케이"
  분석_항목 (각 UFBK):
    what: 사용자가 지적한 내용 (원문 인용)
    model_action: 지적 직전 모델이 한 행동
    root_cause: 왜 모델이 잘못했는가 (판단오류/규칙미숙지/정보부족 등)
    severity: 높음(2회+ 반복지적) / 중간(1회 지적, 핵심기능) / 낮음(1회 지적, 사소)

통합_절차:
  1. 소스 A 수집 (error_tracker.sh 로그)
  2. 소스 B 수집 (트랜스크립트 스캔)
  3. 소스 C 수집 (사용자 피드백 스캔)
  4. 중복 제거 후 통합
  5. /tmp/claude_review_actions.json의 error_log 필드에 포함
  6. 오류 0건이면 즉시 Step 1로 진행

활용:
  미해결_항목: "아직 해결 안 된 오류가 있다" → 심각도 높음
  해결됨_항목: "오류 발생 → 해결 패턴" → 재발방지 교훈 후보
  오류_없음: Step 0.5 즉시 완료, Step 1로 진행

error_log_JSON_필드:
  {
    "error_log_file": "/tmp/claude_errors_{kOID}.md",
    "transcript_errors": 2,
    "user_feedback_errors": 1,
    "total_errors": 6,
    "unresolved": 1,
    "resolved": 5,
    "errors": [
      {
        "id": "ERR-1",
        "source": "hook",
        "category": "BUILD_FAILED",
        "status": "해결됨",
        "resolution": "빌드 오류 수정 후 재빌드 성공"
      },
      {
        "id": "TERR-1",
        "source": "transcript",
        "category": "FILE_NOT_FOUND",
        "command": "cp HISTORY.md ...",
        "status": "미해결",
        "detail": "No such file or directory"
      },
      {
        "id": "UFBK-1",
        "source": "user_feedback",
        "category": "USER_FEEDBACK",
        "user_said": "아니 그게 아니라 ~해야지",
        "model_action": "사용자 요청을 잘못 해석하여 다른 파일 수정",
        "root_cause": "요구사항 해석 오류",
        "status": "해결됨",
        "severity": "중간"
      }
    ]
  }

세션_종료_시: kDone_git에서 커밋 후 push 전에 error log 포함 모든 세션 임시파일 일괄 삭제 (kDone_git "세션 임시파일 삭제" 섹션 참조)
```

## Step 1: 자동 점검 (항상 실행)

```yaml
점검_항목:
  1_도구_오류: "Serena/MCP/Build 등 도구에서 예상치 못한 오류가 발생했는가?"
    → Step 0.5 소스 A(hook 로그) + 소스 B(트랜스크립트) 양쪽 결과를 통합 반영
  2_프로세스_위반: "kDev 배치 빌드, 병렬화 정책, 파일 수정 절차 등을 위반했는가?"
  3_사전_분석_누락: "DB 스키마, 기존 코드 구조 등 사전 확인 없이 작업을 진행했는가?"
  4_병렬화_기회_놓침: "병렬화 조건 충족했으나 순차 처리했는가?"

판정:
  모두_아니오:
    출력: "✅ kDone_review: 문제 없음"
    → /tmp/claude_review_actions.json에 빈 actions 배열 저장
    → 종료
  하나라도_예:
    → Step 2 진행
```

## Step 2: 상세 분석

```yaml
분석_절차:
  1. 5W1H_분석:
    - What: 무엇이 잘못되었는가?
    - Why: 근본 원인은? (필요 시 5 Whys)
    - When: 어느 단계에서 발생했는가?
    - Where: 어떤 파일/도구에서?
    - Who: 어떤 판단이 원인이었는가?
    - How: 어떻게 방지할 수 있는가?

  2. 심각도_판단:
    낮음: 1회 발생, 비정형, 특수 상황
    중간: 도구/환경 제한으로 재발 가능성 높음
    높음: 2회+ 반복 발생, 규칙화 가능

  3. Level_결정 + 조치_방향_결정: → Step 3으로
```

## Step 3: 조치 방향 결정 + JSON 출력

### Level 분류 기준

| Level | 조치 방향 | 강도 | 판단 기준 | 담당 스킬 |
|-------|-----------|------|-----------|-----------|
| 1 | LESSONS.md 기록 | 참고용 | 1회, 비정형, 규칙화 어려움 | kDone_docs |
| 2 | MEMORY.md + LESSONS.md | 매 세션 인지 | 도구/환경 제한, 재발 높음 | kDone_docs |
| 3-hook | Hook 물리 차단 | 강제 | hook 감지 가능 + 반복 위반 | kDone_hooks |
| 3-skill | 스킬 규칙 강화 | 강제 | hook 불가 + 판단 기반 규칙 | kDone_skills |

### 조치 방향 판단 흐름 (Level 3)

```yaml
판단_흐름:
  1. hook으로 차단 가능한가? (도구 호출 시점에서 입력값/상태로 판별 가능)
     → YES: target = "hook" → kDone_hooks가 처리
     → NO: 2번으로

  2. 스킬 규칙 강화로 예방 가능한가? (사전 인지로 실수 방지)
     → YES: target = "skill" → kDone_skills가 처리
     → NO: Level 1 또는 2로 하향 → kDone_docs가 처리

핵심: hook과 skill은 중복 금지 (하나만 선택)
```

### JSON 출력 형식

```yaml
파일: /tmp/claude_review_actions.json
생성: Step 1 완료 후 항상 생성 (문제 없으면 빈 배열)

구조:
  {
    "timestamp": "2026-02-15T22:00:00",
    "summary": "N건 발견",
    "pending_items": [...],   // Step 0에서 발견한 미반영 항목
    "error_log": {            // Step 0.5에서 수집한 오류 추적 로그
      "file": "/tmp/claude_errors_XXXXXXXX.md",
      "total": 3,
      "unresolved": 1,
      "resolved": 2,
      "errors": [...]
    },
    "actions": [
      {
        "id": "L-035",
        "what": "kTest_run 절차 생략",
        "why": "빌드 성공 후 바로 kDone 진입",
        "severity": "높음",
        "level": 3,
        "target": "hook",     // "hook" | "skill" | "docs"
        "target_file": "pipeline_gate.sh",
        "target_section": "kTest 증거 검증",
        "proposed_rule": "kTest 3 Phase 증거 파일 모두 존재해야 kDone 진입 허용",
        "lessons_entry": "L-035: kTest_run 절차 생략 금지 — REST API/스크린샷/로그 전부 검증 필수"
      }
    ]
  }

target 값별 담당:
  "hook"  → kDone_hooks가 소비 (hook 생성/수정)
  "skill" → kDone_skills가 소비 (스킬 규칙 강화/추가)
  "docs"  → kDone_docs가 소비 (LESSONS/MEMORY 기록)
  모든_항목: kDone_docs가 LESSONS.md에 기록 (target 무관)
```

## 출력 형식

```
📋 kDone_review 결과:
- 발견 건수: N건
- [Level X → target] 항목명: 간단 설명
- JSON 저장: /tmp/claude_review_actions.json
```

문제 없을 경우:
```
✅ kDone_review: 문제 없음 (빈 actions 저장됨)
```
