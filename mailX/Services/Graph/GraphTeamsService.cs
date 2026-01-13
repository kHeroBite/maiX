using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mailX.Data;
using mailX.Models;
using Serilog;

namespace mailX.Services.Graph;

/// <summary>
/// Microsoft Teams 메시지 연동 서비스
/// </summary>
public class GraphTeamsService
{
    private readonly GraphAuthService _authService;
    private readonly MailXDbContext _dbContext;
    private readonly ILogger _logger;

    public GraphTeamsService(GraphAuthService authService, MailXDbContext dbContext)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = Log.ForContext<GraphTeamsService>();
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
                config.QueryParameters.Expand = new[] { "members" };
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

            // 기존 메시지 확인
            var existingMessage = await _dbContext.TeamsMessages
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

                _dbContext.TeamsMessages.Add(teamsMessage);
                existingMessage = teamsMessage;
            }

            await _dbContext.SaveChangesAsync();
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
        return await _dbContext.TeamsMessages
            .Where(m => m.LinkedEmailId == emailId)
            .OrderByDescending(m => m.CreatedDateTime)
            .ToListAsync();
    }

    /// <summary>
    /// 채팅방 제목/이름 가져오기
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
        if (chat.ChatType == ChatType.OneOnOne && chat.Members?.Count == 2)
        {
            var otherMember = chat.Members
                .OfType<AadUserConversationMember>()
                .FirstOrDefault(m => m.UserId != null);

            return otherMember?.DisplayName ?? "Direct Chat";
        }

        // 그룹 채팅이지만 topic이 없는 경우
        if (chat.ChatType == ChatType.Group)
        {
            var memberNames = chat.Members?
                .OfType<AadUserConversationMember>()
                .Take(3)
                .Select(m => m.DisplayName)
                .Where(n => !string.IsNullOrEmpty(n));

            if (memberNames?.Any() == true)
                return string.Join(", ", memberNames);
        }

        return "Chat";
    }
}
