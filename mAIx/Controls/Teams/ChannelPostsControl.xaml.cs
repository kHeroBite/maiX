using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.ViewModels;
using mAIx.ViewModels.Teams;
using NLog;

namespace mAIx.Controls.Teams;

/// <summary>
/// 채널 게시물 탭 UserControl — 메시지 목록 + 리치텍스트 입력 + 컨텍스트 메뉴
/// </summary>
public partial class ChannelPostsControl : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    public ChannelPostsControl()
    {
        InitializeComponent();
    }

    private ChannelPostsViewModel? VM => DataContext as ChannelPostsViewModel;

    // ── 입력 영역 ──────────────────────────────────────────────────────────

    /// <summary>메시지 입력 KeyDown — Enter 전송 (Shift+Enter는 줄바꿈)</summary>
    private void TeamsChannelMessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            VM?.SendMessageCommand.Execute(null);
        }
    }

    /// <summary>텍스트 변경 시 @멘션 감지</summary>
    private void TeamsChannelMessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (VM == null) return;
        var text = TeamsChannelMessageInput.Text;
        var atIndex = text.LastIndexOf('@');
        if (atIndex >= 0)
        {
            var query = text.Substring(atIndex + 1);
            _ = VM.TriggerMentionAsync(query);
        }
        else
        {
            VM.IsMentionPopupOpen = false;
        }
    }

    /// <summary>@멘션 버튼 클릭 — 입력창에 '@' 삽입</summary>
    private void MentionButton_Click(object sender, RoutedEventArgs e)
    {
        TeamsChannelMessageInput.Text += "@";
        TeamsChannelMessageInput.CaretIndex = TeamsChannelMessageInput.Text.Length;
        TeamsChannelMessageInput.Focus();
    }

    /// <summary>@멘션 목록 선택</summary>
    private void MentionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is string name)
        {
            VM?.SelectMentionCommand.Execute(name);
            lb.SelectedItem = null;
        }
    }

    // ── 컨텍스트 메뉴 ──────────────────────────────────────────────────────

    /// <summary>컨텍스트 메뉴 — 편집</summary>
    private void ContextMenu_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ChannelMessageViewModel msg })
        {
            _log.Debug("메시지 편집 클릭: {Id}", msg.Id);
            VM?.StartEditCommand.Execute(msg);
        }
    }

    /// <summary>컨텍스트 메뉴 — 삭제</summary>
    private void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ChannelMessageViewModel msg })
        {
            _log.Debug("메시지 삭제 클릭: {Id}", msg.Id);
            _ = VM?.DeleteMessageCommand.ExecuteAsync(msg);
        }
    }

    /// <summary>컨텍스트 메뉴 — 고정</summary>
    private void ContextMenu_Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: ChannelMessageViewModel msg })
        {
            _log.Debug("메시지 고정 클릭: {Id}", msg.Id);
            VM?.PinMessageCommand.Execute(msg);
        }
    }

    // ── 리액션 ────────────────────────────────────────────────────────────

    /// <summary>리액션 버튼 클릭 — Tag에 리액션 타입 저장</summary>
    private void Reaction_Click(object sender, RoutedEventArgs e)
    {
        if (VM == null) return;
        if (sender is not Button { Tag: string reactionType }) return;

        // 부모 DataContext에서 ChannelMessageViewModel 찾기
        var btn = (Button)sender;
        if (btn.DataContext is ChannelMessageViewModel msg)
        {
            _log.Debug("리액션 클릭: {Reaction}, messageId={Id}", reactionType, msg.Id);
            _ = VM.AddReactionCommand.ExecuteAsync(new object[] { msg, reactionType });
        }
    }

    // ── 스레드 회신 ───────────────────────────────────────────────────────

    /// <summary>스레드에서 회신 버튼 클릭 — 부모 윈도우로 이벤트 버블링</summary>
    private void ReplyToThread_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: { } tag })
        {
            _log.Debug("스레드 회신 클릭: {Tag}", tag);
            RaiseEvent(new RoutedEventArgs(ReplyToThreadEvent, tag));
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
