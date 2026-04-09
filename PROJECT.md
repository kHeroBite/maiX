# PROJECT.md - MaiX 프로젝트 구조 문서

이 문서는 MaiX 프로젝트의 전체 파일 구조, 아키텍처, 의존성 정보를 포함합니다.

## 관련 문서

- **[CLAUDE.md](./CLAUDE.md)**: AI Agent 핵심 지침서
- **[ADVANCED.md](./ADVANCED.md)**: CLAUDE.md 확장 문서 (새 폼, 권한, 리팩토링 가이드)
- **[restapi.md](./restapi.md)**: REST API 엔드포인트 상세 문서
- **[DATABASE.md](./DATABASE.md)**: 데이터베이스 스키마, 테이블 정의
- **[MCP.md](./MCP.md)**: MCP 서버 통합 문서

## 프로젝트 메타데이터

<!-- kinfra_skill: kinfra_maix -->

> 실행 경로/포트/로그/스크린샷 등 인프라 설정: **kinfra_maix** SKILL.md 참조.

```json
{
  "프로젝트명": "mAIx",
  "설명": "Microsoft 365 이메일 클라이언트 + AI 분석 시스템",
  "개발자": "김기로",
  "프레임워크": ".NET 10.0-windows",
  "프로젝트_타입": "WPF (MVVM)",
  "UI_라이브러리": "WPF UI (Fluent Design)",
  "데이터베이스": {
    "종류": "SQLite (EF Core)",
    "경로": "%APPDATA%\\MaiX\\MaiX.db"
  },
  "빌드_경로": "C:\\DATA\\Project\\mAIx",
  "솔루션": "mAIx.sln",
  "메인_프로젝트": "mAIx/mAIx.csproj",
  "인코딩": "UTF-8",
  "줄_끝": "CRLF"
}
```

## 디렉토리 구조

```
mAIx/
├── mAIx/                      # 메인 프로젝트
│   ├── App.xaml               # WPF Application 정의
│   ├── App.xaml.cs            # 애플리케이션 진입점
│   ├── appsettings.json       # 설정 파일
│   ├── log4net.config         # 로깅 설정
│   ├── mAIx.csproj           # 프로젝트 파일
│   │
│   ├── Assets/                # 리소스 (이미지, 아이콘)
│   ├── Controls/              # 커스텀 컨트롤
│   ├── Converters/            # XAML 값 변환기
│   ├── Data/                  # DbContext
│   ├── Migrations/            # EF Core 마이그레이션
│   ├── Models/                # 데이터 모델
│   ├── Services/              # 비즈니스 로직
│   │   ├── AI/               # AI 프로바이더
│   │   ├── Analysis/         # 이메일 분석
│   │   ├── Api/              # REST API 서버
│   │   ├── Cloud/            # 클라우드 링크 처리
│   │   ├── Converter/        # 문서 변환
│   │   ├── Graph/            # Microsoft Graph API
│   │   ├── Notification/     # 알림 서비스
│   │   ├── Rules/            # 메일 규칙 엔진 (Phase3 신규)
│   │   ├── Search/           # 검색 서비스
│   │   ├── Storage/          # 데이터 저장
│   │   └── Sync/             # 백그라운드 동기화
│   ├── Utils/                 # 유틸리티 (Log4 등)
│   ├── ViewModels/            # MVVM ViewModel
│   └── Views/                 # WPF View (Window/Page)
│
├── CLAUDE.md                  # AI Agent 지침서
├── PROJECT.md                 # 프로젝트 구조 (이 문서)
├── ADVANCED.md                # 확장 가이드
├── restapi.md                 # REST API 문서
├── DATABASE.md                # DB 스키마 문서
└── MCP.md                     # MCP 통합 문서
```

## 전체 파일 인벤토리

### 1. 시스템 핵심 파일

```yaml
시스템_파일:
  - 파일명: MentionParser.cs (신규 — 2026-04-09 Phase3)
    경로: MaiX/Utils/
    역할: "@멘션 파싱/하이라이트 유틸리티"

  - 파일명: App.xaml.cs
    경로: MaiX/App.xaml.cs
    중요도: ★★★★★
    역할: WPF 애플리케이션 진입점
    클래스:
      - App:
          설명: Application 클래스, OnStartup에서 로그인 → 메인창 흐름 제어
          핵심설정:
            - ShutdownMode: OnExplicitShutdown (명시적 종료만 허용)
          주요함수:
            - OnStartup(): 로그인 창 표시 후 MainWindow 초기화
            - Application_Exit(): 앱 종료 시 REST API 서버 정리

  - 파일명: Log4.cs
    경로: MaiX/Utils/Log4.cs
    중요도: ★★★★★
    역할: log4net 래퍼 클래스
    클래스:
      - Log4:
          설명: 정적 로거 유틸리티
          주요함수:
            - Initialize(): log4net 초기화
            - Debug/Info/Warn/Error/Fatal(): 로그 레벨별 출력
```

### 2. Views (WPF 화면)

```yaml
Views:
  - 파일명: LoginWindow.xaml / LoginWindow.xaml.cs
    경로: MaiX/Views/
    중요도: ★★★★★
    역할: Microsoft 365 로그인 화면
    클래스:
      - LoginWindow:
          설명: M365 인증 처리, MSAL 기반 토큰 발급
          이벤트:
            - LoginSucceeded: 로그인 성공 시 발생

  - 파일명: MainWindow.xaml / MainWindow.xaml.cs
    경로: MaiX/Views/
    중요도: ★★★★★
    역할: 메인 애플리케이션 화면
    클래스:
      - MainWindow:
          설명: 이메일 목록, 상세보기, 사이드바 네비게이션, 설정탭 시스템 메뉴(마이크 설정 포함)
          변경_2026-03-29(Phase2): "📋 브리핑" 버튼 추가 → DailyBriefingDialog 실행, 스누즈 토글 버튼 추가
          변경_2026-04-02: EmailListScrollViewer ScrollChanged 이벤트 추가 → 스크롤 하단 도달 시 LoadMoreEmailsAsync 호출 (인피니티 스크롤)

  - 파일명: DailyBriefingDialog.xaml / DailyBriefingDialog.xaml.cs (신규 — 2026-03-29 Phase2)
    경로: MaiX/Views/Dialogs/
    역할: AI 일일 브리핑 다이얼로그 — 오늘 수신 메일 기반 브리핑 생성 표시
    클래스:
      - DailyBriefingDialog (FluentWindow):
          의존성: AiMailService, List<Email> (오늘 메일 목록)
          기능: 로딩 시 AI 브리핑 자동 생성, ESC 키 닫기, CancellationToken 취소 지원

  - 파일명: MailRuleSettingsDialog.xaml / MailRuleSettingsDialog.xaml.cs (신규 — 2026-03-29 Phase3)
    경로: MaiX/Views/Dialogs/
    역할: 메일 규칙 관리 다이얼로그 — 규칙 추가/편집/삭제/우선순위 관리
    클래스:
      - MailRuleSettingsDialog (FluentWindow):
          의존성: mAIxDbContext, MailRule 목록
          기능: 조건/액션 ComboBox 선택, 규칙 활성화 토글, 우선순위 순서 변경

  - 파일명: CommandPaletteWindow.xaml / CommandPaletteWindow.xaml.cs (신규 — 2026-04-09 Phase4)
    경로: MaiX/Views/
    역할: Ctrl+K 커맨드 팔레트 창 (퍼지 검색 기반 명령 실행)

  - 파일명: ShortcutHelpOverlay.xaml / ShortcutHelpOverlay.xaml.cs (신규 — 2026-04-09 Phase2)
    경로: MaiX/Views/
    역할: ? 키로 표시되는 단축키 도움말 오버레이

  - 파일명: AutoReplyDialog.xaml / AutoReplyDialog.xaml.cs (신규 — 2026-04-09 Phase2)
    경로: MaiX/Views/Dialogs/
    역할: 부재중 자동응답 설정 다이얼로그 (On/Off + 기간 + 메시지)

  - 파일명: EmailViewWindow.xaml / EmailViewWindow.xaml.cs
    경로: MaiX/Views/
    역할: 메일 읽기 창 (별도 창)
    클래스:
      - EmailViewWindow (FluentWindow):
          설명: 이메일 본문 표시 (WebView2), CC/BCC 참조 필드, 첨부파일 패널
          변경_2026-04-02: AttachmentPanel 추가 — 첨부파일 목록(파일명/크기), 다운로드/열기 버튼 이벤트 핸들러 구현 (GraphMailService.DownloadAttachmentAsync 연동)

```

### 3. ViewModels (MVVM)

```yaml
ViewModels:
  - 파일명: ViewModelBase.cs
    경로: MaiX/ViewModels/
    역할: ViewModel 기반 클래스
    클래스:
      - ViewModelBase:
          설명: INotifyPropertyChanged 구현, CommunityToolkit.Mvvm 활용
          변경_2026-04-02: ExecuteAsync에 OperationCanceledException 처리 추가 (취소 = 정상, 에러 표시 안 함)

  - 파일명: LoginViewModel.cs
    경로: MaiX/ViewModels/
    역할: 로그인 화면 ViewModel
    클래스:
      - LoginViewModel:
          설명: M365 로그인 로직 처리
          주요속성:
            - IsLoading: 로딩 상태
            - ErrorMessage: 오류 메시지
          주요커맨드:
            - LoginCommand: 로그인 실행

  - 파일명: MainViewModel.cs
    경로: MaiX/ViewModels/
    역할: 메인 화면 ViewModel
    클래스:
      - MainViewModel:
          설명: 이메일 목록, 폴더 관리
          변경_2026-03-29(Phase2): ShowSnoozedEmails 토글 추가 (스누즈 메일 표시/숨김), 브리핑 명령 추가
          변경_2026-04-02(성능): _loadEmailsCts CancellationTokenSource 도입(폴더 전환 Race Condition 방지), Bulk 작업(읽음/플래그/삭제) SemaphoreSlim(8)+Task.WhenAll+ExecuteUpdateAsync 적용
          변경_2026-04-02(인피니티스크롤): PageSize=100 페이징 도입, _emailSkip 오프셋 관리, IsLoadingMore(bool)/HasMoreEmails(bool) 바인딩 속성, LoadMoreEmailsAsync() 스크롤 페이지 로드
          변경_2026-04-03(초기메일수): PageSize 상수 제거 → App.Settings.UserPreferences.InitialMailCount 동적 참조(LoadEmailsForFolderAsync/LoadMoreEmailsAsync 양쪽)

  - 파일명: TeamsViewModel.cs
    경로: MaiX/ViewModels/
    역할: Teams 채팅 ViewModel

  - 파일명: CalendarViewModel.cs
    경로: MaiX/ViewModels/
    역할: 캘린더 ViewModel

  - 파일명: NewsletterViewModel.cs (신규 — 2026-04-09 Phase3)
    경로: MaiX/ViewModels/
    역할: 뉴스레터 피드 뷰모델
```

### 4. Models (데이터 모델)

```yaml
Models:
  - 파일명: Email.cs
    역할: 이메일 데이터 모델 + INotifyPropertyChanged 구현 (EF Core 직접 바인딩 지원)
    속성: Id, Subject, From, To, Body, ReceivedDateTime, IsRead(INPC), HasAttachments, FlagStatus(INPC), Categories(INPC), PreviewText(NotMapped), PreviewOrSummary(NotMapped)
    변경_2026-03-29: SnoozedUntil(DateTime?, nullable) 스누즈 해제 예정 시각 추가, ScheduledSendTime(DateTime?, nullable) 예약발송 시간 추가
    변경_2026-03-29(Phase3): FollowUpDate(DateTime?, nullable) 팔로업 예정 날짜 추가

  - 파일명: MailRule.cs (신규 — 2026-03-29 Phase3)
    역할: 메일 자동 처리 규칙 모델
    속성: Id, Name, ConditionType, ConditionValue, ActionType, ActionValue, IsEnabled, Priority, AccountEmail, CreatedAt
    조건_타입: FromContains / SubjectContains / HasAttachment / AiCategoryEquals / ToContains
    액션_타입: MoveToFolder / SetCategory / SetFlag / MarkAsRead / Delete

  - 파일명: Attachment.cs
    역할: 첨부파일 모델
    속성: Id, Name, ContentType, Size, ContentBytes

  - 파일명: Folder.cs
    역할: 메일 폴더 모델 + INotifyPropertyChanged 구현 (EF Core 직접 바인딩 지원)
    속성: Id, DisplayName, UnreadItemCount(INPC), IsFavorite(INPC), FavoriteOrder(INPC)

  - 파일명: Account.cs
    역할: 사용자 계정 모델
    속성: Id, Email, DisplayName, AccessToken

  - 파일명: Todo.cs
    역할: 할일 항목 모델

  - 파일명: ContractInfo.cs
    역할: 계약 정보 모델 (AI 추출)

  - 파일명: EmailAnalysisResult.cs
    역할: 이메일 AI 분석 결과

  - 파일명: AISetting.cs
    역할: AI 제공자 설정

  - 파일명: Prompt.cs
    역할: AI 프롬프트 템플릿

  - 파일명: PromptTestHistory.cs
    역할: 프롬프트 테스트 이력

  - 파일명: SyncState.cs
    역할: 동기화 상태

  - 파일명: Signature.cs
    역할: 이메일 서명

  - 파일명: TeamsMessage.cs
    역할: Teams 메시지 모델

  - 파일명: OneNotePage.cs
    역할: OneNote 페이지 모델

  - 파일명: ConverterSetting.cs
    역할: 문서 변환기 설정

  - 파일명: QuickStep.cs (신규 — 2026-04-09 Phase3)
    역할: QuickStep 모델 (EF Core 엔티티) — 반복 작업 자동화 액션 체인

  - 파일명: SplitInboxRule.cs (신규 — 2026-04-09 Phase4)
    역할: SplitInboxRule 모델 (EF Core 엔티티) — Split Inbox 조건 기반 규칙

  - 파일명: ScreenerEntry.cs (신규 — 2026-04-09 Phase4)
    역할: ScreenerEntry 모델 (EF Core 엔티티) — 발신자 화이트/블랙리스트

  - 파일명: ReplyLaterItem.cs (신규 — 2026-04-09 Phase4)
    역할: ReplyLaterItem 모델 (EF Core 엔티티) — Reply Later 큐 아이템

  - 파일명: CommandPaletteItem.cs (신규 — 2026-04-09 Phase4)
    역할: 커맨드 팔레트 아이템 (UI 모델)
```

### 5. Services (비즈니스 로직)

#### 5.1 Services/AI (AI 프로바이더)

```yaml
AI_Services:
  - 파일명: IAIProvider.cs
    역할: AI 프로바이더 인터페이스
    메서드: AnalyzeAsync(), GenerateResponseAsync()

  - 파일명: AIProviderBase.cs
    역할: AI 프로바이더 기반 클래스

  - 파일명: AIService.cs
    역할: AI 서비스 팩토리/매니저

  - 파일명: ClaudeProvider.cs
    역할: Anthropic Claude API 연동

  - 파일명: OpenAIProvider.cs
    역할: OpenAI API 연동

  - 파일명: GeminiProvider.cs
    역할: Google Gemini API 연동

  - 파일명: OllamaProvider.cs
    역할: Ollama 로컬 LLM 연동

  - 파일명: LMStudioProvider.cs
    역할: LM Studio 로컬 LLM 연동

  - 파일명: FileAnalysisService.cs
    역할: OneNote 파일/오디오 AI 분석 서비스
    기능: 프롬프트 템플릿 로딩, 분석 요청 생성

  - 파일명: PromptCacheService.cs
    역할: 프롬프트 템플릿 메모리 캐시 (Singleton)
    기능: 앱 시작 시 1회 로드, 리로드/개별갱신 지원

  - 파일명: RecordingSummaryService.cs
    역할: 오디오 녹음 요약 서비스
```

#### 5.2 Services/Analysis (이메일 분석)

```yaml
Analysis_Services:
  - 파일명: EmailAnalyzer.cs
    역할: 이메일 분석 조율자
    기능: 이메일 내용 분석, AI 호출 조정

  - 파일명: PriorityCalculator.cs
    역할: 이메일 우선순위 계산

  - 파일명: ContractExtractor.cs
    역할: 계약 정보 추출

  - 파일명: TodoExtractor.cs
    역할: 할일 항목 추출
```

#### 5.3 Services/Api (REST API)

```yaml
Api_Services:
  - 파일명: RestApiServer.cs
    경로: MaiX/Services/Api/RestApiServer.cs
    중요도: ★★★★★
    역할: 내장 REST API 서버 (포트 5858)
    클래스:
      - RestApiServer:
          설명: HttpListener 기반 REST API 서버
          엔드포인트:
            - GET /api/health: 헬스 체크
            - GET /api/status: 앱 상태 조회
            - GET /api/logs/latest: 최신 로그 조회
            - POST /api/screenshot: 스크린샷 캡처
            - POST /api/shutdown: 앱 종료
            - POST /api/shutdown/force: 강제 종료
```

#### 5.4 Services/Converter (문서 변환)

```yaml
Converter_Services:
  - 파일명: IDocumentConverter.cs
    역할: 문서 변환기 인터페이스
    메서드: ConvertToTextAsync()

  - 파일명: AttachmentProcessor.cs
    역할: 첨부파일 처리 조율자

  - 파일명: OpenXmlDocConverter.cs
    역할: Word 문서 변환 (OpenXML SDK)

  - 파일명: NpoiDocConverter.cs
    역할: Word 문서 변환 (NPOI)

  - 파일명: NpoiExcelConverter.cs
    역할: Excel 변환 (NPOI)

  - 파일명: ClosedXmlConverter.cs
    역할: Excel 변환 (ClosedXML)

  - 파일명: OpenXmlPptConverter.cs
    역할: PowerPoint 변환 (OpenXML)

  - 파일명: PdfPigConverter.cs
    역할: PDF 텍스트 추출 (PdfPig)

  - 파일명: HwpConverter.cs
    역할: HWP 변환 (외부 도구)

  - 파일명: HwpSharpConverter.cs
    역할: HWP 변환 (HwpSharp)

  - 파일명: PandocConverter.cs
    역할: 범용 문서 변환 (Pandoc)

  - 파일명: OcrConverter.cs
    역할: OCR 텍스트 추출

  - 파일명: WindowsOcrConverter.cs
    역할: Windows OCR API 사용
```

#### 5.5 Services/Graph (Microsoft Graph)

```yaml
Graph_Services:
  - 파일명: GraphAuthService.cs
    역할: Microsoft Graph 인증
    기능: MSAL 토큰 발급/갱신

  - 파일명: TokenCacheHelper.cs
    역할: 토큰 캐시 관리

  - 파일명: GraphMailService.cs
    역할: 이메일 CRUD 작업
    변경_2026-04-02: GetMessageAsync Select에 bccRecipients 추가(R1 CC/BCC 버그수정), DownloadAttachmentAsync(messageId, attachmentId) 추가(R2 첨부파일 다운로드), GetAllMessageIdsAsync() 추가(R3 Reconciliation용 ID 목록 조회)

  - 파일명: GraphTeamsService.cs
    역할: Teams 채팅/채널 조회

  - 파일명: GraphCalendarService.cs
    역할: 캘린더 이벤트 관리

  - 파일명: GraphContactService.cs
    역할: 연락처 조회

  - 파일명: GraphOneNoteService.cs
    역할: OneNote 노트북/페이지 조회
    의존성: IHttpClientFactory (P2-03 소켓 재사용 패턴 — new HttpClient() 직접 생성 제거)
```

#### 5.5.5 Services/Cache (폴더 캐시) — 신규 2026-04-05

```yaml
Cache_Services:
  - 파일명: MailFolderCacheService.cs
    경로: Services/Cache/
    역할: 메일함 폴더별 LRU 메모리 캐시 (DI Singleton)
    기능:
      - TryGet/Set/AppendPage: 캐시 히트/미스/페이지 추가
      - OnEmailAdded/Deleted/Moved/Updated: CRUD 이벤트 훅
      - InvalidateFolder/InvalidateAll: 폴더/전체 무효화
      - SetScrollOffset: 스크롤 오프셋 저장
      - EmailsSavedToFolder 이벤트 구독: BackgroundSyncService 증분 갱신
    설계:
      - 캐시 키: (FolderId, ShowSnoozedEmails)
      - LRU: maxFolders=10, LastAccessedAt 기준 evict
      - 스레드 안전: lock(_lock) 진입
    의존성: BackgroundSyncService (이벤트 구독)

  - 파일명: CachedFolderState.cs
    경로: Services/Cache/
    역할: 폴더별 캐시 상태 레코드 (internal sealed)
    필드: Emails, EmailSkip, HasMoreEmails, ShowSnoozedEmails, LoadedAt, LastAccessedAt, HighWaterMark, ScrollOffset
```

#### 5.5.7 Services/신규 (2026-04-09 Phase 1~4)

```yaml
New_Services_20260409:
  - 파일명: KeyboardShortcutService.cs
    경로: Services/Keyboard/
    역할: 키보드 단축키 서비스 (J/K/E/R/A/F/D/U/S/? 10개 단축키)

  - 파일명: DelayedSendService.cs
    경로: Services/
    역할: 취소전송 서비스 (5~30초 지연 후 발송)

  - 파일명: ExportService.cs
    경로: Services/
    역할: EML/PDF 내보내기

  - 파일명: AutoReplyService.cs
    경로: Services/
    역할: 부재중 자동응답 (On/Off + 기간 + 메시지)

  - 파일명: QuickStepService.cs
    경로: Services/
    역할: 반복 작업 자동화 QuickStep (최대 5개 액션 체인)

  - 파일명: UnsubscribeService.cs
    경로: Services/
    역할: 원클릭 구독 취소 (List-Unsubscribe 헤더 파싱)

  - 파일명: TrackingBlockerService.cs
    경로: Services/
    역할: 추적 픽셀/링크 차단 (img src 패턴 감지)

  - 파일명: ConversationGrouper.cs
    경로: Services/
    역할: 대화 스레딩 (ConversationId 기반 그룹핑)

  - 파일명: CommandPaletteService.cs
    경로: Services/
    역할: Ctrl+K 커맨드 팔레트 (퍼지 검색 기반)

  - 파일명: FocusedInboxService.cs
    경로: Services/
    역할: Focused/Other 자동 분류 (AI 기반 IsImportant 판단)

  - 파일명: SplitInboxService.cs
    경로: Services/
    역할: Split Inbox 규칙 필터 (조건 기반 탭 분류)

  - 파일명: ScreenerService.cs
    경로: Services/
    역할: 발신자 차단/허용 (화이트/블랙리스트)

  - 파일명: ReplyLaterService.cs
    경로: Services/
    역할: Reply Later 큐 (스누즈 시간 기반 재알림)
```

#### 5.6 Services/기타

```yaml
Other_Services:
  - 파일명: CloudLinkdownloader.cs
    경로: Services/Cloud/
    역할: 클라우드 링크 다운로드 (OneDrive, SharePoint)

  - 파일명: EmailSearchService.cs
    경로: Services/Search/
    역할: 이메일 검색

  - 파일명: SearchQuery.cs
    경로: Services/Search/
    역할: 검색 쿼리 모델

  - 파일명: EmailFtsQueries.cs (신규 — 2026-03-29)
    경로: Queries/
    역할: FTS5 SQL 쿼리 상수 분리 정의
    참고: EmailSearchService.cs에서 참조. FTS5 MATCH + LIKE 폴백 SQL 포함.

  - 파일명: MailRuleService.cs (신규 — 2026-03-29 Phase3)
    경로: Services/Rules/
    역할: 메일 규칙 엔진 서비스 — DB에서 활성 규칙 로드 + 이메일에 조건/액션 적용
    기능: 활성 규칙 우선순위 정렬 로드, 조건 매칭(5종), 액션 실행(5종), 계정별 필터

  - 파일명: AiMailService.cs (신규 — 2026-03-29 Phase2)
    경로: Services/AI/
    역할: 이메일 전용 AI 기능 서비스 (답장초안/스레드요약/일일브리핑/회의브리핑)
    메서드:
      - GenerateDraftAsync(email, tone): 톤 선택 답장 초안 생성
      - SummarizeThreadAsync(emails): 스레드 전체 AI 요약
      - GenerateDailyBriefingAsync(emails): 오늘 수신 메일 일일 브리핑
      - GenerateMeetingBriefingAsync(emails): 회의 관련 메일 브리핑
    의존성: AIService (AI 프로바이더 팩토리)

  - 파일명: NotificationService.cs
    경로: Services/Notification/
    역할: 데스크톱 알림

  - 파일명: ToastNotificationService.cs
    경로: Services/Notification/
    역할: Windows 네이티브 토스트 알림 (PowerShell WinRT Interop — BurntToast NuGet 불필요)
    참고: net10.0-windows TFM 호환, 새 메일 수신 시 ToastEnabled 설정에 따라 알림 발송

  - 파일명: NotificationSettings.cs
    경로: Services/Notification/
    역할: 알림 설정

  - 파일명: BackgroundSyncService.cs
    경로: Services/Sync/
    역할: 백그라운드 메일 동기화 (5개 독립 루프: 즐겨찾기/전체/캘린더/채팅/AI분석)
    참고: Interlocked.CompareExchange 레이스컨디션 방지 + MailSyncCompleted 500ms Debounce + ODataError 410 처리
    변경_2026-03-28: 즐겨찾기 주기 30→10초; AI 분석 배치 루프(10분, 최대20건) 신규; ToastNotificationService DI 주입
    변경_2026-03-29: AI 배치 루프에 PriorityScore 기반 AiCategory 자동 분류 매핑 통합 (긴급/업무/일반)
    변경_2026-03-29(Phase2): 스누즈 해제 루프 추가 — 매 분 SnoozedUntil <= UtcNow 조건으로 자동 해제 처리
    변경_2026-03-29(Phase3): 3개 루프 추가 — 규칙 엔진(120초), 팔로업 알림(3600초), 회의 전 브리핑(300초)
    변경_2026-04-02: ReconcileDeletedEmailsAsync() 추가 — Graph API GetAllMessageIdsAsync로 실서버 ID 조회, DB 비교 후 삭제된 메일 DB에서 제거 (영업폴더 잔류 메일 정리)
    변경_2026-04-05: EmailsSavedToFolder(string folderId, IReadOnlyList<Email>) 이벤트 추가 — MailFolderCacheService 증분 갱신용; SaveEmailsAsync 말미에서 invoke

  - 파일명: PromptService.cs
    경로: Services/Storage/
    역할: AI 프롬프트 CRUD
    참고: 파일 우선(Resources/Prompts/*.txt) + DB Fallback 패턴 (P4-02)

  - 파일명: DefaultPromptTemplates.cs
    경로: Services/Storage/
    역할: 기본 프롬프트 템플릿

  - 파일명: ConverterSettingService.cs
    경로: Services/Storage/
    역할: 변환기 설정 관리
```

#### 5.7 Services/Audio (오디오)

```yaml
Audio_Services:
  - 파일명: AudioRecordingService.cs
    경로: Services/Audio/
    역할: OneNote 녹음 서비스 (WasapiCapture 기반, 다중 장치 탐색, 선호 장치 우선순위)

  - 파일명: MicrophoneTestService.cs
    경로: Services/Audio/
    역할: 마이크 테스트 전용 서비스 (장치 열거, 실시간 모니터링, 테스트 녹음/재생, 볼륨 조절)
```

#### 5.8 Services/Speech (음성 인식/합성)

```yaml
Speech_Services:
  - 파일명: SpeechRecognitionService.cs (삭제됨 — 2026-03-25)
    경로: Services/Speech/
    역할: [삭제] 클라이언트 STT 제거, 서버 STT(ServerSpeechService)로 전면 전환

  - 파일명: ServerSpeechService.cs
    경로: Services/Speech/
    역할: Jarvis 서버 HTTP 클라이언트 (STT/TTS/화자분리 API 호출)
    변경_2026-03-28: prefs(UserPreferencesSettings) 파라미터 추가 → 9개 REST 경로 설정값 사용
    API_메서드:
      - GetFullModelStatusAsync(): STT/TTS/VAD 통합 상태 조회 (/api/models/full-status)
      - GetTtsEnginesAsync(): TTS 엔진 목록 + 세부정보 조회 (/api/tts/engines)
      - GetAudioCapabilitiesAsync(): 오디오 지원 포맷/채널/샘플레이트 조회 (/api/audio/capabilities)
    DTO:
      - FullModelStatusResponse: SttStatusInfo + TtsStatusInfo + VadStatusInfo
      - TtsEnginesResponse: 엔진목록 + Ready목록 + Active + Details(TtsEngineDetail)
      - AudioCapabilitiesResponse: SupportedFormats + SupportedSampleRates + SupportedChannels

  - 파일명: ServerWebSocketSpeechService.cs
    경로: Services/Speech/
    역할: WebSocket 기반 서버 STT 서비스 (실시간 오디오 스트리밍, 청크 결과 수신)
    변경_2026-03-28: ConnectAsync에 wsPath 파라미터 추가 → 설정에서 WS 경로 주입 가능

  - 파일명: TextToSpeechService.cs
    경로: Services/Speech/
    역할: TTS 서비스 (NAudio WaveOutEvent 재생, 서버 모드 SynthesizeAsync 연동)
    변경_2026-03-28: prefs(UserPreferencesSettings) 파라미터로 TTS 엔드포인트 설정값 사용
```

### 6. Data (데이터베이스)

```yaml
Data:
  - 파일명: MaiXDbContext.cs
    경로: MaiX/Data/
    중요도: ★★★★★
    역할: EF Core DbContext
    클래스:
      - MaiXDbContext:
          설명: SQLite 데이터베이스 컨텍스트
          DbSet:
            - Emails: 이메일
            - Attachments: 첨부파일
            - Folders: 폴더
            - Accounts: 계정
            - AISettings: AI 설정
            - Prompts: 프롬프트
            - ConverterSettings: 변환기 설정
```

### 7. Converters (XAML 값 변환기)

```yaml
Converters:
  - IntToVisibilityConverter.cs: int → Visibility
  - BoolToVisibilityConverter.cs: bool → Visibility
  - NullToVisibilityConverter.cs: null 체크 → Visibility
  - BoolToFontWeightConverter.cs: bool → FontWeight
  - StringToInitialConverter.cs: 문자열 → 이니셜
  - AiCategoryToBadgeConverter.cs: AI 카테고리 문자열 → 배지 색상/텍스트 (긴급/액션/FYI/일반) [신규 2026-03-29]
```

## 핵심 종속성

```xml
<!-- MaiX.csproj 주요 NuGet 패키지 -->
<PackageReference Include="Microsoft.Identity.Client" />
<PackageReference Include="Microsoft.Graph" />
<PackageReference Include="Microsoft.EntityFrameworkcore.Sqlite" />
<PackageReference Include="CommunityToolkit.Mvvm" />
<PackageReference Include="WPF-UI" />
<PackageReference Include="log4net" />
<PackageReference Include="Newtonsoft.Json" />
<PackageReference Include="DocumentFormat.OpenXml" />
<PackageReference Include="NPOI" />
<PackageReference Include="ClosedXML" />
<PackageReference Include="PdfPig" />
<PackageReference Include="Microsoft.Extensions.Http" />
```

## 아키텍처 다이어그램

```
┌─────────────────────────────────────────────────────────────────┐
│                         MaiX Application                        │
├─────────────────────────────────────────────────────────────────┤
│  Views (WPF XAML)                                               │
│  ├── LoginWindow.xaml        (M365 로그인)                      │
│  └── MainWindow.xaml         (메인 화면)                        │
├─────────────────────────────────────────────────────────────────┤
│  ViewModels (MVVM)                                              │
│  ├── LoginViewModel          (로그인 로직)                      │
│  ├── MainViewModel           (메인 화면 로직)                   │
│  ├── TeamsViewModel          (Teams 로직)                       │
│  └── CalendarViewModel       (캘린더 로직)                      │
├─────────────────────────────────────────────────────────────────┤
│  Services                                                        │
│  ├── Graph/                  (Microsoft Graph API)              │
│  │   ├── GraphAuthService    (인증)                             │
│  │   ├── GraphMailService    (이메일)                           │
│  │   ├── GraphTeamsService   (Teams)                            │
│  │   └── GraphCalendarService (캘린더)                          │
│  ├── AI/                     (AI 분석)                          │
│  │   ├── ClaudeProvider      (Anthropic)                        │
│  │   ├── OpenAIProvider      (OpenAI)                           │
│  │   └── OllamaProvider      (로컬 LLM)                         │
│  ├── Analysis/               (이메일 분석)                      │
│  │   ├── EmailAnalyzer       (분석 조율)                        │
│  │   └── ContractExtractor   (계약 추출)                        │
│  ├── Converter/              (문서 변환)                        │
│  │   ├── OpenXmlDocConverter (Word)                             │
│  │   ├── NpoiExcelConverter  (Excel)                            │
│  │   └── PdfPigConverter     (PDF)                              │
│  └── Api/                    (REST API)                         │
│      └── RestApiServer       (포트 5858)                        │
├─────────────────────────────────────────────────────────────────┤
│  Data                                                            │
│  └── MaiXDbContext          (EF Core + SQLite)                 │
├─────────────────────────────────────────────────────────────────┤
│  External Services                                               │
│  ├── Microsoft Graph API     (M365 데이터)                      │
│  ├── AI APIs                 (Claude, OpenAI, Gemini)           │
│  └── ntfy.sh                 (푸시 알림)                        │
└─────────────────────────────────────────────────────────────────┘
```

## 빌드 및 실행

### 빌드 명령

```bash
dotnet build "C:\DATA\Project\MaiX\MaiX\MaiX.csproj"
```

### 실행

```bash
"C:\DATA\Project\mAIx\mAIx\bin\Debug\net10.0-windows\win-x64\net10.0-windows\mAIx.exe"
```

### REST API 확인

```bash
curl -s http://localhost:5858/api/health
curl -s http://localhost:5858/api/status
```

## 개발 가이드

### 새 ViewModel 추가

1. `ViewModels/` 폴더에 `{Name}ViewModel.cs` 생성
2. `ViewModelBase` 상속
3. `[ObservableProperty]`, `[RelayCommand]` 어트리뷰트 사용

### 새 View 추가

1. `Views/` 폴더에 `{Name}Window.xaml` 또는 `{Name}Page.xaml` 생성
2. DataContext에 해당 ViewModel 바인딩

### 새 Service 추가

1. 적절한 `Services/{Category}/` 폴더에 생성
2. 인터페이스 우선 정의 권장
3. 의존성 주입 고려

### 새 Model 추가

1. `Models/` 폴더에 생성
2. EF Core 엔티티인 경우 `MaiXDbContext`에 DbSet 추가
3. 마이그레이션 생성: `dotnet ef migrations add {Name}`

---

## 작업 이력

| 날짜 | 작업 | 주요 변경 파일 | 비고 |
|------|------|----------------|------|
| 2026-03-26 | P1~P4 코드 품질 이슈 17건 수정 (kplan_dual 검증 기반) | App.xaml.cs, BackgroundSyncService.cs, GraphAuthService.cs, GraphOneNoteService.cs, GraphPlannerService.cs, MainViewModel.cs, OneNoteViewModel.cs, AppSettingsManager.cs, PromptService.cs, ComposeWindow.xaml.cs, EmailViewWindow.xaml.cs, MainWindow.xaml.cs, Email.cs, Folder.cs, LMStudioProvider.cs, OllamaProvider.cs, MaiX.csproj | P1: 비동기/DI Scoped 수정, P2: 레이스컨디션/HttpClientFactory/CTS/Task.Run, P3: DPAPI/Debounce/410Gone/AIService이중방지, P4: PromptService파일우선/DbContextFactory/DB엔티티주석 |
| 2026-03-26 | 프로젝트명 MaiX → mAIx 전체 변경 | mAIx.sln, mAIx/mAIx.csproj, 181개 .cs + 13개 .xaml (namespace/using/clr-namespace), mAIx/Data/mAIxDbContext.cs, Assets/mAIx.ico + icon.png | namespace MaiX→mAIx 일괄 변경, 아이콘 교체, 타이틀바 로고 mAiX→mAIx |
| 2026-03-27 | STT 실시간 시간표시 수정 + 자동후처리(STT/요약) 로직 보완 | ServerWebSocketSpeechService.cs, ServerSpeechService.cs, OneNoteViewModel.cs | SttChunkResult에 StartSeconds/EndSeconds 추가, JSON 시간 파싱(start_time/start 폴백), segments 기반 시간 파싱 개선, TimeSpan.Zero→chunk 시간 반영, RunPostProcessingAsync IsPostSTTEnabled 체크 + 파일 STT 단계 추가 |
| 2026-03-27 | 앱 아이콘 도트스타일 X 디자인으로 재생성 | mAIx/Assets/mAIx.ico, mAIx/Assets/icon.png | 투명배경, 멀티사이즈(.ico), 256×256(.png) 도트스타일 X 아이콘 |
| 2026-03-27 | STT 설정 화면 서버옵션 읽기전용 표시 + 모델 텍스트 전환 | mAIx/Views/MainWindow.xaml.cs | ShowSttTtsSettings()에 청크길이/오버랩/채널/샘플레이트/압축포맷 읽기전용 TextBox 추가, STT 모델 ComboBox→TextBlock 변경 |
| 2026-03-27 | 설정 STT/VAD/화자분리/TTS 서버옵션 통합 읽기전용 표시 추가 | mAIx/Services/Speech/ServerSpeechService.cs, mAIx/Views/MainWindow.xaml.cs | GetFullModelStatusAsync/GetTtsEnginesAsync/GetAudioCapabilitiesAsync 3개 API 메서드 + DTO 7개(FullModelStatusResponse/SttStatusInfo/TtsStatusInfo/VadStatusInfo/TtsEnginesResponse/TtsEngineDetail/AudioCapabilitiesResponse) 추가; ShowSttTtsSettings()에 STT옵션/VAD옵션/화자분리옵션/TTS옵션 4개 그룹 통합, Task.WhenAll 병렬 API 조회 |
| 2026-03-28 | AI 서버 엔드포인트 클라이언트 직접 입력 기능 추가 | UserPreferencesSettings.cs, ServerSpeechService.cs, ServerWebSocketSpeechService.cs, MainWindow.xaml.cs, TextToSpeechService.cs, OneNoteViewModel.cs, FileAnalysisService.cs | UserPreferencesSettings에 REST 9개+WS 3개 엔드포인트 속성 추가; ServerSpeechService/TextToSpeechService/FileAnalysisService에 prefs 파라미터로 경로 설정값 주입; ServerWebSocketSpeechService.ConnectAsync에 wsPath 파라미터 추가; MainWindow에 엔드포인트 입력 UI Expander 추가 |
| 2026-03-28 | 메일 읽지않은 카운트 불일치 버그 수정 | mAIx/ViewModels/MainViewModel.cs | OnMailSyncCompleted: RefreshEmailReadStatusAsync 후 RefreshFolderUnreadCountsAsync 무조건 호출 추가; OnEmailsSynced: newCount==0 else 브랜치에 RefreshFolderUnreadCountsAsync 호출 추가 — 다른 앱에서 메일 읽음/자동 동기화 시 폴더 카운트 미갱신 버그 수정 |
| 2026-03-28 | 메일탭 전체 점검 — 초기 동기화 수/배치저장/발송후동기화/정렬통일/고급검색 개선 6건 | mAIx/Models/Settings/SyncPeriodSettings.cs, mAIx/Services/Graph/GraphMailService.cs, mAIx/Services/Sync/BackgroundSyncService.cs, mAIx/ViewModels/ComposeViewModel.cs, mAIx/ViewModels/MainViewModel.cs | T-01: SyncPeriodSettings Value 기본값 5→100; T-02: SaveEmailsAsync N+1→배치저장; T-03: 메일발송후 SyncSentItemsAsync 자동호출(ComposeViewModel+GraphMailService.CurrentUserEmail); T-04: ApplySortingWithPin 정렬기준 Subject/From/HasAttachments/PriorityScore/default=ReceivedDateTime으로 통일; T-05: 고급검색 To필드+to:/subject: 접두사 파싱 추가; T-06: SyncAllAccountsAsync 지수백오프 재시도 |
| 2026-03-28 | 메일탭 실시간 동기화 + 알림 기능 + UX 고도화 (7개 수정) | mAIx/Services/Notification/ToastNotificationService.cs(신규), mAIx/Models/Settings/NotificationXmlSettings.cs, mAIx/Models/Settings/UserPreferencesSettings.cs, mAIx/App.xaml.cs, mAIx/Services/Sync/BackgroundSyncService.cs, mAIx/Services/Graph/GraphMailService.cs, mAIx/Views/MainWindow.xaml.cs | 즐겨찾기 동기화 30→10초 단축; Graph API 429 Retry-After+지수백오프(최대3회) 방어; 설정 변경 즉시 반영(SetFavoriteSyncInterval 호출); ToastNotificationService 신규(PowerShell WinRT Interop 네이티브 토스트); 알림 TODO→토스트 연동; AI 분석 배치 루프(5번째 독립루프, 10분 주기, 최대20건); 키보드 단축키 7종(Ctrl+D/Q/U, Enter, Ctrl+1~5) |
| 2026-03-29 | Phase 1: AI 스마트 분류 UI + 첨부파일 AI 분석 + 예약발송 + 발송취소 | AiCategoryToBadgeConverter.cs(신규), ScheduledSendDialog.xaml/cs(신규), App.xaml, MainWindow.xaml/cs, MainViewModel.cs, Email.cs, EmailAnalysisResult.cs, EmailAnalyzer.cs, BackgroundSyncService.cs, ComposeViewModel.cs, ComposeWindow.xaml/cs, Migration 3파일 | AI배지 IValueConverter, AiPriority 정렬/필터, AttachmentSummary/RiskLevel, ScheduledSendTime(DateTime?), 5초 카운트다운 CancellationToken 취소 |
| 2026-03-29 | Phase 0 인프라 정비 — Email AI 분류 필드 + FTS5 검색 + AI 자동 트리거 | mAIx/Models/Email.cs, mAIx/Services/Search/EmailSearchService.cs, mAIx/Services/Sync/BackgroundSyncService.cs, mAIx/Migrations/20260329000001_AddAiClassificationFields.cs, mAIx/Migrations/20260329000002_AddEmailFts5.cs, mAIx/Queries/EmailFtsQueries.cs(신규), mAIx/Migrations/mAIxDbContextModelSnapshot.cs | Email에 AiCategory/AiPriority/AiActionRequired/AiSummaryBrief 4개 AI 분류 필드; FTS5 EmailsFts 가상 테이블+트리거 3종+초기 인덱싱; FTS5 MATCH+LIKE 폴백 검색; PriorityScore 기반 AiCategory 자동 분류 |
| 2026-03-28 | 메일탭 UX 완성도 마지막 10% — INPC + 다중선택 도구바 + PreviewText | mAIx/Models/Email.cs, mAIx/Models/Folder.cs, mAIx/Services/Graph/GraphMailService.cs, mAIx/Services/Sync/BackgroundSyncService.cs, mAIx/ViewModels/MainViewModel.cs, mAIx/Views/MainWindow.xaml, mAIx/Views/MainWindow.xaml.cs | (1) Email.cs: INotifyPropertyChanged 구현(IsRead/FlagStatus/Categories INPC), PreviewText(NotMapped)+PreviewOrSummary(폴백) 속성 추가; Folder.cs: INotifyPropertyChanged 구현(UnreadItemCount/IsFavorite/FavoriteOrder INPC); (2) BulkActionBar: MainWindow.xaml에 하단 오버레이 액션바(2건+ 선택 시), MainViewModel에 SelectedEmailCount/IsMultipleEmailsSelected/다건삭제·읽음·플래그 커맨드, MainWindow.xaml.cs에 7개 핸들러+SelectionChanged; (3) GraphMailService: bodyPreview selectFields 추가, BackgroundSyncService: PreviewText 매핑 |
| 2026-04-01 | 메일 동기화 읽음 카운트 불일치 근본 수정 + 설정 동기화 대메뉴 통합 | mAIx/Services/Graph/GraphMailService.cs, mAIx/Services/Sync/BackgroundSyncService.cs, mAIx/Views/MainWindow.xaml.cs, mAIx/Models/Settings/UserPreferencesSettings.cs | GetMessagesReadStatusAsync days 7→30 확장; GetUnreadMessageIdsAsync 신규(서버 미읽음 ID 일괄 조회); SyncReadStatusAsync 서버 미읽음 목록 기준 교체(로컬 N+1→Set 비교 일괄); 동기화 설정 대메뉴 통합; AiBatchSize/MailSyncInitialCount 설정 필드 추가 |
| 2026-04-02 | R1(CC/BCC 버그수정)+R2(첨부파일 다운로드/열기)+R3(영업폴더 Reconciliation)+R4(인피니티 스크롤) | mAIx/Services/Graph/GraphMailService.cs, mAIx/Services/Sync/BackgroundSyncService.cs, mAIx/ViewModels/MainViewModel.cs, mAIx/Views/EmailViewWindow.xaml, mAIx/Views/EmailViewWindow.xaml.cs, mAIx/Views/MainWindow.xaml, mAIx/Views/MainWindow.xaml.cs | R1: GetMessageAsync Select에 bccRecipients 추가; R2: DownloadAttachmentAsync+GetAllMessageIdsAsync 신규, EmailViewWindow 첨부파일 패널(목록/다운로드/열기); R3: ReconcileDeletedEmailsAsync 신규(Graph ID목록↔DB 비교 삭제 정리); R4: PageSize=100 인피니티 스크롤(IsLoadingMore/HasMoreEmails/LoadMoreEmailsAsync+ScrollChanged) |
| 2026-04-03 | 설정>메일 초기 메일수 선택 옵션(20/50/100) 추가 + 동기화 기본값 버그 수정 | mAIx/Models/Settings/UserPreferencesSettings.cs, mAIx/ViewModels/MainViewModel.cs, mAIx/Views/MainWindow.xaml.cs | UserPreferencesSettings에 InitialMailCount(기본50) 추가; MainViewModel PageSize 상수→InitialMailCount 동적 참조; MainWindow에 mail_initial 소메뉴+ShowMailInitialSettings() 신규(20/50/100 RadioButton); Show*SyncSettings() RadioButton 기본값 선택 버그(foreach 클로저 캡처) 수정 |
| 2026-04-05 | 메일함 폴더별 캐시 시스템 구현 — 전환 <100ms + 이벤트 증분 sync | mAIx/Services/Cache/MailFolderCacheService.cs(신규), mAIx/Services/Cache/CachedFolderState.cs(신규), mAIx/Services/Sync/BackgroundSyncService.cs, mAIx/App.xaml.cs, mAIx/ViewModels/MainViewModel.cs, mAIx/Views/MainWindow.xaml.cs | LRU 캐시(maxFolders=10), EmailsSavedToFolder 이벤트 증분 갱신, 스크롤 오프셋 복원 |
| 2026-04-09 | Phase 1~4 기능 확장 (성능최적화+아웃룩기능+Superhuman+혁신기능) | App.xaml.cs, MainViewModel.cs, MainWindow.xaml/cs + 신규 서비스 23개 | k5 파이프라인 |
| 2026-04-10 | Planner 탭 UI 가상화 (ItemsControl→ListView) + kio bash_exec 버그 수정 | mAIx/Views/MainWindow.xaml, AI/MCP-Servers/fio-mcp-server/bash_exec.py, .claude/skills/ko/SKILL.md | L-303/304/305 교훈 반영 |

---

## 코딩 규칙

### WPF MVVM 패턴 규칙

**절대 원칙**: MVVM 패턴 준수. View(XAML) ↔ ViewModel(바인딩) ↔ Model(데이터) 분리.

```yaml
필수:
  - ViewModel은 ViewModelBase 상속 (CommunityToolkit.Mvvm)
  - [ObservableProperty], [RelayCommand] 어트리뷰트 사용
  - View에서 ViewModel 직접 참조 금지 (DataContext 바인딩만)
  - 코드비하인드(.xaml.cs)에 비즈니스 로직 최소화

금지:
  - View에서 직접 서비스 호출
  - ViewModel에서 View 컨트롤 직접 접근
  - 코드비하인드에 DB 쿼리/API 호출
```

### DI (의존성 주입) 규칙

```yaml
필수:
  - 모든 서비스는 App.xaml.cs의 ConfigureServices()에 등록
  - 생성자 주입 사용 (서비스 로케이터 패턴 금지)
  - Scoped 서비스는 CreateScope() 사용
  - IHttpClientFactory 사용 시: services.AddHttpClient() 등록 + MaiX.csproj에 Microsoft.Extensions.Http 패키지 추가 필수

금지:
  - new Service() 직접 생성 (DI 컨테이너 경유 필수)
  - 서비스 간 순환 참조
  - 싱글톤에서 Scoped 서비스 직접 주입
  - new HttpClient() 직접 생성 (IHttpClientFactory.CreateClient() 사용)

NuGet_패키지_의존성_체크리스트:
  - IHttpClientFactory → Microsoft.Extensions.Http
  - IMemoryCache → Microsoft.Extensions.Caching.Memory
  - IOptions<T> → Microsoft.Extensions.Options
  - 새 타입 사용 시: 해당 어셈블리가 .csproj에 PackageReference로 있는지 먼저 확인
```

### XAML 바인딩 규칙

```yaml
필수:
  - Binding Path 명시 (축약 금지)
  - Converter는 Converters/ 폴더에 별도 파일로 분리
  - ResourceDictionary로 스타일 재사용
  - WPF UI (Fluent Design) 컨트롤 우선 사용

네이밍:
  - Converter: {용도}Converter.cs (예: BoolToVisibilityConverter)
  - View: {기능}Window.xaml 또는 {기능}Page.xaml
  - ViewModel: {기능}ViewModel.cs
  - Model: {엔티티명}.cs
```

### 네임스페이스 규칙

```yaml
루트: MaiX
구조:
  - MaiX.Views: WPF Window/Page
  - MaiX.Views.Dialogs: 다이얼로그 창
  - MaiX.ViewModels: MVVM ViewModel
  - MaiX.Models: 데이터 모델
  - MaiX.Models.Settings: 설정 모델
  - MaiX.Services.{카테고리}: 비즈니스 로직
  - MaiX.Data: EF Core DbContext
  - MaiX.Converters: XAML 값 변환기
  - MaiX.Controls: 커스텀 컨트롤
  - MaiX.Utils: 유틸리티
```

### SQLite / EF Core 규칙

```yaml
필수:
  - DbContext는 MaiXDbContext 사용
  - 마이그레이션: dotnet ef migrations add {Name}
  - 스키마 변경 시 DATABASE.md 업데이트
  - using 블록으로 DbContext 관리

금지:
  - 직접 SQL 실행 (EF Core LINQ 사용)
  - Migration 파일 수동 편집
  - DbContext를 싱글톤으로 등록
```

### Microsoft Graph API 규칙

```yaml
필수:
  - GraphAuthService를 통한 인증만 사용
  - TokenCacheHelper로 토큰 캐시 관리
  - 권한 변경 시: GraphAuthService.cs + appsettings.json + Azure Portal 동시 수정
  - Delta Query 사용 (전체 동기화 금지)

금지:
  - 직접 HTTP 호출 (Microsoft.Graph SDK 사용)
  - 토큰을 코드에 하드코딩
  - Rate Limit 무시 (재시도 로직 필수)
```

### 로깅 규칙 (L-296 — 절대 규칙)

```yaml
필수:
  - NLog 표준 로거 사용 (모든 레이어 — Services/ViewModels/Controls 전부)
  - 패턴: private static readonly Logger _log = LogManager.GetCurrentClassLogger();
  - 네임스페이스: using NLog;
  - 적절한 레벨: Debug/Info/Warn/Error/Fatal
  - 예외 처리 블록에 Error 로그
  - 클래스명/메서드명 포함

금지:
  - Serilog 직접 사용 (using Serilog; / Log.ForContext<T>() 패턴) — AC auto_scripts 경로 불일치
  - Console.WriteLine (NLog 사용)
  - 민감 정보 로그 출력 (토큰, 비밀번호)
```

### AI 분석 규칙

```yaml
필수:
  - IAIProvider 인터페이스 구현
  - AIService를 통한 프로바이더 선택
  - 비동기 호출 (async/await)

금지:
  - AI API 키를 코드에 하드코딩 (설정 파일 사용)
  - 동기 API 호출 (UI 블로킹)
```

### 프롬프트 관리 규칙 (L-044 — 절대 규칙)

```yaml
원칙: 프롬프트는 반드시 외부 파일로 관리. 코드 내 하드코딩/DB 조회 절대 금지.

필수:
  - 프롬프트 템플릿은 Resources/Prompts/*.txt 외부 파일로 관리
  - .csproj에 Content + CopyToOutputDirectory=PreserveNewest 설정
  - 변수 치환은 {{변수명}} 플레이스홀더 + string.Replace() 사용
  - 프롬프트 수정 시 .txt 파일만 수정 (C# 코드 변경 불필요)

금지:
  - C# 코드에 프롬프트 문자열 하드코딩 (raw string literal, $\"\"\" 등 포함)
  - PromptService/DB를 통한 프롬프트 조회 (DB 우선 시 외부 파일 무력화)
  - Fallback 하드코딩 ("보험"이라는 명목도 금지)
  - 프롬프트 내용을 코드 변경 없이 수정 불가능한 구조
```

### 설정 필드 추가 규칙 (L-002 — 절대 규칙)

```yaml
적용_범위: UserPreferencesSettings.cs 연동 모든 설정 필드

체크리스트 (설정 필드 추가/수정 시 반드시 확인):
  1. UserPreferencesSettings.cs에 해당 프로퍼티가 선언되었는가?
  2. 프로퍼티에 기본값이 설정되었는가? (null 허용 여부 명시)
  3. SettingsViewModel 또는 관련 ViewModel에서 바인딩 경로가 정확한가?
  4. XAML Binding Path가 UserPreferencesSettings의 실제 프로퍼티명과 일치하는가?
  5. 설정 저장/로드 로직(직렬화/역직렬화)에서 새 필드가 포함되는가?

금지_패턴:
  - ViewModel/XAML에 바인딩 추가 후 UserPreferencesSettings에 프로퍼티 추가 누락
  - 임시 하드코딩으로 바인딩 오류를 우회하고 UserPreferencesSettings 수정 지연
  - 새 설정 섹션을 별도 클래스로 분리 후 UserPreferencesSettings 연결 누락
```

### REST API 서버 규칙

```yaml
포트: 5858 (고정)
필수:
  - RestApiServer.cs에서 엔드포인트 관리
  - JSON 응답 형식
  - 에러 시 적절한 HTTP 상태 코드 반환
  - restapi.md 문서 업데이트

금지:
  - 5858 포트 변경 (자동화 스크립트 의존)
  - 인증 없는 위험한 엔드포인트 추가
```
