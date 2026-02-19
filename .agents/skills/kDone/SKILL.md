---
name: kDone
description: "작업 마무리. 테스트 통과 후 실행. Auto-activates when: all tests passed, finalization needed."
---
# kDone — 작업 마무리

## 🚨 전제조건 (L-008)

kTest 전체 통과 완료 필수 (Phase 1+2+3). 파이프라인 순서 상세: kO 참조.

## 서브스킬 실행 순서 (전체 완료 필수)

```
REQUIRED: /kDone_review → /kDone_trans → /kDone_hooks → /kDone_skills → /kDone_cleanup → /kDone_docs → /kDone_git → /kDone_notify
⛔ kDone_notify까지 완료해야 "작업 완료" 선언 가능

교훈 반영 파이프라인 (review → trans → hooks → skills → docs):
  kDone_review: 교훈 분석 + 조치 방향 결정 → /tmp/claude_review_actions.json 저장
  kDone_trans:  트랜스크립트 우회 패턴 감지 → error md + review_actions.json 보강
  kDone_hooks:  review 결과 중 hook 필요 항목 → hook 생성/수정 (물리 차단 최우선)
  kDone_skills: review 결과 중 스킬 수정 필요 → 스킬 규칙 강화/추가
  kDone_docs:   review 결과 중 문서 기록 + HISTORY/PROJECT/DATABASE 업데이트

강제 순서 근거:
  review 먼저: 교훈 분석 + 조치 방향 JSON 생성
  trans 다음: 트랜스크립트 스캔하여 우회 패턴 추가 감지
  hooks 다음: 물리 차단이 최우선 (다음 작업부터 즉시 방어)
  skills 다음: hook 불가능한 판단 기반 규칙을 스킬에 반영
  docs 마지막: 위 조치들의 결과를 LESSONS/MEMORY/HISTORY에 기록
```

## 🚨 kDone 강제 완주 규칙 (절대 규칙)

```yaml
원칙: kDev/kTest 완료 후 kDone을 반드시 끝까지 완주해야 "작업 완료"
금지:
  - kTest 통과 후 kDone 진입 없이 세션 종료
  - kDone 일부 서브스킬만 실행 후 중단
  - "빌드 성공했으므로 완료" 선언 (kDone 미완주)
  - 커밋/푸시 없이 "작업 완료" 응답

kDone_미완주_방지:
  체크포인트: kTest 통과 직후 "kDone 진입" 명시적 선언
  검증: kDone_notify 실행 완료 여부로 판단 (타이머 정지 신호)
  위반_시: 사용자에게 "kDone 미완주" 경고 후 즉시 재개

컨텍스트_부족_시:
  1. kDone 서브스킬 진행 중 컨텍스트 20% 이하 감지
  2. 즉시 /compact 실행
  3. compact 후 파이프라인 위치 재확인 ("kDone 진행 중, 다음: kDone_XXX")
  4. 남은 서브스킬부터 이어서 실행
  5. kDone_notify까지 반드시 완료
  금지: compact 후 kDone 위치를 잊고 새 작업 시작

세션_종료_임박_시:
  규칙: 컨텍스트가 극도로 부족하더라도 최소 kDone_git(커밋/푸시)는 완료
  최소_보장: kDone_git → kDone_notify (이 2개는 스킵 절대 금지)
  이유: 구현+테스트 완료 후 커밋 없이 세션 종료 = 모든 작업 소실
```

## 🚨 교훈 단계 강제 실행 (절대 규칙 — L-042)

> **질문/탐색 외 모든 분류에서 교훈 단계(kDone_review → kDone_docs) 필수 실행.**

```yaml
적용_범위:
  필수: 퀵, 라이트, 미디엄, 풀 — 모든 코드 수정 작업
  예외: 질문/탐색만 (파이프라인 미진입 → kDone 자체 미실행)

강제_대상_서브스킬:
  - kDone_review: 교훈 분석 + 조치 방향 결정 → /tmp/claude_review_actions.json
  - kDone_hooks: review 결과 중 hook 필요 항목 처리
  - kDone_skills: review 결과 중 스킬 수정 필요 항목 처리
  - kDone_docs: review 결과 문서 기록 + HISTORY/PROJECT/DATABASE 업데이트

금지:
  - "퀵이라서 교훈 없음" → 퀵도 반드시 실행
  - "변경이 단순해서 스킵" → 단순 변경도 반드시 실행
  - kDone_git/kDone_notify만 실행하고 "완료" 선언 → 절대 금지
  - 교훈 단계 없이 커밋 진행 → 절대 금지

퀵_작업_교훈_절차:
  kDone_review: 간략 점검 (변경 내용 + 프로세스 이슈 유무)
  kDone_hooks/skills: review 결과 해당 없으면 "해당 없음" 선언 후 스킵 가능
  kDone_docs: 교훈 기록 해당 없으면 HISTORY만 업데이트 후 통과

위반_사례:
  - L-042 원인: 퀵 작업에서 kDone_review~kDone_docs 전체 생략하고 git+notify만 실행
  - 재발방지: 이 규칙으로 모든 분류에서 교훈 단계 강제화
```

## 교훈 기록 트리거 (사후 대응 — 추가 강화)

> 위 강제 실행과 별개로, **사용자 피드백 기반 사후 대응**도 유지.

```yaml
트리거_조건 (모두 충족 시):
  키워드_감지: "다시", "누락", "빠졌", "왜 안", "또", "안 됐", "고쳐"
  컨텍스트: 완료 응답 후 같은 작업에 대한 추가 요청 발생
  같은_작업_판단: 동일 파일/기능, 30분 이내

수행_절차:
  1. 원인 분석 (심각도 높음은 5 Whys)
  2. 개선안 도출
  3. Level 분류 후 해당 문서에 기록:
    - Level 1 (참고): LESSONS.md — 1회, 비정형
    - Level 2 (인지): MEMORY.md — 도구/환경 제한, 재발 높음
    - Level 3 (강제): CLAUDE.md/스킬 — 2회+ 반복, 규칙화 가능

심각도:
  높음: 3회+ 반복 또는 핵심 기능 누락
  중간: 2회 반복 또는 부가 기능 누락
  낮음: 1회 지적 또는 사소한 누락
```

## Gate 판정 리포팅 (kDone_review 내 실행)

> 풀 작업 경로에서만 실행. 라이트/미디엄은 kO_gate 미경유이므로 스킵.

```yaml
시점: kDone_review 서브스킬 시작 시 (교훈 분석 직전)
조건: /tmp/claude_gate_verify_report.json 존재 시
절차:
  1. /tmp/claude_gate_verify_report.json 읽기
  2. 아래 형식으로 리포트 출력
  3. 실측값 대조 (실제 수정 파일, 실제 도구 호출 등)

리포트_형식: |
  📊 **Gate 판정 리포트**
  ┌────────────────────────────────────────┐
  │ 항목           │ 계획    │ 실측    │ 평가 │
  ├────────────────┼─────────┼─────────┼──────┤
  │ 독립 작업 수   │ {계획}개│ {실측}개│ ✅/❌│
  │ 수정 파일 수   │ {계획}개│ {실측}개│ ✅/❌│
  │ 에이전트 수    │ {계획}개│ {실측}개│ ✅/❌│
  │ 도구 호출(A)   │ {추정}회│ {실측}회│ ✅/❌│
  │ 도구 호출(B)   │ {추정}회│ {실측}회│ ✅/❌│
  │ 메커니즘       │ {계획}  │ {실측}  │ ✅/❌│
  └────────────────┴─────────┴─────────┴──────┘
  판정 사유: {왜 N개 팀에이전트를 선택했는지 설명}

실측값_수집:
  수정_파일: git diff --name-only HEAD~1 (또는 이 세션 수정 파일)
  도구_호출: 에이전트 결과에서 추정 (정확 집계 불필요, 근사치)
  에이전트: 실제 생성된 팀에이전트/서브에이전트 수

평가_기준:
  ✅: 계획과 실측 차이 ±30% 이내
  ❌: 30% 초과 차이 → 교훈 기록 대상
  특별_주의: 에이전트 수가 계획보다 적으면 → 병렬화 미달 (심각)
```

## 공용 리소스 동기화 검증 (kDone_cleanup 내 실행)

> 공용 로직(스킬/hook/MCP/CLAUDE.md) 변경 시 모든 프로젝트에 적용되었는지 검증.

```yaml
시점: kDone_cleanup 서브스킬 내 (코드 정리 완료 후, Lock 해제 전)
조건: 이 세션에서 공용 리소스를 수정한 경우에만 실행

공용_리소스_정의:
  공용_스킬: ~/.claude/skills/ 내 범용 스킬 (kO, kPlan, kDev, kTest, kDone 등)
  공용_hook: ~/.claude/hooks/ 내 모든 .sh 파일
  공용_설정: ~/.claude/settings.json, ~/.claude/settings.local.json
  프로젝트_CLAUDE_md: /mnt/c/DATA/Project/{Mars,AI,MaiX}/CLAUDE.md (하드링크)
  프로젝트_스킬: /mnt/c/DATA/Project/{Mars,AI,MaiX}/.claude/skills/ 내 범용 스킬 (하드링크)

검증_항목:

  1_스킬_하드링크_검증:
    대상: 이 세션에서 수정된 범용 스킬
    방법: stat --format="%i" 로 3개 프로젝트의 inode 비교
    PASS: 모든 프로젝트의 inode 동일
    FAIL: inode 불일치 → 하드링크 재연결 필요
    자동_수정: ln 명령으로 재연결

  2_hook_존재_검증:
    대상: 이 세션에서 생성/수정된 hook 파일
    방법: ls -la ~/.claude/hooks/{hook명} 존재 + 실행 권한 확인
    PASS: 파일 존재 + chmod +x
    FAIL: 파일 누락 또는 실행 권한 없음

  3_settings_등록_검증:
    대상: 이 세션에서 수정된 hook의 settings.json 등록 여부
    방법: jq로 settings.json에서 해당 hook 명령어 검색
    PASS: matcher + command 등록 확인
    FAIL: settings.json에 미등록 → 등록 누락

  4_CLAUDE_md_하드링크_검증:
    대상: CLAUDE.md 변경 시
    방법: stat --format="%i" 로 3개 프로젝트의 CLAUDE.md inode 비교
    PASS: 모든 프로젝트의 inode 동일
    FAIL: inode 불일치 → 하드링크 재연결

리포트_형식: |
  🔗 **공용 리소스 동기화 검증**
  ┌────────────────────────────────────────┐
  │ 리소스               │ 상태 │ 프로젝트 │
  ├──────────────────────┼──────┼──────────┤
  │ {스킬/hook/설정 이름}│ ✅/❌│ Mars,AI,MaiX│
  └──────────────────────┴──────┴──────────┘

FAIL_시_자동_수정:
  스킬_하드링크: ln 명령으로 누락 프로젝트에 연결
  hook_권한: chmod +x 자동 실행
  settings_등록: 사용자에게 등록 누락 경고 (자동 수정은 위험)

스킵_조건: 이 세션에서 공용 리소스 미수정 시 전체 스킵 (불필요한 검증 방지)
```

## 필수 규칙

- Git Push 완료 전 "완료 요약" 출력 금지
- Git Push → ntfy 알림 순서 준수. 타이머 정지/종료 배너는 kFinish에서 수행.
- kFinish는 kO(메인)가 kDone 완료 후 직접 실행

