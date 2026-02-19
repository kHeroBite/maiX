---
name: kO
description: "작업 오케스트레이션. 모든 사용자 메시지에 자동 발동. 타이머 관리, 프로젝트 설정 로딩, 3-way 분류(질문/퀵/코드수정). 코드 수정 시 kO가 직접 5단계 오케스트레이션(kPlan→kDev→kTest→kDone→kFinish). 추가 작업 시 kO-worker 위임. Auto-activates when: every user message — task request, question, exploration."
---
# kO — 작업 오케스트레이션 (디스패처 + 직접 오케스트레이터)

모든 사용자 메시지에 자동 발동되는 단일 진입점.
**메인 에이전트 = 디스패처 + 직접 오케스트레이터** — 분류 + spawn, 코드 수정 시 kO가 직접 5단계 오케스트레이션.

## 설계 원칙

```yaml
목적: 메인 에이전트가 직접 오케스트레이션하되, 팀에이전트에 실행 위임하여 인터럽트 대응 유지
핵심: 분류 → 팀에이전트 spawn → 완료 대기 (각 단계 사이에 인터럽트 처리 가능)
코드_수정_최초: kO가 직접 kPlan→kDev→kTest→kDone→kFinish 5단계 오케스트레이션
코드_수정_추가: 파이프라인 활성 중 추가 작업 → kO-worker×1 spawn (독립 파이프라인)
금지: kO 및 kO-worker가 직접 코드 탐색/수정/검색 수행 (모든 실행은 팀에이전트 위임)
```

## 타이머 리셋 (조건부)

```bash
STATE=$(cat /tmp/claude_pipeline_state 2>/dev/null | awk '{print $1}' || echo "IDLE")
STATE=${STATE:-IDLE}
if [ "$STATE" = "IDLE" ]; then
  echo "reset" > ~/.claude/task_timer_signal.txt
fi
```

> **IDLE일 때만 리셋**. 작업 중(KO/PLAN/DEV/TEST/DONE)에는 리셋하지 않아 누적 소요 시간 보존.

## Hook 헬스체크

> pipeline_gate.sh가 kO 전이 시 자동 실행. 수동 호출 불필요.

## 프로젝트 설정 로딩

```yaml
진입_시: /kInfra_{project} 프로젝트스킬 자동 로딩
내용: 솔루션 경로, API 포트, 로그/스크린샷 경로, ntfy 토픽, 관련 문서
코딩_규칙: /kRules_{project} 참조
```

## 3-way 분류 (kO 직접 판별)

```yaml
## kO는 3가지만 판별한다: 질문/탐색, 퀵, 코드 수정.
## 코드 수정의 세부 분류(라이트/미디엄/풀)는 kPlan 결과 수신 후 kO가 직접 수행.

질문_탐색:
  판별: 코드 수정 요청이 아닌 순수 정보 요청
  예시: "이 코드 뭐야?", "XX 설명해줘", "XX 찾아줘"
  처리: 팀에이전트(kQ) spawn → idle

퀵_작업:
  판별: 빌드 불필요한 극소 수정 (주석/오타/문자열/단순 상수/스킬·문서 수정)
  처리: 팀에이전트(kQ 퀵모드) spawn → idle
  필수: 빌드 후 반드시 실행+헬스체크 (deploy 생략 절대 금지, L-030)

코드_수정_작업:
  판별: 퀵이 아닌 모든 코드 수정 요청
  처리: kO가 직접 5단계 오케스트레이션 (kPlan→kDev→kTest→kDone→kFinish)
  세부_분류: kPlan 결과 수신 후 kO가 직접 라이트/미디엄/풀 판정
```

## 분류 상태 기록 (Hook 연동 — L-040)

```yaml
시점: 분류 완료 직후, 팀에이전트 spawn 직전
기록_주체:
  질문_탐색: 기록하지 않음 (파이프라인 미진입)
  퀵_작업: kO가 직접 기록 — echo "quick" > /tmp/claude_task_classification
  코드_수정_작업: kO가 직접 기록 — echo "code" > /tmp/claude_task_classification
    (세부 분류 light/medium/full은 kPlan 결과 수신 후 kO가 직접 기록)
목적: hook이 분류별 에이전트 정책 강제 (ko_check.sh, full_task_team_guard.sh)
강제: 분류 후 기록 누락 시 hook이 unknown으로 판단 → 서브에이전트 허용 (안전 방향)
```

## 실행 흐름 (3갈래 트리)

```yaml
사용자_메시지 → kO (분류 + 오케스트레이션):

  1_질문_탐색:
    kO → 팀에이전트(kQ) spawn → kO idle
    팀에이전트: 검색 + 응답 생성 → 사용자에게 직접 출력

  2_퀵_작업:
    kO → 팀에이전트(kQ 퀵모드) spawn → kO idle
    팀에이전트: kDev→kTest→kDone 직접 수행 (하위 에이전트 없음)

  3_코드_수정_작업:
    kO가 직접 5단계 오케스트레이션:
      1단계: kPlan 팀에이전트 spawn → 결과 수신 → 상태 전파(PLAN)
      2단계: kO가 분류 + kDev×N 팀에이전트 spawn → 완료 대기 → 상태 전파(DEV)
      3단계: kTest×N 팀에이전트 spawn → 완료 대기 → 상태 전파(TEST)
      4단계: kDone×N 팀에이전트 spawn → 완료 대기 → 상태 전파(DONE)
      5단계: kFinish — kO(메인)가 직접 실행 (팀에이전트 아님)
    (각 단계 사이에 인터럽트 처리 가능 — 팀에이전트 완료 대기 중 사용자 메시지 수신)
```

## kO에서 팀에이전트 spawn 패턴

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

코드_수정_작업:
  kO_판별: 퀵이 아닌 코드 수정 → 코드 수정
  kO_행동: kO가 직접 5단계 오케스트레이션 수행 (아래 "코드 수정 오케스트레이션" 섹션)
  분류_기록: echo "code" > /tmp/claude_task_classification
```

---

## 코드 수정 오케스트레이션 (kO 직접 실행)

> kO가 "코드 수정"으로 분류하면 **kO 자신이 직접** 5단계 오케스트레이션을 수행한다.
> 1~4단계는 팀에이전트(kPlan, kDev, kTest, kDone)를 spawn하고, 5단계(kFinish)는 kO가 메인에서 직접 실행한다.
> kO 자신은 코드를 탐색/수정하지 않으며, 모든 실행을 팀에이전트에 위임한다.
> 각 단계 사이(팀에이전트 완료 대기 중)에 인터럽트 처리가 가능하다.

### 5단계 흐름

```yaml
kO가 직접 5단계 오케스트레이션:

  1단계_kPlan:
    kO → kPlan 팀에이전트 spawn (항상)
    kPlan 내부에서 Quick/Deep 자체 판정:
      Quick: 직접 탐색 + 간단 계획
      Deep: Scout 에이전트 spawn + kPlan_sim + kPlan_review
    결과: 수정_파일_목록 + 단위작업_목록 + 메타데이터 + TODO 파일
    kO: 결과 수신 → 상태 전파(PLAN) → 2단계 진입

  2단계_kDev:
    kO → kPlan 출력 기반 분류 + 에이전트 수 결정 + 파일 할당
    kO → kDev×N 팀에이전트 spawn (파일 할당 매트릭스 기반)
    kO: 모든 kDev 팀에이전트 완료 → 상태 전파(DEV) → 3단계 진입

  3단계_kTest:
    kO → kTest×N 팀에이전트 spawn
    kO: 모든 kTest 팀에이전트 완료 → 상태 전파(TEST) → 4단계 진입

  4단계_kDone:
    kO → kDone×N 팀에이전트 spawn
    kO: 모든 kDone 팀에이전트 완료 → 상태 전파(DONE) → 5단계 진입

  5단계_kFinish:
    kO → kFinish 직접 실행 (팀에이전트 아님, kO 메인이 직접)
    내용: 팀에이전트 일괄 종료, tmux pane 정리, TeamDelete, 통계 출력, 타이머 정지, pipeline→IDLE, 종료 배너
```

### 파이프라인 상태 전파 (필수 — L-045)

> 각 단계 **완료 시** kO가 `/tmp/claude_pipeline_state`를 갱신한다.

```yaml
전파_시점: 각 단계의 팀에이전트 전체 완료 직후
전파_방법: Bash("echo '{단계} {소유자}' > /tmp/claude_pipeline_state")

파일_형식:
  내용: "{상태} {소유자_세션ID}" (공백 구분, 1줄)
  예시: "PLAN abc123-def456"
  읽기: awk '{print $1}' (상태만 추출), awk '{print $2}' (소유자만 추출)
  호환: hook이 awk로 상태만 추출하므로 기존 로직과 하위 호환

전파_매핑:
  kPlan 완료 → echo "PLAN $SESSION_ID" > /tmp/claude_pipeline_state
  kDev 완료  → echo "DEV $SESSION_ID" > /tmp/claude_pipeline_state
  kTest 완료 → echo "TEST $SESSION_ID" > /tmp/claude_pipeline_state
  kDone 완료 → echo "DONE $SESSION_ID" > /tmp/claude_pipeline_state
  kFinish 시작 → echo "FINISH $SESSION_ID" > /tmp/claude_pipeline_state
  kFinish 완료 → echo "IDLE" > /tmp/claude_pipeline_state
  (SESSION_ID = 해당 에이전트의 세션 ID)

소유자_불일치_경고:
  조건: 상태 파일의 소유자 ≠ 현재 세션 ID
  행동: "⚠️ pipeline_state 소유자 불일치: {파일소유자} ≠ {현재세션}" 경고 출력
  차단: 하지 않음 (경고만 — 방안C강화)
  근거: domain-fileops가 파일 수준 충돌 물리 차단, pipeline_state 경합은 작업 중단만 유발 (재시도 복구 가능)

필수_규칙:
  - 각 단계 완료 직후 **즉시** 전파 (지연/생략 금지)
  - 전파 없이 다음 단계 진입 금지
  - kO가 직접 수행 (팀에이전트 위임 불필요)
  - 소유자 태그 누락 금지 (빈 문자열이라도 공백 포함 필수)
```

### 1단계: kPlan 팀에이전트 spawn

```yaml
실행:
  방법: Task(general-purpose, team_name, model: sonnet)
  프롬프트_필수:
    - kPlan 스킬 내용
    - 사용자 요구사항 원문
    - 프로젝트 컨텍스트 (kInfra_{project})
    - kOID 전달: "현재 kOID: {kOID값} — 에러 기록 시 /tmp/claude_errors_{kOID}.md에 기록"
      (kOID 읽기: cat /tmp/claude_current_koid)
  대기: kPlan 팀에이전트 완료까지 대기

kPlan_내부_판정 (kO 관여 안 함):
  Quick: 단일 파일, 명확한 패턴 변경, 복잡도 낮음
  Deep: 멀티 파일, 복잡 로직, Scout 에이전트 필요

kPlan_서브단계별_추가_에이전트:
  원칙: kPlan의 각 서브단계는 필요 시 추가 팀에이전트를 자율적으로 spawn할 수 있다
  kPlan_deep: Scout 에이전트 ×1~3 spawn (코드베이스 탐색, 파일별 독립 분석)
  kPlan_sim: 시뮬레이션 에이전트 ×1~2 spawn (정상/예외/엣지케이스 병렬 검증)
  kPlan_review: 리뷰 에이전트 ×1~2 spawn (요구사항 검증 + 기술 타당성 병렬)
  제약: kPlan이 자체 판단으로 spawn — kO는 관여하지 않음

수신_데이터:
  - 수정_파일_목록: [{path, 변경_요약, 변경_규모}]
  - 단위작업_목록: [{id, 설명, 파일, 의존}]
  - 메타데이터: {총_파일수, 총_작업수, 독립_작업수, 예상_복잡도}
  - TODO 파일 경로

실패_시: kPlan 출력이 불완전 → 재spawn 또는 사용자 질문
```

### 2단계: 분류 + kDev 팀에이전트 spawn

#### Step 2-1: 규모 산정 + 3-way 분류

```yaml
분류_기준:
  라이트:
    조건: 파일 1-2개 AND 단위작업 1-2개 AND 복잡도 낮음
    예시: 상수값 변경, 단일 메서드 수정
  미디엄:
    조건: 파일 2-4개 OR 단위작업 3-5개 OR 복잡도 중간
    예시: 새 API 엔드포인트, UI 폼 수정
  풀:
    조건: 파일 5개+ OR 단위작업 6개+ OR 복잡도 높음
    예시: 새 기능 전체 구현, 대규모 리팩토링

분류_입력:
  - kPlan 메타데이터 (총_파일수, 총_작업수, 독립_작업수, 예상_복잡도)
  - 단위작업별 변경 규모
  - 파일 간 의존 관계
```

#### Step 2-2: 에이전트 수 결정

```yaml
에이전트_수_정책 (유일 출처):
  | 분류    | 빠른 확장 상한 | 신중 확장     |
  |--------|--------------|--------------|
  | 라이트  | 2개까지       | 4개+ 그룹화   |
  | 미디엄  | 4개까지       | 6개+ 그룹화   |
  | 풀     | 6개까지       | 8개+ 그룹화   |

기준값_산출:
  기준값 = max(수정_파일수, 단위작업수)
  예시: 파일 3개 + 작업 5개 → 기준값 = 5

결정_로직:
  1. 기준값 산출: max(수정_파일수, 단위작업수)
  2. 빠른 확장 범위 이내 (기준값 ≤ 상한):
     - 에이전트 수 = 기준값 (1:1 — 그룹화 없이 단순 할당)
     - 예: 미디엄, 기준값 3 → 에이전트 3개 (파일/작업 각 1개씩 담당)
  3. 빠른 확장 초과 (기준값 > 상한):
     - 에이전트 수 = 빠른 확장 상한 (고정)
     - 초과분은 그룹화하여 기존 에이전트에 분배
     - 예: 풀, 기준값 10 → 에이전트 6개, 나머지 4개 작업을 6개 에이전트에 균등 분배
  4. 신중 확장 (사유 필수):
     - 빠른 확장 상한 초과 에이전트가 필요한 경우 → 사유 명시 후 확장 가능
     - 그룹화 기반: 의존 관계 + 작업량 균형 고려

그룹화_규칙 (빠른 확장 초과 시):
  - 의존 관계 있는 작업 → 동일 에이전트에 할당 (최우선)
  - 동일 파일 관련 작업 → 동일 에이전트에 할당
  - 나머지 → 작업량 균형 기반 라운드 로빈
```

#### Step 2-3: 파일 할당 매트릭스

```yaml
원칙:
  - 1 파일 = 1 에이전트 (중복 할당 금지)
  - 의존 관계 있는 작업은 동일 에이전트에 할당
  - 에이전트 간 작업량 균형

출력_형식:
  agent_1:
    파일: [file1.cs, file2.cs]
    작업: [Task 1, Task 2]
  agent_2:
    파일: [file3.cs]
    작업: [Task 3]

영속화 (필수):
  시점: 매트릭스 확정 직후, kDev spawn 직전
  파일: /tmp/claude_file_assignment.json
  형식: |
    {
      "session_id": "{kO 세션 ID}",
      "timestamp": "{ISO 8601}",
      "classification": "{light|medium|full}",
      "agents": {
        "agent_1": {"files": ["file1.cs", "file2.cs"], "tasks": ["Task 1", "Task 2"]},
        "agent_2": {"files": ["file3.cs"], "tasks": ["Task 3"]}
      }
    }
  용도:
    - kO-worker(추가 모드) spawn 시 기존 파일 할당 확인 → 충돌 방지
    - kDone에서 수정 파일 전체 목록 참조 (git add 대상)
    - 디버깅: 어떤 에이전트가 어떤 파일을 담당했는지 추적
  생명주기: kO 시작 시 이전 파일 삭제 (pipeline_gate.sh의 kO 전이 시 자동 클리어)
```

#### Step 2-4: 분류 검증

```yaml
검증_항목:
  1. 분류_정확성: 파일 수/작업 수/복잡도가 분류 기준과 일치하는가?
  2. 에이전트_수_적정성: 분류별 정책 테이블 범위 내인가?
  3. 파일_할당_무결성: 파일 중복 할당 없는가? 의존 관계 준수하는가?
  4. 병렬화_조건: CLAUDE.md 병렬화 정책 충족하는가?

실패_시:
  - 분류 재조정 (한 단계 상향/하향)
  - 에이전트 수 재계산
  - 최대 2회 재시도 후에도 실패 시 → 풀로 강제 분류

분류_기록: echo "{light|medium|full}" > /tmp/claude_task_classification
```

#### Step 2-5: 분류 결과 출력 (필수)

```yaml
출력_형식:

  라이트_작업:
    형식: |
      ⚡ **라이트 작업** — kDev 팀에이전트 {N}개
      근거: {왜 라이트인지 1줄}
      📂 수정 파일:
        - {파일경로} ({변경 내용 요약})
      경로: kDev(팀에이전트 {N}개) → kTest → kDone(Fast)

  미디엄_작업:
    형식: |
      🔧 **미디엄 작업** — kDev 팀에이전트 {N}개
      근거: {왜 미디엄인지 1줄}
      📂 수정 파일:
        - {파일경로1} ({변경 내용 요약})
        - {파일경로2} ({변경 내용 요약})
      🤖 팀에이전트: {N}개 ({할당 근거})
      경로: kDev(팀에이전트 {N}개) → kTest → kDone(Fast)

  풀_작업:
    형식: |
      🏗️ **풀 작업** — kDev 팀에이전트 {N}개+
      근거: {왜 풀인지 1줄}
      📂 수정 파일:
        - {파일경로1} ({변경 내용 요약})
        - ...
      🤖 팀에이전트: {N}개 (파일 할당 매트릭스 확정)
      경로: kDev(팀에이전트 {N}개+) → kTest → kDone
```

#### Step 2-6: kDev 팀에이전트 spawn

```yaml
실행: kDev×N 팀에이전트 spawn (파일 할당 매트릭스 기반)
에이전트_프롬프트_필수:
  - 담당 파일 목록 (수정 허용 파일)
  - 해당 단위작업 상세
  - TODO 파일 경로
  - 프로젝트 컨텍스트 (kInfra_{project})
  - kOID 전달: "현재 kOID: {kOID값} — 에러 기록 시 /tmp/claude_errors_{kOID}.md에 기록"
    (kOID 읽기: cat /tmp/claude_current_koid)
대기: 모든 kDev 팀에이전트 완료까지 대기
실패_시: 실패 에이전트 재시도 또는 kDebug 호출
```

### 3단계: kTest 팀에이전트 spawn

```yaml
전제: 2단계(kDev) 전체 완료
실행: kTest 팀에이전트 spawn (build → deploy → run → quality)
에이전트_프롬프트_필수:
  - kTest 스킬 내용
  - 프로젝트 컨텍스트 (kInfra_{project})
  - kOID 전달: "현재 kOID: {kOID값} — 에러 기록 시 /tmp/claude_errors_{kOID}.md에 기록"
    (kOID 읽기: cat /tmp/claude_current_koid)
  - 검증 체크리스트 (필수 — L-047):
      내용: 사용자 요구사항에서 추출한 구체적 PASS/FAIL 판정 기준 목록
      형식: 항목별 1줄 (예: "마지막 행 위젯 3개가 전체 너비를 빈틈없이 균등 분할")
      출처: kO가 0단계(프롬프트 재작성) 시 사용자 요구사항에서 직접 추출
      전달: kTest 에이전트 프롬프트에 "📋 검증 체크리스트:" 헤더와 함께 명시
      누락_금지: 체크리스트 없이 kTest spawn 시 kTest가 보수적 판정 (의심 시 FAIL)
      예시: |
        📋 검증 체크리스트:
        1. 권한으로 숨겨진 위젯 공간에 나머지 위젯이 균등 재배치됨
        2. 마지막 행 위젯 3개가 전체 너비를 빈틈없이 채움 (오른쪽 빈 공간 없음)
        3. 위젯 간 간격이 균일함
멀티_선택: 프로젝트별 kTest 구성에 따라 N개 병렬 가능
실패_시: kDebug 호출 → 수정 → kTest 재실행

kTest_서브단계별_추가_에이전트:
  원칙: kTest의 각 서브단계는 필요 시 추가 팀에이전트를 자율적으로 spawn할 수 있다
  kTest_build: 빌드 에이전트 ×1~N spawn (멀티 프로젝트 빌드 시 프로젝트별 병렬)
  kTest_deploy: 배포 에이전트 ×1~N spawn (로컬/모바일/원격 타겟별 병렬)
  kTest_run: 테스트 에이전트 ×1~N spawn (REST API + 로그 + 스크린샷 병렬 검증)
  kTest_quality: 품질 에이전트 ×1~N spawn (요청-결과 대조 + DB 정합성 + UI/UX 병렬)
  제약: kTest 팀에이전트가 자체 판단으로 spawn — kO는 관여하지 않음
```

### 4단계: kDone 팀에이전트 spawn (절대 생략 금지 — L-044)

```yaml
전제: 3단계(kTest) 전체 통과
경로:
  라이트/미디엄: Fast Path (git + notify만)
  풀: Full Path (review + cleanup + docs + skills + hooks + git + notify)
에이전트_프롬프트_필수:
  - kDone 스킬 내용
  - 프로젝트 컨텍스트 (kInfra_{project})
  - kOID 전달: "현재 kOID: {kOID값} — 에러 기록 시 /tmp/claude_errors_{kOID}.md에 기록"
    (kOID 읽기: cat /tmp/claude_current_koid)
멀티_선택: 서브스킬별 병렬 가능 (kDone_review + kDone_cleanup 동시 등)

kDone_서브단계별_추가_에이전트:
  원칙: kDone의 각 서브단계는 필요 시 추가 팀에이전트를 자율적으로 spawn할 수 있다
  kDone_review: 리뷰 에이전트 ×1~2 spawn (프로세스 분석 + 조치 방향 병렬)
  kDone_cleanup: 정리 에이전트 ×1~N spawn (파일별 디버그 코드 정리 병렬)
  kDone_docs: 문서 에이전트 ×1~N spawn (HISTORY + LESSONS + PROJECT + DATABASE 병렬 업데이트)
  kDone_skills: 스킬 에이전트 ×1~2 spawn (규칙 강화 + 신규 서브스킬 병렬)
  kDone_hooks: Hook 에이전트 ×1~2 spawn (물리 차단 설계 + 구현 병렬)
  kDone_git: Git 에이전트 ×1 (커밋 + 푸시 — 순차, 병렬화 불가)
  kDone_notify: 알림 에이전트 ×1 (ntfy 발송 + 완료 통계 리뷰)
  제약: kDone 팀에이전트가 자체 판단으로 spawn — kO는 관여하지 않음

절대_규칙 (L-044):
  - kDone은 파이프라인의 최종 단계이며 **절대 생략/스킵 불가**
  - kTest 통과 후 kDone을 건너뛰는 것은 **파이프라인 미완료**
  - kDone 미수행 = git commit 없음 = 작업 미반영 = 작업 실패와 동일
  - kO든 kO-worker든, kDone은 반드시 실행되어야 함

kO_종료_조건:
  - kDone + kFinish 완료 확인 후에만 "작업 완료" 선언 가능
  - kFinish 미완료 상태에서 idle 전환 금지
  - 최소 보장: git commit + notify (Fast Path) + kFinish (팀 정리)

실행_순서:
  1. kTest 통과 확인
  2. kDone 팀에이전트 spawn
  3. kDone 완료 확인 (git commit 존재 확인)
  4. 상태 전파(DONE)
  5. kFinish 직접 실행 (팀에이전트 종료, tmux 정리, 통계 출력, 종료 배너)
```

### 에이전트 발동 통계 리뷰 (kFinish에서 수행)

> kFinish 단계에서 전체 파이프라인 동안 발동된 에이전트 통계를 사용자에게 보여준다.
> 이 통계 출력은 kFinish 스킬에서 수행되므로, kO는 통계를 /tmp/claude_agent_stats.json에 영속화하기만 하면 된다.

```yaml
kO_역할: 통계 영속화만 (출력은 kFinish에서 수행)
수집_대상: 전체 파이프라인(kPlan~kDone)에서 발동된 모든 에이전트

수집_방법 (필수):
  kO_내부_카운터:
    - kO가 각 단계에서 spawn한 팀에이전트 수를 변수로 추적
    - 단계별 카운터: kplan_agents, kdev_agents, ktest_agents, kdone_agents
  팀에이전트_반환_형식 (필수):
    - 팀에이전트 반환 메시지 마지막 줄에 아래 형식 필수 포함:
    - "📊 spawn_stats: team={N} sub={N} task={N}"
    - 예: "📊 spawn_stats: team=0 sub=2 task=3"
    - 미포함 시: kO가 해당 단계를 team=1 sub=0 task=1로 기본 기록
  영속화 (필수):
    - 통계를 /tmp/claude_agent_stats.json에 저장 (kFinish가 읽어서 출력)
    - kO 시작 시 이전 파일 삭제

용어_정의:
  팀에이전트: TeamCreate + Task(team_name) 패턴으로 spawn된 에이전트
  서브에이전트: Task 도구로 spawn된 일반 에이전트 (팀 없음)
  Task: Task 도구 호출 횟수 (서브에이전트 포함 전체)
```

### 파이프라인 순서 엄수

```yaml
순서: kPlan → 분류 → kDev → kTest → kDone → kFinish(메인 직접)
규칙:
  - kPlan 완료 전 분류/kDev spawn 금지
  - 분류/할당 완료 전 kDev spawn 금지
  - kDev 전체 완료 전 kTest 진입 금지
  - kTest 전체 통과 전 kDone 진입 금지
  - kDone 전체 완료 전 kFinish 진입 금지
```

### 팀에이전트 생명주기 (단계 완료 즉시 종료)

> 각 단계(kPlan, kDev, kTest, kDone)의 팀에이전트는 **작업 완료 메시지 수신 즉시 종료**한다.
> 대기시키면 pane이 누적되어 관리 불가 → 즉시 종료가 원칙.

```yaml
정책: 단계 완료 즉시 종료 (작업 완료 메시지 수신 시)
이유: 팀에이전트 pane 누적으로 tmux 관리 불가 방지

생명주기:
  kPlan_팀에이전트: spawn → 결과 반환 → kO가 결과 수신 → **즉시 shutdown**
  kDev_팀에이전트: spawn → 구현 완료 → kO가 완료 확인 → **즉시 shutdown**
  kTest_팀에이전트: spawn → 테스트 완료 → kO가 완료 확인 → **즉시 shutdown**
  kDone_팀에이전트: spawn → 마무리 완료 → kO가 완료 확인 → **즉시 shutdown**

종료_방법: kO가 SendMessage(type: "shutdown_request") 발송 → 팀에이전트 승인 → pane 자동 소멸
종료_시점: 해당 단계 팀에이전트 **전체** 완료 확인 직후 (부분 완료 시 완료된 에이전트만 먼저 종료 가능)

문제_발생_시_재spawn:
  kTest_실패: 기존 kDev 에이전트는 이미 종료됨 → 새 kDev 팀에이전트 spawn하여 수정
  kDone_실패: 새 kDone 팀에이전트 spawn하여 재실행
  트레이드오프: 컨텍스트 재구축 비용 < pane 누적 관리 비용

kFinish_역할_변경:
  - 팀에이전트 일괄 종료 → 불필요 (이미 각 단계에서 종료됨)
  - kFinish는 TeamDelete, 통계 출력, 타이머 정지, pipeline→IDLE, 종료 배너만 수행
  - 잔류 pane 확인 + 정리는 유지 (비정상 종료 대비)
```

### 커스텀 pane 레이아웃

> 팀에이전트 수에 따라 자동으로 열 수를 조정하여 메인 pane(kO) 영역을 최대 확보한다.
> `~/.claude/scripts/pane_layout.sh` 스크립트가 `after-split-window` tmux hook으로 자동 실행.

```yaml
스크립트: ~/.claude/scripts/pane_layout.sh
트리거: tmux after-split-window hook (pane 추가 시 자동)
수동_실행: bash ~/.claude/scripts/pane_layout.sh [session:window]

레이아웃_규칙:
  2~6_panes (2열):
    col1: main pane (전체 높이, 55% 너비)
    col2: agents 스택 (45% 너비)
    예시: |main |ag1 |
          |     |ag2 |
          |     |ag3 |

  7~9_panes (3열):
    col1: main pane (전체 높이, 40% 너비)
    col2: agents 스택 (30% 너비)
    col3: agents 스택 (30% 너비)
    예시: |main |ag1 |ag4 |
          |     |ag2 |ag5 |
          |     |ag3 |ag6 |

  10~12_panes (3열, col1 분할):
    col1: main(상 60%) + agents(하 40%)
    col2: agents 스택 (30% 너비)
    col3: agents 스택 (30% 너비)
    예시: |main |ag1 |ag5 |
          |     |ag2 |ag6 |
          |ag9  |ag3 |ag7 |
          |ag10 |ag4 |ag8 |

  13+_panes (4열):
    col1: main(상 55%) + agents(하 45%), 30% 너비
    col2~4: agents 스택, 각 ~23% 너비
    예시: |main |ag1 |ag5 |ag9  |
          |     |ag2 |ag6 |ag10 |
          |agA  |ag3 |ag7 |ag11 |
          |agB  |ag4 |ag8 |ag12 |

동작_원리:
  1. pane 수 감지 → 열 수 결정 (2/3/4)
  2. 열별 너비, pane별 높이 계산
  3. tmux layout string 생성 + 체크섬 계산
  4. tmux select-layout으로 적용

main_pane: 항상 첫 번째 pane (pane 목록의 0번째)
agent_panes: 나머지 pane (생성 순서대로 배치)

주의:
  - layout string은 tmux 내부 좌표계 기반 (정밀 계산 필요)
  - pane 제거 시에는 tmux 기본 리사이즈 동작 사용
  - 윈도우 리사이즈 시 수동 재실행: bash ~/.claude/scripts/pane_layout.sh
```

### 오케스트레이션 제약 사항

```yaml
금지:
  - kO가 직접 코드 탐색/수정/계획 수립 (순수 오케스트레이션만)
  - 에이전트 수를 인위적으로 축소 (정책 테이블 준수)
  - 파일 할당 매트릭스 없이 kDev spawn
  - kPlan 미완료 상태에서 분류 시도
  - kFinish 미수행 상태에서 idle 전환 또는 "작업 완료" 선언 (L-044)
  - "결과 보고 = 완료" 판단 — 반드시 kDone(git+notify) + kFinish 후 완료 선언
  - kO가 kTest/kDone 단계에서 직접 Bash(curl/screenshot/api/log) 실행 (L-046)
  - kTest/kDone을 팀에이전트 없이 메인에서 직접 수행 (L-046)
  - Task 호출 시 team_name 누락 — 코드 수정의 4단계 모두 TeamCreate+team_name 필수 (L-046)
  - kFinish를 팀에이전트로 spawn — kFinish는 kO 메인이 직접 실행

에스컬레이션:
  조건: kPlan 출력이 불완전하거나 요구사항 불명확
  행동: kPlan 재spawn 또는 사용자 질문
```

### 4단계 전체 팀에이전트 필수 (절대 규칙 — L-046)

> kO의 4단계 오케스트레이션(kPlan, kDev, kTest, kDone) **모두** 팀에이전트로 spawn해야 한다.
> kO가 직접 curl, screenshot, log, API 호출 등 실행 작업을 수행하는 것은 **절대 금지**.

```yaml
절대_규칙 (L-046):
  1_팀에이전트_필수: 4단계(kPlan, kDev, kTest, kDone) 모두 TeamCreate + Task(team_name=...) 패턴 필수
  2_메인_직접_실행_금지: kO는 TEST/DONE 상태에서 Bash(curl/screenshot/api/health/log) 직접 호출 금지
  3_team_name_필수: Task 호출 시 반드시 team_name 파라미터 포함 (누락 시 Hook이 차단)
  4_Hook_강제: orchestrator_direct_guard.sh가 TEST/DONE 상태에서 kO의 직접 Bash 실행 물리 차단

위반_사례 (2026-02-17 실제 발생):
  사례_1_kTest_직접_실행:
    위반: kO가 kTest 팀에이전트를 spawn하지 않고 직접 curl.exe로 헬스체크, 스크린샷, 로그 확인 실행
    원인: "간단한 검증이니 직접 해도 되겠다"는 자의적 판단
    결과: kO 오케스트레이션 원칙 위반, 에이전트 통계 왜곡
  사례_2_kDone_team_name_누락:
    위반: Task(general-purpose, model: sonnet) — team_name 파라미터 없음
    원인: TeamCreate 후 Task 호출에서 team_name을 빠뜨림
    결과: 서브에이전트로 spawn되어 팀 구조 없이 실행, SendMessage 불가

올바른_패턴:
  kTest_spawn: |
    TeamCreate(team_name="mars-test-{id}")
    Task(general-purpose, team_name="mars-test-{id}", model: sonnet)
    프롬프트에 kTest 스킬 내용 포함
  kDone_spawn: |
    Task(general-purpose, team_name="기존팀명", model: sonnet)  # 기존 팀 활용
    프롬프트에 kDone 스킬 내용 포함
  핵심: team_name이 항상 존재해야 함 — 없으면 서브에이전트(팀 아님)

금지_패턴:
  - Task(general-purpose, model: sonnet)  # ← team_name 없음 — 금지!
  - kO가 직접 curl.exe -s http://localhost:5959/api/...  # ← TEST 단계 직접 실행 — 금지!
  - kO가 직접 Read 도구로 스크린샷 확인  # ← 실행 작업 직접 수행 — 금지!
```

---

## 작업 중 추가 명령 처리 (인터럽트 규칙)

> 파이프라인 활성 상태(KO/PLAN/DEV/TEST/DONE)에서 사용자가 추가 명령을 보낸 경우의 처리 규칙.

```yaml
전제: pipeline_state ≠ IDLE (작업 진행 중에 추가 명령 수신)

분류_기준:
  1_기존_명령_수정: 이미 수행 중인 작업의 요구사항을 변경/보정하는 명령
  2_기존_명령_추가: 현재 작업에 새로운 요구사항을 덧붙이거나, 별개의 작업을 요청하는 명령

판별_방법:
  수정: "~하지 말고 ~해라", "~를 ~로 바꿔", "방금 그거 ~로 변경", 기존 작업 대상 파일/기능 언급
  추가: "추가로 ~도 해라", "그리고 ~도 만들어", 기존 작업과 다른 파일/기능 언급, 새로운 요구사항

1_기존_명령_수정:
  처리:
    - 해당 작업을 수행 중인 팀에이전트에게 SendMessage로 수정 사항 전달
  에이전트: 새 에이전트 생성 없음 — 기존 에이전트 활용
  원칙:
    - 수정 대상 파일을 담당 중인 에이전트 식별
    - 해당 에이전트에게 변경 내용을 명확히 전달

2_기존_명령_추가:
  처리: kO-worker×1 팀에이전트 spawn (독립 파이프라인)
  설계:
    - kO-worker가 추가 작업에 대해 독립적으로 kPlan→kDev→kTest→kDone 수행
    - 기존 kO의 파이프라인과 병렬 진행 가능
    - kO-worker 자신은 코드를 탐색/수정하지 않으며, 모든 실행을 하위 팀에이전트에 위임
  원칙:
    - 기존 진행 중인 팀에이전트는 건드리지 않음
    - kO-worker에게 추가 작업만 할당
    - 파일 할당 매트릭스에 기존 팀에이전트 담당 파일과 충돌 없도록 kO가 확인
    - kO-worker 내부에서 자체 kTest/kDone 수행

출력_형식: |
  📌 **작업 중 추가 명령** — {수정 | 추가}
  유형: {기존 명령 수정 → 팀에이전트 전달 | 추가 작업 → kO-worker×1 spawn (독립 파이프라인)}
  대상: {수정 대상 팀에이전트명 | kO-worker 할당 내역}
```

### kO-worker 팀에이전트 (추가 모드 전용)

> kO-worker는 **추가 모드에서만** spawn된다.
> kO와 동일한 오케스트레이션 로직을 가지되, 독립적인 파이프라인을 운영한다.

```yaml
spawn_조건: 파이프라인 활성 중 사용자 추가 작업 요청 시에만
역할: 추가 작업에 대해 독립적으로 kPlan→kDev→kTest→kDone 오케스트레이션
제약:
  - kO-worker 자신은 코드 탐색/수정 금지 (순수 오케스트레이션)
  - 기존 kO 파이프라인의 파일과 충돌 금지
  - kDone 절대 생략 금지 (L-044 동일 적용)
  - 파이프라인 상태 전파는 kO-worker 전용 파일 사용
    (기존 kO의 /tmp/claude_pipeline_state와 충돌 방지)

팀에이전트_생명주기 (kO와 동일 정책):
  정책: 단계 완료 즉시 종료 (kO 본체와 동일)
  종료_시점: 각 단계 팀에이전트 완료 메시지 수신 즉시
  종료_방법: kO-worker가 shutdown_request 발송
  kFinish: TeamDelete + 통계 출력 + 종료 배너 (잔류 에이전트 정리)

spawn_패턴:
  kO_행동: |
    Task(general-purpose, team_name, model: sonnet 또는 opus)
    프롬프트에 4단계 오케스트레이션 지시 포함
    프롬프트_필수:
      - 추가 작업 요구사항
      - 프로젝트 컨텍스트 (kInfra_{project})
      - 기존 파이프라인의 파일 할당 목록 (충돌 방지용)
      - 오케스트레이션 규칙 (이 스킬의 "코드 수정 오케스트레이션" 섹션)
  kO_이후: 기존 파이프라인 오케스트레이션 계속 진행
```

---

## 분류 결과 출력 (필수)

> 분류 직후 사용자에게 판단 근거를 투명하게 보여준다.

```yaml
출력_시점: 분류 완료 직후, 팀에이전트 spawn 직전
출력_주체: kO가 직접 출력 (3가지 모두)
출력_형식:

  질문_탐색:
    형식: |
      🔍 **질문/탐색** → kQ 팀에이전트
      근거: {왜 질문/탐색으로 판단했는지 1줄}

  퀵_작업:
    형식: |
      ⚡ **퀵 작업** → kQ 팀에이전트 (퀵모드)
      근거: {왜 퀵인지 1줄 — 예: "주석 수정, 빌드 불필요"}
      경로: kQ 내부 kDev→kTest→kDone 직접

  코드_수정_작업:
    형식: |
      🔧 **코드 수정 작업** → kO 직접 오케스트레이션
      근거: {왜 코드 수정인지 1줄}
      경로: kO → kPlan → kDev×N → kTest → kDone → kFinish
```

## 파이프라인 단계 전환 배너 (필수)

> 각 메인스킬 진입 시 **표준 배너**를 출력하여 사용자가 현재 단계를 즉시 파악.
> **최초 진입 = 풀 배너**, **재진입/인터럽트 = 단순 1줄**.

```yaml
시점: kO/kPlan/kDev/kTest/kDone 메인스킬 진입 직후 (서브스킬은 제외)

최초_진입_배너 (pipeline_state 전환 시):
  형식: 3줄 배너 (구분선 + 이모지+단계명 + 구분선)
  kO: |
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    🎯 **kO 진입**
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  kPlan: |
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    📋 **kPlan 진입**
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  kDev: |
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    🚀 **kDev 진입**
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  kTest: |
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    🧪 **kTest 진입**
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  kDone: |
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
    ✅ **kDone 진입**
    ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

재진입_배너 (인터럽트/재실행 시):
  형식: 단순 1줄 (이모지 + 단계명 + 사유)
  kO: 🎯 kO — 작업 중 추가 명령 수신
  kPlan: 📋 kPlan — 재실행
  kDev: 🚀 kDev — 추가 수정
  kTest: 🧪 kTest — 재테스트
  kDone: ✅ kDone — 재실행

판정_기준:
  최초_진입: 해당 단계에 처음 진입 (pipeline_state 전환)
  재진입: 이미 해당 단계를 거친 후 다시 진입 (실패 재시도, 인터럽트 등)

규칙:
  - 메인스킬 진입 시 반드시 배너 출력 (스킵 금지)
  - 서브스킬(kDev_parallel, kTest_build 등)은 배너 미출력
  - 배경색: Claude Code 출력은 마크다운이므로 ANSI 이스케이프 미지원 → 구분선+볼드+이모지로 시각 강조

예외_배너_스킵:
  - compact 후 자동 kO 진입: 배너 출력하지 않음
  - 판별: 시스템 메시지에 "continued from a previous conversation" 포함 시
  - 이유: compact 후 자동 진입은 사용자 명시 요청이 아닌 세션 복원이므로 배너 노이즈 방지
```

## 완전자동화 원칙

```yaml
Bypass_Mode:
  원칙: 모든 단계에서 사용자 확인 없이 자동 진행
  예외: 0단계 스마트 질문 (추론 불가 항목, AskUserQuestion 1회만)
  금지: "확인해주세요", "테스트해주세요" 등 절대 금지
  자동_의사결정: 선택지 있어도 기본값으로 자동 진행
  Plan_Mode: ExitPlanMode 호출 즉시 Bypass 모드 전환
  무중단: 처음부터 끝까지 멈추지 않고 연속 진행
  테스트: 로그/REST API/스크린샷/UI 검증 필수
  문서: PROJECT.md, DATABASE.md, RESTAPI.md 항상 참조/업데이트
```

## 0단계: 프롬프트 재작성

```yaml
실행_주체:
  코드_수정: kO가 직접 수행 (팀에이전트 spawn 전, 오케스트레이션의 일부)
  퀵_작업: kQ 팀에이전트가 내부에서 수행 (kO는 spawn만)
  질문_탐색: 해당 없음 (TODO 미생성)

실행_내용:
  - 요구사항 분석 및 추론
  - 추론 불가 항목 식별 → 스마트 질문 (조건부)
  - 스킬 사전 매칭 (0.5단계 — 아래 참조)
  - TODO_YYYYMMDDhhmmss.md 생성
  - 상세 체크리스트 작성
  - 프롬프트 확장 필수 (간소화 금지, 디테일 최대화)

스마트_질문_규칙:
  트리거: 추론 불가 항목 1개+ AND 영향도 높음
  도구: AskUserQuestion (1회, 최대 4개 질문, 선택지 방식)
  스킵: 모든 항목 추론 가능하면 질문 없이 진행
  금지: 2회 이상 질문, 1단계 이후 질문
```

## 0.5단계: 스킬 사전 매칭 (자율 발동 강화)

> 사용자 명령에 없는 스킬도 자율적으로 발동하기 위한 사전 검색 단계.
> 모델의 "연상"이 아닌 "키워드 매칭"으로 누락을 방지한다.

```yaml
시점: 0단계 요구사항 분석 직후, TODO 작성 직전
절차:
  1. 사용자 요청에서 핵심 키워드 추출
  2. 아래 매핑표와 대조하여 관련 스킬 식별
  3. TODO 헤더에 "활용 가능 스킬" 목록 명시
  4. kPlan/kDev가 해당 스킬을 적시에 호출

매핑표 (키워드 → 스킬):
  차트|그래프|시각화|모니터링|LineSeries|ColumnSeries:
    → kSkill_livecharts2
  폼|화면|UI|레이아웃|DataGridView|MDI|컨트롤:
    → domain-winforms
  DB|테이블|스키마|컬럼|마이그레이션|ALTER|CREATE TABLE:
    → domain-database
  리팩토링|코드품질|복잡도|성능|메모리|스레드:
    → domain-csharp
  라이브러리|NuGet|패키지|버전|업그레이드:
    → domain-context7
  버그|오류|예외|디버깅|로그분석|근본원인:
    → kDebug
  영향도|인터페이스변경|공통모듈|참조추적:
    → kDev_impact
  새스킬|스킬생성|워크플로우:
    → skill-creator
  MCP서버|외부API|통합:
    → mcp-builder
  파일Lock|파일충돌|NTFS절차|ext4|rsync|다중세션:
    → domain-fileops

TODO_헤더_형식:
  "## 활용 가능 스킬: kSkill_livecharts2, domain-winforms"
  → kDev 진입 시 이 목록의 스킬을 사전 호출

미매칭_시: 목록 생략 (불필요한 스킬 로딩 방지)
질문_탐색_시: 이 단계 스킵 (TODO 미생성이므로)
```

## 메인스킬 목록 (유일 출처)

```yaml
kPlan: 순수 계획 수립 — 코드 탐색 + 파일 목록 + 단위작업 출력 (서브: kPlan_deep, kPlan_sim, kPlan_review)
kDev: 구현, 병렬, 리뷰, 영향도, Lock 통합 개발 (서브: kDev_parallel, kDev_review, kDev_impact, kDev_lock)
kTest: build, deploy, run, quality 통합 테스트 (서브: kTest_build, kTest_deploy, kTest_run, kTest_quality)
kDone: review, cleanup, docs, skills, hooks, git, notify 통합 마무리 (서브: kDone_review, kDone_cleanup, kDone_docs, kDone_skills, kDone_hooks, kDone_git, kDone_notify)
kFinish: 파이프라인 마무리 — 팀에이전트 종료, tmux 정리, 통계 출력, 종료 배너 (메인 직접 실행)

유틸리티: kDebug, kVerify, kThink
에이전트_전용: kQ (질문/퀵 처리)
프로젝트스킬: kInfra_{project}, kRules_{project}
```

## 다단계 계획 자동 계속

```yaml
규칙: kDone 완료 ≠ 전체 계획 완료
점검_시점: kDone 완료 직후
절차:
  1. 계획 파일(plan .md, TODO_*.md) 존재 여부 확인
  2. 남은 Phase/체크리스트 항목 존재 시:
     - "작업 완료" 선언 금지
     - 타이머 정지 신호 발송 금지
     - 다음 Phase 즉시 자동 진입 (사용자 확인 불필요)
  3. 모든 Phase 완료 시에만 최종 "작업 완료" 선언
컨텍스트_복원_시:
  - 상태 확인 최대 1턴 → 즉시 실행 시작
  - 확인에 2턴 이상 소비 금지
절대_금지:
  - "별도 세션에서 진행" 자의적 판단 금지
  - 규모/세션 수와 무관하게 남은 Phase 즉시 진행
  - 유일한 예외: 사용자가 "여기서 멈춰"/"중단"이라고 명시한 경우만
```

## Watchdog 에이전트 (선택적)

```yaml
활성화_조건 (AND):
  - 풀 작업 + 예상 소요 30분+
  - 병렬 에이전트 3개+ 실행

실행:
  방법: Task(general-purpose, run_in_background: true)
  모델: Haiku (저비용)
  주기: 60초 상태 점검

모니터링:
  1. 프로세스_생존: curl health 응답 확인
  2. Lock_만료: {프로젝트}/.claude/locks/ 내 의도 Lock TTL 초과 감지
  3. 빌드_상태: /tmp/claude_test_build_ok 생성 여부
  4. 에이전트_상태: TaskList로 blocked/stalled 감지

이상_감지_시:
  - ntfy 알림 (priority:4, "Watchdog 경고")
  - /tmp/claude_watchdog_alert 생성
  - 리더에게 요약 5줄 반환

종료: kDone 진입 시 자동
제약: 관찰/알림만 수행, 파일 수정 절대 금지
```

## 예외 및 중단 처리

```yaml
중단_조건: Bypass 모드 중단 / 동일 오류 10회 반복 / 외부 의존성 문제
중단_시:
  - ntfy 알림 (priority:5, warning 태그) — 토픽은 kInfra_{project} 참조
  - echo "stop" > ~/.claude/task_timer_signal.txt
```
