using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Serilog;

// 모호한 참조 해결을 위한 별칭
using MailXTodo = mailX.Models.Todo;
using MailXEmail = mailX.Models.Email;

namespace mailX.Services.Graph;

/// <summary>
/// Microsoft Calendar 연동 서비스
/// </summary>
public class GraphCalendarService
{
    private readonly GraphAuthService _authService;
    private readonly ILogger _logger;

    public GraphCalendarService(GraphAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = Log.ForContext<GraphCalendarService>();
    }

    /// <summary>
    /// 오늘 일정 조회
    /// </summary>
    /// <returns>오늘 일정 목록</returns>
    public async Task<IEnumerable<Event>> GetTodayEventsAsync()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        return await GetEventsAsync(today, tomorrow);
    }

    /// <summary>
    /// 이번 주 일정 조회
    /// </summary>
    /// <returns>이번 주 일정 목록</returns>
    public async Task<IEnumerable<Event>> GetThisWeekEventsAsync()
    {
        var today = DateTime.Today;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);

        return await GetEventsAsync(startOfWeek, endOfWeek);
    }

    /// <summary>
    /// 기간별 일정 조회
    /// </summary>
    /// <param name="startDate">시작일</param>
    /// <param name="endDate">종료일</param>
    /// <returns>일정 목록</returns>
    public async Task<IEnumerable<Event>> GetEventsAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var client = _authService.GetGraphClient();

            // ISO 8601 형식으로 변환
            var startDateTime = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDateTime = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            var response = await client.Me.CalendarView.GetAsync(config =>
            {
                config.QueryParameters.StartDateTime = startDateTime;
                config.QueryParameters.EndDateTime = endDateTime;
                config.QueryParameters.Top = 100;
                config.QueryParameters.Orderby = new[] { "start/dateTime" };
                config.QueryParameters.Select = new[]
                {
                    "id", "subject", "start", "end", "location",
                    "organizer", "attendees", "isAllDay", "importance",
                    "bodyPreview", "webLink"
                };
            });

            _logger.Debug("일정 {Count}개 조회 ({Start} ~ {End})",
                response?.Value?.Count ?? 0, startDate.ToShortDateString(), endDate.ToShortDateString());

            return response?.Value ?? new List<Event>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일정 조회 실패: {Start} ~ {End}", startDate, endDate);
            throw;
        }
    }

    /// <summary>
    /// 메일에서 추출한 마감일을 일정으로 생성
    /// </summary>
    /// <param name="email">이메일 정보</param>
    /// <returns>생성된 일정</returns>
    public async Task<Event?> CreateDeadlineEventFromEmailAsync(MailXEmail email)
    {
        if (email == null)
            throw new ArgumentNullException(nameof(email));

        if (!email.Deadline.HasValue)
        {
            _logger.Warning("마감일이 없는 이메일입니다: EmailId={EmailId}", email.Id);
            return null;
        }

        try
        {
            var client = _authService.GetGraphClient();

            var newEvent = new Event
            {
                Subject = $"[마감] {email.Subject}",
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = BuildEventBodyFromEmail(email)
                },
                Start = new DateTimeTimeZone
                {
                    DateTime = email.Deadline.Value.Date.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Korea Standard Time"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = email.Deadline.Value.Date.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Korea Standard Time"
                },
                IsAllDay = true,
                Importance = ConvertPriorityToImportance(email.PriorityLevel),
                ShowAs = FreeBusyStatus.Free, // 일정 충돌 방지
                ReminderMinutesBeforeStart = 1440, // 하루 전 알림
                Categories = new List<string> { "이메일 마감" }
            };

            var createdEvent = await client.Me.Calendar.Events.PostAsync(newEvent);

            _logger.Information("마감일 일정 생성: {Subject} - {Deadline}",
                email.Subject, email.Deadline.Value.ToShortDateString());

            return createdEvent;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "마감일 일정 생성 실패: EmailId={EmailId}", email.Id);
            throw;
        }
    }

    /// <summary>
    /// 할일을 일정으로 변환
    /// </summary>
    /// <param name="todo">할일 정보</param>
    /// <returns>생성된 일정</returns>
    public async Task<Event?> CreateEventFromTodoAsync(MailXTodo todo)
    {
        if (todo == null)
            throw new ArgumentNullException(nameof(todo));

        try
        {
            var client = _authService.GetGraphClient();

            // 마감일이 없으면 내일로 설정
            var dueDate = todo.DueDate ?? DateTime.Today.AddDays(1);

            var newEvent = new Event
            {
                Subject = $"[TODO] {todo.Content.Substring(0, Math.Min(100, todo.Content.Length))}",
                Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = $"할일 항목\n\n{todo.Content}\n\n상태: {todo.Status}\n우선순위: {todo.Priority}"
                },
                Start = new DateTimeTimeZone
                {
                    DateTime = dueDate.Date.AddHours(9).ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Korea Standard Time"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = dueDate.Date.AddHours(10).ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Korea Standard Time"
                },
                Importance = ConvertTodoPriorityToImportance(todo.Priority),
                ShowAs = FreeBusyStatus.Tentative,
                ReminderMinutesBeforeStart = 60, // 1시간 전 알림
                Categories = new List<string> { "할일" }
            };

            var createdEvent = await client.Me.Calendar.Events.PostAsync(newEvent);

            _logger.Information("할일 일정 생성: {Content} - {DueDate}",
                todo.Content.Substring(0, Math.Min(30, todo.Content.Length)), dueDate.ToShortDateString());

            return createdEvent;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "할일 일정 생성 실패: TodoId={TodoId}", todo.Id);
            throw;
        }
    }

    /// <summary>
    /// 미팅 초대 파싱
    /// </summary>
    /// <param name="eventId">이벤트 ID</param>
    /// <returns>미팅 정보</returns>
    public async Task<MeetingInfo?> ParseMeetingInviteAsync(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            throw new ArgumentNullException(nameof(eventId));

        try
        {
            var client = _authService.GetGraphClient();
            var calendarEvent = await client.Me.Calendar.Events[eventId].GetAsync(config =>
            {
                config.QueryParameters.Expand = new[] { "attachments" };
            });

            if (calendarEvent == null)
                return null;

            var meetingInfo = new MeetingInfo
            {
                EventId = calendarEvent.Id ?? eventId,
                Subject = calendarEvent.Subject ?? "제목 없음",
                Organizer = calendarEvent.Organizer?.EmailAddress?.Address,
                OrganizerName = calendarEvent.Organizer?.EmailAddress?.Name,
                Location = calendarEvent.Location?.DisplayName,
                StartTime = ParseDateTimeTimeZone(calendarEvent.Start),
                EndTime = ParseDateTimeTimeZone(calendarEvent.End),
                IsOnlineMeeting = calendarEvent.IsOnlineMeeting ?? false,
                OnlineMeetingUrl = calendarEvent.OnlineMeeting?.JoinUrl,
                Attendees = calendarEvent.Attendees?
                    .Select(a => new AttendeeInfo
                    {
                        Email = a.EmailAddress?.Address,
                        Name = a.EmailAddress?.Name,
                        ResponseStatus = a.Status?.Response?.ToString()
                    })
                    .ToList() ?? new List<AttendeeInfo>(),
                BodyPreview = calendarEvent.BodyPreview
            };

            _logger.Debug("미팅 정보 파싱 완료: {Subject}", meetingInfo.Subject);
            return meetingInfo;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "미팅 초대 파싱 실패: EventId={EventId}", eventId);
            throw;
        }
    }

    /// <summary>
    /// 일정 응답 (수락/거절/미정)
    /// </summary>
    /// <param name="eventId">이벤트 ID</param>
    /// <param name="response">응답 타입 (accept, decline, tentative)</param>
    /// <param name="comment">응답 코멘트 (선택)</param>
    public async Task RespondToEventAsync(string eventId, string response, string? comment = null)
    {
        if (string.IsNullOrEmpty(eventId))
            throw new ArgumentNullException(nameof(eventId));

        try
        {
            var client = _authService.GetGraphClient();

            switch (response.ToLower())
            {
                case "accept":
                    await client.Me.Calendar.Events[eventId].Accept.PostAsync(
                        new Microsoft.Graph.Me.Calendar.Events.Item.Accept.AcceptPostRequestBody
                        {
                            Comment = comment,
                            SendResponse = true
                        });
                    break;

                case "decline":
                    await client.Me.Calendar.Events[eventId].Decline.PostAsync(
                        new Microsoft.Graph.Me.Calendar.Events.Item.Decline.DeclinePostRequestBody
                        {
                            Comment = comment,
                            SendResponse = true
                        });
                    break;

                case "tentative":
                    await client.Me.Calendar.Events[eventId].TentativelyAccept.PostAsync(
                        new Microsoft.Graph.Me.Calendar.Events.Item.TentativelyAccept.TentativelyAcceptPostRequestBody
                        {
                            Comment = comment,
                            SendResponse = true
                        });
                    break;

                default:
                    throw new ArgumentException($"알 수 없는 응답 타입: {response}", nameof(response));
            }

            _logger.Information("일정 응답 완료: {EventId} - {Response}", eventId, response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일정 응답 실패: EventId={EventId}, Response={Response}", eventId, response);
            throw;
        }
    }

    /// <summary>
    /// 이메일 본문에서 일정 정보 추출 및 생성
    /// </summary>
    /// <param name="email">이메일</param>
    /// <param name="extractedDate">추출된 날짜</param>
    /// <param name="extractedTime">추출된 시간 (선택)</param>
    /// <returns>생성된 일정</returns>
    public async Task<Event?> CreateEventFromEmailContentAsync(
        MailXEmail email,
        DateTime extractedDate,
        TimeSpan? extractedTime = null)
    {
        if (email == null)
            throw new ArgumentNullException(nameof(email));

        try
        {
            var client = _authService.GetGraphClient();

            var startDateTime = extractedTime.HasValue
                ? extractedDate.Date.Add(extractedTime.Value)
                : extractedDate.Date.AddHours(9);

            var endDateTime = startDateTime.AddHours(1);

            var newEvent = new Event
            {
                Subject = email.Subject,
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = BuildEventBodyFromEmail(email)
                },
                Start = new DateTimeTimeZone
                {
                    DateTime = startDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Korea Standard Time"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = endDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Korea Standard Time"
                },
                Importance = ConvertPriorityToImportance(email.PriorityLevel),
                Categories = new List<string> { "이메일 일정" }
            };

            var createdEvent = await client.Me.Calendar.Events.PostAsync(newEvent);

            _logger.Information("이메일에서 일정 생성: {Subject} - {StartDate}",
                email.Subject, startDateTime.ToString("yyyy-MM-dd HH:mm"));

            return createdEvent;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "이메일에서 일정 생성 실패: EmailId={EmailId}", email.Id);
            throw;
        }
    }

    #region Private Helpers

    /// <summary>
    /// 이메일로부터 일정 본문 생성
    /// </summary>
    private string BuildEventBodyFromEmail(MailXEmail email)
    {
        var summary = !string.IsNullOrEmpty(email.SummaryOneline)
            ? $"<p><strong>요약:</strong> {email.SummaryOneline}</p>"
            : "";

        return $@"
<html>
<body>
    <h3>이메일 정보</h3>
    <p><strong>발신자:</strong> {email.From}</p>
    <p><strong>제목:</strong> {email.Subject}</p>
    <p><strong>수신일:</strong> {email.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "알 수 없음"}</p>
    {summary}
    <hr/>
    <h3>원본 내용</h3>
    {email.Body ?? "내용 없음"}
</body>
</html>";
    }

    /// <summary>
    /// 우선순위 레벨을 Importance로 변환
    /// </summary>
    private Importance ConvertPriorityToImportance(string? priorityLevel)
    {
        return priorityLevel?.ToLower() switch
        {
            "critical" => Importance.High,
            "high" => Importance.High,
            "medium" => Importance.Normal,
            "low" => Importance.Low,
            _ => Importance.Normal
        };
    }

    /// <summary>
    /// TODO 우선순위를 Importance로 변환
    /// </summary>
    private Importance ConvertTodoPriorityToImportance(int priority)
    {
        return priority switch
        {
            1 => Importance.High,
            2 => Importance.High,
            3 => Importance.Normal,
            4 => Importance.Low,
            5 => Importance.Low,
            _ => Importance.Normal
        };
    }

    /// <summary>
    /// DateTimeTimeZone 파싱
    /// </summary>
    private DateTime? ParseDateTimeTimeZone(DateTimeTimeZone? dateTimeTimeZone)
    {
        if (dateTimeTimeZone?.DateTime == null)
            return null;

        if (DateTime.TryParse(dateTimeTimeZone.DateTime, out var result))
            return result;

        return null;
    }

    #endregion
}

/// <summary>
/// 미팅 정보 DTO
/// </summary>
public class MeetingInfo
{
    public string EventId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? Organizer { get; set; }
    public string? OrganizerName { get; set; }
    public string? Location { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public bool IsOnlineMeeting { get; set; }
    public string? OnlineMeetingUrl { get; set; }
    public List<AttendeeInfo> Attendees { get; set; } = new();
    public string? BodyPreview { get; set; }
}

/// <summary>
/// 참석자 정보 DTO
/// </summary>
public class AttendeeInfo
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? ResponseStatus { get; set; }
}
