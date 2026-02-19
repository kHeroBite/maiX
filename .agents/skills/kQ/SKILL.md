---
name: kQ
description: "질문/탐색 + 퀵 처리 전용. kO가 spawn한 팀에이전트가 실행. 내부검색, 외부검색, 퀵 수정(kDev→kTest→kDone 직접 수행), 응답 생성. Auto-activates when: question, exploration, quick fix — dispatched by kO."
---
# kQ — 질문/탐색 + 퀵 처리

> kO가 질문/탐색 또는 퀵으로 분류한 후 팀에이전트에 위임하는 실행 단계.
> 메인 에이전트는 kQ를 직접 실행하지 않음 — 항상 팀에이전트가 실행.

## 설계 원칙

```yaml
목적: 메인(kO)을 순수 디스패처로 유지하여 인터럽트 즉시 대응 가능하게 함
핵심: 질문이든 퀵이든 팀에이전트로 넘겨 메인이 도구 호출에 묶이지 않게 함
```

## kQ 분류 (kO로부터 수신)

```yaml
질문_탐색:
  판별: 코드 수정 요청이 아닌 순수 정보 요청
  예시: "이 코드 뭐야?", "XX 설명해줘", "XX 찾아줘"
  처리: 검색 + 응답 생성

퀵_작업:
  판별: 빌드 불필요한 극소 수정 (주석/오타/문자열/단순 상수/스킬 및 문서 수정)
  처리: kQ 팀에이전트가 kDev→kTest→kDone을 직접 수행 (하위 에이전트 없음)
```

## 질문/탐색 처리

```yaml
step_1_검색_유형_판별:
  내부_검색: 코드베이스 내 정보로 답변 가능
    도구: Grep, Glob, Read, Serena (find_symbol, get_symbols_overview 등)
    예시: "이 함수 뭐야?", "XX 클래스 어디있어?"
  외부_검색: 인터넷/문서 검색 필요
    도구: WebSearch, Context7, ref, WebFetch
    예시: "LiveCharts2 사용법", "XX 라이브러리 문서"
  혼합_검색: 내부 + 외부 모두 필요
    예시: "우리 프로젝트에서 쓰는 XX 버전의 최신 변경사항"

step_2_병렬_검색:
  원칙: 독립적인 검색 쿼리는 모두 병렬 도구 호출
  예시: WebSearch + Context7 + ref 동시 호출
  금지: 순차 검색 (결과 의존 관계 없으면)

step_3_응답_생성:
  형식: 간결하고 구조화된 답변
  포함: 소스/출처 링크 (외부 검색 시)
  제한: 메인에 반환 시 요약 5줄 이내 + 상세는 직접 사용자에게 출력
```

## 퀵 작업 처리 (kDev→kTest→kDone 직접 수행)

> kQ 팀에이전트가 하위 에이전트 없이 직접 전 과정을 수행한다.
> 퀵 = 파일 1개, 5줄 이내 극소 수정이므로 에이전트 분리 이점 없음.

```yaml
step_1_kDev_수정:
  대상: 주석/오타/문자열/단순 상수/스킬 및 문서 수정
  도구: Claude Code Edit (NTFS rsync 절차 준수) 또는 Serena 심볼 편집
  NTFS_절차:
    1. cp "/mnt/c/.../파일" ~/work/{project}-ntfs/파일
    2. Edit ~/work/{project}-ntfs/파일
    3. rsync -a --inplace ~/work/{project}-ntfs/파일 "/mnt/c/.../파일"
  예외: Serena 심볼 편집은 rsync 불필요
  예외: 스킬/문서(.md) 수정은 빌드 불필요 → step_2~3 스킵, step_4로 직행

step_2_kTest_빌드_및_배포:
  조건: 코드 파일(.cs 등) 수정 시에만
  빌드: dotnet build (포그라운드, 프로젝트스킬 kTest_build_{project} 참조)
  배포: 프로젝트스킬 kTest_deploy_{project} 참조
  필수: 빌드 후 반드시 실행 + 헬스체크 (deploy 생략 절대 금지)

step_3_kTest_검증:
  조건: 코드 파일 수정 시에만
  최소_검증: 프로세스 실행 확인 + 기본 헬스체크
  참조: 프로젝트스킬 kTest_run_{project}

step_4_kDone_마무리:
  Fast_Path: git 커밋 + ntfy 알림만 (review/cleanup/docs/skills/hooks 스킵)
  커밋: 한국어 + 이모지 메시지
  알림: ntfy 발송 (프로젝트스킬 kInfra_{project} 토픽 참조)
  이후: kFinish는 kO(메인)가 kQ 완료 후 직접 실행 — kQ에서 수행하지 않음
```

## 에이전트 실행 모델

```yaml
spawn_방식: kO가 Task 도구로 팀에이전트 1개 spawn
모델: sonnet (비용 효율, 질문/퀵에 opus 불필요)
프롬프트_필수사항:
  - kQ 스킬 내용
  - 사용자 원본 요청
  - 프로젝트 컨텍스트 (kInfra_{project} 핵심 정보)
결과_반환:
  질문: 사용자에게 직접 응답 (메인 경유 불필요 — 팀에이전트가 직접 출력)
  퀵: 수정 결과 요약 (파일명 + 변경 1줄)
```

## kO에서 kQ 호출 패턴

```yaml
질문_탐색:
  kO_판별: 코드 수정 아님 → 질문/탐색
  kO_행동: |
    Task(general-purpose, team_name, model: sonnet)
    프롬프트: "kQ 질문 처리: {사용자 요청}"
  kO_이후: idle (인터럽트 대기)

퀵_작업:
  kO_판별: 빌드 불필요 극소 수정 → 퀵
  kO_행동: |
    Task(general-purpose, team_name, model: sonnet)
    프롬프트: "kQ 퀵 처리: {사용자 요청}"
  kO_이후: idle (인터럽트 대기)
  분류_기록: echo "quick" > /tmp/claude_task_classification
```

## 제약 사항

```yaml
금지:
  - kQ에서 kPlan/kDev_parallel 호출 (퀵 범위 초과 시 메인에 에스컬레이션)
  - 퀵 범위를 벗어나는 수정 (파일 2개+ 또는 빌드 필수 대규모 변경)
  - 메인 컨텍스트에 대량 결과 반환 (요약 5줄 이내)

에스컬레이션:
  조건: 퀵으로 분류되었으나 실제 작업이 퀵 범위 초과
  행동: 메인에 "규모 초과 — 재분류 필요" 메시지 반환
  메인: kO가 재분류 → 코드 수정 경로(kO 직접 오케스트레이션)로 전환
```
