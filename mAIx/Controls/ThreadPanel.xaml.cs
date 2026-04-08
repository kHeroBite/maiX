using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.ViewModels;
using Serilog;
using Wpf.Ui.Controls;

namespace mAIx.Controls;

/// <summary>
/// 스레드 답글 패널 — Slack 스타일 인라인 사이드패널
/// </summary>
public partial class ThreadPanel : UserControl
{
    private static readonly ILogger _logger = Log.ForContext<ThreadPanel>();

    /// <summary>
    /// 원본 메시지
    /// </summary>
    public MessageItemViewModel? ParentMessage
    {
        get => (MessageItemViewModel?)GetValue(ParentMessageProperty);
        set => SetValue(ParentMessageProperty, value);
    }

    public static readonly DependencyProperty ParentMessageProperty =
        DependencyProperty.Register(nameof(ParentMessage), typeof(MessageItemViewModel), typeof(ThreadPanel));

    /// <summary>
    /// 답글 목록
    /// </summary>
    public ObservableCollection<MessageItemViewModel>? Replies
    {
        get => (ObservableCollection<MessageItemViewModel>?)GetValue(RepliesProperty);
        set => SetValue(RepliesProperty, value);
    }

    public static readonly DependencyProperty RepliesProperty =
        DependencyProperty.Register(nameof(Replies), typeof(ObservableCollection<MessageItemViewModel>), typeof(ThreadPanel));

    /// <summary>
    /// 닫기 이벤트
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// 답글 전송 이벤트
    /// </summary>
    public event EventHandler<string>? ReplySubmitted;

    public ThreadPanel()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void CloseThreadButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ThreadSendButton_Click(object sender, RoutedEventArgs e)
    {
        SendReply();
    }

    private void ThreadReplyInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            SendReply();
        }
    }

    private void SendReply()
    {
        var text = ThreadReplyInput.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        ReplySubmitted?.Invoke(this, text);
        ThreadReplyInput.Text = string.Empty;
    }
}
