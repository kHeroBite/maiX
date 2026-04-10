using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NLog;

namespace mAIx.Controls.Teams;

/// <summary>
/// 채널 게시물 탭 UserControl — 메시지 목록 + 입력 영역
/// </summary>
public partial class ChannelPostsControl : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public ChannelPostsControl()
    {
        InitializeComponent();
    }

    /// <summary>스레드에서 회신 버튼 클릭 — MainWindow 이벤트로 라우팅</summary>
    private void ReplyToThread_Click(object sender, RoutedEventArgs e)
    {
        // 부모 윈도우로 이벤트 버블링 (MainWindow에서 처리)
        if (sender is FrameworkElement { Tag: { } tag })
        {
            _log.Debug("스레드 회신 클릭: {Tag}", tag);
            RaiseEvent(new RoutedEventArgs(ReplyToThreadEvent, tag));
        }
    }

    /// <summary>메시지 입력 KeyDown — Enter 전송</summary>
    private void TeamsChannelMessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            SendMessage();
        }
    }

    /// <summary>전송 버튼 클릭</summary>
    private void TeamsChannelSendButton_Click(object sender, RoutedEventArgs e)
    {
        SendMessage();
    }

    private void SendMessage()
    {
        var text = TeamsChannelMessageInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // ChannelPostsViewModel이 kdev-3 Phase에서 구현됨
        // DataContext에 SendMessageCommand가 있으면 실행
        if (DataContext is { } vm)
        {
            var cmdProp = vm.GetType().GetProperty("SendMessageCommand");
            if (cmdProp?.GetValue(vm) is ICommand cmd && cmd.CanExecute(text))
            {
                _log.Debug("채널 메시지 전송 요청: {Length}자", text.Length);
                cmd.Execute(text);
                TeamsChannelMessageInput.Text = string.Empty;
            }
        }
    }

    // 스레드 회신 라우팅 이벤트
    public static readonly RoutedEvent ReplyToThreadEvent =
        EventManager.RegisterRoutedEvent(
            nameof(ReplyToThread),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(ChannelPostsControl));

    public event RoutedEventHandler ReplyToThread
    {
        add => AddHandler(ReplyToThreadEvent, value);
        remove => RemoveHandler(ReplyToThreadEvent, value);
    }
}
