// MainWindow partial — OneNote 녹음 STT + 주제어 네비게이션 핸들러 (Phase 7)
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.Controls;
using mAIx.Models;
using mAIx.ViewModels;
using mAIx.Utils;
using NLog;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — OneNote 백링크/태그/STT 녹음 핸들러
    /// </summary>
    public partial class MainWindow
    {
        private static readonly Logger _oneNoteLog = LogManager.GetCurrentClassLogger();

        // ─── 주제어 네비게이션 핸들러 ─────────────────────────────────────

        /// <summary>
        /// 주제어 카드 클릭 시 해당 세그먼트 시각 위치로 이동
        /// </summary>
        private async void TopicSegment_Click(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is TopicSegment segment)
                {
                    _oneNoteLog.Info($"[OneNote] 주제어 카드 클릭: {segment.DisplayTitle} ({segment.TimeRangeDisplay})");
                    // ViewModel의 SelectedRecording 오디오 SeekTo — 향후 구현 확장 지점
                    // _oneNoteViewModel?.SeekToTime(segment.StartTime);
                    await Task.CompletedTask;
                }
            }
            catch (Exception ex)
            {
                _oneNoteLog.Error(ex, "[OneNote] TopicSegment_Click 처리 실패");
            }
        }

        /// <summary>
        /// 대화 네비게이션 패널 도킹 위치 토글 (우측 ↔ 하단)
        /// TopicNavOrientation: "Vertical" = 우측 도킹(기본), "Horizontal" = 하단 도킹
        /// </summary>
        private async void OneNoteTopicNavOrientationToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_oneNoteViewModel == null) return;

                var newOrientation = _oneNoteViewModel.TopicNavOrientation == "Horizontal"
                    ? "Vertical"
                    : "Horizontal";

                _oneNoteViewModel.TopicNavOrientation = newOrientation;

                // 도킹 위치 재배치 (단일 Grid 재배치 — 마크업 복제 없음)
                ApplyTopicNavDockLayout();

                // 설정 영구 저장
                var oaiSettings = App.Settings?.OaiRecording;
                if (oaiSettings != null)
                {
                    oaiSettings.TopicNavOrientation = newOrientation;
                    App.Settings?.SaveAll();
                }

                _oneNoteLog.Info($"[OneNote] 대화 네비게이션 도킹 위치 변경: {newOrientation} ({(newOrientation == "Horizontal" ? "하단" : "우측")} 도킹)");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _oneNoteLog.Error(ex, "[OneNote] OneNoteTopicNavOrientationToggle_Click 처리 실패");
            }
        }

        /// <summary>
        /// 녹음내용 Grid 최초 로드 시 영속된 도킹 설정(TopicNavOrientation)을 반영
        /// </summary>
        private void OneNoteRecDockGrid_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ApplyTopicNavDockLayout();
            }
            catch (Exception ex)
            {
                _oneNoteLog.Error(ex, "[OneNote] OneNoteRecDockGrid_Loaded 처리 실패");
            }
        }

        /// <summary>
        /// 대화 네비게이션 패널 도킹 위치를 TopicNavOrientation에 따라 재배치한다.
        /// 단일 Grid 재배치 방식 — STT/요약 패널 마크업 복제 없음 (L-389/L-424 준수: ItemsPanel 동적변경 없음).
        ///  - Mode A(Vertical, 우측 도킹, 기본): 5컬럼 1행. STT=Col0 / 세로Split=Col1 / 대화네비=Col2 / 세로Split=Col3 / 요약=Col4
        ///  - Mode B(Horizontal, 하단 도킹): Row0=상단(STT=Col0 / 세로Split=Col1 / 요약=Col2~Col4 전폭) / Row1=가로Split / Row2=대화네비 전폭
        /// </summary>
        private void ApplyTopicNavDockLayout()
        {
            try
            {
                if (_oneNoteViewModel == null) return;
                if (OneNoteRecDockGrid == null) return;

                bool isBottomDock = string.Equals(
                    _oneNoteViewModel.TopicNavOrientation, "Horizontal", StringComparison.OrdinalIgnoreCase);

                if (isBottomDock)
                {
                    // ===== Mode B: 하단 도킹 =====
                    // 상단 행(STT + 요약)을 Row0에, 대화네비를 Row2 전폭에 배치
                    OneNoteRecRow0.Height = new GridLength(1, GridUnitType.Star);   // 상단 STT/요약
                    OneNoteRecRow1.Height = new GridLength(4);                       // 가로 Splitter
                    OneNoteRecRow2.Height = new GridLength(220, GridUnitType.Pixel); // 하단 대화네비

                    // STT: Row0 Col0 유지
                    Grid.SetRow(OneNoteSTTPanel, 0);
                    Grid.SetColumn(OneNoteSTTPanel, 0);
                    Grid.SetColumnSpan(OneNoteSTTPanel, 1);

                    // 세로 Splitter1(STT↔요약 사이): Row0 Col1 유지, 표시
                    Grid.SetRow(OneNoteRecSplitter1, 0);
                    OneNoteRecSplitter1.Visibility = Visibility.Visible;

                    // 요약: Row0 Col2~Col4 전폭 (대화네비가 빠진 자리 흡수)
                    Grid.SetRow(OneNoteSummaryPanel, 0);
                    Grid.SetColumn(OneNoteSummaryPanel, 2);
                    Grid.SetColumnSpan(OneNoteSummaryPanel, 3);

                    // 세로 Splitter3: 하단 도킹에서는 불필요 → 숨김
                    OneNoteRecSplitter3.Visibility = Visibility.Collapsed;

                    // 가로 Splitter(Row1): 표시
                    OneNoteRecSplitterBottom.Visibility = Visibility.Visible;

                    // 대화네비: Row2 전폭(Col0~Col4)
                    Grid.SetRow(OneNoteTopicNavPanel, 2);
                    Grid.SetColumn(OneNoteTopicNavPanel, 0);
                    Grid.SetColumnSpan(OneNoteTopicNavPanel, 5);
                    OneNoteTopicNavPanel.BorderThickness = new Thickness(0, 1, 0, 0);

                    // L-450 Option B: 가로 레이아웃 표시 / 세로 레이아웃 숨김 (멱등)
                    if (TopicSegmentsContainer != null)
                        TopicSegmentsContainer.Visibility = Visibility.Collapsed;
                    if (TopicNavHorizontalLayout != null)
                        TopicNavHorizontalLayout.Visibility = Visibility.Visible;
                }
                else
                {
                    // ===== Mode A: 우측 도킹 (기본) — 원본 5컬럼 1행 복원 =====
                    OneNoteRecRow0.Height = new GridLength(1, GridUnitType.Star);
                    OneNoteRecRow1.Height = new GridLength(0);
                    OneNoteRecRow2.Height = new GridLength(0);

                    // STT: Row0 Col0
                    Grid.SetRow(OneNoteSTTPanel, 0);
                    Grid.SetColumn(OneNoteSTTPanel, 0);
                    Grid.SetColumnSpan(OneNoteSTTPanel, 1);

                    // 세로 Splitter1: Row0 Col1, 표시
                    Grid.SetRow(OneNoteRecSplitter1, 0);
                    OneNoteRecSplitter1.Visibility = Visibility.Visible;

                    // 대화네비: Row0 Col2
                    Grid.SetRow(OneNoteTopicNavPanel, 0);
                    Grid.SetColumn(OneNoteTopicNavPanel, 2);
                    Grid.SetColumnSpan(OneNoteTopicNavPanel, 1);
                    OneNoteTopicNavPanel.BorderThickness = new Thickness(0, 0, 1, 0);

                    // 세로 Splitter3: Row0 Col3, 표시
                    Grid.SetRow(OneNoteRecSplitter3, 0);
                    OneNoteRecSplitter3.Visibility = Visibility.Visible;

                    // 요약: Row0 Col4
                    Grid.SetRow(OneNoteSummaryPanel, 0);
                    Grid.SetColumn(OneNoteSummaryPanel, 4);
                    Grid.SetColumnSpan(OneNoteSummaryPanel, 1);

                    // 가로 Splitter: 숨김
                    OneNoteRecSplitterBottom.Visibility = Visibility.Collapsed;

                    // L-450 Option B: 세로 레이아웃 표시 / 가로 레이아웃 숨김 (멱등)
                    if (TopicSegmentsContainer != null)
                        TopicSegmentsContainer.Visibility = Visibility.Visible;
                    if (TopicNavHorizontalLayout != null)
                        TopicNavHorizontalLayout.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _oneNoteLog.Error(ex, "[OneNote] ApplyTopicNavDockLayout 처리 실패");
            }
        }

        /// <summary>
        /// 녹음 종료 버튼 클릭 (최종 요약 포함)
        /// </summary>
        private async void OnRecordingStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_oneNoteViewModel == null) return;

                _oneNoteLog.Info("[OneNote] 녹음 종료 버튼 클릭 (최종 요약 포함)");

                // 기존 StopRecording() + UI 업데이트 흐름 사용
                // StopOpenAiServicesAsync는 OnRecordingCompleted 내부에서 자동 호출됨
                _oneNoteViewModel.StopRecording();
                await UpdateRecordingUI(false);
            }
            catch (Exception ex)
            {
                _oneNoteLog.Error(ex, "[OneNote] OnRecordingStop_Click 처리 실패");
            }
        }
        /// <summary>
        /// 현재 선택된 페이지의 백링크 로드
        /// </summary>
        private async Task LoadOneNoteBacklinksAsync()
        {
            if (_oneNoteViewModel == null) return;

            try
            {
                await _oneNoteViewModel.LoadBacklinksAsync();
                Log4.Debug($"[OneNote] 백링크 {_oneNoteViewModel.BacklinkItems.Count}개 로드");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneNote] 백링크 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 백링크 항목 클릭 시 해당 페이지로 이동
        /// </summary>
        private async Task NavigateToBacklinkPage(BacklinkItem backlink)
        {
            try
            {
                if (backlink == null || _oneNoteViewModel == null) return;

                try
                {
                    // 해당 페이지를 찾아 선택
                    foreach (var nb in _oneNoteViewModel.Notebooks)
                    {
                        foreach (var section in nb.Sections)
                        {
                            foreach (var page in section.Pages)
                            {
                                if (page.Id == backlink.PageId)
                                {
                                    _oneNoteViewModel.SelectedNotebook = nb;
                                    _oneNoteViewModel.SelectedSection = section;
                                    _oneNoteViewModel.SelectedPage = page;
                                    Log4.Debug($"[OneNote] 백링크로 이동: {page.Title}");
                                    return;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[OneNote] 백링크 이동 실패: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"[MainWindow] NavigateToBacklinkPage 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 태그 목록 로드
        /// </summary>
        private async Task LoadOneNoteTagsAsync()
        {
            if (_oneNoteViewModel == null) return;

            try
            {
                await _oneNoteViewModel.LoadTagsAsync();
                Log4.Debug($"[OneNote] 태그 {_oneNoteViewModel.TagItems.Count}개 로드");
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneNote] 태그 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 태그 필터 적용
        /// </summary>
        private void ApplyOneNoteTagFilter(string? tag)
        {
            if (_oneNoteViewModel == null) return;
            _oneNoteViewModel.FilterByTag(tag);
        }

        /// <summary>
        /// 핵심요약 네비게이션 스크롤뷰어 크기 변경 시 패널 높이를 ViewModel에 전달
        /// </summary>
        private void TopicNavScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                if (_oneNoteViewModel == null) return;
                // ScrollViewer 제거 후 FrameworkElement(Grid) 기반으로 변경
                var height = e.NewSize.Height;
                var width = e.NewSize.Width;
                if (sender is FrameworkElement fe)
                {
                    if (height <= 0) height = fe.ActualHeight;
                    if (width <= 0) width = fe.ActualWidth;
                }
                if (height > 0)
                    _oneNoteViewModel.SetPanelHeight(height);
                if (width > 0)
                    _oneNoteViewModel.SetPanelWidth(width);
            }
            catch (Exception ex)
            {
                _oneNoteLog.Error(ex, "[OneNote] TopicNavScrollViewer_SizeChanged 실패");
            }
        }
    }
}
