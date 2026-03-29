# HISTORY.md — MaiX 프로젝트 작업 이력

> PROJECT.md 작업 이력 테이블의 상세 보완본

## 2026-03-29: Phase 0 인프라 정비 — Email AI 분류 필드 + FTS5 검색 + AI 자동 트리거

**커밋**: (kdone_git 완료 후 기재)
**분류**: Fast Path (미디엄)
**수정 파일**: 7개 (수정 4 + 신규 5 — Migration 4 + Queries 1)

### 변경 내역

#### 1. Email.cs — AI 분류 필드 4개 추가
- `AiCategory` (string, NULL): AI 자동 분류 카테고리 (긴급/업무/일반)
- `AiPriority` (string, NULL): AI 우선순위 (high/medium/low)
- `AiActionRequired` (bool, DEFAULT false): AI 액션 필요 여부
- `AiSummaryBrief` (string, NULL): AI 간략 요약 (1-2줄)

#### 2. Migration 20260329000001 — AI 분류 컬럼 4개 DB 추가
- Emails 테이블에 AiCategory/AiPriority/AiActionRequired/AiSummaryBrief 컬럼 추가
- ModelSnapshot 업데이트

#### 3. Migration 20260329000002 — FTS5 가상 테이블 + 트리거
- `EmailsFts` FTS5 가상 테이블 생성 (Subject, Body, [From], AiSummaryBrief)
  - SQLite 예약어 `From` → `[From]` 대괄호 이스케이프 적용 (역라우팅 수정)
- INSERT/UPDATE/DELETE 트리거 3종
- 초기 인덱싱: 기존 Emails 데이터 → EmailsFts 일괄 INSERT

#### 4. EmailSearchService.cs — FTS5 검색 + LIKE 폴백
- FTS5 MATCH 검색 우선 시도
- FTS5 실패 시 LIKE 폴백 구조

#### 5. EmailFtsQueries.cs (신규) — FTS5 SQL 쿼리 분리
- `mAIx/Queries/EmailFtsQueries.cs`: FTS5 관련 SQL 쿼리 상수 분리 정의

#### 6. BackgroundSyncService.cs — AI 배치 루프 AiCategory 자동 분류
- PriorityScore 기반 AiCategory 자동 매핑 통합
  - PriorityScore >= 70 → "긴급"
  - PriorityScore >= 40 → "업무"
  - else → "일반"

### 테스트 결과
- 빌드: 오류 0개 ✅
- 실행: 정상 (health 200, Migration 자동 적용) ✅
- 로그: ERROR 0건 ✅

---

## 2026-03-28: 메일탭 UX 완성도 마지막 10% — INPC + 다중선택 도구바 + PreviewText

**커밋**: (kdone_git 완료 후 기재)
**분류**: Fast Path (미디엄)
**수정 파일**: 7개

### 변경 내역

#### 1. INotifyPropertyChanged 구현
- `Email.cs`: `INotifyPropertyChanged` 인터페이스 구현
  - INPC 적용 속성: `IsRead`, `FlagStatus`, `Categories`
  - 신규 속성: `PreviewText`(NotMapped, Graph API bodyPreview), `PreviewOrSummary`(SummaryOneline ?? PreviewText 폴백)
- `Folder.cs`: `INotifyPropertyChanged` 인터페이스 구현
  - INPC 적용 속성: `UnreadItemCount`, `IsFavorite`, `FavoriteOrder`

#### 2. 다중 선택 일괄 작업 도구바 (BulkActionBar)
- `MainWindow.xaml`: 메일 목록 하단 오버레이 BulkActionBar 추가
  - `IsMultipleEmailsSelected`(2건+ 선택 시 Visibility)
  - 선택 건수 표시, 읽음/읽지않음/플래그/플래그해제/삭제/전체취소 버튼
  - `SelectionChanged="EmailListBox_SelectionChanged"` 이벤트 바인딩
- `MainViewModel.cs`: 커맨드 및 속성 추가
  - `SelectedEmailCount`, `IsMultipleEmailsSelected` 속성
  - `BulkMarkReadCommand`, `BulkMarkUnreadCommand`, `BulkFlagCommand`, `BulkUnflagCommand`, `BulkDeleteCommand` 커맨드
- `MainWindow.xaml.cs`: 7개 핸들러 추가
  - `EmailListBox_SelectionChanged`, `BulkMarkRead_Click`, `BulkMarkUnread_Click`, `BulkFlag_Click`, `BulkUnflag_Click`, `BulkDelete_Click`, `BulkSelectAll_Click`

#### 3. PreviewText (미리보기 텍스트)
- `GraphMailService.cs`: `selectFields`에 `bodyPreview` 추가
- `BackgroundSyncService.cs`: bodyPreview → `email.PreviewText` 매핑
- `MainWindow.xaml`: `SummaryOneline` 바인딩 → `PreviewOrSummary`로 변경 (AI 요약 없을 때 bodyPreview 폴백 표시)

#### 기타 수정
- XAML FluentIcon 심볼명 오류 수정: `FolderMove24` → `FolderArrowRight20` (L-273)

### 테스트 결과
- 빌드: 오류 0개 ✅ (경고 170개 — 기존 패키지 경고)
- 배포: 정상 실행 ✅ (PID 확인)
- 런타임: 신규 ERROR 0건 ✅
- UI: 13건 메일 표시, BulkActionBar 코드 검증, PreviewOrSummary 바인딩 확인 ✅
- 품질: 3/3 기능 대조 완료 ✅

---

## 2026-03-29 — Phase 1: AI 스마트 분류 UI + 첨부파일 AI 분석 + 예약발송 + 발송취소

**분류**: k3 (Normal)
**수정 파일**: 17개 (신규 6 + 수정 11)

### 변경 내역

#### 1. AI 카테고리 배지 UI
- `AiCategoryToBadgeConverter.cs` 신규 — AI 카테고리 문자열 → 배지 색상/텍스트 IValueConverter
- `App.xaml` — AiCategoryToBadgeConverter 리소스 등록
- `MainWindow.xaml` — 메일 목록 3행 AI 배지 레이아웃 + 읽지않음 인디케이터

#### 2. AI 정렬+필터
- `MainViewModel.cs` — AiPriority 정렬 항목, FilterActionRequired, SelectedAiCategory 필터
- `MainWindow.xaml.cs` — 필터 도구바 이벤트 핸들러
- `Email.cs` — PreviewOrSummary 프로퍼티 확장 (AiSummaryBrief 우선 폴백)

#### 3. 첨부파일 AI 분석
- `EmailAnalyzer.cs` — PrepareEmailData에 첨부파일 텍스트 포함
- `EmailAnalysisResult.cs` — AttachmentSummary, AttachmentRiskLevel 필드 추가
- `BackgroundSyncService.cs` — RunAnalysisBatchLoopAsync Include Attachments

#### 4. 예약발송+발송취소
- `Email.cs` — ScheduledSendTime (DateTime?) 필드 추가
- `ComposeViewModel.cs` — ScheduleMailAsync + 5초 카운트다운 CancellationToken 취소
- `ComposeWindow.xaml` + `.cs` — 예약발송 버튼 UI 및 이벤트
- `ScheduledSendDialog.xaml` + `.cs` 신규 — DateTimePicker 예약시간 선택 다이얼로그
- `BackgroundSyncService.cs` — 예약발송 루프 추가
- Migration `20260329000003_AddScheduledSendTime` (3파일)

### 변경 파일
- 신규: AiCategoryToBadgeConverter.cs, ScheduledSendDialog.xaml, ScheduledSendDialog.xaml.cs, Migration 3파일
- 수정: App.xaml, MainWindow.xaml/cs, MainViewModel.cs, Email.cs, EmailAnalysisResult.cs, EmailAnalyzer.cs, BackgroundSyncService.cs, ComposeViewModel.cs, ComposeWindow.xaml/cs, mAIxDbContextModelSnapshot.cs

### 테스트 결과
- 빌드: 오류 0개 ✅
- 실행: 정상 ✅
- 로그: ERROR 0건 ✅
- UI: PASS ✅
- 품질: 8/8 Task 확인 ✅
