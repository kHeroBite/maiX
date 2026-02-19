---
name: kDev
description: "코드 구현 메인 라우터. 구현 절차, Lock 획득, 도구 선택. Auto-activates when: implementing code, modifying files."
---
# kDev — 코드 구현 메인

## 구현 절차

### 의도 Lock 획득 (첫 동작 — 필수)

```yaml
절차:
  1. 프로젝트 .claude/locks/ 디렉토리 확인 (없으면 생성)
  2. 대상 파일 목록 확정 (TODO에 명시된 수정 파일)
  3. 기존 의도 Lock JSON 스캔 → 파일 목록 교집합 확인
  4. 충돌 판정:
     충돌_없음: intent_{세션ID}.json 생성 → 정상 진행
     부분_충돌: 비충돌 파일만 등록 → blocked 파일은 kDev_lock 절차
     전체_충돌: ntfy 알림 후 대기 → kDev_lock 절차
  5. Stale 검사: 충돌 Lock의 PID 사망 / TTL 120분 초과 → 자동 삭제

Lock_유지: kDev → kTest → kDone까지 (해제는 kDone이 담당)
kTest_실패_복귀: Lock 유지됨 (재획득 불필요)
상세: /kDev_lock 참조
```

### 롤백 체크포인트 (kDev 진입 시 — 자동)

```yaml
목적: kTest 실패 시 안전한 복귀 지점
실행: rollback_checkpoint.sh hook이 kDev 스킬 호출 시 자동 실행

Hook_자동_절차:
  1. HEAD hash 기록: /tmp/claude_rollback_hash
  2. git tag 생성: kdev-checkpoint-YYYYMMDD-HHMMSS
  3. 이전 체크포인트 정리 (최근 5개만 유지)

활용_시나리오:
  kTest_경미_실패: 해당 코드만 수정 (체크포인트 불사용)
  kTest_3회+_실패: git reset --hard {checkpoint} 고려
    → 사용자 확인 필수 (파괴적 작업)
  TEST→DEV_재진입: 새 체크포인트 생성 (기존 유지)

주의:
  - 체크포인트 = 참조용 (자동 롤백 절대 금지)
  - git reset = 사용자 명시적 승인 후에만
  - uncommitted 변경 있으면 git stash 선행
```

### 기본 준비

- 디버그 모드 OFF 확인
- PROJECT.md/DATABASE.md 참조
- 검증 전략 명시

### 코드 구현

```yaml
기본_도구: MCP Serena (C# 심볼 수정)
Fallback: Claude Code Edit (Designer.cs, 비코드 파일)

Debug2_로그: 주요 분기점에 Debug2 로그 추가
TODO_업데이트: 각 항목 완료 시 즉시 업데이트
```

### SQL 위치 검증 (구현 전 체크리스트)

```yaml
규칙: SQL 문자열은 반드시 Mars.Shared/Queries/*.cs에서만 정의
금지: 폼/컨트롤 파일(.cs, Mars/ 또는 Mars/Controls/)에 SELECT/INSERT/UPDATE/DELETE 문자열 직접 작성
허용: Queries 클래스의 const string 참조 또는 Build*() 메서드 호출
검증:
  구현_시작_전:
    - 새 SQL 필요 → Mars.Shared/Queries/ 해당 파일에 먼저 정의
    - 기존 SQL 재사용 가능 여부 확인 (Grep 검색)
  구현_완료_후:
    - 수정 파일에 SELECT/INSERT/UPDATE/DELETE 문자열 잔존 여부 검사
강제: PreToolUse Hook이 위반 자동 차단 (inline_sql_guard.sh)
```

### 배치 빌드 검증 (TODO 5개 이상 시)

```yaml
규칙: 3개 항목 수정마다 중간 빌드(dotnet build) 실행
목적: 오류 조기 발견, 실패 범위 축소
절차:
  1. TODO 1~3 수정 → dotnet build → 성공 확인
  2. TODO 4~6 수정 → dotnet build → 성공 확인
  3. 전체 완료 → kTest 진행
스킵: TODO 4개 이하면 배치 없이 연속 수정
실패_시_롤백:
  1. git stash로 해당 배치 변경분 격리
  2. 빌드 오류 원인 분석 후 수정
  3. git stash pop → 수정 적용
  4. 해당 배치부터 재개
참고: 배치 빌드는 kTest_build와 다름 (배치 빌드 = 구현 중 검증, kTest_build = 최종 빌드)
```

### 프로그램 실행 중 수정

- .cs 파일은 프로그램 실행 중에도 수정 가능 (파일 잠금 없음)

### 코드 수정 도구 선택

```yaml
Serena_사용:
  - C# 코드 (.cs) 심볼 수정
  - 메서드/클래스 추가 (insert_after_symbol)
  - 변수/함수명 리팩토링 (rename_symbol)
  - 참조 추적 (find_referencing_symbols)
  - NTFS rsync 불필요 (LSP 경유)

Claude_Code_Edit_사용 (NTFS rsync 방식):
  - Designer.cs 파일 (LSP 불안정)
  - 비코드 파일 (MD, JSON, YAML, XML, RESX)
  - 주석, 문자열 리터럴 수정
  - Serena 오류 시 Fallback
  - 절차: cp → Read → Edit (ext4) → rsync --inplace (NTFS)
```

> NTFS 파일 수정: CLAUDE.md 절차 준수 (cp → ext4 Edit → rsync --inplace)

### ext4 작업 파일 보호 (L-037 — 절대 규칙)

```yaml
문제: cp NTFS→ext4 시 다른 에이전트의 미커밋 편집이 덮어써짐
Hook_방어: ext4_freshness_guard.sh가 ext4 파일이 NTFS보다 최신이면 cp 차단

규칙:
  cp_전_확인: ext4 작업 파일이 이미 존재하면 cp 하지 않고 기존 ext4 파일 사용
  병렬_에이전트: 자신에게 할당된 파일의 ext4 복사본만 cp 가능
  다른_에이전트_파일: cp 절대 금지 — 해당 에이전트의 편집이 소실됨
  안전한_순서: rsync ext4→NTFS 먼저 완료 → 그 후에야 다른 에이전트가 cp NTFS→ext4 가능
금지:
  - ext4에 편집 중인 파일을 cp로 덮어쓰기
  - 다른 에이전트 할당 파일 경로로 cp 실행
```

---

## 스킬 자동 로드 (kDev 진입 시 — TODO 기반)

> kO 0.5단계에서 TODO에 명시한 "활용 가능 스킬"을 kDev 진입 시 사전 호출.
> skill_recommender.sh Hook이 리마인더를 출력하지만, 아래 규칙을 직접 따르는 것이 더 확실.

```yaml
시점: TODO 비판적 검토 직후, 코드 작성 직전
절차:
  1. TODO 헤더에서 "활용 가능 스킬" 목록 확인
  2. 목록에 있는 스킬을 Skill() 호출로 사전 로드
  3. 스킬 없으면 스킵

조건부_자동_로드 (TODO에 명시 없어도 감지 시 호출):
  차트_코드_수정 (LiveCharts|Series|Axis|CartesianChart):
    → Skill('kSkill_livecharts2')
  WinForms_UI_신규_생성 (new Form|Designer.cs 생성|InitializeComponent):
    → Skill('domain-winforms')
  DB_스키마_변경 (ALTER TABLE|CREATE TABLE|ADD COLUMN|DROP):
    → Skill('domain-database')
  C#_리팩토링 (Extract Method|Rename|복잡도 높은 메서드 분할):
    → Skill('domain-csharp')
  라이브러리_신규_도입 (NuGet|PackageReference|새 using):
    → Skill('domain-context7')
  인터페이스/공통모듈_변경 (Mars.Shared 시그니처 변경):
    → Skill('kDev_impact')

금지:
  - 관련 없는 스킬 무분별 로딩 (컨텍스트 낭비)
  - 이미 로드된 스킬 중복 호출
```

## TODO 비판적 검토 (kDev 진입 시 필수)

```yaml
시점: kDev 진입 직후, 코드 작성 전
절차:
  1. TODO 파일 전체 읽기
  2. 비판적 검토:
     - 모호한 지시 없는가? (파일 경로, 코드, 예상 출력 명시됨?)
     - 의존성 순서 맞는가? (A를 먼저 수정해야 B 가능?)
     - 누락된 항목 없는가? (사이드 이펙트, 참조 수정)
  3. 우려사항 발견 시: 코드 작성 전 해결 (kPlan 복귀 또는 자체 보완)
금지: TODO 검토 없이 바로 코딩 돌입
```

## Stop and Ask (중단 조건)

```yaml
즉시_중단_후_질문:
  - 차단 발생 (파일 없음, API 미응답, 빌드 불가)
  - TODO 지시 불명확 (어떤 파일? 어떤 메서드?)
  - 검증 2회 반복 실패 (동일 접근법)
  - 계획 변경 감지 (사용자가 TODO 업데이트)
대응: 추측 금지 → kDebug 또는 사용자 질문
```

---

## 서브스킬 디스패치

```yaml
병렬_구현: /kDev_parallel 호출
코드_리뷰: /kDev_review 호출 (구현 에이전트 2개+ 완료 시)
영향도_분석: /kDev_impact 호출 (인터페이스/공통모듈 변경 시)
파일_Lock: /kDev_lock 호출 (충돌 시 대응 절차)
```
