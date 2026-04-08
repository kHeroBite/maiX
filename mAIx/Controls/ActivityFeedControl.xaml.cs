using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.Services.Graph;

namespace mAIx.Controls;

/// <summary>
/// 활동 피드 컨트롤 — 타임라인 형태로 최근 활동 표시
/// </summary>
public partial class ActivityFeedControl : UserControl
{

    private string _currentFilter = "all";

    /// <summary>
    /// 활동 항목 컬렉션
    /// </summary>
    public ObservableCollection<ActivityItem> Activities { get; } = new();

    /// <summary>
    /// 필터된 활동 항목
    /// </summary>
    public ObservableCollection<ActivityItem> FilteredActivities { get; } = new();

    /// <summary>
    /// 활동 클릭 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<ActivityItem>? ActivityClicked;

    /// <summary>
    /// 새로고침 요청 이벤트
    /// </summary>
    public event EventHandler? RefreshRequested;

    public ActivityFeedControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 활동 목록 업데이트
    /// </summary>
    public void SetActivities(IEnumerable<ActivityItem> activities)
    {
        Activities.Clear();
        foreach (var activity in activities)
        {
            Activities.Add(activity);
        }
        ApplyFilter();
        UpdateEmptyState();
        ActivityCountText.Text = Activities.Count > 0 ? $"({Activities.Count})" : "";
    }

    /// <summary>
    /// 로딩 상태 설정
    /// </summary>
    public void SetLoading(bool isLoading)
    {
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        ActivityScrollViewer.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyFilter()
    {
        FilteredActivities.Clear();
        IEnumerable<ActivityItem> filtered;

        if (_currentFilter == "all")
        {
            filtered = Activities;
        }
        else
        {
            var filterType = _currentFilter switch
            {
                "mail" => ActivityType.Email,
                "chat" => ActivityType.Chat,
                "file" => ActivityType.File,
                "calendar" => ActivityType.Other,
                _ => ActivityType.Other
            };

            filtered = _currentFilter == "mail"
                ? Activities.Where(a => a.Type == ActivityType.Email || a.Type == ActivityType.Reply)
                : Activities.Where(a => a.Type == filterType);
        }

        // 날짜 그룹 구분 추가하여 ItemsControl에 바인딩
        foreach (var activity in filtered.OrderByDescending(a => a.Timestamp))
        {
            FilteredActivities.Add(activity);
        }

        ActivityItemsControl.ItemsSource = FilteredActivities;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        var hasItems = FilteredActivities.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ActivityScrollViewer.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetActiveFilter(string filter)
    {
        _currentFilter = filter;

        // 버튼 스타일 업데이트
        FilterAllButton.Appearance = filter == "all" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FilterMailButton.Appearance = filter == "mail" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FilterChatButton.Appearance = filter == "chat" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FilterFileButton.Appearance = filter == "file" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FilterCalendarButton.Appearance = filter == "calendar" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;

        ApplyFilter();
    }

    #region 이벤트 핸들러

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FilterAllButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("all");
    private void FilterMailButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("mail");
    private void FilterChatButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("chat");
    private void FilterFileButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("file");
    private void FilterCalendarButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("calendar");

    private void ActivityItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ActivityItem activity)
        {
            ActivityClicked?.Invoke(this, activity);
        }
    }

    #endregion
}
