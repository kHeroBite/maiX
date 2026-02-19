---
name: kDone_cleanup
description: "코드 정리. 디버그 코드 변환, 테스트 코드 제거, Lock 해제."
---
# kDone_cleanup — 코드 정리

## 정리 항목

### 1. 디버그 코드 변환

```yaml
절차: kInfra_{project} 프로젝트스킬의 디버그 변환 규칙 참조
예시: Debug → Debug2, Trace 비활성화 등 (프로젝트마다 다름)
```

### 2. 테스트용 코드 제거

```yaml
대상:
  - 임시 하드코딩 값
  - 테스트용 주석
  - 디버깅용 Console.WriteLine
```

### 3. 로그 파일 삭제

```yaml
경로: kInfra_{project} 프로젝트스킬의 로그 경로 참조
```

### 4. TODO 파일 삭제

```yaml
명령어: rm -f TODO_*.md
```

### 5. 프로그램 종료 및 재실행

```yaml
절차: kInfra_{project} 프로젝트스킬의 실행/종료 명령 참조
```

### 6. 의도 Lock 해제 (cleanup 마지막 동작 — 필수)

```yaml
절차:
  1. 의도 Lock 해제: rm -f {프로젝트}/.claude/locks/intent_{세션ID}.json
  2. 해제 확인: ls {프로젝트}/.claude/locks/ 에서 현재 세션 Lock JSON 없음 확인

시점: kDone_cleanup의 모든 코드 정리 완료 후, kDone_docs 진입 전
이유: kDone_cleanup이 .cs 파일을 수정하는 마지막 단계
이후: kDone_docs(MD만), kDone_git(읽기만), kDone_notify(수정없음) → Lock 불필요
참고: Low-level Lock(.lock/.lockdir)은 domain-fileops가 파일 수정 직후 자동 해제
```
