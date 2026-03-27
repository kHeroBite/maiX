# PROJECT.md - MaiX 프로젝트 구조 문서

이 문서는 MaiX 프로젝트의 전체 파일 구조, 아키텍처, 의존성 정보를 포함합니다.

## 관련 문서

- **[CLAUDE.md](./CLAUDE.md)**: AI Agent 핵심 지침서
- **[ADVANCED.md](./ADVANCED.md)**: CLAUDE.md 확장 문서 (새 폼, 권한, 리팩토링 가이드)
- **[restapi.md](./restapi.md)**: REST API 엔드포인트 상세 문서
- **[DATABASE.md](./DATABASE.md)**: 데이터베이스 스키마, 테이블 정의
- **[MCP.md](./MCP.md)**: MCP 서버 통합 문서

## 프로젝트 메타데이터

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
  "실행파일": "mAIx\\bin\\Debug\\net10.0-windows\\mAIx.exe",
  "인코딩": "UTF-8",
  "줄_끝": "CRLF",
  "REST_API_포트": 5858,
  "로그_경로": "%APPDATA%\\MaiX\\logs\\",
  "스크린샷_경로": "%APPDATA%\\MaiX\\screenshots\\"
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

  - 파일명: TeamsViewModel.cs
    경로: MaiX/ViewModels/
    역할: Teams 채팅 ViewModel

  - 파일명: CalendarViewModel.cs
    경로: MaiX/ViewModels/
    역할: 캘린더 ViewModel
```

### 4. Models (데이터 모델)

```yaml
Models:
  - 파일명: Email.cs
    역할: 이메일 데이터 모델
    속성: Id, Subject, From, To, Body, ReceivedDateTime, IsRead, HasAttachments

  - 파일명: Attachment.cs
    역할: 첨부파일 모델
    속성: Id, Name, ContentType, Size, ContentBytes

  - 파일명: Folder.cs
    역할: 메일 폴더 모델
    속성: Id, DisplayName, UnreadCount

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

  - 파일명: NotificationService.cs
    경로: Services/Notification/
    역할: 데스크톱 알림

  - 파일명: NotificationSettings.cs
    경로: Services/Notification/
    역할: 알림 설정

  - 파일명: BackgroundSyncService.cs
    경로: Services/Sync/
    역할: 백그라운드 메일 동기화
    참고: Interlocked.CompareExchange 레이스컨디션 방지 + MailSyncCompleted 500ms Debounce + ODataError 410 처리

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

  - 파일명: ServerWebSocketSpeechService.cs
    경로: Services/Speech/
    역할: WebSocket 기반 서버 STT 서비스 (실시간 오디오 스트리밍, 청크 결과 수신)

  - 파일명: TextToSpeechService.cs
    경로: Services/Speech/
    역할: TTS 서비스 (NAudio WaveOutEvent 재생, 서버 모드 SynthesizeAsync 연동)
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
"C:\DATA\Project\MaiX\MaiX\bin\Debug\net10.0-windows\MaiX.exe"
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
