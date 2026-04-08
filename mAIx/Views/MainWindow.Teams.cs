using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using mAIx.Controls;
using mAIx.Dialogs;
using mAIx.Services.Graph;
using mAIx.ViewModels;
using Serilog;

namespace mAIx.Views
{
    /// <summary>
    /// MainWindow partial — Teams 채팅 강화 핸들러
    /// (리액션, 스레드, 멘션, 파일 공유, 미팅)
    /// </summary>
    public partial class MainWindow
    {
        private int _mentionStartIndex = -1;

        #region 리액션

        private async void ReactionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button btn) return;

            var messageId = btn.Tag as string;
            var reaction = btn.Content?.ToString();
            if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(reaction)) return;

            if (_teamsViewModel != null)
            {
                await _teamsViewModel.AddReactionCommand.ExecuteAsync($"{messageId}|{reaction}");
            }
        }

        #endregion

        #region 스레드

        private async void ThreadButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button btn) return;
            if (btn.Tag is not MessageItemViewModel message) return;

            if (_teamsViewModel != null)
            {
                await _teamsViewModel.OpenThreadCommand.ExecuteAsync(message);

                // 스레드 패널 UI 업데이트
                ChatThreadPanel.ParentMessage = _teamsViewModel.ThreadParentMessage;
                ChatThreadPanel.Replies = _teamsViewModel.ThreadReplies;
                ChatThreadPanelBorder.Visibility = Visibility.Visible;
            }
        }

        private void ChatThreadPanel_CloseRequested(object? sender, EventArgs e)
        {
            ChatThreadPanelBorder.Visibility = Visibility.Collapsed;
            _teamsViewModel?.CloseThreadCommand.Execute(null);
        }

        private async void ChatThreadPanel_ReplySubmitted(object? sender, string replyText)
        {
            if (_teamsViewModel == null) return;

            _teamsViewModel.ThreadReplyText = replyText;
            await _teamsViewModel.SendThreadReplyCommand.ExecuteAsync(null);
        }

        #endregion

        #region @멘션 자동완성

        private void ChatMessageInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            var text = textBox.Text ?? string.Empty;
            var caretIndex = textBox.CaretIndex;

            // 플레이스홀더 표시/숨김
            ChatMessagePlaceholder.Visibility = string.IsNullOrEmpty(text)
                ? Visibility.Visible
                : Visibility.Collapsed;

            // '@' 감지 → 멘션 팝업
            if (caretIndex > 0 && text.Length >= caretIndex)
            {
                // 캐럿 바로 앞의 문자열에서 마지막 '@' 찾기
                var textBeforeCaret = text[..caretIndex];
                var lastAtIndex = textBeforeCaret.LastIndexOf('@');

                if (lastAtIndex >= 0)
                {
                    // '@' 앞이 공백이거나 문장 시작인지 확인
                    var isValidMention = lastAtIndex == 0 || char.IsWhiteSpace(text[lastAtIndex - 1]);
                    if (isValidMention)
                    {
                        var filter = textBeforeCaret[(lastAtIndex + 1)..];
                        // 공백이 없는 경우만 (멘션 중)
                        if (!filter.Contains(' '))
                        {
                            _mentionStartIndex = lastAtIndex;
                            _teamsViewModel?.UpdateMentionFilter(filter);

                            if (_teamsViewModel?.MentionCandidates.Count > 0)
                            {
                                ChatMentionPopup.Candidates = _teamsViewModel.MentionCandidates;
                                ChatMentionPopup.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                ChatMentionPopup.Visibility = Visibility.Collapsed;
                            }
                            return;
                        }
                    }
                }
            }

            // 멘션 조건이 아니면 팝업 닫기
            ChatMentionPopup.Visibility = Visibility.Collapsed;
            _mentionStartIndex = -1;
        }

        private void ChatMentionPopup_MentionSelected(object? sender, MentionCandidate candidate)
        {
            if (_mentionStartIndex < 0 || ChatMessageInput == null) return;

            var text = ChatMessageInput.Text ?? string.Empty;
            var caretIndex = ChatMessageInput.CaretIndex;

            // @filter 부분을 @displayName으로 치환
            var mentionText = $"@{candidate.DisplayName} ";
            var newText = text[.._mentionStartIndex] + mentionText;
            if (caretIndex < text.Length)
                newText += text[caretIndex..];

            ChatMessageInput.Text = newText;
            ChatMessageInput.CaretIndex = _mentionStartIndex + mentionText.Length;

            ChatMentionPopup.Visibility = Visibility.Collapsed;
            _mentionStartIndex = -1;
        }

        #endregion

        #region 파일 드래그&드롭

        private void ChatInputBorder_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void ChatInputBorder_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0 || _teamsViewModel == null) return;

            foreach (var file in files)
            {
                try
                {
                    await _teamsViewModel.ShareFileCommand.ExecuteAsync(file);
                    Log.Information("[Teams] 파일 드롭 공유: {File}", file);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[Teams] 파일 드롭 공유 실패: {File}", file);
                }
            }
        }

        private async void ChatAttachButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "공유할 파일 선택",
                Filter = "모든 파일 (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true && _teamsViewModel != null)
            {
                foreach (var file in dialog.FileNames)
                {
                    await _teamsViewModel.ShareFileCommand.ExecuteAsync(file);
                }
            }
        }

        #endregion

        #region Teams 미팅 생성

        private async void ChatMeetingButton_Click(object sender, RoutedEventArgs e)
        {
            var teamsService = ((App)Application.Current).GetService<GraphTeamsService>();
            if (teamsService == null) return;

            var dialog = new MeetingScheduleDialog(teamsService)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && dialog.CreatedMeeting != null)
            {
                // 미팅 링크를 채팅에 전송
                var meetingLink = dialog.CreatedMeeting.JoinWebUrl;
                var subject = dialog.CreatedMeeting.Subject ?? "Teams 미팅";

                if (_teamsViewModel?.SelectedChat != null && !string.IsNullOrEmpty(meetingLink))
                {
                    var messageContent = $"📅 <b>{subject}</b><br/>" +
                                        $"<a href=\"{meetingLink}\">미팅 참가하기</a>";

                    _teamsViewModel.NewMessageText = messageContent;
                    await _teamsViewModel.SendMessageCommand.ExecuteAsync(null);
                }

                Log.Information("[Teams] 미팅 생성 완료: {Subject}", subject);
            }
        }

        #endregion
    }
}
