# kplan 계획서 — 메일 기능 4건 개선
> PIPELINE_UUID: 08d06298-8c86-4c8d-98f4-31d4cd6a81fe
> 확정 tier: **k4 (Heavy)**
> 작성일: 2026-04-02

---

## 요구사항 요약

| # | 요구사항 | 유형 | 난이도 |
|---|---------|------|--------|
| R1 | CC/BCC 표시 버그 수정 | 버그 수정 | 낮음 |
| R2 | 첨부파일 다운로드 기능 | 신규 기능 | 중간 |
| R3 | 영업폴더 삭제 메일 동기화 | 버그 수정 | 중간 |
| R4 | 인피니티 스크롤 (메일 목록) | 성능 개선 | 중간 |

---

## R1: CC/BCC 표시 버그 수정

### 근본 원인 분석

**UI 코드는 정상** — `EmailViewWindow.xaml`(행 103-126)에 CcRow/BccRow가 올바르게 정의되어 있고, `EmailViewWindow.xaml.cs`(행 97-110)에서 `ParseJsonArrayToString()`으로 파싱 후 Visibility 설정하는 로직도 정상.

**진짜 버그: Graph API Select 필드 누락**
- `GraphMailService.GetMessagesAsync()` (행 187-190): `selectFields`에 `"bccRecipients"` 누락 → BCC 데이터 자체가 API에서 반환되지 않음
- 동일 메서드에 `"ccRecipients"`는 포함되어 있으나, `"categories"`, `"parentFolderId"` 등 다른 필수 필드도 누락
- `GetLatestMessagesAsync()` (행 399-405)와 Delta Query (행 296-301)에는 `"bccRecipients"` 포함 → **비일관성**

**추가 버그: Historical Sync에서 Bcc 누락**
- `BackgroundSyncService.cs` 행 1855: Historical sync의 새 Email 생성 시 `Bcc = SerializeRecipients(message.BccRecipients)` 누락 — `Cc`만 설정됨

### 수정 계획

| 파일 | 수정 내용 |
|------|----------|
| `Services/Graph/GraphMailService.cs:187-190` | `GetMessagesAsync()` selectFields에 `"bccRecipients"`, `"categories"`, `"parentFolderId"` 추가 |
| `Services/Sync/BackgroundSyncService.cs:1855` | Historical sync Email 생성 시 `Bcc = SerializeRecipients(message.BccRecipients)` 추가 |

### 검수 조건
- CC가 있는 메일을 열었을 때 "참조:" 행이 표시됨
- BCC가 있는 메일(보낸 메일)을 열었을 때 "숨은 참조:" 행이 표시됨
- 기존 메일도 다음 동기화 사이클에서 CC/BCC 필드가 업데이트됨

---

## R2: 첨부파일 다운로드 기능

### 현재 상태
- `EmailViewWindow.xaml`: 첨부파일 표시 UI 없음
- `GraphMailService.GetInlineAttachmentsAsync()`: 인라인 이미지만 처리 (cid: 방식)
- `Attachment` 모델: LocalPath, Name, ContentType, Size 필드 존재
- Graph API: `client.Me.Messages[messageId].Attachments.GetAsync()` 사용 가능

### 수정 계획

#### 2-1. GraphMailService에 첨부파일 다운로드 메서드 추가

| 파일 | 수정 내용 |
|------|----------|
| `Services/Graph/GraphMailService.cs` | `GetAttachmentsAsync(messageId)` 메서드 추가 — 모든 첨부파일 메타데이터+바이트 반환 |
| `Services/Graph/GraphMailService.cs` | `DownloadAttachmentAsync(messageId, attachmentId)` 메서드 추가 — 개별 첨부파일 다운로드 후 임시 폴더 저장, 파일 경로 반환 |

#### 2-2. EmailViewWindow에 첨부파일 UI 추가

| 파일 | 수정 내용 |
|------|----------|
| `Views/EmailViewWindow.xaml` | 헤더 영역(Grid.Row="1") 하단에 첨부파일 패널 추가: 아이콘 + 파일명 + 크기 + 클릭 이벤트 |
| `Views/EmailViewWindow.xaml` | Grid RowDefinition 추가 (첨부파일 영역) |
| `Views/EmailViewWindow.xaml.cs` | `LoadAttachmentsAsync()` — Graph API로 첨부파일 목록 로드, UI 바인딩 |
| `Views/EmailViewWindow.xaml.cs` | `AttachmentItem_Click()` — 클릭 시 다운로드 → 임시폴더 저장 → `Process.Start()` 로 열기 |

#### UI 디자인
```
┌──────────────────────────────────────────┐
│ 📎 첨부파일 (3개)                         │
│ ┌────────┐ ┌────────┐ ┌────────┐        │
│ │📄 계약서.pdf│ │📊 매출.xlsx│ │🖼 사진.png│        │
│ │  1.2 MB │ │  256 KB │ │  3.4 MB│        │
│ └────────┘ └────────┘ └────────┘        │
└──────────────────────────────────────────┘
```

#### 임시 파일 관리
- 다운로드 경로: `%APPDATA%\MaiX\temp\attachments\{messageId}\`
- 이미 다운로드된 파일은 재다운로드 없이 바로 열기
- 앱 종료 시 또는 주기적으로 오래된 임시 파일 정리 (기존 CleanupTempFolder 활용)

### 검수 조건
- 첨부파일 있는 메일을 열면 첨부파일 패널이 표시됨
- 첨부파일 클릭 시 다운로드 후 기본 프로그램으로 열림
- 첨부파일 없는 메일에서는 패널이 숨겨짐
- 대용량 파일(10MB+) 다운로드 시 프로그레스 표시

---

## R3: 영업폴더 삭제 메일 동기화

### 근본 원인 분석

Delta Query 기반의 삭제 감지(`@removed`)는 정상 작동하지만, **두 가지 시나리오에서 삭제가 감지되지 않음**:

1. **Delta Link 갱신 간격 문제**: 전체 동기화는 5분 간격, 즐겨찾기가 아닌 폴더는 전체 동기화에서만 처리됨. Delta Link가 1시간 이상 갱신되지 않으면 리셋되어 전체 재동기화하는데, 이때 `@removed`가 포함되지 않을 수 있음.

2. **서버-로컬 비교 로직 부재**: Delta Query의 `@removed`에만 의존하므로, Delta Link이 만료되어 전체 재동기화할 때 서버에 없는 메일을 로컬에서 삭제하는 로직이 없음. 서버에서 메일이 삭제/이동되면 Delta에서 `@removed`로 감지해야 하는데, Delta Link 리셋 시 이 정보가 유실됨.

### 수정 계획

| 파일 | 수정 내용 |
|------|----------|
| `Services/Sync/BackgroundSyncService.cs` | `SyncFolderAsync()` 내에 **Full Reconciliation** 로직 추가: Delta Link 리셋 후 전체 재동기화 시 서버 메일 ID 목록과 로컬 DB 메일 ID 목록을 비교하여, 서버에 없는 로컬 메일 삭제 |
| `Services/Sync/BackgroundSyncService.cs` | `ReconcileDeletedEmailsAsync()` 신규 메서드: 서버의 전체 메일 ID 목록(`GetAllMessageIdsAsync`)과 로컬 DB의 EntryId를 비교 |
| `Services/Graph/GraphMailService.cs` | `GetAllMessageIdsAsync(folderId)` 메서드 추가: 해당 폴더의 모든 메일 ID만 가져오기 (Select=id, 페이징 처리) |

#### Reconciliation 로직
```
1. 서버에서 폴더 내 모든 메일 ID 조회 (Select=id만, 페이징)
2. 로컬 DB에서 해당 폴더의 모든 EntryId 조회
3. 로컬에만 있는 EntryId = 서버에서 삭제됨 → 로컬에서도 삭제
4. 삭제 건수 로깅
```

#### 실행 조건
- Delta Link 리셋(만료/410 Gone) 후 전체 재동기화 시에만 실행
- **또는** 주기적으로 실행 (10분 ~ 30분 간격, 설정 가능)
- 즐겨찾기/비즐겨찾기 폴더 모두 대상

### 검수 조건
- 아웃룩에서 삭제한 메일이 다음 동기화 사이클(5분 이내)에서 mAIx에서도 사라짐
- 영업폴더처럼 비즐겨찾기 폴더에서도 삭제 동기화 작동
- 대량 삭제(50건+) 시에도 안정적으로 동작

---

## R4: 인피니티 스크롤 (메일 목록)

### 현재 상태
- `MainViewModel.LoadEmailsAsync()`: DB에서 **전체 로드** (`ToListAsync()` 한 번에)
- `MainWindow.xaml`: `ListBox`에 `VirtualizingPanel.IsVirtualizing="True"` + Recycling — UI 가상화는 있으나 **데이터 가상화** 없음
- `Emails` 프로퍼티: `List<Email>` — 전체 메일을 메모리에 적재

### 수정 계획

| 파일 | 수정 내용 |
|------|----------|
| `ViewModels/MainViewModel.cs` | `LoadEmailsAsync()` 수정: 초기 100건만 로드 (`.Take(100)`) |
| `ViewModels/MainViewModel.cs` | `LoadMoreEmailsAsync()` 신규 메서드: 추가 100건 로드, 기존 목록에 Append |
| `ViewModels/MainViewModel.cs` | `_emails` 타입을 `ObservableCollection<Email>`로 변경 (AddRange 지원) |
| `ViewModels/MainViewModel.cs` | `HasMoreEmails` 프로퍼티 추가 (추가 로드 가능 여부) |
| `ViewModels/MainViewModel.cs` | `_currentPage`, `_pageSize` 필드 추가 |
| `Views/MainWindow.xaml` | ListBox의 ScrollViewer에 `ScrollChanged` 이벤트 핸들러 추가 |
| `Views/MainWindow.xaml.cs` | `EmailListBox_ScrollChanged()`: 스크롤 하단 도달 감지 → `LoadMoreEmailsAsync()` 호출 |
| `Views/MainWindow.xaml` | 로딩 인디케이터 (ProgressBar) ListBox 하단에 추가 |

#### 페이지네이션 로직
```csharp
// 초기 로드
var emails = await query
    .OrderByDescending(e => e.ReceivedDateTime)
    .Take(_pageSize)  // 100
    .ToListAsync(ct);

// 추가 로드
var moreEmails = await query
    .OrderByDescending(e => e.ReceivedDateTime)
    .Skip(_currentPage * _pageSize)
    .Take(_pageSize)
    .ToListAsync(ct);
```

#### 스크롤 감지
```csharp
private void EmailListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
{
    if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 50)
    {
        // 하단 50px 이내 도달 → 추가 로드
        _viewModel.LoadMoreEmailsCommand.Execute(null);
    }
}
```

#### 주의사항
- 검색 모드(`IsSearchMode`)에서는 기존 전체 로드 유지 (검색 결과는 이미 필터링됨)
- 그룹 뷰(Thread/Date) 와의 호환성 확인 필요
- 폴더 변경 시 `_currentPage = 0` 리셋

### 검수 조건
- 폴더 선택 시 최초 100개만 빠르게 로드됨
- 스크롤 하단 도달 시 추가 100개 자동 로드
- 로딩 중 프로그레스 인디케이터 표시
- 전체 메일 수 대비 로드된 수 표시 (예: "100/523개")

---

## 수정 대상 파일 목록

| # | 파일 | 관련 요구사항 | 수정 유형 |
|---|------|-------------|----------|
| 1 | `mAIx/Services/Graph/GraphMailService.cs` | R1, R2, R3 | 수정 |
| 2 | `mAIx/Services/Sync/BackgroundSyncService.cs` | R1, R3 | 수정 |
| 3 | `mAIx/Views/EmailViewWindow.xaml` | R2 | 수정 |
| 4 | `mAIx/Views/EmailViewWindow.xaml.cs` | R2 | 수정 |
| 5 | `mAIx/ViewModels/MainViewModel.cs` | R4 | 수정 |
| 6 | `mAIx/Views/MainWindow.xaml` | R4 | 수정 |
| 7 | `mAIx/Views/MainWindow.xaml.cs` | R4 | 수정 |

---

## 파일 할당 매트릭스 (에이전트별 담당)

### Agent A: 백엔드 (Graph API + Sync)
| 파일 | 요구사항 | 작업 |
|------|---------|------|
| `GraphMailService.cs` | R1 | selectFields에 누락 필드 추가 |
| `GraphMailService.cs` | R2 | `GetAttachmentsAsync()`, `DownloadAttachmentAsync()` 추가 |
| `GraphMailService.cs` | R3 | `GetAllMessageIdsAsync()` 추가 |
| `BackgroundSyncService.cs` | R1 | Historical sync Bcc 누락 수정 |
| `BackgroundSyncService.cs` | R3 | `ReconcileDeletedEmailsAsync()` 추가 + SyncFolderAsync에 연동 |

### Agent B: 프론트엔드 (EmailView + 메일 목록)
| 파일 | 요구사항 | 작업 |
|------|---------|------|
| `EmailViewWindow.xaml` | R2 | 첨부파일 패널 UI 추가 |
| `EmailViewWindow.xaml.cs` | R2 | 첨부파일 로드/다운로드/열기 로직 |
| `MainViewModel.cs` | R4 | 인피니티 스크롤 (페이지네이션, ObservableCollection) |
| `MainWindow.xaml` | R4 | ScrollChanged 이벤트, 로딩 인디케이터 |
| `MainWindow.xaml.cs` | R4 | 스크롤 하단 감지 핸들러 |

### 충돌 파일 없음
- Agent A와 B의 담당 파일이 완전히 분리됨
- `GraphMailService.cs`는 Agent A 단독 소유

---

## 실행 순서

```
Phase 1: Agent A + Agent B 병렬 실행
  ├─ Agent A: R1(CC/BCC) → R3(삭제 동기화) → R2(첨부 API)
  └─ Agent B: R4(인피니티 스크롤) → R2(첨부 UI)

Phase 2: 빌드 검증 (ktest_build)
Phase 3: 기능 테스트 (ktest)
Phase 4: 마무리 (kdone)
```

---

## Tier 판정 근거: k4 (Heavy)

- 4개 요구사항 (2 버그 + 1 신규 기능 + 1 성능 개선)
- 7개 파일 수정
- 2개 에이전트 병렬 투입
- Graph API + DB + UI 전 레이어 관통
- 신규 메서드 5개+ 추가
- 인피니티 스크롤은 기존 데이터 흐름 변경 (List → ObservableCollection)
