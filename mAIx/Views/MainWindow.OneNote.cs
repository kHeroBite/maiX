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
        /// 주제어 네비게이션 가로/세로 방향 토글
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

                // ItemsPanel의 Orientation 동적 변경
                if (TopicSegmentsItemsControl?.ItemsPanel != null)
                {
                    var stackPanel = TopicSegmentsItemsControl.ItemsPanel.LoadContent() as VirtualizingStackPanel;
                    if (stackPanel != null)
                    {
                        stackPanel.Orientation = newOrientation == "Horizontal"
                            ? Orientation.Horizontal
                            : Orientation.Vertical;
                    }
                }

                // 설정 영구 저장
                var oaiSettings = App.Settings?.OaiRecording;
                if (oaiSettings != null)
                {
                    oaiSettings.TopicNavOrientation = newOrientation;
                    App.Settings?.SaveAll();
                }

                _oneNoteLog.Info($"[OneNote] 주제어 네비게이션 방향 변경: {newOrientation}");
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _oneNoteLog.Error(ex, "[OneNote] OneNoteTopicNavOrientationToggle_Click 처리 실패");
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
    }
}
