# ADVANCED.md - mailX 프로젝트 상세 가이드

> **역할**: CLAUDE.md의 상세 참조 문서로, 구체적인 구현 가이드가 필요할 때만 참조
> **읽기 조건**: CLAUDE.md에서 구체적인 상세 내용이 필요할 때만 선택적으로 참조

이 문서는 CLAUDE.md에서 분리된 상세 구현 가이드입니다.

---

## 이 문서 읽기 조건

### 필수 읽기 트리거 키워드

**새 View 생성**: "새 창", "새 페이지", "View 추가", "ViewModel 추가"
- 섹션: 새 View/ViewModel 생성 가이드

**Graph API**: "이메일 조회", "Microsoft Graph", "토큰 갱신", "MSAL"
- 섹션: Microsoft Graph API 사용 가이드

**AI 분석**: "이메일 분석", "프롬프트", "AI Provider", "Claude", "OpenAI"
- 섹션: AI 분석 시스템 사용 가이드

**문서 변환**: "첨부파일 변환", "HWP", "PDF 추출", "OCR"
- 섹션: 문서 변환기 시스템

**테스트**: "테스트", "검증", "로그", "REST API", "스크린샷"
- 섹션: 테스트 3단계 상세 절차

**Context7**: "use context7", "최신 문서", "라이브러리 문서"
- 섹션: Context7-MCP 서버 상세

**람다식**: "람다", "클로저", "foreach", "이벤트 핸들러"
- 섹션: C# 람다식 클로저 문제

---

## MCP 서버 사용

**상세 문서**: [MCP.md](./MCP.md)

MCP 서버(Serena, ref, Context7, vibe-check)의 상세 사용법은 MCP.md를 참조하세요.

---

## REST API 사용

**상세 문서**: [restapi.md](./restapi.md)

REST API 엔드포인트 및 테스트 자동화 방법은 restapi.md를 참조하세요.

---

## 테스트 3단계 상세 절차

### 절대 원칙
다음 3가지 테스트를 **순서대로 모두 통과**해야만 작업 완료 인정

### 1단계: 로그 분석 (필수 통과)

**목적**: 내부 동작 검증, 예외 발생 여부 확인

**방법**:
```bash
# 방법 A: 로그 파일 직접 읽기
Read "$APPDATA/mailX/logs/20260113.log"

# 방법 B: REST API로 실시간 로그 확인
curl -s "http://localhost:5858/api/logs/latest?lines=100"
```

**통과 조건**:
- ERROR/WARN/Exception 로그 0건
- Debug 로그에서 예상한 함수 호출 순서 확인
- 모든 기능 정상 완료 로그 존재

**실패 시**: 계획 수립 단계부터 **전체 사이클 재시작**

---

### 2단계: REST API 테스트 (필수 통과)

**목적**: 기능 동작 검증, 데이터 정확성 확인

**전제 조건**: 1단계 로그 분석 통과 완료

**방법**:
```bash
# 헬스 체크
curl -s http://localhost:5858/api/health

# 상태 확인
curl -s http://localhost:5858/api/status
```

**통과 조건**:
- 모든 API 호출 성공 (HTTP 200)
- 응답 데이터 정확성 100% 검증

**실패 시**: 계획 수립 단계부터 **전체 사이클 재시작**

---

### 3단계: 스크린샷 테스트 (필수 통과)

**목적**: UI 렌더링 검증, 시각적 요소 확인

**전제 조건**: 1단계 로그 분석 + 2단계 REST API 테스트 **모두 통과**

**방법**:
```bash
# 스크린샷 캡처
curl -X POST http://localhost:5858/api/screenshot

# 이미지 검증 (Read 도구)
Read "$APPDATA/mailX/screenshots/screenshot_*.png"
```

**통과 조건**:
- 창 정상 표시
- 한글 텍스트 정상 렌더링
- UI 컨트롤 배치 정확

**실패 시**: 계획 수립 단계부터 **전체 사이클 재시작**

---

### 절대 금지 행동

- **1단계만 통과 후 완료 선언**: 반드시 3단계 모두 통과해야 함
- **2단계 건너뛰기**: 순서대로 모두 실행 필수
- **테스트 실패 후 수정만 시도**: 반드시 계획 수립부터 재시작
- **사용자에게 확인 요청**: "테스트해주세요" 절대 금지, AI가 직접 검증

---

## 새 View/ViewModel 생성 가이드

### 1. ViewModel 생성

**위치**: `mailX/ViewModels/{Name}ViewModel.cs`

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace mailX.ViewModels;

public partial class NewViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "New View";

    [ObservableProperty]
    private bool _isLoading;

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            // 데이터 로드 로직
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

### 2. View 생성

**위치**: `mailX/Views/{Name}Window.xaml`

```xml
<ui:FluentWindow x:Class="mailX.Views.NewWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        xmlns:viewmodels="clr-namespace:mailX.ViewModels"
        Title="{Binding Title}"
        Height="600" Width="800">

    <ui:FluentWindow.DataContext>
        <viewmodels:NewViewModel/>
    </ui:FluentWindow.DataContext>

    <Grid>
        <!-- UI 내용 -->
    </Grid>
</ui:FluentWindow>
```

### 3. Code-Behind

**위치**: `mailX/Views/{Name}Window.xaml.cs`

```csharp
using Wpf.Ui.Controls;

namespace mailX.Views;

public partial class NewWindow : FluentWindow
{
    public NewWindow()
    {
        InitializeComponent();
    }
}
```

---

## Microsoft Graph API 사용 가이드

### 인증 흐름

```
1. MSAL 인증 (GraphAuthService)
   → InteractiveBrowserCredential 또는 DeviceCodeCredential

2. 토큰 캐시 (TokenCacheHelper)
   → %APPDATA%\mailX\token_cache.bin

3. Graph Client 생성
   → Microsoft.Graph.GraphServiceClient
```

### 이메일 조회 예시

```csharp
// GraphMailService 사용
var emails = await _graphMailService.GetEmailsAsync(
    folderId: "inbox",
    top: 50,
    filter: "isRead eq false"
);

// Delta 동기화
var changes = await _graphMailService.GetDeltaChangesAsync(deltaLink);
```

### 토큰 갱신

```csharp
// 자동 갱신 (MSAL 기본 동작)
// 토큰 만료 5분 전 자동으로 갱신 시도

// 수동 갱신 필요 시
await _graphAuthService.RefreshTokenAsync();
```

---

## AI 분석 시스템 사용 가이드

### AI Provider 구조

```
IAIProvider (인터페이스)
├── AIProviderBase (추상 클래스)
│   ├── ClaudeProvider (Anthropic)
│   ├── OpenAIProvider (OpenAI)
│   ├── GeminiProvider (Google)
│   ├── OllamaProvider (로컬)
│   └── LMStudioProvider (로컬)
└── AIService (팩토리/매니저)
```

### 이메일 분석 호출

```csharp
// AIService 사용
var result = await _aiService.AnalyzeEmailAsync(email, prompt);

// 결과 구조
// - SummaryOneline: 한줄 요약
// - Summary: 상세 요약
// - PriorityScore: 우선순위 점수 (0-100)
// - Keywords: 키워드 배열
// - Deadline: 추출된 마감일
```

### 프롬프트 관리

```csharp
// 기본 프롬프트 조회
var prompt = await _promptService.GetPromptAsync("email_summary");

// 프롬프트 템플릿 변수
// {{subject}} - 이메일 제목
// {{body}} - 이메일 본문
// {{from}} - 발신자
// {{attachments}} - 첨부파일 목록
```

---

## 문서 변환기 시스템

### 변환기 구조

```
IDocumentConverter (인터페이스)
├── OpenXmlDocConverter (.docx)
├── NpoiDocConverter (.doc)
├── NpoiExcelConverter (.xls, .xlsx)
├── ClosedXmlConverter (.xlsx)
├── OpenXmlPptConverter (.pptx)
├── PdfPigConverter (.pdf)
├── HwpConverter (.hwp) - 외부 도구
├── HwpSharpConverter (.hwp) - 네이티브
├── PandocConverter (범용)
├── OcrConverter (이미지)
└── WindowsOcrConverter (Windows OCR)
```

### 변환기 사용

```csharp
// AttachmentProcessor 사용
var processor = new AttachmentProcessor();
var markdown = await processor.ConvertToMarkdownAsync(attachment);

// 특정 변환기 직접 사용
var converter = new PdfPigConverter();
var text = await converter.ConvertToTextAsync(filePath);
```

### 변환기 설정

```csharp
// ConverterSettingService로 설정 관리
var setting = await _settingService.GetSettingAsync(".hwp");
// setting.SelectedConverter: "HwpSharp" 또는 "Pandoc"
```

---

## C# 람다식 클로저 문제

### 문제 상황

```csharp
// ❌ 잘못된 예시: foreach 루프에서 람다 캡처
foreach (var email in emails)
{
    button.Click += (s, e) => ProcessEmail(email);
    // 문제: 모든 버튼이 마지막 email만 참조
}
```

### 해결 방법

```csharp
// ✅ 올바른 예시: 지역 변수로 복사
foreach (var email in emails)
{
    var currentEmail = email;  // 지역 변수로 복사
    button.Click += (s, e) => ProcessEmail(currentEmail);
}
```

### 핵심 원칙

1. **람다에서 루프 변수나 파라미터를 직접 사용하지 말 것**
2. **지역 변수로 복사하거나 메서드 파라미터로 명시적으로 전달**
3. **특히 GUI 이벤트 핸들러에서 주의** (sender가 예상과 다를 수 있음)

---

## WPF ShutdownMode 주의사항

### 문제 상황

WPF 기본 ShutdownMode는 `OnLastWindowClose`로, 마지막 창이 닫히면 앱이 종료됩니다.

```csharp
// 문제: LoginWindow.Close() 시 MainWindow가 아직 없으면 앱 종료
LoginWindow.ShowDialog();  // 로그인 성공 후 Close()
MainWindow.Show();  // 이 줄에 도달하기 전에 앱 종료됨
```

### 해결 방법

```xml
<!-- App.xaml에서 ShutdownMode 설정 -->
<Application ...
    ShutdownMode="OnExplicitShutdown">
```

```csharp
// 명시적 종료 호출
Application.Current.Shutdown();
```

### mailX 적용

- `App.xaml`: `ShutdownMode="OnExplicitShutdown"` 설정됨
- 종료는 REST API (`/api/shutdown`) 또는 UI에서 명시적으로 호출

---

## 데이터베이스 마이그레이션

### 마이그레이션 생성

```bash
# 새 마이그레이션 추가
dotnet ef migrations add {MigrationName} --project mailX

# 데이터베이스 업데이트
dotnet ef database update --project mailX

# 마이그레이션 롤백
dotnet ef database update {PreviousMigrationName} --project mailX
```

### 주의사항

- 프로덕션 배포 전 마이그레이션 SQL 스크립트 검토
- 대량 데이터 마이그레이션 시 백업 필수
- 롤백 계획 수립

---

## 로깅 가이드

### Log4 사용법

```csharp
// 로그 레벨별 사용
Log4.Debug("디버그 정보");
Log4.Info("일반 정보");
Log4.Warn("경고");
Log4.Error("오류");
Log4.Fatal("치명적 오류");

// 예외 로깅
try
{
    // 코드
}
catch (Exception ex)
{
    Log4.Error($"작업 실패: {ex.Message}", ex);
}
```

### 로그 파일 위치

- **경로**: `%APPDATA%\mailX\logs\`
- **파일명**: `yyyyMMdd.log` (일별 로테이션)

### 로그 분석

```bash
# REST API로 최신 로그 조회
curl -s "http://localhost:5858/api/logs/latest?lines=100"

# 로그 파일 직접 읽기
Read "$APPDATA/mailX/logs/20260113.log"
```

---

## 에러 핸들링 패턴

### 기본 패턴

```csharp
public async Task<Result<T>> ExecuteAsync<T>(Func<Task<T>> action)
{
    try
    {
        var result = await action();
        return Result<T>.Success(result);
    }
    catch (GraphServiceException ex)
    {
        Log4.Error($"Graph API 오류: {ex.Message}");
        return Result<T>.Failure($"Graph API 오류: {ex.Message}");
    }
    catch (Exception ex)
    {
        Log4.Error($"예기치 않은 오류: {ex.Message}", ex);
        return Result<T>.Failure($"오류 발생: {ex.Message}");
    }
}
```

### ViewModel에서 에러 처리

```csharp
[RelayCommand]
private async Task LoadEmailsAsync()
{
    try
    {
        IsLoading = true;
        ErrorMessage = null;

        var result = await _mailService.GetEmailsAsync();
        if (result.IsSuccess)
        {
            Emails = new ObservableCollection<Email>(result.Value);
        }
        else
        {
            ErrorMessage = result.Error;
        }
    }
    catch (Exception ex)
    {
        ErrorMessage = $"이메일 로드 실패: {ex.Message}";
        Log4.Error(ErrorMessage, ex);
    }
    finally
    {
        IsLoading = false;
    }
}
```
