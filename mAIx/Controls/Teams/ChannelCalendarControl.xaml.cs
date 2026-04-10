using System.Windows;
using System.Windows.Controls;
using mAIx.ViewModels.Teams;
using NLog;

namespace mAIx.Controls.Teams;

/// <summary>
/// 채널 일정 탭 UserControl — 월간 캘린더 뷰 + 이벤트 상세 패널
/// </summary>
public partial class ChannelCalendarControl : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public ChannelCalendarControl()
    {
        InitializeComponent();
    }

    private ChannelCalendarViewModel? VM => DataContext as ChannelCalendarViewModel;

    /// <summary>이벤트 참석 응답 버튼 클릭 (참석/거절/미정)</summary>
    private void ResponseButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string responseStatus } fe) return;
        if (fe.DataContext is not CalendarEvent ev) return;

        _log.Info("이벤트 응답: eventId={Id}, status={Status}", ev.Id, responseStatus);

        // Graph API 연동 지점 — EventResponseAsync 구현 시 교체
        // ex) await VM?.RespondToEventCommand.ExecuteAsync((ev, responseStatus));
    }

    /// <summary>새 이벤트 버튼 클릭</summary>
    private void NewEventButton_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("새 이벤트 생성 클릭");
        // Graph API 연동 지점 — 이벤트 생성 다이얼로그 표시
    }
}
