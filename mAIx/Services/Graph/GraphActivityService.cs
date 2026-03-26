using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mAIx.Utils;

namespace mAIx.Services.Graph;

/// <summary>
/// 활동 피드 서비스
/// - 최근 활동 (메일, 채팅, 파일 등) 통합
/// </summary>
public class GraphActivityService
{
    private readonly GraphAuthService _authService;

    public GraphActivityService(GraphAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// 최근 메일 활동 조회 (받은편지함)
    /// </summary>
    public async Task<IEnumerable<ActivityItem>> GetRecentMailActivityAsync(int count = 20)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Messages.GetAsync(config =>
            {
                config.QueryParameters.Top = count;
                config.QueryParameters.Select = new[] { "id", "subject", "from", "receivedDateTime", "isRead" };
                config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
            });

            var activities = new List<ActivityItem>();
            foreach (var message in response?.Value ?? new List<Message>())
            {
                activities.Add(new ActivityItem
                {
                    Id = message.Id ?? string.Empty,
                    Type = ActivityType.Email,
                    Title = message.Subject ?? "(제목 없음)",
                    Description = $"{message.From?.EmailAddress?.Name ?? message.From?.EmailAddress?.Address ?? "알 수 없음"}님이 보낸 메일",
                    Timestamp = message.ReceivedDateTime?.DateTime ?? DateTime.Now,
                    IsRead = message.IsRead ?? false,
                    SourceId = message.Id
                });
            }

            Log4.Debug($"[ActivityService] 메일 활동 {activities.Count}개 조회");
            return activities;
        }
        catch (Exception ex)
        {
            Log4.Error($"[ActivityService] 메일 활동 조회 실패: {ex.Message}");
            return new List<ActivityItem>();
        }
    }

    /// <summary>
    /// 최근 채팅 활동 조회
    /// </summary>
    public async Task<IEnumerable<ActivityItem>> GetRecentChatActivityAsync(int count = 20)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Chats.GetAsync(config =>
            {
                config.QueryParameters.Top = count;
            });

            var activities = new List<ActivityItem>();
            foreach (var chat in response?.Value ?? new List<Chat>())
            {
                var lastMessage = chat.LastMessagePreview;
                if (lastMessage != null)
                {
                    var content = lastMessage.Body?.Content ?? "";
                    activities.Add(new ActivityItem
                    {
                        Id = chat.Id ?? string.Empty,
                        Type = ActivityType.Chat,
                        Title = chat.Topic ?? "채팅",
                        Description = content.Length > 100 ? content.Substring(0, 100) : content,
                        Timestamp = lastMessage.CreatedDateTime?.DateTime ?? DateTime.Now,
                        IsRead = false,
                        SourceId = chat.Id
                    });
                }
            }

            Log4.Debug($"[ActivityService] 채팅 활동 {activities.Count}개 조회");
            return activities;
        }
        catch (Exception ex)
        {
            Log4.Error($"[ActivityService] 채팅 활동 조회 실패: {ex.Message}");
            return new List<ActivityItem>();
        }
    }

    /// <summary>
    /// 최근 파일 활동 조회
    /// </summary>
    public async Task<IEnumerable<ActivityItem>> GetRecentFileActivityAsync(int count = 20)
    {
        try
        {
            var client = _authService.GetGraphClient();
            // DriveId를 먼저 가져와서 Recent API 호출
            var drive = await client.Me.Drive.GetAsync();
            var driveId = drive?.Id;
            if (string.IsNullOrEmpty(driveId))
            {
                return new List<ActivityItem>();
            }

            var response = await client.Drives[driveId].Recent.GetAsync(config =>
            {
                config.QueryParameters.Top = count;
            });

            var activities = new List<ActivityItem>();
            foreach (var item in response?.Value ?? new List<DriveItem>())
            {
                activities.Add(new ActivityItem
                {
                    Id = item.Id ?? string.Empty,
                    Type = ActivityType.File,
                    Title = item.Name ?? "(파일명 없음)",
                    Description = GetFileActivityDescription(item),
                    Timestamp = item.LastModifiedDateTime?.DateTime ?? DateTime.Now,
                    IsRead = true,
                    SourceId = item.Id
                });
            }

            Log4.Debug($"[ActivityService] 파일 활동 {activities.Count}개 조회");
            return activities;
        }
        catch (Exception ex)
        {
            Log4.Error($"[ActivityService] 파일 활동 조회 실패: {ex.Message}");
            return new List<ActivityItem>();
        }
    }

    /// <summary>
    /// 통합 활동 피드 조회 (모든 활동 통합)
    /// </summary>
    public async Task<IEnumerable<ActivityItem>> GetAllActivitiesAsync(int count = 50)
    {
        var allActivities = new List<ActivityItem>();

        // 병렬로 모든 활동 조회
        var mailTask = GetRecentMailActivityAsync(count / 3);
        var chatTask = GetRecentChatActivityAsync(count / 3);
        var fileTask = GetRecentFileActivityAsync(count / 3);

        await Task.WhenAll(mailTask, chatTask, fileTask);

        allActivities.AddRange(await mailTask);
        allActivities.AddRange(await chatTask);
        allActivities.AddRange(await fileTask);

        // 시간순 정렬
        return allActivities.OrderByDescending(a => a.Timestamp).Take(count);
    }

    private string GetFileActivityDescription(DriveItem item)
    {
        var lastModifiedBy = item.LastModifiedBy?.User?.DisplayName ?? "알 수 없음";
        return $"{lastModifiedBy}님이 수정함";
    }
}

/// <summary>
/// 활동 아이템 모델
/// </summary>
public class ActivityItem
{
    public string Id { get; set; } = string.Empty;
    public ActivityType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsRead { get; set; }
    public string? SourceId { get; set; }
    public string? IconUrl { get; set; }

    /// <summary>
    /// 시간 표시
    /// </summary>
    public string TimestampDisplay
    {
        get
        {
            var diff = DateTime.Now - Timestamp;
            if (diff.TotalMinutes < 1)
                return "방금 전";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}분 전";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}시간 전";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}일 전";

            return Timestamp.ToString("MM/dd HH:mm");
        }
    }

    /// <summary>
    /// 타입별 아이콘
    /// </summary>
    public string TypeIcon => Type switch
    {
        ActivityType.Email => "Mail24",
        ActivityType.Chat => "Chat24",
        ActivityType.File => "Document24",
        ActivityType.Mention => "Mention24",
        ActivityType.Reply => "ArrowReply24",
        ActivityType.Reaction => "Heart24",
        _ => "Alert24"
    };

    /// <summary>
    /// 타입별 색상
    /// </summary>
    public string TypeColor => Type switch
    {
        ActivityType.Email => "#0078D4",
        ActivityType.Chat => "#6264A7",
        ActivityType.File => "#0078D4",
        ActivityType.Mention => "#F7630C",
        ActivityType.Reply => "#31752F",
        ActivityType.Reaction => "#D13438",
        _ => "#808080"
    };
}

/// <summary>
/// 활동 타입
/// </summary>
public enum ActivityType
{
    Email,
    Chat,
    File,
    Mention,
    Reply,
    Reaction,
    Other
}
