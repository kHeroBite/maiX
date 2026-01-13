using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mailX.Models;
using mailX.Services.Graph;
using Serilog;

namespace mailX.ViewModels;

/// <summary>
/// Teams 뷰모델 - 채팅방 및 메시지 관리
/// </summary>
public partial class TeamsViewModel : ViewModelBase
{
    private readonly GraphTeamsService _teamsService;
    private readonly ILogger _logger;

    /// <summary>
    /// 채팅방 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChatItemViewModel> _chats = new();

    /// <summary>
    /// 선택된 채팅방
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedChat))]
    private ChatItemViewModel? _selectedChat;

    /// <summary>
    /// 현재 채팅방의 메시지 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MessageItemViewModel> _messages = new();

    /// <summary>
    /// 읽지 않은 메시지 수 (배지용)
    /// </summary>
    [ObservableProperty]
    private int _unreadCount;

    /// <summary>
    /// 검색어
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// 선택된 채팅방이 있는지 여부
    /// </summary>
    public bool HasSelectedChat => SelectedChat != null;

    public TeamsViewModel(GraphTeamsService teamsService)
    {
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _logger = Log.ForContext<TeamsViewModel>();
    }

    /// <summary>
    /// 채팅방 목록 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadChatsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var chats = await _teamsService.GetChatsAsync();

            Chats.Clear();
            foreach (var chat in chats)
            {
                var chatItem = new ChatItemViewModel
                {
                    Id = chat.Id ?? string.Empty,
                    DisplayName = _teamsService.GetChatDisplayName(chat),
                    ChatType = chat.ChatType?.ToString() ?? "Unknown",
                    LastUpdatedDateTime = chat.LastUpdatedDateTime?.DateTime,
                    Topic = chat.Topic
                };

                Chats.Add(chatItem);
            }

            // 읽지 않은 메시지 수 업데이트
            UnreadCount = await _teamsService.GetUnreadCountAsync();

            _logger.Information("채팅방 {Count}개 로드 완료, 읽지 않은 메시지: {Unread}개",
                Chats.Count, UnreadCount);
        }, "채팅방 목록 로드 실패");
    }

    /// <summary>
    /// 채팅방 선택 시 메시지 로드
    /// </summary>
    partial void OnSelectedChatChanged(ChatItemViewModel? value)
    {
        if (value != null)
        {
            _ = LoadMessagesAsync(value.Id);
        }
        else
        {
            Messages.Clear();
        }
    }

    /// <summary>
    /// 선택된 채팅방의 메시지 로드
    /// </summary>
    /// <param name="chatId">채팅방 ID</param>
    [RelayCommand]
    public async Task LoadMessagesAsync(string chatId)
    {
        if (string.IsNullOrEmpty(chatId))
            return;

        await ExecuteAsync(async () =>
        {
            var messages = await _teamsService.GetChatMessagesAsync(chatId);

            Messages.Clear();
            foreach (var message in messages)
            {
                var messageItem = new MessageItemViewModel
                {
                    Id = message.Id ?? string.Empty,
                    Content = StripHtml(message.Body?.Content),
                    FromUser = message.From?.User?.DisplayName ?? message.From?.User?.Id ?? "Unknown",
                    CreatedDateTime = message.CreatedDateTime?.DateTime,
                    MessageType = message.MessageType?.ToString() ?? "message",
                    IsFromMe = false // 추후 현재 사용자 ID와 비교 필요
                };

                Messages.Add(messageItem);
            }

            _logger.Debug("채팅방 {ChatId} 메시지 {Count}개 로드", chatId, Messages.Count);
        }, "메시지 로드 실패");
    }

    /// <summary>
    /// 메시지 검색
    /// </summary>
    [RelayCommand]
    public async Task SearchMessagesAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        await ExecuteAsync(async () =>
        {
            var results = await _teamsService.SearchMessagesAsync(SearchQuery);

            Messages.Clear();
            foreach (var message in results)
            {
                var messageItem = new MessageItemViewModel
                {
                    Id = message.Id ?? string.Empty,
                    Content = StripHtml(message.Body?.Content),
                    FromUser = message.From?.User?.DisplayName ?? "Unknown",
                    CreatedDateTime = message.CreatedDateTime?.DateTime,
                    MessageType = "search_result"
                };

                Messages.Add(messageItem);
            }

            _logger.Information("검색 '{Query}': {Count}개 결과", SearchQuery, Messages.Count);
        }, "메시지 검색 실패");
    }

    /// <summary>
    /// 읽지 않은 메시지 수 새로고침
    /// </summary>
    [RelayCommand]
    public async Task RefreshUnreadCountAsync()
    {
        await ExecuteAsync(async () =>
        {
            UnreadCount = await _teamsService.GetUnreadCountAsync();
        }, "읽지 않은 메시지 수 조회 실패");
    }

    /// <summary>
    /// 채팅방 메시지 동기화
    /// </summary>
    [RelayCommand]
    public async Task SyncMessagesAsync()
    {
        if (SelectedChat == null)
            return;

        await ExecuteAsync(async () =>
        {
            var syncedCount = await _teamsService.SyncMessagesAsync(SelectedChat.Id);
            _logger.Information("채팅방 {ChatName} 메시지 {Count}개 동기화 완료",
                SelectedChat.DisplayName, syncedCount);

            // 메시지 목록 새로고침
            await LoadMessagesAsync(SelectedChat.Id);
        }, "메시지 동기화 실패");
    }

    /// <summary>
    /// HTML 태그 제거
    /// </summary>
    private string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        // 간단한 HTML 태그 제거
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }
}

/// <summary>
/// 채팅방 아이템 뷰모델
/// </summary>
public partial class ChatItemViewModel : ObservableObject
{
    /// <summary>
    /// 채팅방 ID
    /// </summary>
    [ObservableProperty]
    private string _id = string.Empty;

    /// <summary>
    /// 표시 이름
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// 채팅 유형 (OneOnOne, Group, Meeting)
    /// </summary>
    [ObservableProperty]
    private string _chatType = string.Empty;

    /// <summary>
    /// 마지막 업데이트 시간
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastUpdatedDateTime;

    /// <summary>
    /// 주제 (그룹 채팅의 경우)
    /// </summary>
    [ObservableProperty]
    private string? _topic;

    /// <summary>
    /// 읽지 않은 메시지 수
    /// </summary>
    [ObservableProperty]
    private int _unreadCount;

    /// <summary>
    /// 읽지 않은 메시지가 있는지 여부
    /// </summary>
    public bool HasUnread => UnreadCount > 0;

    /// <summary>
    /// 마지막 업데이트 시간 표시 문자열
    /// </summary>
    public string LastUpdatedDisplay
    {
        get
        {
            if (!LastUpdatedDateTime.HasValue)
                return string.Empty;

            var diff = DateTime.Now - LastUpdatedDateTime.Value;

            if (diff.TotalMinutes < 1)
                return "방금 전";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}분 전";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}시간 전";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}일 전";

            return LastUpdatedDateTime.Value.ToString("MM/dd");
        }
    }
}

/// <summary>
/// 메시지 아이템 뷰모델
/// </summary>
public partial class MessageItemViewModel : ObservableObject
{
    /// <summary>
    /// 메시지 ID
    /// </summary>
    [ObservableProperty]
    private string _id = string.Empty;

    /// <summary>
    /// 메시지 내용 (HTML 제거된)
    /// </summary>
    [ObservableProperty]
    private string? _content;

    /// <summary>
    /// 발신자 이름
    /// </summary>
    [ObservableProperty]
    private string _fromUser = string.Empty;

    /// <summary>
    /// 생성 시간
    /// </summary>
    [ObservableProperty]
    private DateTime? _createdDateTime;

    /// <summary>
    /// 메시지 타입
    /// </summary>
    [ObservableProperty]
    private string _messageType = string.Empty;

    /// <summary>
    /// 내가 보낸 메시지인지 여부
    /// </summary>
    [ObservableProperty]
    private bool _isFromMe;

    /// <summary>
    /// 시간 표시 문자열
    /// </summary>
    public string TimeDisplay
    {
        get
        {
            if (!CreatedDateTime.HasValue)
                return string.Empty;

            var today = DateTime.Today;
            var messageDate = CreatedDateTime.Value.Date;

            if (messageDate == today)
                return CreatedDateTime.Value.ToString("HH:mm");
            if (messageDate == today.AddDays(-1))
                return $"어제 {CreatedDateTime.Value:HH:mm}";

            return CreatedDateTime.Value.ToString("MM/dd HH:mm");
        }
    }

    /// <summary>
    /// 발신자 이니셜 (아바타용)
    /// </summary>
    public string FromUserInitial
    {
        get
        {
            if (string.IsNullOrEmpty(FromUser))
                return "?";

            return FromUser.Substring(0, 1).ToUpper();
        }
    }
}
