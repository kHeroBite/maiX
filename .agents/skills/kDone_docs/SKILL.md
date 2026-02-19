---
name: kDone_docs
description: "프로젝트 문서 업데이트. 교훈 기록(LESSONS/MEMORY) + HISTORY/PROJECT/DATABASE/CLAUDE 점검."
---
# kDone_docs — 프로젝트 문서 업데이트 + 교훈 기록

## 입력: /tmp/claude_review_actions.json

```yaml
소비_대상: actions 배열의 **모든 항목** (target 무관)
역할:
  1. 교훈_기록: 모든 교훈을 LESSONS.md에 기록 (target이 hook/skill이어도)
  2. Level_2_메모리: level == 2 항목은 MEMORY.md에도 기록
  3. 반영_추적: Level 3 항목은 LESSONS.md 반영 추적 테이블에 기록
  4. 프로젝트_문서: 기존 HISTORY/PROJECT/DATABASE/CLAUDE.md 업데이트

절차:
  1. /tmp/claude_review_actions.json 읽기
  2. 교훈 기록 (아래 "교훈 기록 절차" 참조)
  3. 프로젝트 문서 업데이트 (아래 "필수 점검 대상" 참조)
```

## 에러 기반 재발방지 (review JSON의 error_log 활용)

```yaml
입력: /tmp/claude_review_actions.json의 error_log 필드
목적: 세션 중 발생한 hook 차단/도구 오류에서 재발 패턴을 발견하고 교훈화

절차:
  1. error_log.errors 배열 확인
  2. 각 에러의 category별 재발 패턴 분석:
     - HOOK_BLOCK_* → 모델이 규칙을 위반한 패턴 → Level 2+ 교훈
     - PIPELINE_BLOCK_* → 파이프라인 순서 위반 → Level 1 교훈 (이미 hook이 차단)
     - BUILD_FAILED → 코드 오류 → Level 1 (빌드 오류는 수정으로 해결)
     - FILE_NOT_FOUND → 경로/파일 실수 → Level 1~2
  3. 해결됨 에러: "오류→해결" 쌍을 교훈으로 기록 (재발방지)
  4. 미해결 에러: 심각도 높음 → Level 2+ 필수, kDone_hooks/skills에서 추가 조치
  5. 동일 category 2회+ 반복: 자동 Level 3 승격 (hook/skill 강화 대상)

교훈_기록_형식:
  LESSONS.md: "### L-NNN: [category 요약] — [재발방지 방법] (날짜)"
  MEMORY.md: Level 2+ 항목만 (매 세션 시작 시 자동 인지)
```

## 교훈 기록 절차

```yaml
모든_항목 → LESSONS.md:
  - actions 배열의 모든 항목을 LESSONS.md에 추가
  - error_log의 에러 기반 교훈도 함께 기록
  - 형식: "### L-NNN: [what] (날짜)"
  - lessons_entry 필드 참조

Level_2_항목 → MEMORY.md 추가:
  - level == 2 항목은 MEMORY.md 해당 섹션에 주의사항 추가
  - 효과: 매 세션 시작 시 자동 인지

Level_3_항목 → 반영 추적 테이블:
  - LESSONS.md 하단 반영 추적 테이블에 기록
  - 테이블_형식:
    | 교훈 ID | 교훈 요약 | 반영 대상 | 반영 위치 | 반영일 | 검증 |
    |---------|-----------|-----------|-----------|--------|------|
  - target == "hook"이면 kDone_hooks에서 이미 반영됨 → 검증 ✅
  - target == "skill"이면 kDone_skills에서 이미 반영됨 → 검증 ✅

pending_items 처리:
  - review가 발견한 이전 미반영 항목
  - kDone_hooks/kDone_skills에서 이미 반영되었으면 → ✅로 변경
  - 아직 미반영이면 → 이번 세션의 actions에 추가되어 처리됨
```

## 필수 점검 대상 (매 작업마다)

```yaml
HISTORY.md:
  조건: 항상 (매 작업)
  내용: 날짜, 작업 내용, 변경 파일, 커밋 해시

PROJECT.md:
  조건: 파일 추가/삭제/변경 시
  내용: 파일 인벤토리 업데이트

DATABASE.md:
  조건: DB 스키마 변경 시
  내용: 테이블/컬럼 추가/삭제 반영

CLAUDE.md:
  조건: 프로세스/규칙 변경 시
  내용: 해당 섹션 업데이트
```

## 점검 체크리스트

```yaml
- [ ] 에러: review JSON의 error_log 에러 기반 재발방지 교훈 기록됨
- [ ] 교훈: review JSON의 모든 actions → LESSONS.md 기록됨
- [ ] 교훈: Level 2 항목 → MEMORY.md 기록됨
- [ ] 교훈: Level 3 항목 → 반영 추적 테이블 기록됨
- [ ] HISTORY.md에 작업 이력 추가됨 (필수, 매번)
- [ ] PROJECT.md 파일 인벤토리가 현재 상태와 일치함
- [ ] DB 변경이 있었으면 DATABASE.md 업데이트됨
- [ ] 프로세스/규칙 변경이 있었으면 CLAUDE.md 업데이트됨
```

## 병렬 업데이트 (대규모 시)

```yaml
Case_A_단순 (1-2개 문서):
  Sonnet 메인 직접 수정

Case_B_대규모 (3개+ 문서):
  Task(Sonnet) #1: PROJECT.md + HISTORY.md
  Task(Sonnet) #2: DATABASE.md + CLAUDE.md + LESSONS.md + MEMORY.md
```

## 절대 금지

- HISTORY.md 이력 추가 없이 마무리 완료
- 파일 추가/삭제 후 PROJECT.md 미반영
- DB 변경 후 DATABASE.md 미반영
- review JSON의 교훈을 LESSONS.md에 기록하지 않고 넘어감
