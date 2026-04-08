using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using mAIx.ViewModels;

namespace mAIx.Controls;

/// <summary>
/// 통화 이력 컨트롤 — 통화 기록 목록, 필터, 검색
/// </summary>
public partial class CallHistoryControl : UserControl
{

    private string _currentFilter = "all";
    private string _searchQuery = string.Empty;

    /// <summary>
    /// 통화 기록 컬렉션
    /// </summary>
    public ObservableCollection<CallRecordViewModel> CallRecords { get; } = new();

    /// <summary>
    /// 필터된 통화 기록
    /// </summary>
    public ObservableCollection<CallRecordViewModel> FilteredRecords { get; } = new();

    /// <summary>
    /// 통화 기록 선택 이벤트
    /// </summary>
    public event EventHandler<CallRecordViewModel>? CallRecordSelected;

    /// <summary>
    /// 다시 전화 버튼 클릭 이벤트
    /// </summary>
    public event EventHandler<CallRecordViewModel>? CallBackRequested;

    /// <summary>
    /// 새로고침 요청 이벤트
    /// </summary>
    public event EventHandler? RefreshRequested;

    /// <summary>
    /// 다이얼패드 토글 이벤트
    /// </summary>
    public event EventHandler? DialpadToggleRequested;

    public CallHistoryControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 통화 기록 목록 업데이트
    /// </summary>
    public void SetCallRecords(IEnumerable<CallRecordViewModel> records)
    {
        CallRecords.Clear();
        foreach (var record in records)
        {
            CallRecords.Add(record);
        }
        ApplyFilter();
        UpdateMissedCallsSummary();
    }

    /// <summary>
    /// 로딩 상태 설정
    /// </summary>
    public void SetLoading(bool isLoading)
    {
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        CallHistoryListView.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyFilter()
    {
        FilteredRecords.Clear();
        IEnumerable<CallRecordViewModel> filtered = CallRecords;

        // 필터 적용
        filtered = _currentFilter switch
        {
            "missed" => filtered.Where(r => r.IsMissed),
            "incoming" => filtered.Where(r => r.Type == "incoming"),
            "outgoing" => filtered.Where(r => r.Type == "outgoing"),
            _ => filtered
        };

        // 검색어 적용
        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            var query = _searchQuery.ToLower();
            filtered = filtered.Where(r =>
                r.CallerName.ToLower().Contains(query) ||
                r.CallerPhone.ToLower().Contains(query) ||
                r.CallerEmail.ToLower().Contains(query));
        }

        foreach (var record in filtered.OrderByDescending(r => r.StartTime))
        {
            FilteredRecords.Add(record);
        }

        CallHistoryListView.ItemsSource = FilteredRecords;
        UpdateEmptyState();
        CallCountText.Text = FilteredRecords.Count > 0 ? $"({FilteredRecords.Count})" : "";
    }

    private void UpdateEmptyState()
    {
        var hasItems = FilteredRecords.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        CallHistoryListView.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateMissedCallsSummary()
    {
        var missedCount = CallRecords.Count(r => r.IsMissed);
        MissedCallsSummary.Text = missedCount > 0
            ? $"부재중 {missedCount}건"
            : "";
    }

    private void SetActiveFilter(string filter)
    {
        _currentFilter = filter;

        FilterAllButton.Appearance = filter == "all" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FilterMissedButton.Appearance = filter == "missed" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FilterIncomingButton.Appearance = filter == "incoming" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        FilterOutgoingButton.Appearance = filter == "outgoing" ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Transparent;

        ApplyFilter();
    }

    #region 이벤트 핸들러

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text;
        ApplyFilter();
    }

    private void FilterAllButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("all");
    private void FilterMissedButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("missed");
    private void FilterIncomingButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("incoming");
    private void FilterOutgoingButton_Click(object sender, RoutedEventArgs e) => SetActiveFilter("outgoing");

    private void CallHistoryListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CallHistoryListView.SelectedItem is CallRecordViewModel record)
        {
            CallRecordSelected?.Invoke(this, record);
        }
    }

    private void CallBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is CallRecordViewModel record)
        {
            CallBackRequested?.Invoke(this, record);
        }
    }

    private void DialpadToggle_Click(object sender, RoutedEventArgs e)
    {
        DialpadToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion
}
