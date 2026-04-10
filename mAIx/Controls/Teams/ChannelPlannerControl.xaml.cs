using System.Windows;
using System.Windows.Controls;
using NLog;
using mAIx.ViewModels.Teams;

namespace mAIx.Controls.Teams;

/// <summary>
/// 채널 작업(Planner) 탭 UserControl — 칸반 보드 + 목록 보기
/// </summary>
public partial class ChannelPlannerControl : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public ChannelPlannerControl()
    {
        InitializeComponent();
    }

    private ChannelPlannerViewModel? Vm => DataContext as ChannelPlannerViewModel;

    /// <summary>새 작업 버튼 클릭 (툴바)</summary>
    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("새 작업 버튼 클릭");
        Vm?.AddTaskCommand.Execute(null);
    }

    /// <summary>카드 내 작업 추가 버튼 클릭 (열별)</summary>
    private void AddCardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button btn) return;
        if (btn.Tag is not PlannerBucket bucket) return;

        _log.Debug("카드 추가 버튼 클릭: 버킷={Bucket}", bucket.Name);
        Vm?.AddTaskCommand.Execute(bucket);
    }

    /// <summary>보드 뷰 버튼 클릭</summary>
    private void BoardViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null)
        {
            Vm.IsBoardView = true;
            _log.Debug("보드 뷰로 전환");
        }
    }

    /// <summary>목록 뷰 버튼 클릭</summary>
    private void ListViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm != null)
        {
            Vm.IsBoardView = false;
            _log.Debug("목록 뷰로 전환");
        }
    }

    /// <summary>담당자 필터 변경</summary>
    private void AssigneeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var tag = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        _log.Debug("담당자 필터 변경: {Filter}", tag);
        Vm?.ApplyAssigneeFilter(tag);
    }

    /// <summary>우선순위 필터 변경</summary>
    private void PriorityFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        var tag = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        _log.Debug("우선순위 필터 변경: {Filter}", tag);
        Vm?.ApplyPriorityFilter(tag);
    }
}
