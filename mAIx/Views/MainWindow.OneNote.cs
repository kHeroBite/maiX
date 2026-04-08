using System;
using System.Threading.Tasks;
using System.Windows;
using mAIx.Controls;
using mAIx.ViewModels;
using mAIx.Utils;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — OneNote 백링크/태그 강화 핸들러 (Phase 6)
    /// 기존 핸들러는 MainWindow.xaml.cs에 유지, 여기는 Phase 6 신규 기능만
    /// </summary>
    public partial class MainWindow
    {
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
        private async void NavigateToBacklinkPage(BacklinkItem backlink)
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
