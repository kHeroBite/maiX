using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mAIx.Data;
using mAIx.Models;
using Serilog;

namespace mAIx.Services.Graph;

/// <summary>
/// Microsoft Teams 메시지 연동 서비스
/// </summary>
public class GraphTeamsService
{
    private readonly GraphAuthService _authService;
    private readonly IDbContextFactory<mAIxDbContext> _dbContextFactory;
    private readonly ILogger _logger;
    private string? _cachedCurrentUserId;

    // 사용자 사진 메모리 캐시 (userId -> Base64 photo, null이면 사진 없음)
    private readonly Dictionary<string, string?> _userPhotoCache = new();
    private readonly object _photoCacheLock = new();

    // 로컬 파일 캐시 경로
    private readonly string _photoCacheDir;

    public GraphTeamsService(GraphAuthService authService, IDbContextFactory<mAIxDbContext> dbContextFactory)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = Log.ForContext<GraphTeamsService>();

        // 사진 캐시 디렉토리 초기화
        _photoCacheDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "mAIx", "cache", "photos");
        if (!System.IO.Directory.Exists(_photoCacheDir))
        {
            System.IO.Directory.CreateDirectory(_photoCacheDir);
        }
    }

    /// <summary>
    /// 현재 사용자 ID 가져오기 (캐시됨)
    /// </summary>
    public async Task<string?> GetCachedCurrentUserIdAsync()
    {
        if (_cachedCurrentUserId == null)
        {
            _cachedCurrentUserId = await GetCurrentUserIdAsync();
        }
        return _cachedCurrentUserId;
    }

    /// <summary>
    /// 채팅방 목록 조회
    /// </summary>
    /// <returns>채팅방 목록</returns>
    public async Task<IEnumerable<Chat>> GetChatsAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Chats.GetAsync(config =>
            {
                config.QueryParameters.Top = 50;
                config.QueryParameters.Expand = new[] { "members", "lastMessagePreview" };
            });

            _logger.Debug("채팅방 {Count}개 조회", response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Chat>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채팅방 목록 조회 실패");
            throw;
        }
    }

    /// <summary>
    /// 특정 채팅방의 메시지 목록 조회
    /// </summary>
    /// <param name="chatId">채팅방 ID</param>
    /// <param name="top">조회할 메시지 수</param>
    /// <returns>메시지 목록</returns>
    public async Task<IEnumerable<ChatMessage>> GetChatMessagesAsync(string chatId, int top = 50)
    {
        if (string.IsNullOrEmpty(chatId))
            throw new ArgumentNullException(nameof(chatId));

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Chats[chatId].Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
            });

            _logger.Debug("채팅방 {ChatId} 메시지 {Count}개 조회", chatId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<ChatMessage>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채팅 메시지 조회 실패: ChatId={ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// 채팅방의 실제 마지막 일반 메시지 정보 조회 (시스템 이벤트 제외)
    /// </summary>
    /// <param name="chatId">채팅방 ID</param>
    /// <returns>(마지막 메시지 시간, 메시지 미리보기) 또는 (null, null)</returns>
    public async Task<(DateTime? LastMessageTime, string? Preview)> GetLastRealMessageAsync(string chatId)
    {
        if (string.IsNullOrEmpty(chatId))
            return (null, null);

        try
        {
            var client = _authService.GetGraphClient();
            // 최근 10개 메시지를 가져와서 시스템 이벤트가 아닌 첫 번째 메시지 찾기
            var response = await client.Me.Chats[chatId].Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = 10;
                config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
            });

            if (response?.Value == null || response.Value.Count == 0)
                return (null, null);

            // 시스템 이벤트(EventDetail)가 아니고, 빈 본문이 아닌 첫 번째 메시지 찾기
            var realMessage = response.Value.FirstOrDefault(m =>
                m.EventDetail == null &&
                m.MessageType != ChatMessageType.SystemEventMessage &&
                !string.IsNullOrWhiteSpace(m.Body?.Content) &&
                m.Body?.Content != "<systemEventMessage/>");

            if (realMessage == null || !realMessage.CreatedDateTime.HasValue)
            {
                _logger.Warning("채팅방 {ChatId}: 유효한 메시지를 찾지 못함", chatId);
                return (null, null);
            }

            // UTC 시간을 로컬 시간으로 변환하고 Kind를 Local로 명시적 설정
            var utcTime = realMessage.CreatedDateTime.Value.UtcDateTime;
            var lastTime = DateTime.SpecifyKind(utcTime.ToLocalTime(), DateTimeKind.Local);
            var preview = realMessage.Body?.Content;

            // HTML 태그 제거
            if (!string.IsNullOrEmpty(preview))
            {
                preview = System.Text.RegularExpressions.Regex.Replace(preview, "<[^>]*>", "");
                preview = System.Net.WebUtility.HtmlDecode(preview)?.Trim();
            }

            _logger.Debug("채팅방 {ChatId} 마지막 메시지: {Time}", chatId, lastTime);
            return (lastTime, preview);
        }
        catch (Exception ex)
        {
            _logger.Debug("채팅방 {ChatId} 마지막 메시지 조회 실패: {Error}", chatId, ex.Message);
            return (null, null);
        }
    }

    /// <summary>
    /// 읽지 않은 메시지 수 확인
    /// </summary>
    /// <returns>읽지 않은 메시지 수</returns>
    public async Task<int> GetUnreadCountAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var chats = await client.Me.Chats.GetAsync(config =>
            {
                config.QueryParameters.Filter = "unreadMessagesCount gt 0";
            });

            // 모든 채팅방의 읽지 않은 메시지 수 합산
            // Note: Microsoft Graph에서 unreadMessagesCount는 chats endpoint에서 개별적으로 가져와야 함
            // 현재는 읽지 않은 메시지가 있는 채팅방 수를 반환
            var unreadCount = chats?.Value?.Count ?? 0;

            _logger.Debug("읽지 않은 채팅방: {Count}개", unreadCount);
            return unreadCount;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "읽지 않은 메시지 수 조회 실패");
            return 0;
        }
    }

    /// <summary>
    /// 메시지 검색
    /// </summary>
    /// <param name="searchQuery">검색어</param>
    /// <returns>검색된 메시지 목록</returns>
    public async Task<IEnumerable<ChatMessage>> SearchMessagesAsync(string searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return new List<ChatMessage>();

        try
        {
            var allMessages = new List<ChatMessage>();
            var chats = await GetChatsAsync();

            foreach (var chat in chats)
            {
                if (string.IsNullOrEmpty(chat.Id))
                    continue;

                var messages = await GetChatMessagesAsync(chat.Id, 100);

                // 클라이언트측 필터링 (Graph API는 메시지 내용 검색을 직접 지원하지 않음)
                var matchedMessages = messages.Where(m =>
                    m.Body?.Content?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true ||
                    m.Subject?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true);

                allMessages.AddRange(matchedMessages);
            }

            _logger.Debug("메시지 검색 '{Query}': {Count}개 발견", searchQuery, allMessages.Count);
            return allMessages;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "메시지 검색 실패: Query={Query}", searchQuery);
            throw;
        }
    }

    /// <summary>
    /// Teams 메시지를 로컬 DB에 저장
    /// </summary>
    /// <param name="chatMessage">Graph API 메시지</param>
    /// <param name="chatId">채팅방 ID</param>
    /// <param name="linkedEmailId">연결된 이메일 ID (선택)</param>
    /// <returns>저장된 TeamsMessage</returns>
    public async Task<TeamsMessage> SaveMessageAsync(ChatMessage chatMessage, string chatId, int? linkedEmailId = null)
    {
        if (chatMessage == null)
            throw new ArgumentNullException(nameof(chatMessage));

        try
        {
            var messageId = chatMessage.Id ?? Guid.NewGuid().ToString();

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            // 기존 메시지 확인
            var existingMessage = await dbContext.TeamsMessages
                .FirstOrDefaultAsync(m => m.Id == messageId);

            if (existingMessage != null)
            {
                // 업데이트
                existingMessage.Content = chatMessage.Body?.Content;
                existingMessage.LinkedEmailId = linkedEmailId ?? existingMessage.LinkedEmailId;
            }
            else
            {
                // 새로 생성
                var teamsMessage = new TeamsMessage
                {
                    Id = messageId,
                    ChatId = chatId,
                    Content = chatMessage.Body?.Content,
                    FromUser = chatMessage.From?.User?.DisplayName ?? chatMessage.From?.User?.Id,
                    LinkedEmailId = linkedEmailId,
                    CreatedDateTime = chatMessage.CreatedDateTime?.DateTime
                };

                dbContext.TeamsMessages.Add(teamsMessage);
                existingMessage = teamsMessage;
            }

            await dbContext.SaveChangesAsync();
            _logger.Debug("Teams 메시지 저장: {MessageId}", messageId);

            return existingMessage;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Teams 메시지 저장 실패");
            throw;
        }
    }

    /// <summary>
    /// 채팅방별 메시지 동기화
    /// </summary>
    /// <param name="chatId">채팅방 ID</param>
    /// <param name="count">동기화할 메시지 수</param>
    /// <returns>동기화된 메시지 수</returns>
    public async Task<int> SyncMessagesAsync(string chatId, int count = 50)
    {
        if (string.IsNullOrEmpty(chatId))
            throw new ArgumentNullException(nameof(chatId));

        try
        {
            var messages = await GetChatMessagesAsync(chatId, count);
            var syncedCount = 0;

            foreach (var message in messages)
            {
                await SaveMessageAsync(message, chatId);
                syncedCount++;
            }

            _logger.Information("채팅방 {ChatId} 메시지 {Count}개 동기화", chatId, syncedCount);
            return syncedCount;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "메시지 동기화 실패: ChatId={ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// 이메일과 연결된 Teams 메시지 조회
    /// </summary>
    /// <param name="emailId">이메일 ID</param>
    /// <returns>연결된 Teams 메시지 목록</returns>
    public async Task<IEnumerable<TeamsMessage>> GetLinkedMessagesAsync(int emailId)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.TeamsMessages
            .Where(m => m.LinkedEmailId == emailId)
            .OrderByDescending(m => m.CreatedDateTime)
            .ToListAsync();
    }

    /// <summary>
    /// 채팅 메시지 전송
    /// </summary>
    /// <param name="chatId">채팅방 ID</param>
    /// <param name="content">메시지 내용 (HTML 지원)</param>
    /// <returns>전송된 메시지</returns>
    public async Task<ChatMessage?> SendMessageAsync(string chatId, string content)
    {
        if (string.IsNullOrEmpty(chatId))
            throw new ArgumentNullException(nameof(chatId));
        if (string.IsNullOrEmpty(content))
            throw new ArgumentNullException(nameof(content));

        try
        {
            var client = _authService.GetGraphClient();

            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = content
                }
            };

            var response = await client.Me.Chats[chatId].Messages.PostAsync(chatMessage);

            _logger.Information("채팅 메시지 전송 성공: ChatId={ChatId}", chatId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채팅 메시지 전송 실패: ChatId={ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// 채팅방의 최신 메시지 로드 (Delta Query 사용)
    /// </summary>
    /// <param name="chatId">채팅방 ID</param>
    /// <param name="sinceDateTime">이 시간 이후의 메시지만</param>
    /// <returns>새 메시지 목록</returns>
    public async Task<IEnumerable<ChatMessage>> GetNewMessagesAsync(string chatId, DateTime? sinceDateTime = null)
    {
        if (string.IsNullOrEmpty(chatId))
            throw new ArgumentNullException(nameof(chatId));

        try
        {
            var client = _authService.GetGraphClient();

            // 기본값: 5분 전부터
            var since = sinceDateTime ?? DateTime.UtcNow.AddMinutes(-5);
            var filter = $"createdDateTime gt {since:yyyy-MM-ddTHH:mm:ssZ}";

            var response = await client.Me.Chats[chatId].Messages.GetAsync(config =>
            {
                config.QueryParameters.Filter = filter;
                config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
                config.QueryParameters.Top = 50;
            });

            _logger.Debug("채팅방 {ChatId} 새 메시지 {Count}개 조회", chatId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<ChatMessage>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "새 메시지 조회 실패: ChatId={ChatId}", chatId);
            return new List<ChatMessage>();
        }
    }

    /// <summary>
    /// 채팅방의 마지막 메시지 가져오기
    /// </summary>
    /// <param name="chatId">채팅방 ID</param>
    /// <returns>마지막 메시지</returns>
    public async Task<ChatMessage?> GetLastMessageAsync(string chatId)
    {
        if (string.IsNullOrEmpty(chatId))
            return null;

        try
        {
            var messages = await GetChatMessagesAsync(chatId, 1);
            return messages.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "마지막 메시지 조회 실패: ChatId={ChatId}", chatId);
            return null;
        }
    }

    /// <summary>
    /// 채팅방 제목/이름 가져오기 (비동기)
    /// </summary>
    /// <param name="chat">채팅 객체</param>
    /// <returns>채팅방 이름</returns>
    public async Task<string> GetChatDisplayNameAsync(Chat chat)
    {
        if (chat == null)
            return "Unknown";

        // 그룹 채팅인 경우 topic 사용
        if (!string.IsNullOrEmpty(chat.Topic))
            return chat.Topic;

        // 1:1 채팅인 경우 상대방 이름 사용
        if (chat.ChatType == ChatType.OneOnOne && chat.Members?.Count >= 2)
        {
            var currentUserId = await GetCachedCurrentUserIdAsync();
            var otherMember = chat.Members
                .OfType<AadUserConversationMember>()
                .FirstOrDefault(m => !string.IsNullOrEmpty(m.UserId) && m.UserId != currentUserId);

            return otherMember?.DisplayName ?? "Direct Chat";
        }

        // 그룹 채팅이지만 topic이 없는 경우 - 본인 제외한 멤버 이름
        if (chat.ChatType == ChatType.Group)
        {
            var currentUserId = await GetCachedCurrentUserIdAsync();
            var memberNames = chat.Members?
                .OfType<AadUserConversationMember>()
                .Where(m => m.UserId != currentUserId)
                .Take(3)
                .Select(m => m.DisplayName)
                .Where(n => !string.IsNullOrEmpty(n));

            if (memberNames?.Any() == true)
                return string.Join(", ", memberNames);
        }

        return "Chat";
    }

    /// <summary>
    /// 채팅방 제목/이름 가져오기 (동기 - 캐시된 사용자 ID 사용)
    /// </summary>
    /// <param name="chat">채팅 객체</param>
    /// <returns>채팅방 이름</returns>
    public string GetChatDisplayName(Chat chat)
    {
        if (chat == null)
            return "Unknown";

        // 그룹 채팅인 경우 topic 사용
        if (!string.IsNullOrEmpty(chat.Topic))
            return chat.Topic;

        // 1:1 채팅인 경우 상대방 이름 사용
        if (chat.ChatType == ChatType.OneOnOne && chat.Members?.Count >= 2)
        {
            var otherMember = chat.Members
                .OfType<AadUserConversationMember>()
                .FirstOrDefault(m => !string.IsNullOrEmpty(m.UserId) && m.UserId != _cachedCurrentUserId);

            return otherMember?.DisplayName ?? "Direct Chat";
        }

        // 그룹 채팅이지만 topic이 없는 경우 - 본인 제외한 멤버 이름
        if (chat.ChatType == ChatType.Group)
        {
            var memberNames = chat.Members?
                .OfType<AadUserConversationMember>()
                .Where(m => m.UserId != _cachedCurrentUserId)
                .Take(3)
                .Select(m => m.DisplayName)
                .Where(n => !string.IsNullOrEmpty(n));

            if (memberNames?.Any() == true)
                return string.Join(", ", memberNames);
        }

        return "Chat";
    }

    #region Teams 팀/채널 관련 메서드

    /// <summary>
    /// 내가 속한 팀 목록 조회
    /// </summary>
    /// <returns>팀 목록</returns>
    public async Task<IEnumerable<Team>> GetMyTeamsAsync()
    {
        var (teams, _) = await GetMyTeamsWithErrorAsync();
        return teams;
    }

    /// <summary>
    /// 내가 속한 팀 목록 조회 (오류 메시지 포함)
    /// </summary>
    /// <returns>(팀 목록, 오류 메시지)</returns>
    public async Task<(IEnumerable<Team> Teams, string? ErrorMessage)> GetMyTeamsWithErrorAsync()
    {
        try
        {
            Serilog.Log.Information("[GraphTeamsService] ========== GetMyTeamsAsync 시작 ==========");
            Utils.Log4.Info("[GraphTeamsService] ========== GetMyTeamsAsync 시작 ==========");

            var client = _authService.GetGraphClient();
            Serilog.Log.Information("[GraphTeamsService] GraphClient 획득 완료");
            Utils.Log4.Info("[GraphTeamsService] GraphClient 획득 완료");

            Serilog.Log.Information("[GraphTeamsService] Graph API 호출 중... (Me.JoinedTeams)");
            var response = await client.Me.JoinedTeams.GetAsync();

            var count = response?.Value?.Count ?? 0;
            Serilog.Log.Information("[GraphTeamsService] Graph API 응답: {Count}개 팀", count);
            Utils.Log4.Info($"[GraphTeamsService] Graph API 응답: {count}개 팀");

            if (count == 0)
            {
                Serilog.Log.Warning("[GraphTeamsService] ⚠️ 팀 목록이 비어있습니다. 권한 확인 필요: Team.ReadBasic.All");
                Utils.Log4.Info("[GraphTeamsService] ⚠️ 팀 목록이 비어있습니다. 권한 확인 필요: Team.ReadBasic.All");
            }

            return (response?.Value ?? new List<Team>(), null);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx) when (odataEx.Message?.Contains("Missing scope permissions") == true)
        {
            // 권한 오류인 경우 사용자 친화적 메시지 반환
            Serilog.Log.Warning(odataEx, "[GraphTeamsService] Teams 권한 부족");
            Utils.Log4.Error($"[GraphTeamsService] Teams 권한 부족: {odataEx.Message}");

            return (new List<Team>(), "Teams 권한이 없습니다.\nAzure Portal에서 'Team.ReadBasic.All' 권한을 앱에 추가하고 관리자 동의를 받아주세요.");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[GraphTeamsService] 팀 목록 조회 실패");
            Utils.Log4.Error($"[GraphTeamsService] 팀 목록 조회 실패: {ex.Message}");
            Utils.Log4.Error($"[GraphTeamsService] StackTrace: {ex.StackTrace}");
            return (new List<Team>(), $"팀 목록 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 특정 팀의 상세 정보 조회
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <returns>팀 정보</returns>
    public async Task<Team?> GetTeamAsync(string teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return null;

        try
        {
            var client = _authService.GetGraphClient();
            var team = await client.Teams[teamId].GetAsync();
            return team;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "팀 정보 조회 실패: TeamId={TeamId}", teamId);
            return null;
        }
    }

    /// <summary>
    /// 팀의 채널 목록 조회
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <returns>채널 목록</returns>
    public async Task<IEnumerable<Channel>> GetChannelsAsync(string teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return new List<Channel>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Teams[teamId].Channels.GetAsync();

            _logger.Debug("팀 {TeamId} 채널 {Count}개 조회", teamId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Channel>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채널 목록 조회 실패: TeamId={TeamId}", teamId);
            return new List<Channel>();
        }
    }

    /// <summary>
    /// 채널의 메시지 목록 조회
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <param name="channelId">채널 ID</param>
    /// <param name="top">조회할 메시지 수</param>
    /// <returns>메시지 목록</returns>
    public async Task<IEnumerable<ChatMessage>> GetChannelMessagesAsync(string teamId, string channelId, int top = 50)
    {
        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(channelId))
            return new List<ChatMessage>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Teams[teamId].Channels[channelId].Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = top;
            });

            _logger.Debug("팀 {TeamId} 채널 {ChannelId} 메시지 {Count}개 조회", teamId, channelId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<ChatMessage>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채널 메시지 조회 실패: TeamId={TeamId}, ChannelId={ChannelId}", teamId, channelId);
            return new List<ChatMessage>();
        }
    }

    /// <summary>
    /// 채널에 메시지 전송
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <param name="channelId">채널 ID</param>
    /// <param name="content">메시지 내용 (HTML 지원)</param>
    /// <returns>전송된 메시지</returns>
    public async Task<ChatMessage?> SendChannelMessageAsync(string teamId, string channelId, string content)
    {
        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(content))
            return null;

        try
        {
            var client = _authService.GetGraphClient();

            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = content
                }
            };

            var response = await client.Teams[teamId].Channels[channelId].Messages.PostAsync(chatMessage);

            _logger.Information("채널 메시지 전송 성공: TeamId={TeamId}, ChannelId={ChannelId}", teamId, channelId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채널 메시지 전송 실패: TeamId={TeamId}, ChannelId={ChannelId}", teamId, channelId);
            return null;
        }
    }

    /// <summary>
    /// 팀의 멤버 목록 조회
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <returns>멤버 목록</returns>
    public async Task<IEnumerable<ConversationMember>> GetTeamMembersAsync(string teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return new List<ConversationMember>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Teams[teamId].Members.GetAsync();

            _logger.Debug("팀 {TeamId} 멤버 {Count}명 조회", teamId, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<ConversationMember>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "팀 멤버 조회 실패: TeamId={TeamId}", teamId);
            return new List<ConversationMember>();
        }
    }

    /// <summary>
    /// 채널의 파일 목록 조회 (SharePoint 연동)
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <param name="channelId">채널 ID</param>
    /// <returns>파일 목록</returns>
    public async Task<IEnumerable<DriveItem>> GetChannelFilesAsync(string teamId, string channelId)
    {
        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(channelId))
            return new List<DriveItem>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Teams[teamId].Channels[channelId].FilesFolder.GetAsync();

            if (response?.Id != null)
            {
                // 폴더의 자식 항목 조회
                var driveId = response.ParentReference?.DriveId;
                if (!string.IsNullOrEmpty(driveId))
                {
                    var childrenResponse = await client.Drives[driveId].Items[response.Id].Children.GetAsync();
                    return childrenResponse?.Value ?? new List<DriveItem>();
                }
            }

            return new List<DriveItem>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채널 파일 조회 실패: TeamId={TeamId}, ChannelId={ChannelId}", teamId, channelId);
            return new List<DriveItem>();
        }
    }

    /// <summary>
    /// 메시지의 답글 조회
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <param name="channelId">채널 ID</param>
    /// <param name="messageId">메시지 ID</param>
    /// <returns>답글 목록</returns>
    public async Task<IEnumerable<ChatMessage>> GetMessageRepliesAsync(string teamId, string channelId, string messageId)
    {
        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(channelId) || string.IsNullOrEmpty(messageId))
            return new List<ChatMessage>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Teams[teamId].Channels[channelId].Messages[messageId].Replies.GetAsync();

            return response?.Value ?? new List<ChatMessage>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "답글 조회 실패: MessageId={MessageId}", messageId);
            return new List<ChatMessage>();
        }
    }

    /// <summary>
    /// 메시지에 답글 작성
    /// </summary>
    /// <param name="teamId">팀 ID</param>
    /// <param name="channelId">채널 ID</param>
    /// <param name="messageId">메시지 ID</param>
    /// <param name="content">답글 내용</param>
    /// <returns>작성된 답글</returns>
    public async Task<ChatMessage?> ReplyToMessageAsync(string teamId, string channelId, string messageId, string content)
    {
        if (string.IsNullOrEmpty(teamId) || string.IsNullOrEmpty(channelId) ||
            string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(content))
            return null;

        try
        {
            var client = _authService.GetGraphClient();

            var reply = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = content
                }
            };

            var response = await client.Teams[teamId].Channels[channelId].Messages[messageId].Replies.PostAsync(reply);

            _logger.Information("답글 작성 성공: MessageId={MessageId}", messageId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "답글 작성 실패: MessageId={MessageId}", messageId);
            return null;
        }
    }

    #endregion

    #region 리액션/스레드/파일공유/미팅/멤버

    /// <summary>
    /// 메시지에 리액션 추가 (채팅)
    /// </summary>
    public async Task AddReactionAsync(string chatId, string messageId, string reactionType)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(messageId)) return;

        try
        {
            var client = _authService.GetGraphClient();
            var reaction = new ChatMessageReaction
            {
                ReactionType = reactionType,
                CreatedDateTime = DateTimeOffset.UtcNow
            };

            await client.Me.Chats[chatId].Messages[messageId]
                .SetReaction.PostAsync(new Microsoft.Graph.Me.Chats.Item.Messages.Item.SetReaction.SetReactionPostRequestBody
                {
                    ReactionType = reactionType
                });

            _logger.Debug("리액션 추가: ChatId={ChatId}, MessageId={MessageId}, Reaction={Reaction}", chatId, messageId, reactionType);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "리액션 추가 실패: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
            throw;
        }
    }

    /// <summary>
    /// 메시지에서 리액션 제거 (채팅)
    /// </summary>
    public async Task RemoveReactionAsync(string chatId, string messageId, string reactionType)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(messageId)) return;

        try
        {
            var client = _authService.GetGraphClient();
            await client.Me.Chats[chatId].Messages[messageId]
                .UnsetReaction.PostAsync(new Microsoft.Graph.Me.Chats.Item.Messages.Item.UnsetReaction.UnsetReactionPostRequestBody
                {
                    ReactionType = reactionType
                });

            _logger.Debug("리액션 제거: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "리액션 제거 실패: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
            throw;
        }
    }

    /// <summary>
    /// 채팅 메시지의 답글 조회
    /// </summary>
    public async Task<IEnumerable<ChatMessage>> GetChatMessageRepliesAsync(string chatId, string messageId)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(messageId))
            return new List<ChatMessage>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Chats[chatId].Messages[messageId].Replies.GetAsync();
            return response?.Value ?? new List<ChatMessage>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채팅 답글 조회 실패: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
            return new List<ChatMessage>();
        }
    }

    /// <summary>
    /// 채팅 메시지에 답글 전송
    /// </summary>
    public async Task<ChatMessage?> SendChatReplyAsync(string chatId, string messageId, string content)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(content))
            return null;

        try
        {
            var client = _authService.GetGraphClient();
            var reply = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = content
                }
            };

            var response = await client.Me.Chats[chatId].Messages[messageId].Replies.PostAsync(reply);
            _logger.Information("채팅 답글 전송 성공: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채팅 답글 전송 실패: ChatId={ChatId}, MessageId={MessageId}", chatId, messageId);
            return null;
        }
    }

    /// <summary>
    /// 채팅방 멤버 목록 조회
    /// </summary>
    public async Task<IEnumerable<ConversationMember>> GetChatMembersAsync(string chatId)
    {
        if (string.IsNullOrEmpty(chatId))
            return new List<ConversationMember>();

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Chats[chatId].Members.GetAsync();
            return response?.Value ?? new List<ConversationMember>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "채팅 멤버 조회 실패: ChatId={ChatId}", chatId);
            return new List<ConversationMember>();
        }
    }

    /// <summary>
    /// 채팅방에 파일 공유 (OneDrive 파일을 채팅으로 공유)
    /// </summary>
    public async Task ShareFileToChatAsync(string chatId, string filePath)
    {
        if (string.IsNullOrEmpty(chatId) || string.IsNullOrEmpty(filePath)) return;

        try
        {
            var client = _authService.GetGraphClient();

            // 드라이브 ID 조회
            var drive = await client.Me.Drive.GetAsync();
            var driveId = drive?.Id;
            if (string.IsNullOrEmpty(driveId))
            {
                _logger.Error("OneDrive ID를 가져올 수 없습니다");
                return;
            }

            // 파일을 OneDrive에 업로드
            var fileName = System.IO.Path.GetFileName(filePath);
            using var fileStream = System.IO.File.OpenRead(filePath);

            var driveItem = await client.Drives[driveId].Items["root"]
                .ItemWithPath($"MaiX Shared/{fileName}")
                .Content.PutAsync(fileStream);

            if (driveItem != null)
            {
                // 채팅에 파일 참조 메시지 전송
                var shareMessage = new ChatMessage
                {
                    Body = new ItemBody
                    {
                        ContentType = BodyType.Html,
                        Content = $"<attachment id=\"{driveItem.Id}\"></attachment>"
                    },
                    Attachments = new List<ChatMessageAttachment>
                    {
                        new ChatMessageAttachment
                        {
                            Id = driveItem.Id,
                            ContentType = "reference",
                            ContentUrl = driveItem.WebUrl,
                            Name = fileName
                        }
                    }
                };

                await client.Me.Chats[chatId].Messages.PostAsync(shareMessage);
                _logger.Information("파일 공유 성공: {FileName} → ChatId={ChatId}", fileName, chatId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "파일 공유 실패: ChatId={ChatId}, FilePath={FilePath}", chatId, filePath);
            throw;
        }
    }

    /// <summary>
    /// 온라인 미팅 생성
    /// </summary>
    public async Task<OnlineMeeting?> CreateOnlineMeetingAsync(string subject, DateTime startTime, DateTime endTime, IList<string>? attendeeEmails = null)
    {
        try
        {
            var client = _authService.GetGraphClient();

            var meeting = new OnlineMeeting
            {
                Subject = subject,
                StartDateTime = new DateTimeOffset(startTime, TimeZoneInfo.Local.GetUtcOffset(startTime)),
                EndDateTime = new DateTimeOffset(endTime, TimeZoneInfo.Local.GetUtcOffset(endTime)),
            };

            if (attendeeEmails?.Any() == true)
            {
                meeting.Participants = new MeetingParticipants
                {
                    Attendees = attendeeEmails.Select(email => new MeetingParticipantInfo
                    {
                        Identity = new IdentitySet
                        {
                            User = new Identity
                            {
                                DisplayName = email
                            }
                        },
                        Upn = email
                    }).ToList()
                };
            }

            var result = await client.Me.OnlineMeetings.PostAsync(meeting);
            _logger.Information("온라인 미팅 생성 성공: {Subject}", subject);
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "온라인 미팅 생성 실패: {Subject}", subject);
            throw;
        }
    }

    #endregion

    #region 사용자 프로필 사진

    /// <summary>
    /// 로컬 캐시에서 사진 가져오기 (즉시 반환)
    /// </summary>
    /// <param name="userId">사용자 ID</param>
    /// <returns>캐시된 Base64 사진 또는 null</returns>
    public string? GetCachedUserPhoto(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        // 1. 메모리 캐시 확인
        lock (_photoCacheLock)
        {
            if (_userPhotoCache.TryGetValue(userId, out var cachedPhoto))
            {
                return cachedPhoto;
            }
        }

        // 2. 로컬 파일 캐시 확인
        var cacheFile = GetPhotoCachePath(userId);
        if (System.IO.File.Exists(cacheFile))
        {
            try
            {
                var photo = System.IO.File.ReadAllText(cacheFile);
                // 메모리 캐시에도 저장
                lock (_photoCacheLock)
                {
                    _userPhotoCache[userId] = photo;
                }
                return photo;
            }
            catch
            {
                // 파일 읽기 실패 시 무시
            }
        }

        return null;
    }

    /// <summary>
    /// 사용자 프로필 사진 가져오기 (Base64) - 로컬 캐시 + API 조회
    /// </summary>
    /// <param name="userId">사용자 ID</param>
    /// <returns>Base64 인코딩된 사진 또는 null</returns>
    public async Task<string?> GetUserPhotoAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        // 1. 메모리 캐시 확인
        lock (_photoCacheLock)
        {
            if (_userPhotoCache.TryGetValue(userId, out var cachedPhoto))
            {
                return cachedPhoto;
            }
        }

        // 2. 로컬 파일 캐시 확인
        var cacheFile = GetPhotoCachePath(userId);
        if (System.IO.File.Exists(cacheFile))
        {
            try
            {
                var cachedPhoto = await System.IO.File.ReadAllTextAsync(cacheFile);
                // 메모리 캐시에도 저장
                lock (_photoCacheLock)
                {
                    _userPhotoCache[userId] = cachedPhoto;
                }
                return cachedPhoto;
            }
            catch
            {
                // 파일 읽기 실패 시 API에서 조회
            }
        }

        // 3. API에서 조회
        string? photo = null;
        try
        {
            var client = _authService.GetGraphClient();
            var photoStream = await client.Users[userId].Photo.Content.GetAsync();

            if (photoStream != null)
            {
                using var ms = new System.IO.MemoryStream();
                await photoStream.CopyToAsync(ms);
                var photoBytes = ms.ToArray();
                photo = Convert.ToBase64String(photoBytes);

                // 로컬 파일에 저장
                await System.IO.File.WriteAllTextAsync(cacheFile, photo);
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            // 사진이 없는 경우 무시 (404 에러)
            if (odataEx.ResponseStatusCode != 404)
            {
                _logger.Debug("사용자 {UserId} 사진 조회 실패: {Error}", userId, odataEx.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("사용자 {UserId} 사진 조회 실패: {Error}", userId, ex.Message);
        }

        // 메모리 캐시에 저장 (사진이 없는 경우도 캐시하여 반복 요청 방지)
        lock (_photoCacheLock)
        {
            _userPhotoCache[userId] = photo;
        }

        return photo;
    }

    /// <summary>
    /// 사용자 사진을 백그라운드에서 새로고침 (API 조회 후 캐시 업데이트)
    /// </summary>
    /// <param name="userId">사용자 ID</param>
    /// <returns>새로 조회된 Base64 사진 또는 null</returns>
    public async Task<string?> RefreshUserPhotoAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        string? photo = null;
        try
        {
            var client = _authService.GetGraphClient();
            var photoStream = await client.Users[userId].Photo.Content.GetAsync();

            if (photoStream != null)
            {
                using var ms = new System.IO.MemoryStream();
                await photoStream.CopyToAsync(ms);
                var photoBytes = ms.ToArray();
                photo = Convert.ToBase64String(photoBytes);

                // 로컬 파일에 저장
                var cacheFile = GetPhotoCachePath(userId);
                await System.IO.File.WriteAllTextAsync(cacheFile, photo);

                // 메모리 캐시 업데이트
                lock (_photoCacheLock)
                {
                    _userPhotoCache[userId] = photo;
                }
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            if (odataEx.ResponseStatusCode != 404)
            {
                _logger.Debug("사용자 {UserId} 사진 새로고침 실패: {Error}", userId, odataEx.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("사용자 {UserId} 사진 새로고침 실패: {Error}", userId, ex.Message);
        }

        return photo;
    }

    /// <summary>
    /// 사진 캐시 파일 경로 가져오기
    /// </summary>
    private string GetPhotoCachePath(string userId)
    {
        // userId에서 파일명으로 사용 불가능한 문자 제거
        var safeUserId = string.Join("_", userId.Split(System.IO.Path.GetInvalidFileNameChars()));
        return System.IO.Path.Combine(_photoCacheDir, $"{safeUserId}.txt");
    }

    /// <summary>
    /// 사진 캐시 초기화 (메모리 + 파일)
    /// </summary>
    public void ClearPhotoCache()
    {
        lock (_photoCacheLock)
        {
            _userPhotoCache.Clear();
        }

        // 파일 캐시도 삭제
        try
        {
            if (System.IO.Directory.Exists(_photoCacheDir))
            {
                foreach (var file in System.IO.Directory.GetFiles(_photoCacheDir, "*.txt"))
                {
                    System.IO.File.Delete(file);
                }
            }
            _logger.Debug("사용자 사진 캐시 초기화됨 (메모리 + 파일)");
        }
        catch (Exception ex)
        {
            _logger.Debug("사진 캐시 파일 삭제 실패: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// 채팅 멤버들의 프로필 사진 일괄 조회
    /// </summary>
    /// <param name="chat">채팅 정보</param>
    /// <returns>첫 번째 상대방의 프로필 사진 (1:1 채팅) 또는 null</returns>
    public async Task<string?> GetChatPhotoAsync(Chat chat)
    {
        if (chat?.Members == null || !chat.Members.Any())
            return null;

        try
        {
            // 1:1 채팅인 경우 상대방 사진 가져오기
            if (chat.ChatType == ChatType.OneOnOne)
            {
                // 현재 사용자가 아닌 멤버 찾기
                var client = _authService.GetGraphClient();
                var me = await client.Me.GetAsync();
                var myId = me?.Id;

                var otherMember = chat.Members
                    .OfType<AadUserConversationMember>()
                    .FirstOrDefault(m => m.UserId != myId);

                if (otherMember?.UserId != null)
                {
                    return await GetUserPhotoAsync(otherMember.UserId);
                }
            }
            // 그룹 채팅인 경우 첫 번째 멤버 사진 (임시)
            else
            {
                var firstMember = chat.Members
                    .OfType<AadUserConversationMember>()
                    .FirstOrDefault();

                if (firstMember?.UserId != null)
                {
                    return await GetUserPhotoAsync(firstMember.UserId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug("채팅 사진 조회 실패: {Error}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// 그룹 채팅 멤버들의 프로필 사진 목록 조회 (최대 4명)
    /// </summary>
    /// <param name="chat">채팅 정보</param>
    /// <returns>멤버 사진 정보 목록 (userId, displayName, photoBase64)</returns>
    public async Task<List<(string UserId, string DisplayName, string? PhotoBase64)>> GetGroupChatMemberPhotosAsync(Chat chat)
    {
        var result = new List<(string UserId, string DisplayName, string? PhotoBase64)>();

        if (chat?.Members == null || !chat.Members.Any())
            return result;

        try
        {
            // 현재 사용자 ID 조회
            var client = _authService.GetGraphClient();
            var me = await client.Me.GetAsync();
            var myId = me?.Id;

            // 나를 제외한 멤버 최대 4명
            var members = chat.Members
                .OfType<AadUserConversationMember>()
                .Where(m => m.UserId != myId)
                .Take(4)
                .ToList();

            foreach (var member in members)
            {
                if (string.IsNullOrEmpty(member.UserId))
                    continue;

                var photo = await GetUserPhotoAsync(member.UserId);
                result.Add((member.UserId, member.DisplayName ?? "알 수 없음", photo));
            }

            _logger.Debug("그룹 채팅 멤버 사진 {Count}개 조회", result.Count);
        }
        catch (Exception ex)
        {
            _logger.Debug("그룹 채팅 멤버 사진 조회 실패: {Error}", ex.Message);
        }

        return result;
    }

    /// <summary>
    /// 현재 사용자 ID 조회
    /// </summary>
    /// <returns>현재 사용자 ID</returns>
    public async Task<string?> GetCurrentUserIdAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var me = await client.Me.GetAsync();
            return me?.Id;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "현재 사용자 ID 조회 실패");
            return null;
        }
    }

    #endregion
}
