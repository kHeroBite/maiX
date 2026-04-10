using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph.Models;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.Graph;
using mAIx.ViewModels.Teams;
using NLog;
using Serilog;

namespace mAIx.ViewModels;

/// <summary>
/// Teams 뷰모델 - 채팅방 및 메시지 관리
/// </summary>
public partial class TeamsViewModel : ViewModelBase
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();
    private readonly GraphTeamsService _teamsService;
    private readonly IDbContextFactory<mAIxDbContext> _dbContextFactory;
    private readonly Serilog.ILogger _logger;

    /// <summary>
    /// 채팅방 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChatItemViewModel> _chats = new();

    /// <summary>
    /// 즐겨찾기 채팅방 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChatItemViewModel> _favoriteChats = new();

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
    /// 채팅 목록 로딩 중 여부 (동시성 보호용)
    /// </summary>
    private bool _isLoadingChats;

    /// <summary>
    /// 채팅 필터 모드 (all, unread, pinned)
    /// </summary>
    [ObservableProperty]
    private string _chatFilterMode = "all";

    /// <summary>
    /// 전체 채팅방 목록 (필터링 전)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ChatItemViewModel> _allChats = new();

    /// <summary>
    /// 읽지 않음 필터
    /// </summary>
    [ObservableProperty]
    private bool _filterUnread = false;

    /// <summary>
    /// 채팅 필터 (1:1 채팅)
    /// </summary>
    [ObservableProperty]
    private bool _filterChat = true;

    /// <summary>
    /// 모임 채팅 필터 (그룹 채팅)
    /// </summary>
    [ObservableProperty]
    private bool _filterMeeting = false;

    #region 팀/채널 관련 속성

    /// <summary>
    /// 팀 목록
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTeams))]
    [NotifyPropertyChangedFor(nameof(NoTeamsMessage))]
    private ObservableCollection<TeamItemViewModel> _teams = new();

    /// <summary>
    /// 팀이 있는지 여부
    /// </summary>
    public bool HasTeams => Teams.Count > 0;

    /// <summary>
    /// 팀이 없을 때 표시할 메시지
    /// </summary>
    public string NoTeamsMessage => TeamsLoadError ?? "가입한 팀이 없습니다";

    /// <summary>
    /// 팀 로드 오류 메시지
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoTeamsMessage))]
    private string? _teamsLoadError;

    /// <summary>
    /// 즐겨찾기 채널 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FavoriteChannelViewModel> _favoriteChannels = new();

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

    #region Hub 패턴 — 채널 탭 Sub-ViewModel

    /// <summary>
    /// 채널별 게시물 ViewModel 캐시 (탭 전환 시 상태 유지)
    /// </summary>
    private readonly Dictionary<string, ChannelPostsViewModel> _postsVmCache = new();

    /// <summary>
    /// 채널별 파일 ViewModel 캐시
    /// </summary>
    private readonly Dictionary<string, ChannelFilesViewModel> _filesVmCache = new();

    /// <summary>
    /// 현재 채널의 게시물 Sub-ViewModel
    /// </summary>
    [ObservableProperty]
    private ChannelPostsViewModel? _channelPostsVm;

    /// <summary>
    /// 채널별 설정 ViewModel 캐시
    /// </summary>
    private readonly Dictionary<string, ChannelSettingsViewModel> _settingsVmCache = new();

    /// <summary>
    /// 현재 채널의 파일 Sub-ViewModel
    /// </summary>
    [ObservableProperty]
    private ChannelFilesViewModel? _channelFilesVm;

    /// <summary>
    /// 현재 채널의 설정 Sub-ViewModel
    /// </summary>
    [ObservableProperty]
    private ChannelSettingsViewModel? _channelSettingsVm;

    #endregion

    #endregion

    /// <summary>
    /// 선택된 채팅방이 있는지 여부
    /// </summary>
    public bool HasSelectedChat => SelectedChat != null;

    /// <summary>
    /// 메시지 전송 가능 여부
    /// </summary>
    public bool CanSendMessage => !string.IsNullOrWhiteSpace(NewMessageText) && HasSelectedChat;

    #region 스레드/리액션/멘션/미팅 속성

    /// <summary>
    /// 스레드 패널 표시 여부
    /// </summary>
    [ObservableProperty]
    private bool _isThreadPanelOpen;

    /// <summary>
    /// 스레드 원본 메시지
    /// </summary>
    [ObservableProperty]
    private MessageItemViewModel? _threadParentMessage;

    /// <summary>
    /// 스레드 답글 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MessageItemViewModel> _threadReplies = new();

    /// <summary>
    /// 스레드 답글 입력 텍스트
    /// </summary>
    [ObservableProperty]
    private string _threadReplyText = string.Empty;

    /// <summary>
    /// 멘션 팝업 표시 여부
    /// </summary>
    [ObservableProperty]
    private bool _isMentionPopupOpen;

    /// <summary>
    /// 멘션 후보 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MentionCandidate> _mentionCandidates = new();

    /// <summary>
    /// 멘션 필터 텍스트
    /// </summary>
    [ObservableProperty]
    private string _mentionFilter = string.Empty;

    /// <summary>
    /// 채팅 멤버 목록 (멘션 자동완성용)
    /// </summary>
    private List<MentionCandidate> _chatMembers = new();

    /// <summary>
    /// 사용 가능한 리액션 이모지 목록
    /// </summary>
    public static readonly string[] AvailableReactions = { "👍", "❤️", "😂", "😮", "😢", "🎉" };

    #endregion

    public TeamsViewModel(GraphTeamsService teamsService, IDbContextFactory<mAIxDbContext> dbContextFactory)
    {
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = Log.ForContext<TeamsViewModel>();
    }

    /// <summary>
    /// 채팅방 목록 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadChatsAsync()
    {
        // 동시성 보호: 이미 로딩 중이면 무시
        if (_isLoadingChats)
        {
            _logger.Debug("채팅 목록 로딩 중 - 중복 호출 무시");
            return;
        }

        _isLoadingChats = true;
        try
        {
        await ExecuteAsync(async () =>
        {
            // 먼저 현재 사용자 ID 캐시 (1:1 채팅에서 상대방 이름 표시용)
            CurrentUserId = await _teamsService.GetCachedCurrentUserIdAsync() ?? string.Empty;

            var chats = await _teamsService.GetChatsAsync();
            var chatsList = chats.ToList();

            // 로컬 DB에서 즐겨찾기 목록 조회 (SortOrder 포함)
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            var favoriteList = await dbContext.ChatFavorites
                .OrderBy(f => f.SortOrder)
                .ToListAsync();
            var favoriteIds = favoriteList.Select(f => f.ChatId).ToList();

            Chats.Clear();
            FavoriteChats.Clear();
            AllChats.Clear();

            foreach (var chat in chatsList)
            {
                // 비동기로 채팅방 이름 가져오기 (1:1 채팅에서 상대방 이름 정확히 표시)
                var displayName = await _teamsService.GetChatDisplayNameAsync(chat);

                // 마지막 메시지 시간 및 미리보기 결정
                // lastMessagePreview는 읽음 표시 등으로 인해 정확하지 않으므로
                // 항상 실제 마지막 메시지를 조회
                var (lastMessageTime, lastMessagePreview) = await _teamsService.GetLastRealMessageAsync(chat.Id ?? string.Empty);

                var chatItem = new ChatItemViewModel
                {
                    Id = chat.Id ?? string.Empty,
                    DisplayName = displayName,
                    ChatType = chat.ChatType?.ToString() ?? "Unknown",
                    LastUpdatedDateTime = lastMessageTime,
                    LastMessage = lastMessagePreview,
                    Topic = chat.Topic,
                    IsFavorite = favoriteIds.Contains(chat.Id ?? string.Empty)
                };

                AllChats.Add(chatItem);
            }

            // 최신순 정렬 (마지막 메시지 시간 기준, 내림차순)
            var sortedChats = AllChats.OrderByDescending(c => c.LastUpdatedDateTime ?? DateTime.MinValue).ToList();
            AllChats.Clear();
            Chats.Clear();
            FavoriteChats.Clear();

            foreach (var chatItem in sortedChats)
            {
                AllChats.Add(chatItem);
                Chats.Add(chatItem);
            }

            // 즐겨찾기는 SortOrder 순서대로 추가
            foreach (var favInfo in favoriteList)
            {
                var chatItem = AllChats.FirstOrDefault(c => c.Id == favInfo.ChatId);
                if (chatItem != null)
                {
                    FavoriteChats.Add(chatItem);
                }
            }

            // 읽지 않은 메시지 수 업데이트
            UnreadCount = await _teamsService.GetUnreadCountAsync();

            _logger.Information("채팅방 {Count}개 로드 완료 (최신순), 즐겨찾기: {Fav}개, 읽지 않은 메시지: {Unread}개",
                Chats.Count, FavoriteChats.Count, UnreadCount);

            // 백그라운드에서 사진 로드 (UI 차단 방지)
            _ = LoadChatPhotosAsync(chatsList);
        }, "채팅방 목록 로드 실패");
        }
        finally
        {
            _isLoadingChats = false;
        }
    }

    /// <summary>
    /// 채팅방 프로필 사진 비동기 로드 (캐시 우선 + 백그라운드 새로고침)
    /// </summary>
    private async Task LoadChatPhotosAsync(List<Chat> chats)
    {
        try
        {
            // 1단계: 캐시된 사진 먼저 즉시 표시
            await LoadCachedPhotosAsync(chats);
            _logger.Debug("채팅방 캐시 사진 로드 완료");

            // 2단계: 백그라운드에서 API로 새로고침
            _ = RefreshPhotosInBackgroundAsync(chats);
        }
        catch (Exception ex)
        {
            _logger.Debug("채팅방 사진 로드 중 오류: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// 캐시된 사진 먼저 로드 (즉시 표시)
    /// </summary>
    private async Task LoadCachedPhotosAsync(List<Chat> chats)
    {
        var myId = await _teamsService.GetCachedCurrentUserIdAsync();

        foreach (var chat in chats)
        {
            var chatItem = Chats.FirstOrDefault(c => c.Id == chat.Id);
            if (chatItem == null)
                continue;

            // 멤버 수 저장
            chatItem.MemberCount = chat.Members?.Count ?? 0;

            // 1:1 채팅인 경우
            if (chat.ChatType == ChatType.OneOnOne)
            {
                var otherMember = chat.Members?
                    .OfType<Microsoft.Graph.Models.AadUserConversationMember>()
                    .FirstOrDefault(m => m.UserId != myId);

                if (otherMember?.UserId != null)
                {
                    // 캐시에서 즉시 로드
                    var cachedPhoto = _teamsService.GetCachedUserPhoto(otherMember.UserId);
                    if (!string.IsNullOrEmpty(cachedPhoto))
                    {
                        // UI 스레드에서 속성 변경 (PropertyChanged가 UI에 반영되도록)
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            chatItem.PhotoBase64 = cachedPhoto;
                        });
                    }
                }
            }
            // 그룹 채팅인 경우 - 멤버 사진 최대 4명
            else if (chatItem.MemberPhotos.Count == 0)
            {
                var members = chat.Members?
                    .OfType<Microsoft.Graph.Models.AadUserConversationMember>()
                    .Where(m => m.UserId != myId)
                    .Take(4)
                    .ToList() ?? new();

                foreach (var member in members)
                {
                    if (string.IsNullOrEmpty(member.UserId))
                        continue;

                    var cachedPhoto = _teamsService.GetCachedUserPhoto(member.UserId);
                    var memberPhoto = new MemberPhotoInfo
                    {
                        UserId = member.UserId,
                        DisplayName = member.DisplayName ?? "알 수 없음",
                        PhotoBase64 = cachedPhoto
                    };

                    // UI 스레드에서 컬렉션 변경 (PropertyChanged가 UI에 반영되도록)
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        chatItem.MemberPhotos.Add(memberPhoto);
                    });
                }
            }
        }
    }

    /// <summary>
    /// 백그라운드에서 API로 사진 새로고침
    /// </summary>
    private async Task RefreshPhotosInBackgroundAsync(List<Chat> chats)
    {
        try
        {
            var myId = await _teamsService.GetCachedCurrentUserIdAsync();

            foreach (var chat in chats)
            {
                var chatItem = Chats.FirstOrDefault(c => c.Id == chat.Id);
                if (chatItem == null)
                    continue;

                // 1:1 채팅인 경우
                if (chat.ChatType == ChatType.OneOnOne)
                {
                    var otherMember = chat.Members?
                        .OfType<Microsoft.Graph.Models.AadUserConversationMember>()
                        .FirstOrDefault(m => m.UserId != myId);

                    if (otherMember?.UserId != null)
                    {
                        var newPhoto = await _teamsService.RefreshUserPhotoAsync(otherMember.UserId);
                        if (!string.IsNullOrEmpty(newPhoto) && chatItem.PhotoBase64 != newPhoto)
                        {
                            // UI 스레드에서 속성 업데이트 (PropertyChanged가 UI에 반영되도록)
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                chatItem.PhotoBase64 = newPhoto;
                            });
                        }
                    }
                }
                // 그룹 채팅인 경우
                else
                {
                    var members = chat.Members?
                        .OfType<Microsoft.Graph.Models.AadUserConversationMember>()
                        .Where(m => m.UserId != myId)
                        .Take(4)
                        .ToList() ?? new();

                    for (int i = 0; i < members.Count && i < chatItem.MemberPhotos.Count; i++)
                    {
                        var member = members[i];
                        if (string.IsNullOrEmpty(member.UserId))
                            continue;

                        var newPhoto = await _teamsService.RefreshUserPhotoAsync(member.UserId);
                        if (!string.IsNullOrEmpty(newPhoto) && chatItem.MemberPhotos[i].PhotoBase64 != newPhoto)
                        {
                            // UI 스레드에서 속성 업데이트
                            var index = i;
                            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                            {
                                chatItem.MemberPhotos[index].PhotoBase64 = newPhoto;
                            });
                        }
                    }
                }
            }
            _logger.Debug("채팅방 사진 백그라운드 새로고침 완료");
        }
        catch (Exception ex)
        {
            _logger.Debug("채팅방 사진 백그라운드 새로고침 중 오류: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// 메시지 발신자 사진을 백그라운드에서 로드
    /// </summary>
    private async Task LoadMessagePhotosInBackgroundAsync()
    {
        try
        {
            // 사진이 없는 발신자 ID 수집 (중복 제거)
            var userIds = Messages
                .Where(m => !m.IsFromMe && !string.IsNullOrEmpty(m.FromUserId) && !m.HasFromUserPhoto)
                .Select(m => m.FromUserId)
                .Distinct()
                .ToList();

            foreach (var userId in userIds)
            {
                var photo = await _teamsService.RefreshUserPhotoAsync(userId);
                if (!string.IsNullOrEmpty(photo))
                {
                    // 같은 발신자의 모든 메시지에 사진 적용
                    foreach (var msg in Messages.Where(m => m.FromUserId == userId))
                    {
                        msg.FromUserPhoto = photo;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("메시지 발신자 사진 로드 중 오류: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// 메시지 목록에 날짜 분리선 설정
    /// </summary>
    private void SetDateSeparators()
    {
        DateTime? lastDate = null;

        foreach (var message in Messages)
        {
            if (message.CreatedDateTime.HasValue)
            {
                var messageDate = message.CreatedDateTime.Value.Date;

                // 이전 메시지와 날짜가 다르면 분리선 표시
                if (lastDate == null || messageDate != lastDate)
                {
                    message.ShowDateSeparator = true;
                    lastDate = messageDate;
                }
                else
                {
                    message.ShowDateSeparator = false;
                }
            }
        }
    }

    /// <summary>
    /// 채팅방 선택 시 메시지 로드
    /// </summary>
    partial void OnSelectedChatChanged(ChatItemViewModel? value)
    {
        if (value != null)
        {
            _ = LoadMessagesAsync(value.Id);
            _ = LoadChatMembersAsync(value.Id);
        }
        else
        {
            Messages.Clear();
            _chatMembers.Clear();
        }

        // 스레드 패널 닫기
        IsThreadPanelOpen = false;
        ThreadParentMessage = null;
        ThreadReplies.Clear();
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
            var myId = await _teamsService.GetCachedCurrentUserIdAsync();

            // 시간순 정렬 (오래된 메시지가 위, 최신 메시지가 아래)
            var sortedMessages = messages.OrderBy(m => m.CreatedDateTime).ToList();

            foreach (var message in sortedMessages)
            {
                var fromUserId = message.From?.User?.Id ?? string.Empty;
                var isFromMe = !string.IsNullOrEmpty(myId) && fromUserId == myId;

                var messageItem = new MessageItemViewModel
                {
                    Id = message.Id ?? string.Empty,
                    Content = StripHtml(message.Body?.Content),
                    FromUser = message.From?.User?.DisplayName ?? message.From?.User?.Id ?? "Unknown",
                    FromUserId = fromUserId,
                    CreatedDateTime = message.CreatedDateTime?.ToLocalTime().DateTime,
                    MessageType = message.MessageType?.ToString() ?? "message",
                    IsFromMe = isFromMe
                };

                // 발신자 사진 로드 (캐시에서 먼저)
                if (!isFromMe && !string.IsNullOrEmpty(fromUserId))
                {
                    var cachedPhoto = _teamsService.GetCachedUserPhoto(fromUserId);
                    if (!string.IsNullOrEmpty(cachedPhoto))
                    {
                        messageItem.FromUserPhoto = cachedPhoto;
                    }
                }

                Messages.Add(messageItem);
            }

            // 날짜 분리선 설정
            SetDateSeparators();

            // 백그라운드에서 사진 새로고침
            _ = LoadMessagePhotosInBackgroundAsync();

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
                    CreatedDateTime = message.CreatedDateTime?.ToLocalTime().DateTime,
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
                    CreatedDateTime = sentMessage.CreatedDateTime?.ToLocalTime().DateTime ?? DateTime.Now,
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
                        CreatedDateTime = msg.CreatedDateTime?.ToLocalTime().DateTime,
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

    /// <summary>
    /// 채팅 즐겨찾기 토글
    /// </summary>
    /// <param name="chatItem">대상 채팅</param>
    [RelayCommand]
    public async Task ToggleFavoriteAsync(ChatItemViewModel chatItem)
    {
        if (chatItem == null)
            return;

        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            if (chatItem.IsFavorite)
            {
                // 즐겨찾기 해제
                var favorite = await dbContext.ChatFavorites.FindAsync(chatItem.Id);
                if (favorite != null)
                {
                    dbContext.ChatFavorites.Remove(favorite);
                    await dbContext.SaveChangesAsync();
                }

                chatItem.IsFavorite = false;
                FavoriteChats.Remove(chatItem);

                _logger.Information("채팅 즐겨찾기 해제: {Name}", chatItem.DisplayName);
            }
            else
            {
                // 즐겨찾기 추가
                var favorite = new ChatFavorite
                {
                    ChatId = chatItem.Id,
                    DisplayName = chatItem.DisplayName,
                    ChatType = chatItem.ChatType,
                    FavoritedAt = DateTime.Now,
                    SortOrder = FavoriteChats.Count
                };

                dbContext.ChatFavorites.Add(favorite);
                await dbContext.SaveChangesAsync();

                chatItem.IsFavorite = true;
                FavoriteChats.Add(chatItem);

                _logger.Information("채팅 즐겨찾기 추가: {Name}", chatItem.DisplayName);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채팅 즐겨찾기 토글 실패: {ChatId}", chatItem.Id);
        }
    }

    /// <summary>
    /// 즐겨찾기 순서 변경 (드래그 앤 드롭)
    /// </summary>
    /// <param name="draggedChatId">드래그한 채팅 ID</param>
    /// <param name="targetChatId">드롭 대상 채팅 ID</param>
    public async Task ReorderFavoriteAsync(string draggedChatId, string targetChatId)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            // 현재 즐겨찾기 목록 SortOrder 순으로 가져오기
            var favorites = await dbContext.ChatFavorites
                .OrderBy(f => f.SortOrder)
                .ToListAsync();

            var draggedFav = favorites.FirstOrDefault(f => f.ChatId == draggedChatId);
            var targetFav = favorites.FirstOrDefault(f => f.ChatId == targetChatId);

            if (draggedFav == null || targetFav == null)
            {
                _logger.Warning("즐겨찾기 순서 변경 실패: 아이템을 찾을 수 없음 (dragged: {DraggedId}, target: {TargetId})",
                    draggedChatId, targetChatId);
                return;
            }

            int oldIndex = favorites.IndexOf(draggedFav);
            int newIndex = favorites.IndexOf(targetFav);

            if (oldIndex == newIndex)
                return;

            // 컬렉션에서 재정렬
            favorites.RemoveAt(oldIndex);
            favorites.Insert(newIndex, draggedFav);

            // SortOrder 재할당
            for (int i = 0; i < favorites.Count; i++)
            {
                favorites[i].SortOrder = i;
            }

            await dbContext.SaveChangesAsync();

            _logger.Information("즐겨찾기 순서 변경: {Name} ({OldIndex} → {NewIndex})",
                draggedFav.DisplayName, oldIndex, newIndex);

            // UI 컬렉션 업데이트
            var draggedItem = FavoriteChats.FirstOrDefault(c => c.Id == draggedChatId);
            if (draggedItem != null)
            {
                int uiOldIndex = FavoriteChats.IndexOf(draggedItem);
                if (uiOldIndex >= 0 && newIndex != uiOldIndex)
                {
                    // Move 메서드로 UI 컬렉션 순서 변경
                    FavoriteChats.Move(uiOldIndex, newIndex > uiOldIndex ? newIndex : newIndex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "즐겨찾기 순서 변경 실패: {DraggedId} → {TargetId}", draggedChatId, targetChatId);
        }
    }

    #region 리액션/스레드/멘션/미팅 관련 메서드

    /// <summary>
    /// 메시지에 리액션 추가
    /// </summary>
    [RelayCommand]
    public async Task AddReactionAsync(string parameter)
    {
        // parameter 형식: "messageId|reaction"
        var parts = parameter?.Split('|');
        if (parts?.Length != 2) return;

        var messageId = parts[0];
        var reaction = parts[1];

        if (SelectedChat == null || string.IsNullOrEmpty(messageId)) return;

        try
        {
            await _teamsService.AddReactionAsync(SelectedChat.Id, messageId, reaction);

            // UI 업데이트: 해당 메시지의 리액션 목록 갱신
            var message = Messages.FirstOrDefault(m => m.Id == messageId);
            if (message != null)
            {
                var existing = message.Reactions.FirstOrDefault(r => r.Reaction == reaction);
                if (existing != null)
                {
                    existing.Count++;
                    existing.IsMine = true;
                }
                else
                {
                    message.Reactions.Add(new MessageReaction { Reaction = reaction, Count = 1, IsMine = true });
                }
                message.OnPropertyChanged(nameof(message.HasReactions));
            }

            _logger.Debug("리액션 추가: {MessageId} - {Reaction}", messageId, reaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "리액션 추가 실패: {MessageId}", messageId);
        }
    }

    /// <summary>
    /// 메시지에서 리액션 제거
    /// </summary>
    [RelayCommand]
    public async Task RemoveReactionAsync(string parameter)
    {
        var parts = parameter?.Split('|');
        if (parts?.Length != 2) return;

        var messageId = parts[0];
        var reaction = parts[1];

        if (SelectedChat == null || string.IsNullOrEmpty(messageId)) return;

        try
        {
            await _teamsService.RemoveReactionAsync(SelectedChat.Id, messageId, reaction);

            var message = Messages.FirstOrDefault(m => m.Id == messageId);
            if (message != null)
            {
                var existing = message.Reactions.FirstOrDefault(r => r.Reaction == reaction);
                if (existing != null)
                {
                    existing.Count--;
                    existing.IsMine = false;
                    if (existing.Count <= 0)
                        message.Reactions.Remove(existing);
                }
                message.OnPropertyChanged(nameof(message.HasReactions));
            }

            _logger.Debug("리액션 제거: {MessageId} - {Reaction}", messageId, reaction);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "리액션 제거 실패: {MessageId}", messageId);
        }
    }

    /// <summary>
    /// 스레드 열기 (답글 패널)
    /// </summary>
    [RelayCommand]
    public async Task OpenThreadAsync(MessageItemViewModel message)
    {
        if (message == null || SelectedChat == null) return;

        ThreadParentMessage = message;
        ThreadReplies.Clear();
        ThreadReplyText = string.Empty;
        IsThreadPanelOpen = true;

        try
        {
            var replies = await _teamsService.GetChatMessageRepliesAsync(SelectedChat.Id, message.Id);
            var myId = await _teamsService.GetCachedCurrentUserIdAsync();

            foreach (var reply in replies.OrderBy(r => r.CreatedDateTime))
            {
                var fromUserId = reply.From?.User?.Id ?? string.Empty;
                ThreadReplies.Add(new MessageItemViewModel
                {
                    Id = reply.Id ?? string.Empty,
                    Content = StripHtml(reply.Body?.Content),
                    FromUser = reply.From?.User?.DisplayName ?? "Unknown",
                    FromUserId = fromUserId,
                    CreatedDateTime = reply.CreatedDateTime?.ToLocalTime().DateTime,
                    IsFromMe = fromUserId == myId,
                    FromUserPhoto = !string.IsNullOrEmpty(fromUserId) ? _teamsService.GetCachedUserPhoto(fromUserId) : null
                });
            }

            _logger.Debug("스레드 열기: {MessageId}, 답글 {Count}개", message.Id, ThreadReplies.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "스레드 답글 로드 실패: {MessageId}", message.Id);
        }
    }

    /// <summary>
    /// 스레드 닫기
    /// </summary>
    [RelayCommand]
    public void CloseThread()
    {
        IsThreadPanelOpen = false;
        ThreadParentMessage = null;
        ThreadReplies.Clear();
    }

    /// <summary>
    /// 스레드 답글 전송
    /// </summary>
    [RelayCommand]
    public async Task SendThreadReplyAsync()
    {
        if (ThreadParentMessage == null || SelectedChat == null || string.IsNullOrWhiteSpace(ThreadReplyText))
            return;

        var replyText = ThreadReplyText.Trim();
        ThreadReplyText = string.Empty;

        try
        {
            var sent = await _teamsService.SendChatReplyAsync(SelectedChat.Id, ThreadParentMessage.Id, replyText);
            if (sent != null)
            {
                ThreadReplies.Add(new MessageItemViewModel
                {
                    Id = sent.Id ?? Guid.NewGuid().ToString(),
                    Content = StripHtml(sent.Body?.Content),
                    FromUser = sent.From?.User?.DisplayName ?? "나",
                    CreatedDateTime = sent.CreatedDateTime?.ToLocalTime().DateTime ?? DateTime.Now,
                    IsFromMe = true
                });

                // 원본 메시지 답글 수 갱신
                ThreadParentMessage.ReplyCount = ThreadReplies.Count;
            }

            _logger.Information("스레드 답글 전송: {MessageId}", ThreadParentMessage.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "스레드 답글 전송 실패");
        }
    }

    /// <summary>
    /// 파일 공유 (드래그 & 드롭)
    /// </summary>
    [RelayCommand]
    public async Task ShareFileAsync(string filePath)
    {
        if (SelectedChat == null || string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _teamsService.ShareFileToChatAsync(SelectedChat.Id, filePath);
            _logger.Information("파일 공유 완료: {FilePath} → {ChatName}", filePath, SelectedChat.DisplayName);

            // 메시지 새로고침
            await LoadMessagesAsync(SelectedChat.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "파일 공유 실패: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Teams 미팅 생성
    /// </summary>
    [RelayCommand]
    public async Task CreateMeetingAsync()
    {
        // MeetingScheduleDialog에서 호출됨 — 결과 처리는 다이얼로그에서
        _logger.Debug("미팅 생성 요청");
    }

    /// <summary>
    /// @멘션 필터 업데이트
    /// </summary>
    public void UpdateMentionFilter(string filter)
    {
        MentionFilter = filter;
        MentionCandidates.Clear();

        if (string.IsNullOrEmpty(filter))
        {
            foreach (var member in _chatMembers.Take(10))
                MentionCandidates.Add(member);
        }
        else
        {
            var filtered = _chatMembers
                .Where(m => m.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(10);
            foreach (var member in filtered)
                MentionCandidates.Add(member);
        }

        IsMentionPopupOpen = MentionCandidates.Count > 0;
    }

    /// <summary>
    /// 채팅 멤버 목록 로드 (멘션용)
    /// </summary>
    public async Task LoadChatMembersAsync(string chatId)
    {
        _chatMembers.Clear();
        try
        {
            var client = _teamsService;
            var members = await _teamsService.GetChatMembersAsync(chatId);
            var myId = await _teamsService.GetCachedCurrentUserIdAsync();

            foreach (var member in members)
            {
                var aadMember = member as Microsoft.Graph.Models.AadUserConversationMember;
                if (aadMember?.UserId != null && aadMember.UserId != myId)
                {
                    _chatMembers.Add(new MentionCandidate
                    {
                        UserId = aadMember.UserId,
                        DisplayName = aadMember.DisplayName ?? "Unknown",
                        PhotoBase64 = _teamsService.GetCachedUserPhoto(aadMember.UserId)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("채팅 멤버 로드 실패: {Error}", ex.Message);
        }
    }

    #endregion

    #region 팀/채널 관련 메서드

    /// <summary>
    /// 팀 목록 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadTeamsAsync()
    {
        try
        {
            TeamsLoadError = null; // 오류 초기화
            Serilog.Log.Information("[TeamsViewModel] ========== LoadTeamsAsync 시작 ==========");
            Utils.Log4.Info("[TeamsViewModel] ========== LoadTeamsAsync 시작 ==========");

            Serilog.Log.Information("[TeamsViewModel] GetMyTeamsAsync 호출 전...");
            var (teams, errorMessage) = await _teamsService.GetMyTeamsWithErrorAsync();

            if (!string.IsNullOrEmpty(errorMessage))
            {
                TeamsLoadError = errorMessage;
                Serilog.Log.Warning("[TeamsViewModel] 팀 목록 로드 오류: {Error}", errorMessage);
                Utils.Log4.Info($"[TeamsViewModel] 팀 목록 로드 오류: {errorMessage}");
                OnPropertyChanged(nameof(HasTeams));
                return;
            }

            var teamsList = teams.ToList();
            Serilog.Log.Information("[TeamsViewModel] GetMyTeamsAsync 결과: {Count}개 팀", teamsList.Count);
            Utils.Log4.Info($"[TeamsViewModel] GetMyTeamsAsync 결과: {teamsList.Count}개 팀");

            Teams.Clear();
            foreach (var team in teamsList)
            {
                Serilog.Log.Information("[TeamsViewModel] 팀 처리 중: {TeamName}", team.DisplayName);
                Utils.Log4.Info($"[TeamsViewModel] 팀 처리 중: {team.DisplayName}");

                var teamItem = new TeamItemViewModel
                {
                    Id = team.Id ?? string.Empty,
                    DisplayName = team.DisplayName ?? "(이름 없음)",
                    Description = team.Description ?? string.Empty,
                    IsArchived = team.IsArchived ?? false
                };

                // 팀의 채널 로드
                var channels = await _teamsService.GetChannelsAsync(team.Id ?? string.Empty);
                Serilog.Log.Information("[TeamsViewModel] 팀 '{TeamName}'의 채널: {ChannelCount}개", team.DisplayName, channels.Count());
                Utils.Log4.Info($"[TeamsViewModel] 팀 '{team.DisplayName}'의 채널: {channels.Count()}개");

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

            Serilog.Log.Information("[TeamsViewModel] 팀 목록 로드 완료: {Count}개", Teams.Count);
            Utils.Log4.Info($"[TeamsViewModel] 팀 목록 로드 완료: {Teams.Count}개");
            OnPropertyChanged(nameof(HasTeams));
        }
        catch (Exception ex)
        {
            TeamsLoadError = $"팀 목록 로드 실패: {ex.Message}";
            Serilog.Log.Error(ex, "[TeamsViewModel] LoadTeamsAsync 오류");
            Utils.Log4.Error($"[TeamsViewModel] LoadTeamsAsync 오류: {ex.Message}");
            Utils.Log4.Error($"[TeamsViewModel] StackTrace: {ex.StackTrace}");
            OnPropertyChanged(nameof(HasTeams));
        }
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

        // Hub 패턴: 현재 탭의 Sub-VM 초기화
        if (!string.IsNullOrEmpty(channel.TeamId) && !string.IsNullOrEmpty(channel.Id))
        {
            var key = $"{channel.TeamId}_{channel.Id}";
            await InitializeCurrentTabVmAsync(key, channel.TeamId, channel.Id);
        }
    }

    /// <summary>
    /// 현재 활성 탭의 Sub-ViewModel을 초기화 (Lazy 캐시 패턴)
    /// </summary>
    public async Task InitializeCurrentTabVmAsync(string key, string teamId, string channelId)
    {
        switch (CurrentChannelTab)
        {
            case "posts":
                if (!_postsVmCache.TryGetValue(key, out var postsVm))
                {
                    postsVm = new ChannelPostsViewModel(_teamsService, teamId, channelId);
                    _postsVmCache[key] = postsVm;
                    _log.Debug("ChannelPostsViewModel 신규 생성: {Key}", key);
                }
                ChannelPostsVm = postsVm;
                await postsVm.LoadAsync();
                break;

            case "files":
                if (!_filesVmCache.TryGetValue(key, out var filesVm))
                {
                    filesVm = new ChannelFilesViewModel(_teamsService, teamId, channelId);
                    _filesVmCache[key] = filesVm;
                    _log.Debug("ChannelFilesViewModel 신규 생성: {Key}", key);
                }
                ChannelFilesVm = filesVm;
                await filesVm.LoadAsync();
                break;

            case "settings":
                if (!_settingsVmCache.TryGetValue(key, out var settingsVm))
                {
                    settingsVm = new ChannelSettingsViewModel(_teamsService, _dbContextFactory);
                    _settingsVmCache[key] = settingsVm;
                    _log.Debug("ChannelSettingsViewModel 신규 생성: {Key}", key);
                }
                ChannelSettingsVm = settingsVm;
                if (SelectedChannel != null)
                    await settingsVm.LoadSettingsAsync(SelectedChannel);
                break;

            default:
                _log.Debug("알 수 없는 탭: {Tab}", CurrentChannelTab);
                break;
        }
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
                    CreatedDateTime = msg.CreatedDateTime?.ToLocalTime().DateTime ?? DateTime.Now,
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
    /// 생성자 - MemberPhotos 컬렉션 변경 감지 설정
    /// </summary>
    public ChatItemViewModel()
    {
        // MemberPhotos 컬렉션이 변경될 때 HasGroupPhotos 속성 변경 알림
        _memberPhotos.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasGroupPhotos));
            OnPropertyChanged(nameof(GroupPhotoCount));
        };
    }

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
    /// LastUpdatedDateTime이 변경될 때 호출되는 partial 메서드
    /// </summary>
    partial void OnLastUpdatedDateTimeChanged(DateTime? value)
    {
        // LastUpdatedDisplay 값 갱신
        RefreshLastUpdatedDisplay();
    }

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
    /// 프로필 사진 (Base64) - 1:1 채팅용
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    [NotifyPropertyChangedFor(nameof(IsOneOnOneWithPhoto))]
    private string? _photoBase64;

    /// <summary>
    /// 그룹 채팅 멤버 사진 목록 (최대 4명)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroupPhotos))]
    [NotifyPropertyChangedFor(nameof(GroupPhotoCount))]
    [NotifyPropertyChangedFor(nameof(IsGroupChat))]
    private ObservableCollection<MemberPhotoInfo> _memberPhotos = new();

    /// <summary>
    /// 즐겨찾기 여부
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPinned))]
    private bool _isFavorite;

    /// <summary>
    /// 멤버 수
    /// </summary>
    [ObservableProperty]
    private int _memberCount;

    /// <summary>
    /// 읽지 않은 메시지가 있는지 여부
    /// </summary>
    public bool HasUnread => UnreadCount > 0;

    /// <summary>
    /// 프로필 사진이 있는지 여부 (1:1 채팅)
    /// </summary>
    public bool HasPhoto => !string.IsNullOrEmpty(PhotoBase64);

    /// <summary>
    /// 그룹 사진이 있는지 여부
    /// </summary>
    public bool HasGroupPhotos => MemberPhotos.Count > 0;

    /// <summary>
    /// 그룹 사진 개수
    /// </summary>
    public int GroupPhotoCount => MemberPhotos.Count;

    /// <summary>
    /// 그룹 채팅인지 여부
    /// </summary>
    public bool IsGroupChat => ChatType == "Group" || ChatType == "Meeting";

    /// <summary>
    /// 1:1 채팅이면서 사진이 있는지 여부
    /// </summary>
    public bool IsOneOnOneWithPhoto => !IsGroupChat && HasPhoto;

    /// <summary>
    /// 즐겨찾기 상태 (IsFavorite 와 동일)
    /// </summary>
    public bool IsPinned => IsFavorite;

    /// <summary>
    /// 마지막 업데이트 시간 표시 문자열 (backing field)
    /// </summary>
    private string _lastUpdatedDisplay = string.Empty;

    /// <summary>
    /// 마지막 업데이트 시간 표시 문자열
    /// </summary>
    public string LastUpdatedDisplay
    {
        get => _lastUpdatedDisplay;
        private set => SetProperty(ref _lastUpdatedDisplay, value);
    }

    /// <summary>
    /// LastUpdatedDisplay 값 계산 및 설정
    /// </summary>
    private string CalculateLastUpdatedDisplay()
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

        return LastUpdatedDateTime.Value.ToString("MM-dd");
    }

    /// <summary>
    /// LastUpdatedDisplay 갱신
    /// UI 바인딩 갱신을 위해 사용
    /// </summary>
    public void RefreshLastUpdatedDisplay()
    {
        LastUpdatedDisplay = CalculateLastUpdatedDisplay();
    }
}

/// <summary>
/// 채팅 멤버 사진 정보
/// </summary>
public partial class MemberPhotoInfo : ObservableObject
{
    /// <summary>
    /// 사용자 ID
    /// </summary>
    [ObservableProperty]
    private string _userId = string.Empty;

    /// <summary>
    /// 표시 이름
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// 프로필 사진 (Base64)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private string? _photoBase64;

    /// <summary>
    /// 사진이 있는지 여부
    /// </summary>
    public bool HasPhoto => !string.IsNullOrEmpty(PhotoBase64);

    /// <summary>
    /// 이니셜 (사진 없을 때 표시)
    /// </summary>
    public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpper();
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
    /// 발신자 ID
    /// </summary>
    [ObservableProperty]
    private string _fromUserId = string.Empty;

    /// <summary>
    /// 발신자 사진 (Base64)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFromUserPhoto))]
    private string? _fromUserPhoto;

    /// <summary>
    /// 발신자 사진이 있는지 여부
    /// </summary>
    public bool HasFromUserPhoto => !string.IsNullOrEmpty(FromUserPhoto);

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

    /// <summary>
    /// 리액션 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<MessageReaction> _reactions = new();

    /// <summary>
    /// 리액션이 있는지 여부
    /// </summary>
    public bool HasReactions => Reactions.Count > 0;

    /// <summary>
    /// 답글 수
    /// </summary>
    [ObservableProperty]
    private int _replyCount;

    /// <summary>
    /// 답글이 있는지 여부
    /// </summary>
    public bool HasReplies => ReplyCount > 0;

    /// <summary>
    /// 리액션 패널 표시 여부 (호버 시)
    /// </summary>
    [ObservableProperty]
    private bool _showReactionBar;

    /// <summary>
    /// PropertyChanged 호출 래퍼 (외부에서 접근용)
    /// </summary>
    public new void OnPropertyChanged(string propertyName)
    {
        base.OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// 날짜 분리선 표시 여부 (날짜가 바뀌는 첫 메시지에만 true)
    /// </summary>
    [ObservableProperty]
    private bool _showDateSeparator;

    /// <summary>
    /// 날짜 분리선에 표시할 날짜 문자열
    /// </summary>
    public string DateSeparatorText
    {
        get
        {
            if (!CreatedDateTime.HasValue)
                return string.Empty;

            var today = DateTime.Today;
            var messageDate = CreatedDateTime.Value.Date;

            if (messageDate == today)
                return "오늘";
            if (messageDate == today.AddDays(-1))
                return "어제";

            // "1월 8일 수요일" 형식
            var dayOfWeek = messageDate.ToString("dddd", new System.Globalization.CultureInfo("ko-KR"));
            return $"{messageDate.Month}월 {messageDate.Day}일 {dayOfWeek}";
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

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private string _reactions = string.Empty;

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

    [ObservableProperty]
    private string _createdBy = string.Empty;

    [ObservableProperty]
    private string _driveId = string.Empty;

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

/// <summary>
/// 즐겨찾기 채널 ViewModel (채널 바로가기용)
/// </summary>
public partial class FavoriteChannelViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _channelId = string.Empty;

    [ObservableProperty]
    private string _teamId = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _teamName = string.Empty;

    [ObservableProperty]
    private DateTime? _lastActivityDateTime;

    [ObservableProperty]
    private int _sortOrder;

    /// <summary>
    /// 마지막 활동 날짜 표시 (01-12 형태)
    /// </summary>
    public string LastActivityDisplay
    {
        get
        {
            if (LastActivityDateTime == null) return string.Empty;
            var date = LastActivityDateTime.Value;
            if (date.Date == DateTime.Today)
                return "오늘";
            if (date.Year == DateTime.Now.Year)
                return date.ToString("MM-dd");
            return date.ToString("yyyy-MM-dd");
        }
    }
}

/// <summary>
/// 메시지 리액션 모델
/// </summary>
public partial class MessageReaction : ObservableObject
{
    [ObservableProperty]
    private string _reaction = string.Empty;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isMine;
}

/// <summary>
/// @멘션 후보 모델
/// </summary>
public partial class MentionCandidate : ObservableObject
{
    [ObservableProperty]
    private string _userId = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _photoBase64;

    public bool HasPhoto => !string.IsNullOrEmpty(PhotoBase64);

    public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName[..1].ToUpper();
}

#endregion
