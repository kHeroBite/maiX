---
name: kDone_skills
description: "스킬 파일 업데이트. 규칙 강화, 서브스킬 신규 생성."
---
# kDone_skills — 스킬 파일 업데이트

## 역할

작업 중 발견된 중요 패턴, 규칙, 교훈을 스킬 파일(.claude/skills/)에 직접 반영.

## 입력: /tmp/claude_review_actions.json

```yaml
소비_대상: actions 배열에서 target == "skill" 인 항목만
소비_절차:
  1. /tmp/claude_review_actions.json 읽기
  2. target == "skill" 항목 필터링
  3. 항목 없으면 "✅ kDone_skills: 스킬 수정 대상 없음" 출력 후 종료
  4. 항목 있으면 아래 업데이트 분류 및 수정 절차 실행
  5. 각 항목의 target_file, target_section, proposed_rule 참조하여 수정

판단_기준 (review에서 이미 결정됨):
  - hook으로 감지 불가능한 판단/맥락 기반 규칙
  - 스킬 규칙 강화로 예방 가능한 패턴
```

## 추가 트리거 (review JSON 외)

```yaml
트리거_조건 (OR — review JSON과 별도로 발동):
  - 작업 중 기존 스킬 규칙 위반 직접 발견 → 규칙 강화/명확화
  - 새로운 패턴/규칙 발견 → 해당 스킬에 추가
  - 스킬 간 정보 불일치/중복 발견 → 정합성 수정
  - 새 서브스킬/유틸리티 스킬 생성 필요

스킵_조건:
  - review JSON에 skill 항목 없고, 추가 트리거도 해당 없음
```

## 업데이트 분류 및 대상

```yaml
Level_1_참고 (스킬 미수정):
  - 1회성 이슈, 재발 가능성 낮음
  - LESSONS.md에만 기록 (kDone_review 담당)

Level_2_스킬_강화:
  - 기존 스킬의 규칙 명확화, 체크리스트 항목 추가
  - 예: kDone Gate에 새 체크 항목 추가
  - 예: kTest_build에 특정 빌드 옵션 추가
  - 해당 스킬 SKILL.md 직접 수정

Level_3_스킬_신규:
  - 새로운 서브스킬/유틸리티 스킬 필요
  - 예: 새 프로젝트스킬, 새 도메인 스킬
  - SKILL.md 생성 + kDone/SKILL.md 순서 반영 + CLAUDE.md 목록 추가
```

## 수정 대상 매핑

```yaml
스킬_파일_수정_시_연쇄_업데이트:
  서브스킬_수정:
    - 해당 SKILL.md 수정
    - 상위 메인스킬에 영향 있으면 메인스킬도 수정

  새_서브스킬_생성:
    - SKILL.md 생성 (.claude/skills/{스킬명}/SKILL.md)
    - 상위 메인스킬 SKILL.md에 실행 순서 반영
    - CLAUDE.md 서브스킬 목록에 추가
    - kO SKILL.md 서브스킬 목록에 추가

  메인스킬_수정:
    - 해당 메인스킬 SKILL.md 수정
    - CLAUDE.md에 영향 있으면 CLAUDE.md도 수정

Tier_준수 (절대 규칙):
  - 메인/서브스킬에 프로젝트 고유 경로/설정 금지
  - 프로젝트 고유 → 프로젝트스킬(_{project})에만 기재
```

## 수정 절차

```yaml
1_판단: 이번 작업에서 스킬 업데이트 필요 여부 확인
2_분류: Level 2(강화) 또는 Level 3(신규) 판정
3_수정:
  - NTFS rsync 방식 준수 (cp → Edit → rsync)
  - Tier 분리 원칙 준수
4_연쇄: 상위 스킬/CLAUDE.md 연쇄 업데이트 필요 시 수행
5_검증: 수정된 스킬 파일이 기존 스킬과 충돌/중복 없는지 확인
```

## 절대 금지

- kDone_docs 대상(HISTORY/PROJECT/DATABASE.md) 수정 (역할 분리)
- 스킬 파일에 프로젝트 고유 정보 기재 (Tier 위반)
- 유일 출처 원칙 위반 — 동일 규칙을 여러 스킬에 중복 기재

---

## 재발방지 판단 기준 — Skills vs Hooks

재발방지가 필요한 경우, **Skills와 Hooks 중 하나만 선택**한다 (중복 금지).

```yaml
판단_흐름:
  1. hook으로 차단 가능한가? (도구 호출 시점에서 입력값/상태로 판별 가능)
     → YES: kDone_hooks만 수행 (hook이 차단 + 안내 메시지를 겸함)
     → NO: 2번으로

  2. 스킬 규칙 강화로 예방 가능한가? (사전 인지로 실수 방지)
     → YES: kDone_skills만 수행
     → NO: kDone_docs에 교훈 기록만 (1회성 이슈)

핵심_원칙:
  - hook 차단 가능하면 스킬 중복 기재 금지 (hook이 강제 차단 + 안내를 겸함)
  - 스킬은 "hook으로 감지 불가능한" 판단/맥락 기반 규칙에만 사용
  - kDone_docs (MEMORY.md)는 hook/스킬의 존재 자체를 기록하는 용도로 항상 수행

예시:
  MCP_MySQL_한글 (L-024):
    - hook 가능 (SQL 텍스트에서 한글 감지) → kDone_hooks만 수행
    - 스킬에 "한글 금지" 중복 기재 불필요

  배치_빌드_3개_규칙:
    - hook 불가 (수정 횟수 추적은 복잡/부정확) → kDone_skills로 규칙 명시

  1회성_타이포:
    - hook/스킬 모두 불필요 → kDone_docs에 참고 기록만
```
