using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Services.Graph;
using mAIx.ViewModels;
using NLog;

namespace mAIx.ViewModels.Teams;

/// <summary>
/// 채널 게시물 탭 Sub-ViewModel — Hub(TeamsViewModel)에서 채널별 Lazy 생성
/// </summary>
public partial class ChannelPostsViewModel : ObservableObject
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly GraphTeamsService _teamsService;
    private readonly string _teamId;
    private readonly string _channelId;

    // 편집 대상 메시지 ID (null이면 편집 모드 아님)
    private string? _editingMessageId;

    /// <summary>채널 메시지 목록</summary>
    [ObservableProperty]
    private ObservableCollection<ChannelMessageViewModel> _messages = new();

    /// <summary>로딩 중 여부</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>오프라인(연결 실패) 여부</summary>
    [ObservableProperty]
    private bool _isOffline;

    /// <summary>오류 발생 여부</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>오류 메시지</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>입력 텍스트</summary>
    [ObservableProperty]
    private string _inputText = string.Empty;

    /// <summary>입력 HTML (리치텍스트 내용, 일반 텍스트 fallback)</summary>
    [ObservableProperty]
    private string _inputHtml = string.Empty;

    /// <summary>@멘션 팝업 표시 여부</summary>
    [ObservableProperty]
    private bool _isMentionPopupOpen;

    /// <summary>@멘션 후보 목록</summary>
    [ObservableProperty]
    private ObservableCollection<string> _mentionCandidates = new();

    /// <summary>현재 선택된 메시지 (컨텍스트 메뉴용)</summary>
    [ObservableProperty]
    private ChannelMessageViewModel? _selectedMessage;

    /// <summary>편집 모드 여부</summary>
    [ObservableProperty]
    private bool _isEditMode;

    public ChannelPostsViewModel(GraphTeamsService teamsService, string teamId, string channelId)
    {
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _teamId = teamId ?? throw new ArgumentNullException(nameof(teamId));
        _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));
    }

    /// <summary>
    /// 채널 메시지 로드 — Graph API /teams/{teamId}/channels/{channelId}/messages 연동
    /// </summary>
    public async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            var messages = await _teamsService.GetChannelMessagesAsync(_teamId, _channelId, 50);

            Messages.Clear();
            foreach (var msg in messages.OrderBy(m => m.CreatedDateTime))
            {
                var bodyContent = msg.Body?.Content ?? string.Empty;
                var plainText = StripHtml(bodyContent) ?? string.Empty;

                Messages.Add(new ChannelMessageViewModel
                {
                    Id = msg.Id ?? string.Empty,
                    Content = plainText,
                    HtmlContent = bodyContent,
                    FromUser = msg.From?.User?.DisplayName ?? "알 수 없음",
                    CreatedDateTime = msg.CreatedDateTime?.ToLocalTime().DateTime ?? DateTime.Now,
                    ReplyCount = 0
                });
            }

            IsOffline = false;
            _log.Debug("채널 게시물 로드 완료: teamId={TeamId}, channelId={ChannelId}, count={Count}",
                _teamId, _channelId, Messages.Count);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = "게시물을 불러오지 못했습니다.";
            IsOffline = ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
                     || ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase);
            _log.Error(ex, "채널 게시물 로드 실패: teamId={TeamId}, channelId={ChannelId}", _teamId, _channelId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 메시지 전송 (일반 텍스트 + HTML 지원). 편집 모드이면 수정 수행.
    /// </summary>
    [RelayCommand]
    public async Task SendMessageAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // HTML 우선, 없으면 텍스트를 <p>로 감싸서 전송
        var html = string.IsNullOrEmpty(InputHtml) ? $"<p>{text}</p>" : InputHtml;

        if (IsEditMode && _editingMessageId != null)
        {
            await EditMessageAsync(_editingMessageId, html);
            CancelEdit();
            return;
        }

        try
        {
            var sent = await _teamsService.SendChannelMessageAsync(_teamId, _channelId, html);
            if (sent != null)
            {
                Messages.Add(new ChannelMessageViewModel
                {
                    Id = sent.Id ?? string.Empty,
                    Content = text,
                    HtmlContent = html,
                    FromUser = sent.From?.User?.DisplayName ?? "나",
                    CreatedDateTime = sent.CreatedDateTime?.ToLocalTime().DateTime ?? DateTime.Now,
                    ReplyCount = 0
                });
                InputText = string.Empty;
                InputHtml = string.Empty;
                _log.Debug("채널 메시지 전송 완료: {Length}자", text.Length);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "채널 메시지 전송 실패: teamId={TeamId}, channelId={ChannelId}", _teamId, _channelId);
        }
    }

    /// <summary>
    /// 메시지 수정 (내부 호출)
    /// </summary>
    private async Task EditMessageAsync(string messageId, string newHtml)
    {
        try
        {
            var success = await _teamsService.EditChannelMessageAsync(_teamId, _channelId, messageId, newHtml);
            if (success)
            {
                var target = Messages.FirstOrDefault(m => m.Id == messageId);
                if (target != null)
                {
                    target.HtmlContent = newHtml;
                    target.Content = StripHtml(newHtml) ?? string.Empty;
                }
                _log.Debug("채널 메시지 수정 완료: messageId={MessageId}", messageId);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "채널 메시지 수정 실패: messageId={MessageId}", messageId);
        }
    }

    /// <summary>
    /// 메시지 편집 모드 시작 (컨텍스트 메뉴 — 편집)
    /// </summary>
    [RelayCommand]
    public void StartEdit(ChannelMessageViewModel? message)
    {
        if (message == null) return;
        _editingMessageId = message.Id;
        IsEditMode = true;
        InputText = message.Content;
        InputHtml = message.HtmlContent;
        _log.Debug("메시지 편집 모드 시작: messageId={MessageId}", message.Id);
    }

    /// <summary>
    /// 편집 취소
    /// </summary>
    [RelayCommand]
    public void CancelEdit()
    {
        _editingMessageId = null;
        IsEditMode = false;
        InputText = string.Empty;
        InputHtml = string.Empty;
    }

    /// <summary>
    /// 메시지 삭제 (컨텍스트 메뉴 — 삭제)
    /// </summary>
    [RelayCommand]
    public async Task DeleteMessageAsync(ChannelMessageViewModel? message)
    {
        if (message == null) return;

        try
        {
            var success = await _teamsService.DeleteChannelMessageAsync(_teamId, _channelId, message.Id);
            if (success)
            {
                Messages.Remove(message);
                _log.Debug("채널 메시지 삭제 완료: messageId={MessageId}", message.Id);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "채널 메시지 삭제 실패: messageId={MessageId}", message.Id);
        }
    }

    /// <summary>
    /// 메시지 고정 (컨텍스트 메뉴 — 고정, Graph API 미지원 시 로컬 플래그만 토글)
    /// </summary>
    [RelayCommand]
    public void PinMessage(ChannelMessageViewModel? message)
    {
        if (message == null) return;
        message.IsPinned = !message.IsPinned;
        _log.Debug("메시지 고정 토글: messageId={MessageId}, isPinned={IsPinned}", message.Id, message.IsPinned);
    }

    /// <summary>
    /// 리액션 추가 (like/heart/laugh/wow/sad/angry)
    /// </summary>
    [RelayCommand]
    public async Task AddReactionAsync(object? parameter)
    {
        if (parameter is not object[] args || args.Length < 2) return;
        if (args[0] is not ChannelMessageViewModel message || args[1] is not string reactionType) return;

        try
        {
            await _teamsService.AddChannelReactionAsync(_teamId, _channelId, message.Id, reactionType);
            _log.Debug("리액션 추가: messageId={MessageId}, reaction={Reaction}", message.Id, reactionType);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "리액션 추가 실패: messageId={MessageId}", message.Id);
        }
    }

    /// <summary>Bold 포맷 적용 — 선택 텍스트를 &lt;strong&gt;으로 감쌈</summary>
    [RelayCommand]
    public void FormatBold()
    {
        InputHtml = WrapSelection(InputHtml, "<strong>", "</strong>");
        _log.Debug("Bold 포맷 적용");
    }

    /// <summary>Italic 포맷 적용 — 선택 텍스트를 &lt;em&gt;으로 감쌈</summary>
    [RelayCommand]
    public void FormatItalic()
    {
        InputHtml = WrapSelection(InputHtml, "<em>", "</em>");
        _log.Debug("Italic 포맷 적용");
    }

    /// <summary>Code 포맷 적용 — 선택 텍스트를 &lt;code&gt;로 감쌈</summary>
    [RelayCommand]
    public void FormatCode()
    {
        InputHtml = WrapSelection(InputHtml, "<code>", "</code>");
        _log.Debug("Code 포맷 적용");
    }

    /// <summary>
    /// @멘션 팝업 토글 — 입력 중 '@' 감지 시 호출
    /// </summary>
    [RelayCommand]
    public async Task TriggerMentionAsync(string? query)
    {
        if (string.IsNullOrEmpty(query))
        {
            IsMentionPopupOpen = false;
            return;
        }

        try
        {
            var members = await _teamsService.GetTeamMembersAsync(_teamId);
            MentionCandidates.Clear();
            foreach (var m in members.Where(m =>
                m.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
            {
                MentionCandidates.Add(m.DisplayName ?? string.Empty);
            }
            IsMentionPopupOpen = MentionCandidates.Count > 0;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "@멘션 후보 로드 실패");
            IsMentionPopupOpen = false;
        }
    }

    /// <summary>@멘션 선택 확정</summary>
    [RelayCommand]
    public void SelectMention(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return;
        InputHtml += $"<at>{displayName}</at> ";
        InputText += $"@{displayName} ";
        IsMentionPopupOpen = false;
    }

    // ── 내부 유틸 ──────────────────────────────────────────────────────────

    private static string WrapSelection(string current, string open, string close)
    {
        // 단순 구현: 전체 내용을 태그로 감쌈 (실제 선택 영역 지원은 RichTextBox 레이어에서 처리)
        if (string.IsNullOrEmpty(current))
            return $"{open}{close}";
        return $"{current}{open}{close}";
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var text = Regex.Replace(html, "<[^>]*>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }
}
