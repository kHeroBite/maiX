# HISTORY.md — MaiX 프로젝트 작업 이력

> PROJECT.md 작업 이력 테이블의 상세 보완본

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
