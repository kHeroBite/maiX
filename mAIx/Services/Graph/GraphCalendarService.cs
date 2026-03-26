using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Serilog;

// 모호한 참조 해결을 위한 별칭
using mAIxTodo = mAIx.Models.Todo;
using mAIxEmail = mAIx.Models.Email;

namespace mAIx.Services.Graph;

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
            _logger.Error(ex, "일정 조회 실패: {Start} ~ {End}, 오류: {Message}", startDate, endDate, ex.Message);
            // 오류가 발생해도 빈 목록 반환 (UI가 깨지지 않도록)
            return new List<Event>();
        }
    }

    /// <summary>
    /// 메일에서 추출한 마감일을 일정으로 생성
    /// </summary>
    /// <param name="email">이메일 정보</param>
    /// <returns>생성된 일정</returns>
    public async Task<Event?> CreateDeadlineEventFromEmailAsync(mAIxEmail email)
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
    public async Task<Event?> CreateEventFromTodoAsync(mAIxTodo todo)
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

    #region 일정 CRUD

    /// <summary>
    /// 새 일정 생성
    /// </summary>
    public async Task<Event?> CreateEventAsync(EventCreateRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var client = _authService.GetGraphClient();

            // 타임존 (한국 표준시)
            const string timeZone = "Asia/Seoul";

            var newEvent = new Event
            {
                Subject = request.Subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = request.IsAllDay
                        ? request.StartDateTime.Date.ToString("yyyy-MM-dd")
                        : request.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = timeZone
                },
                End = new DateTimeTimeZone
                {
                    DateTime = request.IsAllDay
                        ? request.EndDateTime.Date.AddDays(1).ToString("yyyy-MM-dd")
                        : request.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = timeZone
                },
                IsAllDay = request.IsAllDay,
                ReminderMinutesBeforeStart = request.ReminderMinutesBefore > 0 ? request.ReminderMinutesBefore : 15,
                IsReminderOn = request.ReminderMinutesBefore > 0
            };

            // 장소 설정 (값이 있을 때만)
            if (!string.IsNullOrEmpty(request.Location))
            {
                newEvent.Location = new Location { DisplayName = request.Location };
            }

            // 본문 설정 (값이 있을 때만)
            if (!string.IsNullOrEmpty(request.Body))
            {
                newEvent.Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = request.Body
                };
            }

            // 카테고리 설정 (null이 아닌 경우에만)
            if (request.Categories?.Any() == true)
            {
                newEvent.Categories = request.Categories;
            }

            // 상태 설정 (ShowAs)
            if (!string.IsNullOrEmpty(request.ShowAs))
            {
                newEvent.ShowAs = request.ShowAs.ToLower() switch
                {
                    "free" => FreeBusyStatus.Free,
                    "tentative" => FreeBusyStatus.Tentative,
                    "busy" => FreeBusyStatus.Busy,
                    "oof" => FreeBusyStatus.Oof,
                    "workingelsewhere" => FreeBusyStatus.WorkingElsewhere,
                    _ => FreeBusyStatus.Busy
                };
            }

            // 온라인 회의 설정 (true일 때만 설정, false면 null 유지)
            if (request.IsOnlineMeeting)
            {
                newEvent.IsOnlineMeeting = true;
                newEvent.OnlineMeetingProvider = OnlineMeetingProviderType.TeamsForBusiness;
            }

            // 참석자 추가 (필수 + 선택적)
            var allAttendees = new List<Attendee>();

            if (request.Attendees?.Any() == true)
            {
                allAttendees.AddRange(request.Attendees.Select(email => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = email },
                    Type = AttendeeType.Required
                }));
            }

            if (request.OptionalAttendees?.Any() == true)
            {
                allAttendees.AddRange(request.OptionalAttendees.Select(email => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = email },
                    Type = AttendeeType.Optional
                }));
            }

            if (allAttendees.Any())
            {
                newEvent.Attendees = allAttendees;
            }

            _logger.Debug("일정 생성 API 호출: Subject={Subject}, Start={Start}, End={End}, IsAllDay={IsAllDay}",
                request.Subject, request.StartDateTime, request.EndDateTime, request.IsAllDay);

            var createdEvent = await client.Me.Calendar.Events.PostAsync(newEvent);
            _logger.Information("일정 생성 완료: {Subject} ({Start})", request.Subject, request.StartDateTime);
            return createdEvent;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            _logger.Error(odataEx, "일정 생성 Graph API 오류: {Subject}, Code={Code}, Message={Message}",
                request.Subject, odataEx.Error?.Code, odataEx.Error?.Message);
            throw new Exception($"Graph API 오류: {odataEx.Error?.Message ?? odataEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일정 생성 실패: {Subject}, Type={Type}", request.Subject, ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// 일정 수정
    /// </summary>
    public async Task<Event?> UpdateEventAsync(string eventId, EventCreateRequest request)
    {
        if (string.IsNullOrEmpty(eventId))
            throw new ArgumentNullException(nameof(eventId));
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        try
        {
            var client = _authService.GetGraphClient();

            // 타임존 (한국 표준시)
            const string timeZone = "Asia/Seoul";

            var updatedEvent = new Event
            {
                Subject = request.Subject,
                Start = new DateTimeTimeZone
                {
                    DateTime = request.IsAllDay
                        ? request.StartDateTime.Date.ToString("yyyy-MM-dd")
                        : request.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = timeZone
                },
                End = new DateTimeTimeZone
                {
                    DateTime = request.IsAllDay
                        ? request.EndDateTime.Date.AddDays(1).ToString("yyyy-MM-dd")
                        : request.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = timeZone
                },
                IsAllDay = request.IsAllDay,
                ReminderMinutesBeforeStart = request.ReminderMinutesBefore > 0 ? request.ReminderMinutesBefore : 15,
                IsReminderOn = request.ReminderMinutesBefore > 0
            };

            // 장소 설정 (값이 있을 때만)
            if (!string.IsNullOrEmpty(request.Location))
            {
                updatedEvent.Location = new Location { DisplayName = request.Location };
            }

            // 본문 설정 (값이 있을 때만)
            if (!string.IsNullOrEmpty(request.Body))
            {
                updatedEvent.Body = new ItemBody
                {
                    ContentType = BodyType.Text,
                    Content = request.Body
                };
            }

            // 카테고리 설정 (null이 아닌 경우에만)
            if (request.Categories?.Any() == true)
            {
                updatedEvent.Categories = request.Categories;
            }

            // 상태 설정 (ShowAs)
            if (!string.IsNullOrEmpty(request.ShowAs))
            {
                updatedEvent.ShowAs = request.ShowAs.ToLower() switch
                {
                    "free" => FreeBusyStatus.Free,
                    "tentative" => FreeBusyStatus.Tentative,
                    "busy" => FreeBusyStatus.Busy,
                    "oof" => FreeBusyStatus.Oof,
                    "workingelsewhere" => FreeBusyStatus.WorkingElsewhere,
                    _ => FreeBusyStatus.Busy
                };
            }

            // 온라인 회의 설정
            if (request.IsOnlineMeeting)
            {
                updatedEvent.IsOnlineMeeting = true;
                updatedEvent.OnlineMeetingProvider = OnlineMeetingProviderType.TeamsForBusiness;
            }

            // 참석자 추가 (필수 + 선택적)
            var allAttendees = new List<Attendee>();

            if (request.Attendees?.Any() == true)
            {
                allAttendees.AddRange(request.Attendees.Select(email => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = email },
                    Type = AttendeeType.Required
                }));
            }

            if (request.OptionalAttendees?.Any() == true)
            {
                allAttendees.AddRange(request.OptionalAttendees.Select(email => new Attendee
                {
                    EmailAddress = new EmailAddress { Address = email },
                    Type = AttendeeType.Optional
                }));
            }

            if (allAttendees.Any())
            {
                updatedEvent.Attendees = allAttendees;
            }

            _logger.Debug("일정 수정 API 호출: EventId={EventId}, Subject={Subject}", eventId, request.Subject);

            var result = await client.Me.Calendar.Events[eventId].PatchAsync(updatedEvent);
            _logger.Information("일정 수정 완료: {EventId} - {Subject}", eventId, request.Subject);
            return result;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
        {
            _logger.Error(odataEx, "일정 수정 Graph API 오류: {EventId}, Code={Code}, Message={Message}",
                eventId, odataEx.Error?.Code, odataEx.Error?.Message);
            throw new Exception($"Graph API 오류: {odataEx.Error?.Message ?? odataEx.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일정 수정 실패: {EventId}, Type={Type}", eventId, ex.GetType().Name);
            throw;
        }
    }

    /// <summary>
    /// 일정 삭제
    /// </summary>
    public async Task<bool> DeleteEventAsync(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            throw new ArgumentNullException(nameof(eventId));

        try
        {
            var client = _authService.GetGraphClient();
            await client.Me.Calendar.Events[eventId].DeleteAsync();
            _logger.Information("일정 삭제 완료: {EventId}", eventId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일정 삭제 실패: {EventId}", eventId);
            return false;
        }
    }

    /// <summary>
    /// 일정 상세 조회
    /// </summary>
    public async Task<Event?> GetEventByIdAsync(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            throw new ArgumentNullException(nameof(eventId));

        try
        {
            var client = _authService.GetGraphClient();
            var evt = await client.Me.Calendar.Events[eventId].GetAsync();
            return evt;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "일정 조회 실패: {EventId}", eventId);
            return null;
        }
    }

    #endregion

    /// <summary>
    /// 이메일 본문에서 일정 정보 추출 및 생성
    /// </summary>
    /// <param name="email">이메일</param>
    /// <param name="extractedDate">추출된 날짜</param>
    /// <param name="extractedTime">추출된 시간 (선택)</param>
    /// <returns>생성된 일정</returns>
    public async Task<Event?> CreateEventFromEmailContentAsync(
        mAIxEmail email,
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
    private string BuildEventBodyFromEmail(mAIxEmail email)
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
    /// DateTimeTimeZone 파싱 및 로컬 시간으로 변환
    /// Graph API는 지정된 TimeZone의 로컬 시간을 반환하므로,
    /// 해당 TimeZone에서 시스템 로컬 시간으로 변환 필요
    /// </summary>
    private DateTime? ParseDateTimeTimeZone(DateTimeTimeZone? dateTimeTimeZone)
    {
        if (dateTimeTimeZone?.DateTime == null)
            return null;

        if (!DateTime.TryParse(dateTimeTimeZone.DateTime, out var parsedTime))
            return null;

        // 시간대 변환
        return ConvertGraphTimeToLocal(parsedTime, dateTimeTimeZone.TimeZone);
    }

    /// <summary>
    /// Graph API 시간을 로컬 시간으로 변환
    /// </summary>
    /// <param name="dateTime">Graph API에서 파싱한 DateTime (Kind=Unspecified)</param>
    /// <param name="timeZoneId">Graph API의 TimeZone ID (예: "Korea Standard Time", "UTC")</param>
    /// <returns>시스템 로컬 시간</returns>
    private DateTime ConvertGraphTimeToLocal(DateTime dateTime, string? timeZoneId)
    {
        try
        {
            // TimeZone이 없으면 UTC로 가정
            if (string.IsNullOrEmpty(timeZoneId))
            {
                var utcTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                return utcTime.ToLocalTime();
            }

            // TimeZoneInfo 가져오기
            TimeZoneInfo sourceTimeZone;
            try
            {
                sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows 시간대 ID가 아닌 경우 (IANA 형식 등) - UTC로 폴백
                _logger.Warning("알 수 없는 시간대: {TimeZone}, UTC로 처리", timeZoneId);
                var utcTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                return utcTime.ToLocalTime();
            }

            // 소스 시간대의 시간을 UTC로 변환 후 로컬로 변환
            var sourceTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(sourceTime, sourceTimeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, TimeZoneInfo.Local);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "시간대 변환 실패: {DateTime}, {TimeZone}", dateTime, timeZoneId);
            return dateTime; // 변환 실패 시 원본 반환
        }
    }

    #endregion

    #region Delta Query 및 캘린더 동기화

    /// <summary>
    /// 사용자의 모든 캘린더 목록 조회
    /// </summary>
    /// <returns>캘린더 목록</returns>
    public async Task<List<CalendarInfo>> GetCalendarsAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Calendars.GetAsync(config =>
            {
                config.QueryParameters.Select = new[]
                {
                    "id", "name", "color", "isDefaultCalendar",
                    "canEdit", "owner"
                };
            });

            var calendars = response?.Value?.Select(c => new CalendarInfo
            {
                Id = c.Id ?? string.Empty,
                Name = c.Name ?? "이름 없음",
                Color = c.Color?.ToString(),
                IsDefaultCalendar = c.IsDefaultCalendar ?? false,
                CanEdit = c.CanEdit ?? false,
                Owner = c.Owner?.Address
            }).ToList() ?? new List<CalendarInfo>();

            _logger.Debug("캘린더 {Count}개 조회", calendars.Count);
            return calendars;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "캘린더 목록 조회 실패");
            return new List<CalendarInfo>();
        }
    }

    /// <summary>
    /// Delta Query로 변경된 이벤트 조회
    /// </summary>
    /// <param name="deltaLink">이전 Delta 링크 (null이면 전체 동기화)</param>
    /// <param name="startDate">동기화 시작 날짜</param>
    /// <param name="endDate">동기화 종료 날짜</param>
    /// <returns>변경된 이벤트 목록과 새 Delta 링크</returns>
    public async Task<CalendarDeltaResult> GetEventsDeltaAsync(
        string? deltaLink = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var result = new CalendarDeltaResult();

        try
        {
            var client = _authService.GetGraphClient();

            // 기본 동기화 범위: 과거 3개월 ~ 미래 6개월
            var syncStart = startDate ?? DateTime.Today.AddMonths(-3);
            var syncEnd = endDate ?? DateTime.Today.AddMonths(6);

            if (!string.IsNullOrEmpty(deltaLink))
            {
                // Delta 링크로 변경분 조회
                result = await GetDeltaWithLinkAsync(client, deltaLink);
            }
            else
            {
                // 초기 동기화 - CalendarView 사용
                result = await GetInitialEventsAsync(client, syncStart, syncEnd);
            }

            _logger.Information("캘린더 Delta 조회: 추가={Added}, 수정={Updated}, 삭제={Deleted}",
                result.AddedCount, result.UpdatedCount, result.DeletedCount);

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "캘린더 Delta 조회 실패");
            throw;
        }
    }

    /// <summary>
    /// Delta 링크로 변경분 조회
    /// </summary>
    private async Task<CalendarDeltaResult> GetDeltaWithLinkAsync(GraphServiceClient client, string deltaLink)
    {
        var result = new CalendarDeltaResult();
        var allEvents = new List<Event>();
        var deletedIds = new List<string>();

        try
        {
            // Delta 링크 파싱 및 요청
            var request = new Microsoft.Graph.Me.CalendarView.Delta.DeltaRequestBuilder(deltaLink, client.RequestAdapter);
            var response = await request.GetAsDeltaGetResponseAsync();

            while (response != null)
            {
                if (response.Value != null)
                {
                    foreach (var evt in response.Value)
                    {
                        // @removed가 있으면 삭제된 이벤트
                        if (evt.AdditionalData?.ContainsKey("@removed") == true)
                        {
                            if (!string.IsNullOrEmpty(evt.Id))
                            {
                                deletedIds.Add(evt.Id);
                            }
                        }
                        else
                        {
                            allEvents.Add(evt);
                        }
                    }
                }

                // 다음 페이지가 있으면 계속
                if (response.OdataNextLink != null)
                {
                    request = new Microsoft.Graph.Me.CalendarView.Delta.DeltaRequestBuilder(response.OdataNextLink, client.RequestAdapter);
                    response = await request.GetAsDeltaGetResponseAsync();
                }
                else
                {
                    // Delta 링크 저장
                    result.DeltaLink = response.OdataDeltaLink;
                    break;
                }
            }

            result.Events = allEvents;
            result.DeletedEventIds = deletedIds;
            result.AddedCount = allEvents.Count;
            result.DeletedCount = deletedIds.Count;

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Delta 링크로 조회 실패, 전체 동기화로 폴백");
            // Delta 링크가 만료된 경우 전체 동기화로 폴백
            return await GetInitialEventsAsync(client, DateTime.Today.AddMonths(-3), DateTime.Today.AddMonths(6));
        }
    }

    /// <summary>
    /// 초기 전체 이벤트 조회 (CalendarView 사용)
    /// </summary>
    private async Task<CalendarDeltaResult> GetInitialEventsAsync(
        GraphServiceClient client,
        DateTime startDate,
        DateTime endDate)
    {
        var result = new CalendarDeltaResult();
        var allEvents = new List<Event>();

        try
        {
            var startDateTime = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDateTime = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Delta를 지원하는 CalendarView 사용
            var response = await client.Me.CalendarView.Delta.GetAsDeltaGetResponseAsync(config =>
            {
                config.QueryParameters.StartDateTime = startDateTime;
                config.QueryParameters.EndDateTime = endDateTime;
                // Select를 명시적으로 지정
                config.QueryParameters.Select = new[]
                {
                    "id", "subject", "body", "bodyPreview", "start", "end",
                    "location", "organizer", "attendees", "isAllDay",
                    "importance", "sensitivity", "showAs", "type",
                    "recurrence", "iCalUId", "seriesMasterId",
                    "isOnlineMeeting", "onlineMeeting", "onlineMeetingProvider",
                    "reminderMinutesBeforeStart", "isReminderOn",
                    "categories", "webLink", "createdDateTime", "lastModifiedDateTime",
                    "responseStatus", "isCancelled"
                };
            });

            while (response != null)
            {
                if (response.Value != null)
                {
                    allEvents.AddRange(response.Value);
                }

                // 다음 페이지가 있으면 계속
                if (response.OdataNextLink != null)
                {
                    var request = new Microsoft.Graph.Me.CalendarView.Delta.DeltaRequestBuilder(
                        response.OdataNextLink, client.RequestAdapter);
                    response = await request.GetAsDeltaGetResponseAsync();
                }
                else
                {
                    // Delta 링크 저장
                    result.DeltaLink = response.OdataDeltaLink;
                    break;
                }
            }

            result.Events = allEvents;
            result.AddedCount = allEvents.Count;

            _logger.Debug("초기 동기화 완료: {Count}개 이벤트 ({Start} ~ {End})",
                allEvents.Count, startDate.ToShortDateString(), endDate.ToShortDateString());

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "초기 이벤트 동기화 실패");
            throw;
        }
    }

    /// <summary>
    /// 특정 캘린더의 이벤트 Delta 조회
    /// </summary>
    /// <param name="calendarId">캘린더 ID</param>
    /// <param name="deltaLink">이전 Delta 링크</param>
    /// <param name="startDate">동기화 시작 날짜</param>
    /// <param name="endDate">동기화 종료 날짜</param>
    /// <returns>변경된 이벤트 목록</returns>
    public async Task<CalendarDeltaResult> GetCalendarEventsDeltaAsync(
        string calendarId,
        string? deltaLink = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var result = new CalendarDeltaResult();

        try
        {
            var client = _authService.GetGraphClient();

            var syncStart = startDate ?? DateTime.Today.AddMonths(-3);
            var syncEnd = endDate ?? DateTime.Today.AddMonths(6);

            if (!string.IsNullOrEmpty(deltaLink))
            {
                // 특정 캘린더의 Delta 조회
                result = await GetCalendarDeltaWithLinkAsync(client, calendarId, deltaLink);
            }
            else
            {
                // 특정 캘린더의 초기 동기화
                result = await GetCalendarInitialEventsAsync(client, calendarId, syncStart, syncEnd);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "캘린더 {CalendarId} Delta 조회 실패", calendarId);
            throw;
        }
    }

    /// <summary>
    /// 특정 캘린더의 Delta 링크로 변경분 조회
    /// </summary>
    private async Task<CalendarDeltaResult> GetCalendarDeltaWithLinkAsync(
        GraphServiceClient client,
        string calendarId,
        string deltaLink)
    {
        var result = new CalendarDeltaResult();
        var allEvents = new List<Event>();
        var deletedIds = new List<string>();

        try
        {
            var request = new Microsoft.Graph.Me.Calendars.Item.CalendarView.Delta.DeltaRequestBuilder(
                deltaLink, client.RequestAdapter);
            var response = await request.GetAsDeltaGetResponseAsync();

            while (response != null)
            {
                if (response.Value != null)
                {
                    foreach (var evt in response.Value)
                    {
                        if (evt.AdditionalData?.ContainsKey("@removed") == true)
                        {
                            if (!string.IsNullOrEmpty(evt.Id))
                            {
                                deletedIds.Add(evt.Id);
                            }
                        }
                        else
                        {
                            allEvents.Add(evt);
                        }
                    }
                }

                if (response.OdataNextLink != null)
                {
                    request = new Microsoft.Graph.Me.Calendars.Item.CalendarView.Delta.DeltaRequestBuilder(
                        response.OdataNextLink, client.RequestAdapter);
                    response = await request.GetAsDeltaGetResponseAsync();
                }
                else
                {
                    result.DeltaLink = response.OdataDeltaLink;
                    break;
                }
            }

            result.Events = allEvents;
            result.DeletedEventIds = deletedIds;
            result.AddedCount = allEvents.Count;
            result.DeletedCount = deletedIds.Count;

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "캘린더 {CalendarId} Delta 링크 조회 실패", calendarId);
            return await GetCalendarInitialEventsAsync(client, calendarId,
                DateTime.Today.AddMonths(-3), DateTime.Today.AddMonths(6));
        }
    }

    /// <summary>
    /// 특정 캘린더의 초기 이벤트 조회
    /// </summary>
    private async Task<CalendarDeltaResult> GetCalendarInitialEventsAsync(
        GraphServiceClient client,
        string calendarId,
        DateTime startDate,
        DateTime endDate)
    {
        var result = new CalendarDeltaResult();
        var allEvents = new List<Event>();

        try
        {
            var startDateTime = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDateTime = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            var response = await client.Me.Calendars[calendarId].CalendarView.Delta.GetAsDeltaGetResponseAsync(config =>
            {
                config.QueryParameters.StartDateTime = startDateTime;
                config.QueryParameters.EndDateTime = endDateTime;
            });

            while (response != null)
            {
                if (response.Value != null)
                {
                    allEvents.AddRange(response.Value);
                }

                if (response.OdataNextLink != null)
                {
                    var request = new Microsoft.Graph.Me.Calendars.Item.CalendarView.Delta.DeltaRequestBuilder(
                        response.OdataNextLink, client.RequestAdapter);
                    response = await request.GetAsDeltaGetResponseAsync();
                }
                else
                {
                    result.DeltaLink = response.OdataDeltaLink;
                    break;
                }
            }

            result.Events = allEvents;
            result.AddedCount = allEvents.Count;

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "캘린더 {CalendarId} 초기 동기화 실패", calendarId);
            throw;
        }
    }

    /// <summary>
    /// 반복 일정의 인스턴스 조회
    /// </summary>
    /// <param name="seriesMasterId">반복 일정 마스터 ID</param>
    /// <param name="startDate">시작 날짜</param>
    /// <param name="endDate">종료 날짜</param>
    /// <returns>인스턴스 목록</returns>
    public async Task<List<Event>> GetRecurringEventInstancesAsync(
        string seriesMasterId,
        DateTime startDate,
        DateTime endDate)
    {
        try
        {
            var client = _authService.GetGraphClient();

            var startDateTime = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDateTime = endDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            var response = await client.Me.Calendar.Events[seriesMasterId].Instances.GetAsync(config =>
            {
                config.QueryParameters.StartDateTime = startDateTime;
                config.QueryParameters.EndDateTime = endDateTime;
            });

            var instances = response?.Value?.ToList() ?? new List<Event>();

            _logger.Debug("반복 일정 {SeriesMasterId} 인스턴스 {Count}개 조회",
                seriesMasterId, instances.Count);

            return instances;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "반복 일정 인스턴스 조회 실패: {SeriesMasterId}", seriesMasterId);
            return new List<Event>();
        }
    }

    /// <summary>
    /// Graph API Event를 CalendarEvent 모델로 변환
    /// </summary>
    /// <param name="graphEvent">Graph API Event</param>
    /// <param name="accountEmail">계정 이메일</param>
    /// <param name="calendarId">캘린더 ID (선택)</param>
    /// <param name="calendarName">캘린더 이름 (선택)</param>
    /// <returns>CalendarEvent 모델</returns>
    public mAIx.Models.CalendarEvent ConvertToCalendarEvent(
        Event graphEvent,
        string accountEmail,
        string? calendarId = null,
        string? calendarName = null)
    {
        var calendarEvent = new mAIx.Models.CalendarEvent
        {
            GraphId = graphEvent.Id,
            ICalUId = graphEvent.ICalUId,
            SeriesMasterId = graphEvent.SeriesMasterId,
            Subject = graphEvent.Subject ?? string.Empty,
            Body = graphEvent.Body?.Content,
            BodyContentType = graphEvent.Body?.ContentType?.ToString(),
            Location = graphEvent.Location?.DisplayName,
            StartDateTime = ParseDateTimeTimeZone(graphEvent.Start) ?? DateTime.UtcNow,
            EndDateTime = ParseDateTimeTimeZone(graphEvent.End) ?? DateTime.UtcNow.AddHours(1),
            StartTimeZone = graphEvent.Start?.TimeZone,
            EndTimeZone = graphEvent.End?.TimeZone,
            IsAllDay = graphEvent.IsAllDay ?? false,
            IsRecurring = graphEvent.Recurrence != null,
            ShowAs = graphEvent.ShowAs?.ToString(),
            ResponseStatus = graphEvent.ResponseStatus?.Response?.ToString(),
            Importance = graphEvent.Importance?.ToString(),
            Sensitivity = graphEvent.Sensitivity?.ToString(),
            IsOnlineMeeting = graphEvent.IsOnlineMeeting ?? false,
            OnlineMeetingUrl = graphEvent.OnlineMeeting?.JoinUrl,
            OnlineMeetingProvider = graphEvent.OnlineMeetingProvider?.ToString(),
            ReminderMinutesBeforeStart = graphEvent.ReminderMinutesBeforeStart ?? 15,
            IsReminderOn = graphEvent.IsReminderOn ?? true,
            OrganizerEmail = graphEvent.Organizer?.EmailAddress?.Address,
            OrganizerName = graphEvent.Organizer?.EmailAddress?.Name,
            CalendarId = calendarId,
            CalendarName = calendarName,
            AccountEmail = accountEmail,
            WebLink = graphEvent.WebLink,
            LastModifiedDateTime = graphEvent.LastModifiedDateTime?.UtcDateTime,
            CreatedDateTime = graphEvent.CreatedDateTime?.UtcDateTime,
            IsCancelled = graphEvent.IsCancelled ?? false,
            EventType = graphEvent.Type?.ToString(),
            SyncedAt = DateTime.UtcNow
        };

        // 반복 패턴 JSON 변환
        if (graphEvent.Recurrence != null)
        {
            calendarEvent.RecurrencePattern = System.Text.Json.JsonSerializer.Serialize(
                graphEvent.Recurrence.Pattern);
            calendarEvent.RecurrenceRange = System.Text.Json.JsonSerializer.Serialize(
                graphEvent.Recurrence.Range);
        }

        // 참석자 JSON 변환
        if (graphEvent.Attendees?.Any() == true)
        {
            var attendeesList = graphEvent.Attendees.Select(a => new
            {
                email = a.EmailAddress?.Address,
                name = a.EmailAddress?.Name,
                type = a.Type?.ToString(),
                status = a.Status?.Response?.ToString()
            });
            calendarEvent.Attendees = System.Text.Json.JsonSerializer.Serialize(attendeesList);
        }

        // 카테고리 JSON 변환
        if (graphEvent.Categories?.Any() == true)
        {
            calendarEvent.Categories = System.Text.Json.JsonSerializer.Serialize(graphEvent.Categories);
        }

        return calendarEvent;
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

/// <summary>
/// 일정 생성/수정용 DTO
/// </summary>
public class EventCreateRequest
{
    public string Subject { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public bool IsAllDay { get; set; }
    public string? Body { get; set; }
    public bool IsOnlineMeeting { get; set; }
    public List<string>? Attendees { get; set; }
    public List<string>? OptionalAttendees { get; set; }
    public int ReminderMinutesBefore { get; set; } = 15;
    public string? RecurrencePattern { get; set; } // None, Daily, Weekly, Monthly, Yearly
    public List<string>? Categories { get; set; }
    public string? ShowAs { get; set; } // free, tentative, busy, oof, workingElsewhere
}

/// <summary>
/// Delta Query 결과
/// </summary>
public class CalendarDeltaResult
{
    public List<Event> Events { get; set; } = new();
    public List<string> DeletedEventIds { get; set; } = new();
    public string? DeltaLink { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DeletedCount { get; set; }
}

/// <summary>
/// 캘린더 정보 DTO
/// </summary>
public class CalendarInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public bool IsDefaultCalendar { get; set; }
    public bool CanEdit { get; set; }
    public string? Owner { get; set; }
}
