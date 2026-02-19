---
name: kDone_notify
description: "ntfy 알림 발송. 잔여 Phase 체크 및 완료 요약 출력."
---
# kDone_notify — ntfy 알림 & 완료

## ntfy 알림 발송

```yaml
방법 (JSON 파일 방식 필수):
  토픽: kInfra_{project} 프로젝트스킬의 ntfy 토픽 참조
  echo '{"topic":"{ntfy_topic}","title":"✅ 작업 완료","message":"작업 내용 요약","tags":["white_check_mark"]}' > /tmp/ntfy.json
  curl -X POST https://ntfy.sh -d @/tmp/ntfy.json -H "Content-Type: application/json"

금지: -H "Title:" 헤더 방식 (한글 깨짐)
```

## 중단 시 알림

```yaml
조건: Bypass 모드 중단 / 동일 오류 10회 / 외부 의존성 문제
형식:
  echo '{"topic":"{ntfy_topic}","title":"⚠️ 작업 중단","message":"중단 사유","priority":5,"tags":["warning"]}' > /tmp/ntfy.json
  curl -X POST https://ntfy.sh -d @/tmp/ntfy.json -H "Content-Type: application/json"
```

## 최종 체크리스트

```yaml
C#_수정_시:
  - ✅ 빌드 성공
  - ✅ 실행 정상
  - ✅ 로그 ERROR 0건
  - ✅ REST API 200
  - ✅ 스크린샷 정상

모든_작업:
  - ✅ 문서 업데이트 (PROJECT.md/DATABASE.md)
  - ✅ Git 커밋 & Push
  - ✅ ntfy 알림 발송
```

## 다단계 계획 잔여 Phase 체크 (강제)

```yaml
시점: ntfy 알림 발송 직후, 타이머 정지 전
절차:
  1. 계획 파일(plan .md, TODO_*.md) 존재 확인
  2. 남은 Phase/체크리스트 항목 존재 시:
     - 타이머 정지 금지
     - 완료 요약 출력 금지
     - 다음 Phase로 즉시 자동 진입
  3. 모든 Phase 완료 확인 시에만 타이머 정지 + 완료 요약
절대_금지:
  - "별도 세션에서 진행" 자의적 판단 금지
  - 규모와 무관하게 남은 Phase 즉시 진행
  - 유일한 예외: 사용자 명시적 "중단" 요청
```

## Context 자동 Compact (L-039 — 절대 규칙)

```yaml
시점: 타이머 정지 직전 (모든 작업 완료 후, 완료 요약 출력 전)
절차:
  1. /tmp/claude_context_pct 파일에서 남은 비율 읽기 (Bash: cat /tmp/claude_context_pct)
  2. 30% 미만이면 → 즉시 /compact 실행
  3. /compact 완료 후 → 타이머 정지 → 완료 요약 출력
  4. 30% 이상이면 → 스킵, 타이머 정지 진행

강제_규칙:
  - 이 단계는 스킵 절대 금지 (kDone_notify 실행마다 반드시 체크)
  - /compact 실행 여부를 완료 요약에 명시 ("context {X}% → compact 실행/스킵")
  - 모델 자율 판단 아님 — 수치 기반 자동 실행 (30% 미만 = 무조건 /compact)

근거 (L-039):
  - PostToolUse Hook(autocompact_reminder.sh)은 도구 실행 후에만 발동
  - kDone 완료 후 IDLE 상태에서는 도구 호출이 없어 Hook 발동 불가
  - 따라서 kDone_notify에서 파이프라인 레벨로 강제 체크
```

## 타이머 정지 시그널

```yaml
시점: 완료 요약 출력 직전 (Context Compact 완료 후)
명령: echo "stop" > ~/.claude/task_timer_signal.txt
효과: statusline ⏱️ 타이머가 현재 시간에 고정 (다음 사용자 메시지에서 리셋)
```

## 완료 요약 출력

```yaml
필수_전제: Git Push 완료 후에만 출력
순서: Git Push → ntfy 알림 → Context Compact 체크 → 타이머 정지 → 완료 요약
금지: Git Push 없이 완료 요약 출력

확인_사항:
  - ntfy 알림 전송 확인
  - 프로그램 정상 실행 확인
  - git 커밋 푸시 확인
  - context compact 체크 완료
  - 타이머 정지 시그널 전송
```
