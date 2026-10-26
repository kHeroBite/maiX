> 보관: 2026-10-26, 구 oinfra_maix 스킬 부속 문서. 고유 내용은 루트 ADVANCED.md "MS365 탭 UI·아키텍처 패턴" 절로 병합됨.
# ADVANCED.md — MaiX 고급 UI/아키텍처 가이드

> oinfra_maix SKILL.md 의 확장 가이드. UI 패턴, 컨트롤 구현, 아키텍처 심층 정보.

---

## 1. MainWindow Partial Class 분할 패턴

### 배경
MainWindow.xaml.cs가 800KB를 초과하여 관리 불가 수준에 도달.
Phase 0에서 탭별 partial class로 분할 완료.

### 분할 구조
```csharp
// 각 파일은 partial class MainWindow 선언
public partial class MainWindow : FluentWindow
{
    // MainWindow.xaml.cs — 공통 초기화, 네비게이션, DI
    // MainWindow.Calendar.cs — 캘린더 탭 전용 이벤트/핸들러
    // MainWindow.Todo.cs    — ToDo 탭
    // MainWindow.Contacts.cs — 연락처 탭
    // MainWindow.Teams.cs   — Teams 탭
    // MainWindow.OneDrive.cs — OneDrive 탭
    // MainWindow.OneNote.cs  — OneNote 탭
    // MainWindow.Planner.cs  — Planner 탭
    // MainWindow.Activity.cs — Activity 탭
    // MainWindow.Calls.cs    — 통화 탭
}
```

### 규칙
- 각 partial 파일은 해당 탭의 이벤트 핸들러와 탭-전환 로직만 포함
- 비즈니스 로직은 ViewModel에 위임 (MVVM 원칙)
- 공유 상태(ViewModel 인스턴스, DI 컨테이너)는 MainWindow.xaml.cs에서 초기화

---

## 2. Controls/ 디렉토리 — WPF UserControl 패턴

### 네임스페이스
```xml
xmlns:controls="clr-namespace:mAIx.Controls"
```

### 캘린더 뷰 컨트롤 (Phase 1)

#### CalendarGridControl — 월간 그리드
- 6행 × 7열 UniformGrid로 날짜 셀 렌더링
- 각 셀은 DayCell 템플릿, 이벤트 목록은 ItemsControl
- 의존 프로퍼티: `CurrentMonth`, `Events`

#### WeekViewControl — 주간 뷰
- 시간대 축(0~23시) + 7일 컬럼 그리드
- 이벤트는 Canvas 오버레이로 절대 위치 배치
- 겹침 이벤트 처리: 너비 분할 알고리즘

#### DayViewControl — 일간 뷰
- 시간 슬롯 단위 세로 스크롤
- 이벤트 클릭 → EventEditDialog 팝업

#### MiniCalendarControl — 사이드바 미니 캘린더
- 42개 날짜 버튼 (6주 × 7일)
- 선택일 하이라이트, 오늘 강조

### 칸반 컨트롤 (Phase 6)

#### KanbanBoardControl
```csharp
// 버킷(컬럼)별 ItemsControl + ScrollViewer
// 드래그&드롭: DragOver/Drop 이벤트로 카드 이동
// ViewModel: PlannerViewModel.Buckets (ObservableCollection)
```

#### KanbanCardControl
- 제목, 담당자 아바타, 우선순위 색상 바지
- 더블클릭 → 작업 상세 다이얼로그
- 의존 프로퍼티: `Task` (PlannerTask)

### OneDrive 컨트롤 (Phase 5)

#### FilePreviewPanel
- 파일 유형별 프리뷰: 이미지(Image), 텍스트(RichTextBox), Office(WebView2)
- 닫기/다운로드/공유 버튼 포함

#### BreadcrumbNav
- 경로 세그먼트를 Button으로 렌더링
- 각 세그먼트 클릭 → NavigateTo(folderId) 이벤트

#### FileGridControl
- 보기 전환: 목록(ListView) / 타일(WrapPanel)
- 정렬: 이름/수정일/크기

### Teams 컨트롤 (Phase 4)

#### MessageBubbleControl
- 발신/수신 좌우 정렬 분기 (IsOutgoing 의존 프로퍼티)
- 리액션 이모지 표시 영역
- 스레드 답글 수 표시

#### MentionPopup
- TextBox 커서 위치 기준 팝업 위치 계산
- @입력 시 필터링된 멤버 목록 표시

---

## 3. ViewModel 패턴

### 자연어 날짜 파싱 (CalendarViewModel + NaturalLanguageDateParser)
```csharp
// Utilities/NaturalLanguageDateParser.cs
// 지원: "오늘", "내일", "다음 주 월요일", "이번 주 금요일" 등
var parser = new NaturalLanguageDateParser();
DateTime? result = parser.Parse("다음 주 월요일");
```

### 스마트 목록 (TodoViewModel)
```csharp
// 오늘 할 일, 중요, 계획됨, 완료됨 — 필터 기반 가상 목록
public IEnumerable<TodoItem> SmartListItems =>
    _allTasks.Where(t => SmartListFilter(t, SelectedSmartList));
```

### 칸반 상태 이동 (PlannerViewModel)
```csharp
// MoveTaskToBucket(taskId, targetBucketId) — Graph API 호출 + 로컬 갱신
await MoveTaskToBucketAsync(task, targetBucket);
```

### 크로스탭 통합 (CrossTabIntegrationService)
```csharp
// Phase 7: 탭 간 이벤트 브로드캐스트
// 예: Teams 미팅 → 캘린더 자동 등록
_crossTabService.OnMeetingCreated += async (meeting) =>
    await _calendarViewModel.AddEventAsync(meeting.ToCalendarEvent());
```

---

## 4. Graph API 서비스 확장 패턴

### 공통 패턴
```csharp
private static readonly Logger _log = LogManager.GetCurrentClassLogger();

public async Task<T> CallGraphAsync<T>(...)
{
    try { ... }
    catch (ServiceException ex)
    {
        _log.Error(ex, "Graph API 오류: {Message}", ex.Message);
        throw;
    }
}
```

### OneDrive 청크 업로드 (ChunkedUploadService)
```csharp
// 5MB 초과 파일: 업로드 세션 생성 → 청크 분할 전송
var session = await _graphClient.Drive.Items[parentId]
    .ItemWithPath(fileName).CreateUploadSession(uploadProps)
    .Request().PostAsync();

// 320KB 단위 청크 반복 전송
foreach (var chunk in SplitIntoChunks(stream, 320 * 1024))
    await UploadChunkAsync(session, chunk, ...);
```

---

## 5. DB / EF Core 패턴

### PlannerCustomField 엔티티
```csharp
// Models/PlannerCustomField.cs
public class PlannerCustomField
{
    public int Id { get; set; }
    public string PlanId { get; set; }
    public string FieldName { get; set; }
    public string FieldType { get; set; } // text, number, date, bool
    public string DefaultValue { get; set; }
}
```

### 마이그레이션 추가 패턴
```bash
# Windows dotnet 사용 필수 (WSL dotnet 금지)
cmd.exe /c "dotnet ef migrations add MigrationName --project C:\DATA\Project\mAIx\mAIx\mAIx.csproj"
```

---

## 6. 주의사항 / 알려진 제약

### MiniCalendarControl 미생성
- Controls/ 디렉토리에 MiniCalendarControl.xaml/.cs 파일이 실제로 생성되지 않았을 수 있음
- CalendarGridControl이 미니 캘린더 역할도 겸함

### TodoEditDialog / ContactEditDialog
- Views/Dialogs/ 또는 Dialogs/ 에 스텁 파일만 존재할 수 있음
- 실제 상세 UI는 TodoDetailPanel(Control) 또는 ContactDetailControl로 대체 구현됨

### 20260409000012_AddCalendarSyncToken
- Migrations/ 디렉토리 실물 확인 필요 (git status에서 미노출 — 기존 파일일 가능성)

---

## 7. 관련 문서

- [PROJECT.md](./PROJECT.md) — 파일 인벤토리 전체 목록
- [DATABASE.md](./DATABASE.md) — 테이블/스키마 상세
- [MCP.md](./MCP.md) — MCP 서버 통합
- [RESTAPI.md](./restapi.md) — REST API 엔드포인트
