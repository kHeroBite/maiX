using System;
using System.Windows;
using mAIx.Services;
using mAIx.Utils;
using mAIx.ViewModels;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — Activity(활동) 탭 활성화/비활성화 + 크로스탭 연동
    /// </summary>
    public partial class MainWindow
    {
        private CrossTabIntegrationService? _crossTabService;

        /// <summary>
        /// Activity 탭 활성화 시 호출 — 폴링 시작 + 자동 새로고침
        /// </summary>
        private async void OnActivityTabActivated()
        {
            try
            {
                if (_activityViewModel == null)
                {
                    _activityViewModel = ((App)Application.Current).GetService<ActivityViewModel>()!;
                }

                await _activityViewModel.OnTabActivatedAsync();

                // 활동 목록 바인딩
                ActivityListView.ItemsSource = _activityViewModel.FilteredActivities;

                if (_activityViewModel.FilteredActivities.Count == 0)
                {
                    ActivityEmptyState.Visibility = Visibility.Visible;
                }
                else
                {
                    ActivityEmptyState.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"Activity 탭 활성화 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// Activity 탭 비활성화 시 호출 — 폴링 중지
        /// </summary>
        private void OnActivityTabDeactivated()
        {
            _activityViewModel?.OnTabDeactivated();
        }

        /// <summary>
        /// 활동에서 원본 항목으로 이동 (크로스탭)
        /// </summary>
        private async System.Threading.Tasks.Task NavigateToActivitySource(ActivityItemViewModel activity)
        {
            try
            {
                if (activity == null || _activityViewModel == null) return;

                await _activityViewModel.NavigateToSourceAsync(activity, tabName =>
                {
                    _ = Dispatcher.InvokeAsync(() => NavigateToTab(tabName));
                });
            }
            catch (Exception ex)
            {
                Log4.Error($"[MainWindow] NavigateToActivitySource 실패: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
