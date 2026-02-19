---
name: kFinish
description: "파이프라인 마무리. 팀에이전트 종료, tmux 정리, 종료 배너. 메인 에이전트에서 직접 실행. Auto-activates when: pipeline completed, all team agents need cleanup."
---

# kFinish — 파이프라인 마무리

**실행 주체**: 메인 에이전트(kO/kO-worker) 전용
**실행 시점**: kDone의 모든 서브스킬 완료 후
**파이프라인 구조**: `kO(메인) → kPlan(팀) → kDev(팀) → kTest(팀) → kDone(팀) → kFinish(메인)`

## 책임 범위

kFinish는 파이프라인 종료 시 리소스 정리 및 종료 절차를 담당합니다:

1. **1차 종료: config.json 기반 전체 멤버 shutdown** — 모든 팀에이전트 명시적 종료 (필수)
2. **2차 종료: tmux pane ID 기반 잔류 에이전트 탐색** — 1차 종료에서 누락된 에이전트 pane 강제 정리
3. **3차 종료: tmux capture-pane 기반 에이전트명 추출 shutdown** — 이전 세션 잔류 에이전트까지 포착하는 최종 안전망
4. **4차 정리: 메인 pane 외 전체 pane 강제 kill** — 1~3차 결과와 무관하게 항상 실행 (빈 bash pane 포함 모든 잔류 제거)
5. **TeamDelete** — 팀 리소스 정리 (config.json 삭제는 여기서만)
6. **종료 배너 출력** — 작업 요약 테이블
7. **타이머 정지** — task_timer_signal.txt
8. **pipeline_state → IDLE 리셋**
9. **에이전트 발동 통계 출력** — claude_agent_stats.json
10. **임시 파일 삭제** — 에러 추적 파일 등

> **중요**: config.json은 kFinish에서만 삭제 (TeamDelete 경유). 1차 종료 전 삭제 시 멤버 목록 유실 → 에이전트 미종료 사고.

---

## 실행 절차

### Step 1: 1차 종료 — config.json 기반 전체 멤버 shutdown (필수)

```yaml
도구: SendMessage (type: "shutdown_request")
대상: 전체 팀에이전트 (리더 제외)
절차:
  1. 팀 설정 파일 읽기 (~/.claude/teams/{team-name}/config.json)
     - 파일 없음 시: 경고 출력 + Step 2(tmux 2차 탐색)로 Fallback
  2. members 배열에서 리더 제외 전체 멤버 name 추출
  3. **모든 멤버**에게 SendMessage(type:"shutdown_request") 발송
     - 이미 종료된 에이전트는 응답 없음 → 무시하고 계속
  4. 각 멤버의 shutdown_response 수신 확인 (타임아웃 30초)
     - approve: 정상 종료 확인
     - reject: 에러 메시지 출력 + 사용자 판단 대기
     - 무응답: 경고 출력 + 계속 진행 (Step 2에서 재확인)

**절대 규칙**:
  - config.json은 이 단계에서 절대 삭제 금지 — TeamDelete에서만 삭제
  - config.json 읽기 전 삭제 시 멤버 목록 유실 → 에이전트 미종료 사고
```

**config.json 보호 규칙**:
```yaml
절대_규칙:
  - config.json은 kFinish에서만 삭제 (TeamDelete 경유)
  - kFinish 이전 단계에서 config.json 삭제 절대 금지
  - config.json 읽기 실패 시 → tmux 2차 탐색으로 Fallback (에러 출력 + 계속 진행)
```

---

### Step 2: 2차 종료 — tmux pane ID 기반 잔류 에이전트 탐색 (안전망)

```yaml
도구: Bash
목적: Step 1에서 종료되지 않은 잔류 에이전트 pane 강제 정리
명령어:
  1. tmux list-panes -a -F "#{session_name}:#{window_index}.#{pane_index} #{pane_pid} #{pane_current_command}"
  2. 현재 팀과 관련된 pane 식별:
     - 팀 config의 tmuxPaneId와 대조 (config.json 읽기 성공 시)
     - 또는 pane_current_command에 "claude" 또는 "node" 포함 여부 확인
  3. Step 1에서 종료되지 않은 잔류 에이전트 pane 발견 시:
     - tmux kill-pane으로 강제 제거
  4. 이것은 1차(config.json) 이후의 보완 안전망
```

**예시**:
```bash
# 잔류 pane 확인
tmux list-panes -a -F "#{session_name}:#{window_index}.#{pane_index} #{pane_pid} #{pane_current_command}"

# 잔류 pane 강제 제거
tmux kill-pane -t mars-dev:0.1
tmux kill-pane -t mars-dev:0.2
```

---

### Step 3: 3차 종료 — tmux capture-pane 기반 에이전트명 추출 shutdown (최종 안전망)

> **배경**: 1차(config.json)와 2차(pane ID)는 현재 팀의 에이전트만 정리한다.
> 이전 세션에서 비정상 종료된 잔류 에이전트는 config.json에도 없고 현재 팀 pane ID에도 없다.
> 3차 종료는 tmux pane을 스캔하여 **현재 세션 소속** Claude Code 에이전트를 식별하고 graceful shutdown을 시도한다.

```yaml
도구: Bash + SendMessage (type: "shutdown_request")
목적: 1차/2차에서 누락된 현재 세션의 잔류 에이전트 포착 + 정리
핵심: tmux capture-pane으로 pane 내용을 읽어 --parent-session-id로 소유권 확인 → @에이전트명 추출 → shutdown

⚠️ 소유권_필터링 (절대 규칙 — 다른 세션 보호):
  원칙: 현재 세션이 spawn한 에이전트만 종료 대상. 다른 세션의 팀에이전트는 절대 건드리지 않음.
  식별: 에이전트 실행 명령에 포함된 --parent-session-id를 현재 세션 ID와 대조
  불일치_시: 해당 pane 스킵 (경고 출력: "⚠️ 다른 세션 소속 — 스킵: {pane} (parent: {다른세션ID})")

절차:
  1. tmux list-panes로 전체 pane 목록 조회
     - 메인 pane(자기 자신)과 pane index 0 제외
     - pane_current_command가 에이전트 바이너리(예: "2.1.44")인 pane만 필터링
     - "claude" 프로세스(메인 세션)는 제외 — 에이전트 바이너리명과 구분됨

  2. 각 후보 pane에 tmux capture-pane 실행 (넓은 범위):
     - tmux capture-pane -t {session}:{window}.{pane} -p -S -50
     - 마지막 50줄을 읽어 두 가지 정보 추출:
       a. --parent-session-id 값 추출 (소유권 확인)
       b. @에이전트명 추출 (statusline에서)

  3. 소유권 확인 (필수 — 다른 세션 보호):
     - 추출한 --parent-session-id를 현재 세션 ID와 비교
     - 일치: 현재 세션이 spawn한 에이전트 → 종료 대상
     - 불일치: 다른 세션 소속 → 해당 pane 스킵 + 경고 출력
     - 미발견: --parent-session-id를 찾을 수 없는 경우 → 해당 pane 스킵 (안전 방향)

  4. 소유권 확인 통과 + @에이전트명 추출 성공 시:
     - SendMessage(type: "shutdown_request", recipient: "{에이전트명}") 발송
     - shutdown_response 수신 대기 (타임아웃 5초)
     - approve: 정상 종료 확인 → pane 자동 소멸
     - reject/무응답: 경고 출력 + tmux kill-pane Fallback

  5. 소유권 확인 통과 + @에이전트명 추출 실패 시:
     - tmux kill-pane으로 강제 제거 (현재 세션 소속이 확인되었으므로 안전)

핵심_발견:
  - SendMessage는 config.json/팀 컨텍스트 없이도 에이전트명만으로 라우팅 가능
  - Claude Code statusline에 @에이전트명이 표시됨 (예: @kPlan-chart, @kdev-infra)
  - 에이전트 실행 명령에 --parent-session-id가 포함됨 → 소유권 판별 가능
  - 이전 세션의 팀이 TeamDelete 없이 사라져도 에이전트 프로세스는 tmux pane에 잔류
  - idle 상태의 잔류 에이전트는 SendMessage에 응답하지 못함 → kill-pane Fallback 필수
```

**예시**:
```bash
# 1. 전체 pane 목록 조회 (에이전트 바이너리만 — "claude" 메인 세션 제외)
tmux list-panes -a -F "#{session_name}:#{window_index}.#{pane_index} #{pane_pid} #{pane_current_command}"
# 출력에서 pane_current_command가 에이전트 바이너리(예: 2.1.44)인 것만 필터

# 2. 각 pane의 내용 읽기 (넓은 범위 — parent-session-id + statusline)
tmux capture-pane -t 8:1.2 -p -S -50

# 3. --parent-session-id 추출하여 현재 세션과 대조
#    출력 예: --parent-session-id 395c06fe-fd08-4201-9555-99946e060d4a
#    현재 세션 ID와 일치 → 종료 대상

# 4. @에이전트명 추출 (statusline에서 "@kPlan-chart" 발견)

# 5. SendMessage(type: "shutdown_request", recipient: "kPlan-chart") 발송

# 6. 무응답 시 → tmux kill-pane -t 8:1.2
```

**1차/2차와의 차이점**:
```yaml
1차_종료: config.json members → 현재 팀 에이전트만 (팀 존재 필수)
2차_종료: tmux pane ID 대조 → 현재 팀 config의 tmuxPaneId만 (config 필요)
3차_종료: tmux capture-pane → --parent-session-id로 현재 세션 소속만 필터 → 안전한 정리

적용_범위:
  - 현재 세션의 팀에이전트: 1차에서 처리 (정상 경로)
  - 1차에서 누락된 현재 팀 pane: 2차에서 처리 (ID 기반)
  - 1차/2차에서 누락된 현재 세션 잔류 에이전트: 3차에서 처리 (parent-session-id 기반)
  - 다른 세션의 에이전트: 3차에서도 절대 건드리지 않음 (소유권 필터)
```

---

### Step 4: 4차 정리 — 메인 pane 외 전체 pane 강제 kill (항상 실행)

> **목적**: 1~3차 종료 결과와 무관하게 메인 pane 외 모든 잔류 pane을 제거.
> 에이전트 spawn 실패로 생긴 빈 bash pane, 비정상 종료 잔류 pane 등을 포함하여 완전히 정리.

```yaml
도구: Bash
시점: 3차 종료 완료 직후 (항상 실행 — 예외 없음)
원칙:
  - 메인 pane(자기 자신) 제외 + 다른 세션 pane 제외
  - 현재 세션의 현재 window에서 메인 pane(pane_index 가장 작은 것) 외 전체 kill
  - 에이전트든 빈 bash든 attach 실패 잔류든 무조건 제거

절차:
  1. 현재 세션:window 확인 (tmux display-message -p "#{session_name}:#{window_index}")
  2. 해당 window의 전체 pane 목록 조회
  3. pane_index가 가장 작은 pane = 메인 pane (보존)
  4. 나머지 전체 pane → tmux kill-pane

명령어_예시:
  # 현재 session:window 내 메인 pane(index 최소값) 제외 전체 kill
  MAIN_WIN=$(tmux display-message -p "#{session_name}:#{window_index}")
  MAIN_PANE=$(tmux list-panes -t "$MAIN_WIN" -F "#{pane_index}" | sort -n | head -1)
  tmux list-panes -t "$MAIN_WIN" -F "#{pane_index}" | grep -v "^${MAIN_PANE}$" | xargs -I{} tmux kill-pane -t "${MAIN_WIN}.{}"

항상_실행_이유:
  - 1~3차: 에이전트 프로세스 감지 기반 → 빈 bash pane 감지 불가
  - 4차: 프로세스 종류 무관, pane 존재 자체를 기준으로 정리
  - spawn 실패(can't find pane) 후 생긴 빈 pane까지 확실히 제거
```

**에러 처리**:
```yaml
4차_정리_실패:
  - tmux 세션 없음: 무시 (정상)
  - kill-pane 실패 (pane 이미 없음): 무시
  - 중요: 4차 실패가 전체 kFinish 진행을 차단하지 않음
```

---

### Step 5: TeamDelete 실행

```yaml
도구: TeamDelete
시점: 1~4차 정리 완료 후
효과:
  - ~/.claude/teams/{team-name}/ 디렉토리 삭제 (config.json 포함)
  - ~/.claude/tasks/{team-name}/ 디렉토리 삭제
  - 세션 팀 컨텍스트 클리어

**중요**: config.json 삭제는 여기서만 수행. Step 1 이전에 삭제 금지.
```

---

### Step 6: 에이전트 발동 통계 출력

```yaml
도구: Read
경로: /tmp/claude_agent_stats.json
형식:
  {
    "team": 4,
    "sub": 0,
    "task": 12
  }

출력_예시:
  📊 **에이전트 발동 통계**
  - 팀에이전트: 4회 (kPlan, kDev, kTest, kDone)
  - 서브에이전트: 0회
  - Task 도구: 12회
```

---

### Step 7: 타이머 정지

```yaml
도구: Bash
명령어: echo "stop" > ~/.claude/task_timer_signal.txt
효과: 백그라운드 타이머 프로세스 종료
```

---

### Step 8: pipeline_state → IDLE 리셋

```yaml
도구: Bash
명령어: echo "IDLE" > /tmp/claude_pipeline_state
효과: 파이프라인 상태를 IDLE로 초기화 (다음 작업 대기)
```

---

### Step 9: 임시 파일 삭제

```yaml
도구: Bash
명령어:
  # kOID 기반 파일 삭제
  rm -f /tmp/claude_current_koid
  rm -f /tmp/claude_koid_*
  rm -f /tmp/claude_errors_*.md
  rm -f /tmp/claude_team_created
  rm -f /tmp/claude_task_classification
  rm -f /tmp/claude_agent_stats.json
  rm -f /tmp/claude_file_assignment.json
```

---

### Step 10: 종료 배너 출력

```yaml
형식:
  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  🎉 **작업 완료**
  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  | 항목              | 값                    |
  |-------------------|-----------------------|
  | 커밋 해시         | {git rev-parse HEAD}  |
  | 변경 파일 수      | {git diff --stat}     |
  | 파이프라인 상태   | IDLE                  |

  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

**데이터 수집**:
```bash
# 커밋 해시
git rev-parse --short HEAD

# 변경 파일 수
git diff HEAD~1 --stat | tail -1

# 파이프라인 상태
cat /tmp/claude_pipeline_state
```

---

## 실행 순서 (엄수)

```
1. 1차 종료: config.json 기반 전체 멤버 shutdown (모든 멤버!)
   - 팀 설정 파일 읽기 (~/.claude/teams/{team-name}/config.json)
   - 리더 제외 전체 멤버에게 shutdown_request 발송
   - config.json은 이 단계에서 절대 삭제 금지
   ↓
2. shutdown_response 수신 확인 (타임아웃 30초)
   - approve: 정상 종료 확인
   - reject/무응답: 경고 출력 + 계속 진행
   ↓
3. 2차 종료: tmux pane ID 기반 잔류 에이전트 탐색 (안전망)
   - tmux list-panes로 잔류 pane 확인
   - Step 1에서 종료 안 된 에이전트 pane 강제 정리
   ↓
4. 3차 종료: tmux capture-pane 기반 에이전트명 추출 shutdown (최종 안전망)
   - 전체 tmux pane 스캔 (에이전트 바이너리 프로세스만)
   - capture-pane -S -50으로 --parent-session-id 추출 → 현재 세션 소속만 필터
   - ⚠️ 다른 세션 소속 pane은 절대 스킵 (소유권 불일치)
   - @에이전트명 추출 → SendMessage(shutdown_request) 발송
   - 무응답(5초) 시 tmux kill-pane Fallback
   ↓
5. 4차 정리: 메인 pane 외 전체 pane 강제 kill (항상 실행 — 예외 없음)
   - 현재 세션:window에서 pane_index 최솟값 = 메인 pane (보존)
   - 나머지 전체 kill (에이전트든 빈 bash든 무조건)
   - 이유: 1~3차는 에이전트 프로세스 기반 → 빈 bash pane 감지 불가
   ↓
6. TeamDelete (config.json + 팀 디렉토리 삭제)
   - 1~4차 정리 완료 후에만 실행
   - config.json은 여기서 삭제됨
   ↓
7. 에이전트 발동 통계 출력 (Read claude_agent_stats.json)
   ↓
8. 타이머 정지 (echo stop > task_timer_signal.txt)
   ↓
9. pipeline_state → IDLE 리셋
   ↓
10. 임시 파일 삭제 (rm -f /tmp/claude_*)
   ↓
11. 종료 배너 출력
```

---

## 에러 처리

```yaml
config.json_읽기_실패:
  - 파일 없음: 경고 출력 "⚠️ config.json 없음 → tmux 2차 탐색으로 Fallback"
  - 파일 손상: 경고 출력 + Step 2(tmux)로 진행
  - 중요: config.json 없어도 Step 2(tmux 탐색)로 에이전트 정리 가능

팀에이전트_종료_실패:
  - shutdown_response reject 시: 에러 메시지 출력 + 사용자 판단 대기
  - 응답 없음 (30초 타임아웃): 경고 출력 + Step 2(tmux)에서 재확인

tmux_pane_정리_실패 (2차):
  - tmux 세션 없음: 무시 (정상)
  - kill-pane 실패: 경고 출력 + 계속 진행

3차_종료_실패:
  - capture-pane 실패: 해당 pane 스킵 + 계속 진행
  - --parent-session-id 미발견: 소유권 불명 → 해당 pane 스킵 (안전 방향)
  - --parent-session-id 불일치: 다른 세션 소속 → 해당 pane 스킵 + 경고 출력
  - @에이전트명 미발견 (소유권 확인 통과): 이미 종료된 pane → kill-pane으로 정리
  - SendMessage 발송 실패: 경고 출력 + kill-pane Fallback
  - shutdown_response 무응답 (5초): 경고 출력 + kill-pane Fallback
  - 중요: 3차 종료 실패가 전체 kFinish 진행을 차단하지 않음

TeamDelete_실패:
  - 팀 디렉토리 없음: 무시 (정상)
  - 권한 오류: 경고 출력 + 계속 진행

통계_파일_없음:
  - claude_agent_stats.json 없음: "통계 파일 없음" 메시지 출력 + 스킵

Git_커밋_없음:
  - git rev-parse 실패: "커밋 없음" 표시
  - git diff 실패: "변경 파일 없음" 표시
```

---

## 병렬화 정책

**kFinish는 순차 실행**:
- 1차 종료 → 2차 종료 → 3차 종료 → **4차 정리(항상)** → TeamDelete → 통계 → 타이머 → 리셋 → 삭제 → 배너
- 각 단계는 이전 단계 완료 후 실행 (병렬화 불가)
- **4차 정리는 1~3차 결과 무관하게 항상 실행** (예외 없음)

---

## 금지 사항

1. **팀에이전트에서 kFinish 실행 금지** — 메인 에이전트 전용
2. **kDone 이전 실행 금지** — kDone의 모든 서브스킬 완료 후에만 (hook: pipeline_gate.sh가 DONE 상태만 허용)
3. **프로젝트 고유 경로 사용 금지** — 범용 스킬 원칙 (프로젝트명, 포트 등 없음)
4. **pipeline_state 강제 변경 금지** — IDLE로만 리셋 (다른 상태로 변경 금지)
5. **kFinish 건너뛰기 금지** — IDLE 전환 시 kFinish 마커 필수 (hook: kfinish_idle_guard.sh가 물리 차단)
6. **config.json 사전 삭제 금지** — Step 1(전체 멤버 shutdown) 전에 config.json 삭제 절대 금지 (hook: team_shutdown_guard.sh가 멤버 잔류 시 TeamDelete 차단)
7. **다른 세션 에이전트 종료 금지** — 3차 종료에서 --parent-session-id가 현재 세션과 불일치하는 pane은 절대 kill/shutdown 금지

---

## 반환 메시지 형식

```
✅ kFinish 완료

- 팀에이전트 종료: 4개
- tmux pane 정리: 2개
- 에이전트 발동 통계: team=4 sub=0 task=12
- pipeline_state: IDLE

파일: /mnt/c/DATA/Project/Mars/.claude/skills/kFinish/SKILL.md
📊 spawn_stats: team=0 sub=0 task=0
```

---

## 참조

- **kDone**: kFinish 실행 직전 단계
- **kDone_cleanup**: kFinish로 이관된 로직 (팀 종료, tmux 정리)
- **SendMessage**: 팀에이전트 종료 프로토콜
- **TeamDelete**: 팀 리소스 정리 도구
