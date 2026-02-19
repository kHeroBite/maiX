---
name: kTest_build
description: "빌드 — 프로세스 종료 + 병렬 빌드 + 증거 수집"
---
# kTest_build — 빌드

> **프로젝트별 빌드 명령**: kInfra_{project} 및 kTest_build_{project} 참조

## 빌드 전 선행 작업 (필수)

```yaml
프로세스_종료:
  이유: 실행 중인 프로세스가 DLL/exe를 잠금
  절차: kInfra_{project} 프로젝트스킬의 shutdown 명령 실행
  규칙: 모든 dotnet build 전에 항상 실행 (예외 없음)
```

## 빌드 명령어

```yaml
명령: kInfra_{project} 프로젝트스킬의 빌드 명령 참조
백그라운드: run_in_background: true (항상 — 예외 없음)
대기_중_compact: context 30% 미만이면 /compact 실행 후 TaskOutput 대기 (kO 범용 규칙)
성공_감지: "Build succeeded" 패턴
실패_감지: "Build FAILED" 패턴
```

## background 빌드 증거 수집

```yaml
문제: run_in_background 빌드는 PostToolUse에 stdout 비어있음 → 자동 증거 수집 불가
자동_캡처: 빌드 결과를 tail/cat(Bash)으로 읽으면 evidence_gate가 자동 감지
Fallback: Read 도구로 결과를 확인한 경우 자동 감지 불가 → 수동 touch 필수
  명령: touch /tmp/claude_test_build_ok
  시점: background 빌드 결과 확인 후 "Build succeeded" 확인된 직후
  조건: Read 도구로 결과를 읽어서 evidence_gate를 안 거친 경우에만
```

## 선택적 빌드 (Selective Build)

> 변경된 파일만 감지하여 해당 프로젝트만 빌드. 불필요한 빌드 시간 절약.

```yaml
감지_방법:
  명령: git status --porcelain (커밋되지 않은 변경 파일 목록)
  대상: 수정(M), 추가(A/?), 이름변경(R) 파일
  제외: 삭제(D) 파일, .gitignore 대상

프로젝트_매핑:
  절차: 변경 파일 경로의 최상위 폴더로 프로젝트 식별
  매핑_테이블: kTest_build_{project} 프로젝트스킬 참조
  예시: "Mars/Form1.cs" → Mars 프로젝트

빌드_대상_결정:
  단일_프로젝트: 해당 프로젝트만 빌드
  복수_프로젝트: 관련 프로젝트 모두 빌드 (병렬)
  공통_라이브러리: 참조하는 모든 프로젝트 빌드 (kTest_build_{project} 매핑 참조)
  비코드_파일만: .md, .claude/, 루트 설정 → 빌드 스킵 (kTest에 통보)
  감지_실패_Fallback: 전체 솔루션 빌드

빌드_스킵_판단:
  조건: 변경 파일이 모두 비코드 파일 (빌드 불필요)
  행동: "빌드 스킵 — 비코드 파일만 변경" 출력 + 배포 단계로 직행
  비코드: .md, .json (스킬/설정), .sh, .bat (스크립트), .gitignore
  주의: .csproj, .sln 변경은 빌드 필수 (프로젝트 구조 변경)
```

## 병렬 빌드 순서

```yaml
선행: 실행 중 프로세스 종료
대상: 선택적 빌드로 결정된 프로젝트만 (전체가 아닌 변경 대상만)
실행: 각 프로젝트별 run_in_background: true
순서: 병렬 (동시 실행)
Mars.Shared: ProjectReference로 자동 선행 빌드 (별도 빌드 불필요)
연속_진행: 빌드 완료 즉시 배포로 연속 진행
```

## 빌드 오류 분류 및 대응

```yaml
현재_세션_파일:
  판단: git diff --name-only 또는 이 세션에서 수정한 파일
  조치: 즉시 수정 후 재빌드

다른_세션_파일:
  판단: 현재 세션에서 수정하지 않은 파일
  조치: kDev_lock (수정 금지, 대기)

실패_시: kDev 복귀 (빌드 오류만 수정)
```

## 빌드 에러 자동 진단 (Hook 자동)

build_diagnosis.sh hook이 PostToolUse:Bash에서 dotnet build 실패를 자동 감지하고 에러 패턴을 분류합니다.

```yaml
자동_분류_패턴:
  CS0117 (멤버_미존재):
    원인: 삭제된 메서드/프로퍼티를 호출 중
    제안: find_referencing_symbols로 호출부 추적 → 대체 멤버로 교체
    예시: "'MemberQueries' does not contain a definition for 'GetAll'"

  CS0246 (타입_미발견):
    원인: using 누락 또는 참조 프로젝트 미포함
    제안: 해당 타입의 네임스페이스 찾아 using 추가
    예시: "The type or namespace name 'JsonConvert' could not be found"

  CS0103 (이름_미존재):
    원인: 변수/메서드명 오타 또는 삭제된 심볼 참조
    제안: find_symbol로 올바른 이름 검색
    예시: "The name 'oldMethodName' does not exist"

  CS1061 (멤버_없음):
    원인: 인스턴스에 해당 멤버 미존재
    제안: 해당 클래스의 실제 멤버 목록 확인
    예시: "'DataGridView' does not contain a definition for 'NewColumn'"

Hook_동작:
  감지: PostToolUse Bash에서 "Build FAILED" + "error CS" 패턴
  출력: /tmp/claude_build_diagnosis.txt에 분류 결과 저장
  참조: kDev 복귀 시 진단 파일 자동 참조
  차단: 없음 (PostToolUse = 감시만, 차단 불가)
```
