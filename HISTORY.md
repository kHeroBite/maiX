# HISTORY.md — MaiX 프로젝트 작업 이력

> PROJECT.md 작업 이력 테이블의 상세 보완본

## 2026-04-01 — 받은편지함 읽음 상태 동기화 실패 근본 수정 (DbContext 오염 방지 + SyncReadStatusAsync 독립 실행)

**분류**: Fast Path (k3)
**수정 파일**: 1개 (BackgroundSyncService.cs)

### 변경 내역

#### BackgroundSyncService.cs — UNIQUE 위반 DbContext 오염 방지 + 독립 try/catch 분리

- **근본 원인**: `SaveEmailsAsync` 배치 저장 중 `InternetMessageId+ParentFolderId` UNIQUE 제약 위반 → DbContext 오염 → 같은 try 블록의 `SyncReadStatusAsync` 미도달 → 읽음 상태 영구 미동기화
- **수정 1 — SaveEmailsAsync**: 배치 `SaveChangesAsync` → 개별 저장 + catch 시 `Entry(email).State = EntityState.Detached` (DbContext 오염 상태 즉시 해제)
- **수정 2 — SyncFavoriteFoldersAsync**: `SyncFolderAsync`와 `SyncReadStatusAsync`를 독립 try/catch 블록으로 분리 (메일 저장 실패가 읽음 동기화를 차단하지 않음)
- **수정 3 — SyncAccountAsync**: 동일 패턴 적용 — 폴더 동기화와 읽음 동기화 독립 실행 보장

### 교훈 기록
- L-286: EF Core DbContext 오염 방지 — UNIQUE 위반 시 개별 저장 + Detach 패턴 (Level 1)

---

## 2026-04-01 — 메일 동기화 읽음 카운트 불일치 근본 수정 + 설정 동기화 대메뉴 통합

**분류**: Fast Path (k3)
**수정 파일**: 4개

### 변경 내역

#### 1. GraphMailService.cs — Graph API 동기화 범위 확장 + 미읽음 목록 조회 신규
- `GetMessagesReadStatusAsync`: days 파라미터 7→30으로 확장 (7일 이상 된 메일의 읽음 상태 동기화 누락 방지)
- `GetUnreadMessageIdsAsync` (신규): 특정 폴더의 서버 미읽음 메일 ID 목록 조회 메서드 추가
  - Graph API `/mailFolders/{folderId}/messages?$filter=isRead eq false&$select=id` 호출
  - 페이징 지원 (nextLink 추적)

#### 2. BackgroundSyncService.cs — SyncReadStatusAsync 서버 미읽음 목록 기준 교체
- 기존 로직: 로컬 DB 미읽음 메일을 순회하며 Graph API 개별 조회 (N+1 패턴)
- 신규 로직: `GetUnreadMessageIdsAsync`로 서버 미읽음 ID 목록을 일괄 조회 후 로컬 DB와 Set 비교
  - 서버에서 읽음 처리된 메일(로컬만 미읽음) → 로컬 `IsRead = true` 일괄 업데이트
  - 서버에서 미읽음인 메일(로컬만 읽음) → 로컬 `IsRead = false` 일괄 업데이트 (선택적)
- 적용 폴더: Inbox, SentItems (L-284 참조: 향후 설정으로 외부화 권장)

#### 3. MainWindow.xaml.cs — 동기화 설정 대메뉴 통합
- 기존: 동기화 관련 설정이 여러 하위 메뉴로 분산
- 변경: 동기화 설정을 단일 대메뉴로 통합하여 UX 일관성 향상
- 관련 UI 항목 재배치 및 이벤트 핸들러 정리

#### 4. UserPreferencesSettings.cs — 설정 필드 2개 추가
- `AiBatchSize` (int, 기본값 20): AI 분석 배치 처리 건수 설정 — 기존 하드코딩 값을 설정으로 외부화
- `MailSyncInitialCount` (int, 기본값 100): 초기 메일 동기화 건수 설정 — SyncPeriodSettings.Value 보완용

### 근본 원인 분석 (L-283 기록)
- 증상 수준 수정(ViewModel 폴더 카운트 갱신)으로는 재발 차단 불가
- Graph API 동기화 범위(7일)와 SyncReadStatusAsync의 로컬 기준 처리가 실제 근본 원인
- kdev-2 추가 투입으로 데이터 흐름 전체 추적 후 서버 미읽음 목록 기준 교체로 근본 해결

### 교훈 기록
- L-283 (medium): kplan 증상 수준 계획 → 근본 원인 미포착 → kdev 추가 투입
- L-284 (low): SyncReadStatusAsync 받은/보낸편지함만 적용 — 범위 문서화
- L-285 (low): EmailsSynced 이벤트 0건 패턴 의미 모호성 — 이벤트 페이로드 개선 권장

### 빌드/테스트
- 빌드: 오류 0개 ✅
- 런타임: 정상 ✅

---

## 2026-03-29: Phase 3 — AI 규칙엔진 + 자동 팔로업 + 회의 전 브리핑

**분류**: Fast Path (k3)
**수정 파일**: 8개 수정 + 7개 신규

### 변경 내역

#### 1. AI 규칙엔진
- `MailRule.cs` (신규): 메일 규칙 모델 — 조건 5종 + 액션 5종
  - 조건 타입: `FromContains`, `SubjectContains`, `HasAttachment`, `AiCategoryEquals`, `ToContains`
  - 액션 타입: `MoveToFolder`, `SetCategory`, `SetFlag`, `MarkAsRead`, `Delete`
  - 필드: Name, ConditionType, ConditionValue, ActionType, ActionValue, IsEnabled, Priority, AccountEmail
- `MailRuleService.cs` (신규): 규칙 엔진 서비스 — DB에서 활성 규칙 로드 + 메일 적용
- `MailRuleSettingsDialog.xaml/.cs` (신규): 규칙 관리 다이얼로그 — 규칙 추가/편집/삭제/순서
- `BackgroundSyncService.cs`: 120초 루프 추가 — 신규 메일에 규칙 자동 적용
- `Migration 20260329000005_AddMailRules`: MailRules 테이블 생성

#### 2. 자동 팔로업
- `Email.cs`: `FollowUpDate` (DateTime?, nullable) 필드 추가 — 팔로업 예정 날짜 (UTC)
- `ComposeWindow.xaml/.cs`: 팔로업 ComboBox 추가 — 3일/7일/14일/30일 선택
- `BackgroundSyncService.cs`: 3600초 루프 추가 — 팔로업 기한 만료 메일 토스트 알림
- `Migration 20260329000006_AddFollowUpDate`: FollowUpDate 컬럼 + 인덱스 추가

#### 3. 회의 전 브리핑
- `BackgroundSyncService.cs`: 300초 루프 추가 — 30분 이내 회의 감지 → 참석자 메일 수집 → AI 브리핑 생성
- `GraphCalendarService` 연동: 다음 회의 이벤트 조회 + 참석자 추출
- `AiMailService.GenerateMeetingBriefingAsync`: 참석자 관련 최근 메일 → 브리핑 생성 → 토스트 알림

### 빌드/테스트
- 빌드: 오류 0개 ✅
- 런타임: 정상 (health 200, Migration 정상 적용) ✅
- 로그: ERROR 0건 ✅
- 품질: 3/3 기능 확인 ✅

### 변경 파일
수정: App.xaml.cs, mAIxDbContext.cs, mAIxDbContextModelSnapshot.cs, Email.cs,
      BackgroundSyncService.cs, ComposeWindow.xaml, ComposeWindow.xaml.cs, MainWindow.xaml.cs
신규: MailRule.cs, MailRuleService.cs, MailRuleSettingsDialog.xaml/.cs,
      20260329000005_AddMailRules.cs, 20260329000005_AddMailRules.Designer.cs,
      20260329000006_AddFollowUpDate.cs, 20260329000006_AddFollowUpDate.Designer.cs

---

## 2026-03-29: Phase 2 — AI 기능 + 메일 스누즈 + TTS 읽기 + 일일 브리핑

**분류**: Fast Path (k3)
**수정 파일**: 12개 수정 + 3개 신규

### 변경 내역

#### 1. AiMailService.cs (신규) — AI 메서드 4개
- `GenerateDraftAsync(email, tone)`: 답장 초안 생성 (톤 선택: 공식/친근/간결)
- `SummarizeThreadAsync(emails)`: 스레드 전체 AI 요약
- `GenerateDailyBriefingAsync(emails)`: 오늘 수신 메일 일일 브리핑
- `GenerateMeetingBriefingAsync(emails)`: 회의 관련 메일 브리핑

#### 2. 메일 스누즈
- `Email.SnoozedUntil` (DateTime?, nullable): 스누즈 해제 예정 시각 (UTC)
- Migration `20260329000004_AddSnoozedUntil`: SnoozedUntil 컬럼 + 인덱스 추가
- `BackgroundSyncService`: 매 분 `SnoozedUntil <= UtcNow` 조건으로 자동 해제 루프
- `MainViewModel.ShowSnoozedEmails` 토글: 스누즈 중인 메일 표시/숨김 필터

#### 3. AI 답장 초안
- `EmailViewWindow`: "AI 답장" 버튼 + 톤 선택 ComboBox
- AiMailService.GenerateDraftAsync 호출 → ComposeWindow Body 자동 입력

#### 4. TTS 메일 읽기
- `EmailViewWindow`: "읽어주기/중지" 토글 버튼
- `System.Speech.Synthesis.SpeechSynthesizer` 기반 (외부 NuGet 없음)
- SpeakAsync / SpeakAsyncCancelAll + SpeakCompleted 이벤트로 버튼 상태 복원

#### 5. AI 일일 브리핑
- `MainWindow`: "📋 브리핑" 버튼 추가
- `DailyBriefingDialog.xaml/.cs` (신규): FluentWindow 기반 브리핑 표시 다이얼로그
- 오늘 수신 메일 목록을 AI에 전달 → 스트리밍 브리핑 표시

#### 6. 스레드 AI 요약
- `EmailViewWindow`: 접이식 패널 (Expander) 형태로 스레드 요약 섹션 추가
- 같은 ConversationId 메일들을 AiMailService.SummarizeThreadAsync로 요약

### 빌드/테스트
- 빌드: 오류 0개 ✅
- 런타임: 정상 (health 200, Migration 정상 적용) ✅
- 로그: ERROR 0건 ✅
- 품질: 11/11 항목 확인 ✅

### 변경 파일
수정: App.xaml.cs, mAIxDbContext.cs, mAIxDbContextModelSnapshot.cs, Email.cs,
      BackgroundSyncService.cs, ComposeViewModel.cs, MainViewModel.cs,
      EmailViewWindow.xaml/.cs, MainWindow.xaml/.cs, AGENTS.md
신규: 20260329000004_AddSnoozedUntil.cs, DailyBriefingDialog.xaml/.cs

---

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
