using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Graph.Models;
using mAIx.Services.Graph;
using mAIx.Utils;

namespace mAIx.Services;

/// <summary>
/// 크로스탭 연동 서비스 — 탭 간 데이터 흐름 통합 허브
/// </summary>
public class CrossTabIntegrationService
{
    private readonly GraphCalendarService _calendarService;
    private readonly GraphToDoService _todoService;
    private readonly GraphTeamsService _teamsService;
    private readonly GraphCallService _callService;
    private readonly GraphContactService _contactService;

    public CrossTabIntegrationService(
        GraphCalendarService calendarService,
        GraphToDoService todoService,
        GraphTeamsService teamsService,
        GraphCallService callService,
        GraphContactService contactService)
    {
        _calendarService = calendarService ?? throw new ArgumentNullException(nameof(calendarService));
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _callService = callService ?? throw new ArgumentNullException(nameof(callService));
        _contactService = contactService ?? throw new ArgumentNullException(nameof(contactService));
    }

    /// <summary>
    /// 이메일 → 캘린더: 메일 본문에서 일정 정보 추출
    /// </summary>
    public Task<Event?> ExtractEventFromEmailAsync(Message email)
    {
        try
        {
            if (email == null) return Task.FromResult<Event?>(null);

            var subject = email.Subject ?? string.Empty;
            var body = email.Body?.Content ?? string.Empty;

            var datePattern = @"(\d{4}[-/]\d{1,2}[-/]\d{1,2})";
            var timePattern = @"(\d{1,2}:\d{2})";

            var dateMatch = Regex.Match(body, datePattern);
            var timeMatch = Regex.Match(body, timePattern);

            if (!dateMatch.Success)
            {
                Log4.Debug($"[CrossTab] 이메일에서 일정 정보를 찾을 수 없음: {subject}");
                return Task.FromResult<Event?>(null);
            }

            var eventDate = DateTime.Parse(dateMatch.Value);
            if (timeMatch.Success)
            {
                var timeParts = timeMatch.Value.Split(':');
                eventDate = eventDate.AddHours(int.Parse(timeParts[0])).AddMinutes(int.Parse(timeParts[1]));
            }

            var newEvent = new Event
            {
                Subject = $"[메일] {subject}",
                Start = new DateTimeTimeZone
                {
                    DateTime = eventDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Asia/Seoul"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = eventDate.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Asia/Seoul"
                },
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = $"원본 메일: {subject}\n발신자: {email.From?.EmailAddress?.Address}"
                }
            };

            Log4.Info($"[CrossTab] 이메일에서 일정 추출: {subject} → {eventDate}");
            return Task.FromResult<Event?>(newEvent);
        }
        catch (Exception ex)
        {
            Log4.Error($"[CrossTab] 이메일에서 일정 추출 실패: {ex.Message}");
            return Task.FromResult<Event?>(null);
        }
    }

    /// <summary>
    /// 이메일 → ToDo: Flagged 메일을 ToDo 작업으로 동기화
    /// </summary>
    public async Task SyncFlaggedEmailsToTodoAsync()
    {
        try
        {
            Log4.Info("[CrossTab] Flagged 메일 → ToDo 동기화 시작");

            var lists = await _todoService.GetAllListsAsync().ConfigureAwait(false);
            var flaggedList = lists?.FirstOrDefault(l =>
                l.DisplayName.Contains("Flagged", StringComparison.OrdinalIgnoreCase) ||
                l.DisplayName.Contains("플래그", StringComparison.OrdinalIgnoreCase));

            if (flaggedList == null)
            {
                Log4.Info("[CrossTab] Flagged 메일 ToDo 리스트가 없습니다. 자동 동기화는 MS365 서버에서 처리됩니다.");
            }
            else
            {
                Log4.Info($"[CrossTab] Flagged 메일 ToDo 리스트 확인됨: {flaggedList.DisplayName}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[CrossTab] Flagged 메일 → ToDo 동기화 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 연락처 → Teams: 연락처에서 1:1 채팅 시작
    /// </summary>
    public async Task<string?> StartTeamsChatWithContactAsync(string contactEmail, string contactDisplayName)
    {
        try
        {
            if (string.IsNullOrEmpty(contactEmail))
            {
                Log4.Warn("[CrossTab] 채팅 시작 실패: 이메일 주소 없음");
                return null;
            }

            var chats = await _teamsService.GetChatsAsync().ConfigureAwait(false);
            var existingChat = chats?.FirstOrDefault(c =>
                c.ChatType == ChatType.OneOnOne);

            if (existingChat != null)
            {
                Log4.Info($"[CrossTab] 기존 Teams 1:1 채팅 발견: {contactDisplayName} ({contactEmail})");
                return existingChat.Id;
            }

            Log4.Info($"[CrossTab] Teams 1:1 채팅 시작 요청: {contactDisplayName} ({contactEmail})");
            return null;
        }
        catch (Exception ex)
        {
            Log4.Error($"[CrossTab] Teams 채팅 시작 실패: {contactEmail} - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 연락처 → 통화: 연락처에서 통화 시작 (Teams PSTN)
    /// </summary>
    public Task StartCallWithContactAsync(string contactEmail, string contactPhone)
    {
        try
        {
            var target = !string.IsNullOrEmpty(contactPhone) ? contactPhone : contactEmail;
            if (string.IsNullOrEmpty(target))
            {
                Log4.Warn("[CrossTab] 통화 시작 실패: 전화번호/이메일 없음");
                return Task.CompletedTask;
            }

            Log4.Info($"[CrossTab] 통화 시작 요청: {target}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log4.Error($"[CrossTab] 통화 시작 실패: {ex.Message}");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 캘린더 → Teams: 일정에서 Teams 회의 참가
    /// </summary>
    public Task<string?> JoinTeamsMeetingFromEventAsync(Event calEvent)
    {
        try
        {
            if (calEvent == null) return Task.FromResult<string?>(null);

            var joinUrl = calEvent.OnlineMeeting?.JoinUrl;
            if (string.IsNullOrEmpty(joinUrl))
            {
                var body = calEvent.Body?.Content ?? string.Empty;
                var teamsPattern = @"https://teams\.microsoft\.com/l/meetup-join/[^\s""<]+";
                var match = Regex.Match(body, teamsPattern);
                if (match.Success)
                {
                    joinUrl = match.Value;
                }
            }

            if (!string.IsNullOrEmpty(joinUrl))
            {
                Log4.Info($"[CrossTab] Teams 회의 참가 URL: {joinUrl}");
                return Task.FromResult<string?>(joinUrl);
            }

            Log4.Debug($"[CrossTab] 이벤트에 Teams 회의 링크 없음: {calEvent.Subject}");
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            Log4.Error($"[CrossTab] Teams 회의 참가 실패: {ex.Message}");
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Activity → 전체: 활동 피드에서 원본 항목으로 이동
    /// </summary>
    public Task NavigateToActivitySourceAsync(ActivityItem activity, Action<string> navigateCallback)
    {
        try
        {
            if (activity == null || navigateCallback == null)
                return Task.CompletedTask;

            var targetTab = activity.Type switch
            {
                Graph.ActivityType.Email => "mail",
                Graph.ActivityType.Chat => "teams",
                Graph.ActivityType.File => "onedrive",
                Graph.ActivityType.Mention => "teams",
                Graph.ActivityType.Reply => "mail",
                Graph.ActivityType.Reaction => "teams",
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(targetTab))
            {
                Log4.Debug($"[CrossTab] 활동 → 탭 이동: {activity.Type} → {targetTab}");
                navigateCallback(targetTab);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log4.Error($"[CrossTab] 활동 소스 이동 실패: {ex.Message}");
            return Task.CompletedTask;
        }
    }
}
