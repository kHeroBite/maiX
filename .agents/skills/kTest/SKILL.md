---
name: kTest
description: "빌드/테스트 라우터 — Build/Deploy/Run/Quality 디스패치"
---
# kTest — 빌드/테스트 라우터

## 실행 파이프라인 (핵심 순서)

```
Phase 1: 대상 프로젝트 병렬 빌드 & 배포
  ┌─ [MarsGW] 빌드 → 배포 (run_in_background)
  ├─ [Mars] 빌드 → 배포 (run_in_background)
  └─ [Mars.Mobile] 빌드 → 배포 (run_in_background)
  각 프로젝트: 빌드 완료 즉시 배포 연속 진행
  Mars.Shared는 각 빌드 시 자동 포함 (ProjectReference)

Phase 2: 순차 실행 & 테스트 (고정 순서)
  1순위: MarsGW (원격 서버)
  2순위: Mars (로컬 실행)
  3순위: Mars.Mobile (에뮬레이터)
  각 프로젝트: 앞 프로젝트 완료 후 다음 진행

Phase 3: 품질 검증 (전체)
  요청-결과 대조, DB 정합성, UI/UX 품질

CONDITIONAL: DB 변경 시 정합성 검증
```

## 수정 대상 자동 감지

```yaml
Mars만_수정: Mars만 빌드&배포&테스트
MarsGW만_수정: MarsGW만 빌드&배포&테스트
Mars.Mobile만_수정: Mars.Mobile만 빌드&배포&테스트
Mars.Shared_수정: 전체 대상 (MarsGW, Mars, Mars.Mobile 모두)
복수_프로젝트_수정: 해당 프로젝트들 모두
```

---

## 디스패치 로직

```yaml
Build: Skill('kTest_build') 호출
  → 프로세스 종료 + 병렬 빌드 + 증거 수집

Deploy: Skill('kTest_deploy') 호출
  → 빌드 완료 즉시 배포 (로컬/모바일/원격)

Run: Skill('kTest_run') 호출
  → 순차 테스트 (로그/API/스크린샷) + 스마트 라우팅

Quality: Skill('kTest_quality') 호출
  → 요청-결과 대조 + DB 정합성 + UI/UX 검증
```

---

## 전체 흐름도

```
kDev 완료
     ↓
[Phase 1] 병렬 빌드&배포 ─────────── 실패 → kDev (해당 프로젝트만)
  ┌─ MarsGW: 빌드 → 배포 (Mars.Shared 자동 포함)
  ├─ Mars: 빌드 → 배포 (Mars.Shared 자동 포함)
  └─ Mars.Mobile: 빌드 → 배포 (Mars.Shared 자동 포함)
     ↓ (전체 완료 대기)
[Phase 2] 순차 테스트 ────────────── 실패 → 스마트 라우팅
  1. MarsGW: 상태확인/로그
  2. Mars: REST API/로그/스크린샷
  3. Mars.Mobile: 설치/실행/로그
     ↓
[Phase 3] 품질 검증 ──────────────── 실패 → kPlan
     ↓
⛔ Phase 3 완료 = kTest 완료 → kDone 진입 가능
```

## TDD 강제 규칙 (해당 시)

```yaml
적용_조건: 단위 테스트 프레임워크 존재 시 (xUnit, NUnit 등)
Iron_Law: 실패하는 테스트 없이 프로덕션 코드 작성 금지
절차:
  1. RED: 실패 테스트 작성 → 실행 → 실패 확인 (실패 이유 = 기능 미구현)
  2. GREEN: 최소 코드로 테스트 통과 → 실행 → 통과 확인
  3. REFACTOR: 코드 정리 (테스트 유지)
금지:
  - 코드 먼저 작성 후 테스트 (tests-after = 구현 편향)
  - 테스트 즉시 통과 (기존 동작 테스트 중 → 테스트 수정 필요)
  - GREEN 단계에서 테스트 외 기능 추가 (YAGNI)
합리화_차단:
  - "너무 간단해서 테스트 불필요" → 간단한 코드도 깨짐
  - "나중에 테스트" → 즉시 통과 테스트는 증명 안 함
  - "이미 수동 테스트" → Ad-hoc ≠ 체계적, 재실행 불가
Mars_예외: Mars 프로젝트는 xUnit 미사용 → REST API/로그/스크린샷 검증이 TDD 대체
```
