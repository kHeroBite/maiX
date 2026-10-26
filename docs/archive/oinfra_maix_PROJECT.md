> 보관: 2026-10-26, 구 oinfra_maix 스킬 부속 문서. 고유 내용은 루트 PROJECT.md "MS365 탭 인벤토리" 절로 병합됨.
# PROJECT.md — MaiX 프로젝트 구조 및 파일 인벤토리

> oinfra_maix SKILL.md 의 상세 보완본. 파일 구조/아키텍처/인벤토리 전용.

---

## 프로젝트 개요

| 항목 | 값 |
|------|-----|
| 솔루션 | mAIx.sln |
| 주 프로젝트 | mAIx/mAIx.csproj |
| 프레임워크 | .NET 10.0-windows (WPF) |
| UI 라이브러리 | WPF UI (Fluent Design) |
| 데이터베이스 | SQLite (EF Core) |
| 아키텍처 | MVVM + Partial Class 분할 |

---

## 디렉토리 구조

```
mAIx/
├── Controls/          # WPF UserControl 컴포넌트
├── Converters/        # IValueConverter 구현체
├── Data/              # EF Core DbContext
├── Dialogs/           # 독립 Dialog 창 (Views/Dialogs/ 외 별도)
├── Migrations/        # EF Core 마이그레이션
├── Models/            # 도메인 모델 / DB 엔티티
├── Queries/           # DB 쿼리 헬퍼
├── Resources/         # XAML 리소스 딕셔너리
├── Services/          # 비즈니스 로직 서비스
│   ├── AI/
│   ├── Analysis/
│   ├── Api/
│   ├── Audio/
│   ├── Cache/
│   ├── Cloud/
│   ├── Converter/
│   ├── Editor/
│   ├── Graph/         # MS Graph API 서비스
│   ├── Notification/
│   ├── Rules/
│   ├── Search/
│   ├── Speech/
│   ├── Storage/
│   ├── Sync/
│   └── Theme/
├── Utilities/         # 범용 유틸리티 (날짜 파서 등)
├── Utils/             # 기타 유틸리티
├── ViewModels/        # MVVM ViewModel
└── Views/             # WPF Window / MainWindow partial class
    └── Dialogs/       # WPF Dialog (ContentDialog 등)
```

---

## Controls/ — WPF UserControl 목록

### 캘린더 (Phase 1)
| 파일 | 설명 |
|------|------|
| CalendarGridControl.xaml/.cs | 월간 그리드 뷰 |
| WeekViewControl.xaml/.cs | 주간 뷰 |
| DayViewControl.xaml/.cs | 일간 뷰 |
| MiniCalendarControl.xaml/.cs | 사이드 미니 캘린더 |
| EventCardControl.xaml/.cs | 이벤트 카드 |

### ToDo (Phase 2)
| 파일 | 설명 |
|------|------|
| TodoListControl.xaml/.cs | ToDo 목록 패널 |
| MyDayControl.xaml/.cs | 오늘 할 일 뷰 |
| TodoDetailPanel.xaml/.cs | Todo 상세 패널 |

### 연락처 (Phase 3)
| 파일 | 설명 |
|------|------|
| ContactListControl.xaml/.cs | 연락처 목록 |
| ContactDetailControl.xaml/.cs | 연락처 상세 |
| ContactActionBar.xaml/.cs | 연락처 액션바 |

### Teams (Phase 4)
| 파일 | 설명 |
|------|------|
| MessageBubbleControl.xaml/.cs | 채팅 말풍선 |
| ThreadPanel.xaml/.cs | 스레드 패널 |
| MentionPopup.xaml/.cs | @멘션 팝업 |

### OneDrive (Phase 5)
| 파일 | 설명 |
|------|------|
| FilePreviewPanel.xaml/.cs | 파일 미리보기 패널 |
| BreadcrumbNav.xaml/.cs | 경로 탐색 네비게이션 |
| FileGridControl.xaml/.cs | 파일 그리드 뷰 |

### Planner / OneNote (Phase 6)
| 파일 | 설명 |
|------|------|
| KanbanBoardControl.xaml/.cs | 칸반 보드 |
| KanbanCardControl.xaml/.cs | 칸반 카드 |
| BacklinkPanel.xaml/.cs | OneNote 백링크 패널 |
| NotebookTreeControl.xaml/.cs | 노트북 트리 |

### Activity / 통화 (Phase 7)
| 파일 | 설명 |
|------|------|
| ActivityFeedControl.xaml/.cs | 활동 피드 |
| CallHistoryControl.xaml/.cs | 통화 기록 |

---

## ViewModels/

| 파일 | 설명 | 추가 시기 |
|------|------|----------|
| ViewModelBase.cs | 기반 ViewModel | 초기 |
| LoginViewModel.cs | 로그인 | 초기 |
| MainViewModel.cs | 메인 (메일 + 탭 전환) | 초기 |
| ComposeViewModel.cs | 메일 작성 | 초기 |
| NewsletterViewModel.cs | 뉴스레터 | 2026-04 |
| CalendarViewModel.cs | 캘린더 (4가지 뷰, 자연어 날짜 파싱) | Phase 1 |
| TodoViewModel.cs | ToDo (스마트목록, 반복패턴) | Phase 2 |
| ContactsViewModel.cs | 연락처 (CRUD, 그룹 필터) | Phase 3 |
| TeamsViewModel.cs | Teams (스레드, 리액션, @멘션, 미팅) | Phase 4 |
| OneDriveViewModel.cs | OneDrive (미리보기, 청크업로드, 공유, 버전) | Phase 5 |
| PlannerViewModel.cs | Planner (칸반, 타임라인) | Phase 6 |
| OneNoteViewModel.cs | OneNote (백링크, 태그) | Phase 6 |
| ActivityViewModel.cs | 활동 피드 | Phase 7 |
| CallsViewModel.cs | 통화 기록 | Phase 7 |

---

## Services/

### Services/Graph/ — MS Graph API 서비스
| 파일 | 설명 |
|------|------|
| GraphMailService.cs | 메일 Graph API |
| GraphCalendarService.cs | 캘린더 Graph API |
| GraphContactService.cs | 연락처 Graph API |
| GraphToDoService.cs | ToDo Graph API |
| GraphTeamsService.cs | Teams Graph API (스레드, 리액션, 미팅) |
| GraphOneDriveService.cs | OneDrive Graph API (공유, 버전, 업로드) |
| GraphPlannerService.cs | Planner Graph API (칸반, 버킷) |
| GraphActivityService.cs | Activity Graph API |
| GraphCallService.cs | 통화 Graph API |

### Services/ 루트 — 주요 서비스
| 파일 | 설명 | 추가 시기 |
|------|------|----------|
| AutoReplyService.cs | 자동 응답 | 초기 |
| ChunkedUploadService.cs | 청크 업로드 (OneDrive 대용량) | Phase 5 |
| CommandPaletteService.cs | 커맨드 팔레트 | 2026-04 |
| ConversationGrouper.cs | 대화 그룹화 | 2026-04 |
| CrossTabIntegrationService.cs | 크로스탭 이벤트 통합 | Phase 7 |
| DelayedSendService.cs | 예약 발송 | 2026-04 |
| ExportService.cs | 내보내기 | 2026-04 |
| FocusedInboxService.cs | 포커스 받은편지함 | 2026-04 |
| KeyboardShortcutService.cs | 키보드 단축키 | 2026-04 |
| QuickStepService.cs | 빠른 단계 | 2026-04 |
| ReplyLaterService.cs | 나중에 답장 | 2026-04 |
| ScreenerService.cs | 스크리너 | 2026-04 |
| SplitInboxService.cs | 분할 받은편지함 | 2026-04 |
| TrackingBlockerService.cs | 추적 차단 | 2026-04 |
| UnsubscribeService.cs | 구독 취소 | 2026-04 |

---

## Models/

| 파일 | 설명 | 추가 시기 |
|------|------|----------|
| (기존 모델들) | Emails, Folders, CalendarEvent 등 | 초기 |
| PlannerCustomField.cs | Planner 커스텀 필드 | Phase 6 |

---

## Migrations/ — EF Core 마이그레이션 이력

| 마이그레이션 | 설명 | 날짜 |
|---|---|---|
| 20260112233939_InitialCreate | 초기 스키마 (Emails, Folders 등) | 2026-01 |
| 20260113061653_AddConverterSettings | 변환기 설정 | 2026-01 |
| 20260113091616_RemoveAccountsTable | Accounts 테이블 제거 | 2026-01 |
| 20260115114605_AddFolderIsFavorite | 폴더 즐겨찾기 | 2026-01 |
| 20260116040905_AddFavoriteOrder | 즐겨찾기 순서 | 2026-01 |
| 20260116061003_AddIsHtmlColumn | HTML 여부 컬럼 | 2026-01 |
| 20260116102555_AddFlagCategoriesStarred | 플래그/카테고리/별표 | 2026-01 |
| 20260117100000_AddEmailPinned | 메일 고정 | 2026-01 |
| 20260120081558_AddCalendarTables | 캘린더 테이블 | 2026-01 |
| 20260121100000_AddChatFavorites | 채팅 즐겨찾기 | 2026-01 |
| 20260329000001_AddAiClassificationFields | AI 분류 필드 | 2026-03 |
| 20260329000002_AddEmailFts5 | 이메일 FTS5 전문검색 | 2026-03 |
| 20260329000003_AddScheduledSendTime | 예약 발송 시간 | 2026-03 |
| 20260329000004_AddSnoozedUntil | 미루기 | 2026-03 |
| 20260329000005_AddMailRules | 메일 규칙 | 2026-03 |
| 20260329000006_AddFollowUpDate | 팔로업 날짜 | 2026-03 |
| 20260408000007_AddEmailCompositeIndex | 이메일 복합 인덱스 | 2026-04-08 |
| 20260408000008_AddQuickStep | QuickSteps 테이블 | 2026-04-08 |
| 20260408000009_AddConversationIndex | 대화 인덱스 | 2026-04-08 |
| 20260408000010_AddSplitInboxRule | 분할 받은편지함 규칙 | 2026-04-08 |
| 20260408000011_AddScreenerAndReplyLater | 스크리너/나중에 답장 | 2026-04-08 |
| 20260409000012_AddCalendarSyncToken | 캘린더 동기화 토큰 | 2026-04-09 |
| 20260409000013_AddPlannerCustomField | Planner 커스텀 필드 | 2026-04-09 |

---

## Utilities/

| 파일 | 설명 | 추가 시기 |
|------|------|----------|
| NaturalLanguageDateParser.cs | 자연어 날짜 파싱 ("다음 주 월요일" 등) | Phase 0 |

---

## Views/ — MainWindow Partial Class 분할 구조

| 파일 | 설명 |
|------|------|
| MainWindow.xaml | XAML 레이아웃 (전체 탭 UI) |
| MainWindow.xaml.cs | 메인 코드비하인드 + 공통 로직 |
| MainWindow.Calendar.cs | 캘린더 탭 이벤트/로직 |
| MainWindow.Todo.cs | ToDo 탭 이벤트/로직 |
| MainWindow.Contacts.cs | 연락처 탭 이벤트/로직 |
| MainWindow.Teams.cs | Teams 탭 이벤트/로직 |
| MainWindow.OneDrive.cs | OneDrive 탭 이벤트/로직 |
| MainWindow.OneNote.cs | OneNote 탭 이벤트/로직 |
| MainWindow.Planner.cs | Planner 탭 이벤트/로직 |
| MainWindow.Activity.cs | Activity 탭 이벤트/로직 |
| MainWindow.Calls.cs | 통화 탭 이벤트/로직 |

---

## Views/Dialogs/

| 파일 | 설명 |
|------|------|
| EventEditDialog.xaml/.cs | 일정 편집 다이얼로그 |
| ShareDialog.xaml/.cs | OneDrive 공유 다이얼로그 |
| VersionHistoryDialog.xaml/.cs | OneDrive 버전 기록 다이얼로그 |

## Dialogs/ (루트)

| 파일 | 설명 |
|------|------|
| MeetingScheduleDialog.xaml/.cs | Teams 미팅 일정 잡기 다이얼로그 |

---

## 작업 이력 (요약)

| 날짜 | 작업 | 분류 |
|------|------|------|
| 2026-04-09 | MS365 탭 전체 기능 구현 (Phase 0~7) — Controls 20종 신규, ViewModel 4종 신규, Service 2종 신규 | o5 |
| 2026-04-08 | 아웃룩 대비 기능 대폭 확장 + 성능 최적화 | o5 |
| 2026-04-08 | 메일함 폴더별 캐시 시스템 + 이벤트 증분 sync | o4 |
| 2026-04-03 | 설정>메일 초기 메일수 선택 옵션 + 동기화 기본값 버그 수정 | o3 |
| 2026-04-02 | R1~R4: CC/BCC 버그, 첨부파일, 삭제 Reconciliation, 인피니티 스크롤 | o4 |
| 2026-04-01 | cid 인라인 이미지 virtual host 전환 + 빈 본문 처리 | o3 |
