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
    /// 새 메시지 입력
    /// </summary>
    [ObservableProperty]
    private string _newMessageText = string.Empty;

    /// <summary>
    /// 현재 사용자 ID (내 메시지 표시용)
    /// </summary>
    [ObservableProperty]
    private string _currentUserId = string.Empty;

    /// <summary>
    /// 로딩 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMessages;

    /// <summary>
    /// 채팅 필터 모드 (all, unread, pinned)
    /// </summary>
    [ObservableProperty]
    private string _chatFilterMode = "all";

    #region 팀/채널 관련 속성

    /// <summary>
    /// 팀 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TeamItemViewModel> _teams = new();

    /// <summary>
    /// 선택된 팀
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTeam))]
    private TeamItemViewModel? _selectedTeam;

    /// <summary>
    /// 선택된 채널
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedChannel))]
    private ChannelItemViewModel? _selectedChannel;

    /// <summary>
    /// 현재 채널의 메시지 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChannelMessageViewModel> _channelMessages = new();

    /// <summary>
    /// 현재 채널의 파일 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChannelFileViewModel> _channelFiles = new();

    /// <summary>
    /// 현재 선택된 탭 (posts, files)
    /// </summary>
    [ObservableProperty]
    private string _currentChannelTab = "posts";

    /// <summary>
    /// 팀이 선택되어 있는지 여부
    /// </summary>
    public bool HasSelectedTeam => SelectedTeam != null;

    /// <summary>
    /// 채널이 선택되어 있는지 여부
    /// </summary>
    public bool HasSelectedChannel => SelectedChannel != null;

    #endregion

    /// <summary>
    /// 선택된 채팅방이 있는지 여부
    /// </summary>
    public bool HasSelectedChat => SelectedChat != null;

    /// <summary>
    /// 메시지 전송 가능 여부
    /// </summary>
    public bool CanSendMessage => !string.IsNullOrWhiteSpace(NewMessageText) && HasSelectedChat;

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
    /// 메시지 전송
    /// </summary>
    [RelayCommand]
    public async Task SendMessageAsync()
    {
        if (SelectedChat == null || string.IsNullOrWhiteSpace(NewMessageText))
            return;

        var messageToSend = NewMessageText.Trim();
        NewMessageText = string.Empty; // 즉시 입력창 클리어

        await ExecuteAsync(async () =>
        {
            // 메시지 전송
            var sentMessage = await _teamsService.SendMessageAsync(SelectedChat.Id, messageToSend);

            if (sentMessage != null)
            {
                // 전송된 메시지를 즉시 목록에 추가 (UI 응답성 향상)
                var messageItem = new MessageItemViewModel
                {
                    Id = sentMessage.Id ?? Guid.NewGuid().ToString(),
                    Content = StripHtml(sentMessage.Body?.Content),
                    FromUser = sentMessage.From?.User?.DisplayName ?? "나",
                    CreatedDateTime = sentMessage.CreatedDateTime?.DateTime ?? DateTime.Now,
                    MessageType = "message",
                    IsFromMe = true
                };

                // 목록 맨 위에 추가 (최신 메시지가 위로)
                Messages.Insert(0, messageItem);

                _logger.Information("메시지 전송 완료: {ChatName}", SelectedChat.DisplayName);
            }
        }, "메시지 전송 실패");
    }

    /// <summary>
    /// 새 메시지 실시간 수신 체크
    /// </summary>
    [RelayCommand]
    public async Task CheckNewMessagesAsync()
    {
        if (SelectedChat == null)
            return;

        try
        {
            // 가장 최근 메시지 시간 기준으로 새 메시지 조회
            DateTime? since = Messages.FirstOrDefault()?.CreatedDateTime;
            if (since.HasValue)
            {
                var newMessages = await _teamsService.GetNewMessagesAsync(SelectedChat.Id, since.Value);

                foreach (var msg in newMessages.Reverse())
                {
                    // 이미 있는 메시지는 스킵
                    if (Messages.Any(m => m.Id == msg.Id))
                        continue;

                    var messageItem = new MessageItemViewModel
                    {
                        Id = msg.Id ?? string.Empty,
                        Content = StripHtml(msg.Body?.Content),
                        FromUser = msg.From?.User?.DisplayName ?? msg.From?.User?.Id ?? "Unknown",
                        CreatedDateTime = msg.CreatedDateTime?.DateTime,
                        MessageType = msg.MessageType?.ToString() ?? "message",
                        IsFromMe = msg.From?.User?.Id == CurrentUserId
                    };

                    Messages.Insert(0, messageItem);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "새 메시지 확인 실패");
        }
    }

    /// <summary>
    /// 채팅 필터 변경
    /// </summary>
    [RelayCommand]
    public void SetChatFilter(string filter)
    {
        ChatFilterMode = filter;
        // 추후 필터링 로직 구현
        _logger.Debug("채팅 필터 변경: {Filter}", filter);
    }

    #region 팀/채널 관련 메서드

    /// <summary>
    /// 팀 목록 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadTeamsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var teams = await _teamsService.GetMyTeamsAsync();

            Teams.Clear();
            foreach (var team in teams)
            {
                var teamItem = new TeamItemViewModel
                {
                    Id = team.Id ?? string.Empty,
                    DisplayName = team.DisplayName ?? "(이름 없음)",
                    Description = team.Description ?? string.Empty,
                    IsArchived = team.IsArchived ?? false
                };

                // 팀의 채널 로드
                var channels = await _teamsService.GetChannelsAsync(team.Id ?? string.Empty);
                foreach (var channel in channels)
                {
                    teamItem.Channels.Add(new ChannelItemViewModel
                    {
                        Id = channel.Id ?? string.Empty,
                        DisplayName = channel.DisplayName ?? "(채널명 없음)",
                        Description = channel.Description ?? string.Empty,
                        TeamId = team.Id ?? string.Empty,
                        MembershipType = channel.MembershipType?.ToString() ?? "standard"
                    });
                }

                Teams.Add(teamItem);
            }

            _logger.Information("팀 목록 로드 완료: {Count}개", Teams.Count);
        }, "팀 목록 로드 실패");
    }

    /// <summary>
    /// 팀 선택
    /// </summary>
    [RelayCommand]
    public void SelectTeam(TeamItemViewModel team)
    {
        SelectedTeam = team;
        SelectedChannel = null;
        ChannelMessages.Clear();
        ChannelFiles.Clear();
    }

    /// <summary>
    /// 채널 선택
    /// </summary>
    [RelayCommand]
    public async Task SelectChannelAsync(ChannelItemViewModel channel)
    {
        SelectedChannel = channel;
        await LoadChannelContentAsync();
    }

    /// <summary>
    /// 채널 콘텐츠 로드 (메시지, 파일)
    /// </summary>
    private async Task LoadChannelContentAsync()
    {
        if (SelectedChannel == null || string.IsNullOrEmpty(SelectedChannel.TeamId))
            return;

        await ExecuteAsync(async () =>
        {
            // 메시지 로드
            var messages = await _teamsService.GetChannelMessagesAsync(
                SelectedChannel.TeamId,
                SelectedChannel.Id,
                50);

            ChannelMessages.Clear();
            foreach (var msg in messages.OrderBy(m => m.CreatedDateTime))
            {
                var bodyContent = msg.Body?.Content ?? string.Empty;
                var plainText = StripHtml(bodyContent) ?? string.Empty;

                ChannelMessages.Add(new ChannelMessageViewModel
                {
                    Id = msg.Id ?? string.Empty,
                    Content = plainText,
                    HtmlContent = bodyContent,
                    FromUser = msg.From?.User?.DisplayName ?? "알 수 없음",
                    CreatedDateTime = msg.CreatedDateTime?.DateTime ?? DateTime.Now,
                    ReplyCount = 0
                });
            }

            // 파일 로드
            var files = await _teamsService.GetChannelFilesAsync(
                SelectedChannel.TeamId,
                SelectedChannel.Id);

            ChannelFiles.Clear();
            foreach (var file in files)
            {
                ChannelFiles.Add(new ChannelFileViewModel
                {
                    Id = file.Id ?? string.Empty,
                    Name = file.Name ?? "(파일명 없음)",
                    Size = file.Size ?? 0,
                    LastModified = file.LastModifiedDateTime?.DateTime ?? DateTime.Now,
                    WebUrl = file.WebUrl ?? string.Empty,
                    IsFolder = file.Folder != null
                });
            }

            _logger.Information("채널 콘텐츠 로드 완료: 메시지 {MsgCount}개, 파일 {FileCount}개",
                ChannelMessages.Count, ChannelFiles.Count);
        }, "채널 콘텐츠 로드 실패");
    }

    /// <summary>
    /// 채널에 메시지 전송
    /// </summary>
    [RelayCommand]
    public async Task SendChannelMessageAsync(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || SelectedChannel == null)
            return;

        await ExecuteAsync(async () =>
        {
            var result = await _teamsService.SendChannelMessageAsync(
                SelectedChannel.TeamId,
                SelectedChannel.Id,
                content);

            if (result != null)
            {
                // 메시지 목록에 추가
                ChannelMessages.Add(new ChannelMessageViewModel
                {
                    Id = result.Id ?? string.Empty,
                    Content = StripHtml(content) ?? string.Empty,
                    HtmlContent = content,
                    FromUser = "나",
                    CreatedDateTime = DateTime.Now,
                    ReplyCount = 0
                });

                _logger.Information("채널 메시지 전송 성공");
            }
        }, "채널 메시지 전송 실패");
    }

    /// <summary>
    /// 채널 탭 전환
    /// </summary>
    [RelayCommand]
    public void SwitchChannelTab(string tab)
    {
        CurrentChannelTab = tab;
    }

    /// <summary>
    /// 팀/채널 새로고침
    /// </summary>
    [RelayCommand]
    public async Task RefreshTeamsAsync()
    {
        if (SelectedChannel != null)
        {
            await LoadChannelContentAsync();
        }
        else
        {
            await LoadTeamsAsync();
        }
    }

    #endregion

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
    /// 마지막 메시지 미리보기
    /// </summary>
    [ObservableProperty]
    private string? _lastMessage;

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

#region 팀/채널 관련 ViewModel 클래스

/// <summary>
/// 팀 아이템 ViewModel
/// </summary>
public partial class TeamItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isArchived;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private ObservableCollection<ChannelItemViewModel> _channels = new();
}

/// <summary>
/// 채널 아이템 ViewModel
/// </summary>
public partial class ChannelItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _teamId = string.Empty;

    [ObservableProperty]
    private string _membershipType = "standard";

    /// <summary>
    /// 일반 채널인지 여부
    /// </summary>
    public bool IsGeneral => DisplayName.Equals("General", StringComparison.OrdinalIgnoreCase) ||
                             DisplayName.Equals("일반", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// 채널 메시지 ViewModel
/// </summary>
public partial class ChannelMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _htmlContent = string.Empty;

    [ObservableProperty]
    private string _fromUser = string.Empty;

    [ObservableProperty]
    private DateTime _createdDateTime;

    [ObservableProperty]
    private int _replyCount;

    /// <summary>
    /// 시간 표시
    /// </summary>
    public string ChannelTimeDisplay
    {
        get
        {
            var diff = DateTime.Now - CreatedDateTime;
            if (diff.TotalMinutes < 1)
                return "방금 전";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}분 전";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}시간 전";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}일 전";

            return CreatedDateTime.ToString("MM/dd HH:mm");
        }
    }
}

/// <summary>
/// 채널 파일 ViewModel
/// </summary>
public partial class ChannelFileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private DateTime _lastModified;

    [ObservableProperty]
    private string _webUrl = string.Empty;

    [ObservableProperty]
    private bool _isFolder;

    /// <summary>
    /// 파일 크기 표시
    /// </summary>
    public string SizeDisplay
    {
        get
        {
            if (IsFolder) return "-";
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024:F1} KB";
            if (Size < 1024 * 1024 * 1024) return $"{Size / (1024 * 1024):F1} MB";
            return $"{Size / (1024 * 1024 * 1024):F2} GB";
        }
    }

    /// <summary>
    /// 파일 아이콘
    /// </summary>
    public string FileIcon
    {
        get
        {
            if (IsFolder) return "Folder24";
            var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
            return ext switch
            {
                ".doc" or ".docx" => "Document24",
                ".xls" or ".xlsx" => "TableSimple24",
                ".ppt" or ".pptx" => "SlideText24",
                ".pdf" => "DocumentPdf24",
                ".jpg" or ".jpeg" or ".png" or ".gif" => "Image24",
                ".zip" or ".rar" or ".7z" => "Archive24",
                _ => "Document24"
            };
        }
    }
}

#endregion
