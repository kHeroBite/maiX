using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Models;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.Graph;
using Serilog;

// 모호한 참조 해결을 위한 별칭
using mAIxTodo = mAIx.Models.Todo;
using mAIxEmail = mAIx.Models.Email;
using mAIxMeetingInfo = mAIx.Services.Graph.MeetingInfo;
using mAIxCalendarEvent = mAIx.Models.CalendarEvent;

namespace mAIx.ViewModels;

/// <summary>
/// Calendar 뷰모델 - 일정 관리 및 마감일 연동
/// DB 캐싱 및 Graph API 동기화 지원
/// </summary>
public partial class CalendarViewModel : ViewModelBase
{
    private readonly GraphCalendarService _calendarService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// 일정 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<EventItemViewModel> _events = new();

    /// <summary>
    /// 선택된 일정
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEvent))]
    private EventItemViewModel? _selectedEvent;

    /// <summary>
    /// 현재 표시 모드 (Today, Week, Month)
    /// </summary>
    [ObservableProperty]
    private CalendarViewMode _viewMode = CalendarViewMode.Week;

    /// <summary>
    /// 오늘 날짜
    /// </summary>
    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    /// <summary>
    /// 마감일 연동된 일정 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<EventItemViewModel> _deadlineEvents = new();

    /// <summary>
    /// 선택된 일정이 있는지 여부
    /// </summary>
    public bool HasSelectedEvent => SelectedEvent != null;

    /// <summary>
    /// 오늘 일정 수
    /// </summary>
    public int TodayEventCount => Events.Count(e => e.StartDateTime?.Date == DateTime.Today);

    public CalendarViewModel(GraphCalendarService calendarService, IServiceProvider serviceProvider)
    {
        _calendarService = calendarService ?? throw new ArgumentNullException(nameof(calendarService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = Log.ForContext<CalendarViewModel>();
    }

    /// <summary>
    /// 뷰 모드 변경 시 일정 새로고침
    /// </summary>
    partial void OnViewModeChanged(CalendarViewMode value)
    {
        _ = LoadEventsAsync();
    }

    /// <summary>
    /// 선택된 날짜 변경 시 일정 새로고침
    /// </summary>
    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadEventsAsync();
    }

    /// <summary>
    /// 일정 목록 로드 (DB 우선, 필요시 API)
    /// </summary>
    [RelayCommand]
    public async Task LoadEventsAsync()
    {
        await ExecuteAsync(async () =>
        {
            // 먼저 DB에서 로드 시도
            var dbEvents = await LoadEventsFromDbAsync();

            if (dbEvents.Count > 0)
            {
                // DB에서 로드 성공
                Events.Clear();
                foreach (var evt in dbEvents)
                {
                    Events.Add(evt);
                }

                OnPropertyChanged(nameof(TodayEventCount));
                _logger.Information("일정 {Count}개 DB에서 로드 완료 (모드: {Mode})", Events.Count, ViewMode);
            }
            else
            {
                // DB에 없으면 API에서 로드
                await LoadEventsFromApiAsync();
            }
        }, "일정 로드 실패");
    }

    /// <summary>
    /// DB에서 일정 로드
    /// </summary>
    private async Task<List<EventItemViewModel>> LoadEventsFromDbAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();

        if (!graphAuthService.IsLoggedIn || string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
        {
            return new List<EventItemViewModel>();
        }

        var accountEmail = graphAuthService.CurrentUserEmail;

        DateTime startDate, endDate;
        switch (ViewMode)
        {
            case CalendarViewMode.Today:
                startDate = DateTime.Today;
                endDate = DateTime.Today.AddDays(1);
                break;

            case CalendarViewMode.Week:
                startDate = SelectedDate.AddDays(-(int)SelectedDate.DayOfWeek);
                endDate = startDate.AddDays(7);
                break;

            case CalendarViewMode.Month:
                startDate = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
                endDate = startDate.AddMonths(1);
                break;

            default:
                startDate = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
                endDate = startDate.AddDays(7);
                break;
        }

        var dbEvents = await dbContext.CalendarEvents
            .Where(e => e.AccountEmail == accountEmail
                && !e.IsDeleted
                && e.StartDateTime >= startDate
                && e.StartDateTime < endDate)
            .OrderBy(e => e.StartDateTime)
            .ThenBy(e => e.Subject)
            .ThenBy(e => e.Id)
            .ToListAsync();

        return dbEvents.Select(MapFromDbEvent).ToList();
    }

    /// <summary>
    /// API에서 일정 로드 (폴백)
    /// </summary>
    private async Task LoadEventsFromApiAsync()
    {
        IEnumerable<Event> events;

        switch (ViewMode)
        {
            case CalendarViewMode.Today:
                events = await _calendarService.GetTodayEventsAsync();
                break;

            case CalendarViewMode.Week:
                events = await _calendarService.GetThisWeekEventsAsync();
                break;

            case CalendarViewMode.Month:
                var startOfMonth = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
                var endOfMonth = startOfMonth.AddMonths(1);
                events = await _calendarService.GetEventsAsync(startOfMonth, endOfMonth);
                break;

            default:
                events = await _calendarService.GetThisWeekEventsAsync();
                break;
        }

        Events.Clear();
        foreach (var evt in events)
        {
            var eventItem = MapToEventItem(evt);
            Events.Add(eventItem);
        }

        OnPropertyChanged(nameof(TodayEventCount));
        _logger.Information("일정 {Count}개 API에서 로드 완료 (모드: {Mode})", Events.Count, ViewMode);
    }

    /// <summary>
    /// 동기화 완료 시 UI 새로고침
    /// </summary>
    public void OnCalendarEventsSynced(int added, int updated, int deleted)
    {
        _logger.Information("캘린더 동기화 완료: 추가 {Added}, 수정 {Updated}, 삭제 {Deleted}", added, updated, deleted);

        // UI 스레드에서 새로고침
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await LoadEventsAsync();
        });
    }

    /// <summary>
    /// DB CalendarEvent를 EventItemViewModel로 변환
    /// </summary>
    private EventItemViewModel MapFromDbEvent(mAIxCalendarEvent dbEvent)
    {
        var categories = new List<string>();
        if (!string.IsNullOrEmpty(dbEvent.Categories))
        {
            try
            {
                categories = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dbEvent.Categories) ?? new List<string>();
            }
            catch
            {
                // JSON 파싱 실패 시 빈 목록
            }
        }

        return new EventItemViewModel
        {
            Id = dbEvent.GraphId ?? dbEvent.Id.ToString(),
            Subject = dbEvent.Subject,
            Location = dbEvent.Location,
            StartDateTime = dbEvent.StartDateTime,
            EndDateTime = dbEvent.EndDateTime,
            IsAllDay = dbEvent.IsAllDay,
            Importance = dbEvent.Importance ?? "Normal",
            OrganizerName = dbEvent.OrganizerName,
            OrganizerEmail = dbEvent.OrganizerEmail,
            BodyPreview = dbEvent.Body?.Length > 200 ? dbEvent.Body.Substring(0, 200) + "..." : dbEvent.Body,
            WebLink = dbEvent.WebLink,
            IsOnlineMeeting = dbEvent.IsOnlineMeeting,
            OnlineMeetingUrl = dbEvent.OnlineMeetingUrl,
            Categories = categories,
            ResponseStatus = dbEvent.ResponseStatus,
            IsRecurring = dbEvent.IsRecurring
        };
    }

    /// <summary>
    /// 오늘 일정만 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadTodayEventsAsync()
    {
        ViewMode = CalendarViewMode.Today;
        await LoadEventsAsync();
    }

    /// <summary>
    /// 이번 주 일정 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadWeekEventsAsync()
    {
        ViewMode = CalendarViewMode.Week;
        await LoadEventsAsync();
    }

    /// <summary>
    /// 이메일 마감일을 일정으로 생성
    /// </summary>
    /// <param name="email">이메일 정보</param>
    [RelayCommand]
    public async Task CreateDeadlineEventAsync(mAIxEmail email)
    {
        if (email == null || !email.Deadline.HasValue)
        {
            ErrorMessage = "마감일이 있는 이메일만 일정으로 생성할 수 있습니다.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            var createdEvent = await _calendarService.CreateDeadlineEventFromEmailAsync(email);

            if (createdEvent != null)
            {
                var eventItem = MapToEventItem(createdEvent);
                eventItem.IsDeadline = true;
                Events.Add(eventItem);
                DeadlineEvents.Add(eventItem);

                _logger.Information("마감일 일정 생성 완료: {Subject}", email.Subject);
            }
        }, "마감일 일정 생성 실패");
    }

    /// <summary>
    /// 할일을 일정으로 변환
    /// </summary>
    /// <param name="todo">할일 정보</param>
    [RelayCommand]
    public async Task CreateEventFromTodoAsync(mAIxTodo todo)
    {
        if (todo == null)
            return;

        await ExecuteAsync(async () =>
        {
            var createdEvent = await _calendarService.CreateEventFromTodoAsync(todo);

            if (createdEvent != null)
            {
                var eventItem = MapToEventItem(createdEvent);
                eventItem.IsTodo = true;
                Events.Add(eventItem);

                _logger.Information("할일 일정 생성 완료: {Content}",
                    todo.Content.Substring(0, Math.Min(30, todo.Content.Length)));
            }
        }, "할일 일정 생성 실패");
    }

    /// <summary>
    /// 미팅 초대 파싱
    /// </summary>
    /// <param name="eventId">이벤트 ID</param>
    [RelayCommand]
    public async Task<mAIxMeetingInfo?> ParseMeetingInviteAsync(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            return null;

        return await ExecuteAsync(async () =>
        {
            var meetingInfo = await _calendarService.ParseMeetingInviteAsync(eventId);
            _logger.Debug("미팅 정보 파싱: {Subject}", meetingInfo?.Subject);
            return meetingInfo;
        }, "미팅 초대 파싱 실패");
    }

    /// <summary>
    /// 일정 응답 (수락)
    /// </summary>
    [RelayCommand]
    public async Task AcceptEventAsync()
    {
        if (SelectedEvent == null)
            return;

        await RespondToEventAsync("accept");
    }

    /// <summary>
    /// 일정 응답 (거절)
    /// </summary>
    [RelayCommand]
    public async Task DeclineEventAsync()
    {
        if (SelectedEvent == null)
            return;

        await RespondToEventAsync("decline");
    }

    /// <summary>
    /// 일정 응답 (미정)
    /// </summary>
    [RelayCommand]
    public async Task TentativeEventAsync()
    {
        if (SelectedEvent == null)
            return;

        await RespondToEventAsync("tentative");
    }

    /// <summary>
    /// 일정 응답 공통 처리
    /// </summary>
    private async Task RespondToEventAsync(string response)
    {
        if (SelectedEvent == null)
            return;

        await ExecuteAsync(async () =>
        {
            await _calendarService.RespondToEventAsync(SelectedEvent.Id, response);
            SelectedEvent.ResponseStatus = response;
            _logger.Information("일정 응답 완료: {Subject} - {Response}", SelectedEvent.Subject, response);
        }, "일정 응답 실패");
    }

    /// <summary>
    /// 다음 기간으로 이동
    /// </summary>
    [RelayCommand]
    public void NavigateNext()
    {
        switch (ViewMode)
        {
            case CalendarViewMode.Today:
                SelectedDate = SelectedDate.AddDays(1);
                break;
            case CalendarViewMode.Week:
                SelectedDate = SelectedDate.AddDays(7);
                break;
            case CalendarViewMode.Month:
                SelectedDate = SelectedDate.AddMonths(1);
                break;
        }
    }

    /// <summary>
    /// 이전 기간으로 이동
    /// </summary>
    [RelayCommand]
    public void NavigatePrevious()
    {
        switch (ViewMode)
        {
            case CalendarViewMode.Today:
                SelectedDate = SelectedDate.AddDays(-1);
                break;
            case CalendarViewMode.Week:
                SelectedDate = SelectedDate.AddDays(-7);
                break;
            case CalendarViewMode.Month:
                SelectedDate = SelectedDate.AddMonths(-1);
                break;
        }
    }

    /// <summary>
    /// 오늘로 이동
    /// </summary>
    [RelayCommand]
    public void NavigateToday()
    {
        SelectedDate = DateTime.Today;
    }

    /// <summary>
    /// Graph Event를 EventItemViewModel로 변환
    /// </summary>
    private EventItemViewModel MapToEventItem(Event evt)
    {
        return new EventItemViewModel
        {
            Id = evt.Id ?? string.Empty,
            Subject = evt.Subject ?? "제목 없음",
            Location = evt.Location?.DisplayName,
            StartDateTime = ParseDateTimeTimeZone(evt.Start),
            EndDateTime = ParseDateTimeTimeZone(evt.End),
            IsAllDay = evt.IsAllDay ?? false,
            Importance = evt.Importance?.ToString() ?? "Normal",
            OrganizerName = evt.Organizer?.EmailAddress?.Name,
            OrganizerEmail = evt.Organizer?.EmailAddress?.Address,
            BodyPreview = evt.BodyPreview,
            WebLink = evt.WebLink,
            IsOnlineMeeting = evt.IsOnlineMeeting ?? false,
            OnlineMeetingUrl = evt.OnlineMeeting?.JoinUrl,
            Categories = evt.Categories?.ToList() ?? new System.Collections.Generic.List<string>()
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
}

/// <summary>
/// 캘린더 표시 모드
/// </summary>
public enum CalendarViewMode
{
    Today,
    Week,
    Month
}

/// <summary>
/// 일정 아이템 뷰모델
/// </summary>
public partial class EventItemViewModel : ObservableObject
{
    /// <summary>
    /// 이벤트 ID
    /// </summary>
    [ObservableProperty]
    private string _id = string.Empty;

    /// <summary>
    /// 제목
    /// </summary>
    [ObservableProperty]
    private string _subject = string.Empty;

    /// <summary>
    /// 장소
    /// </summary>
    [ObservableProperty]
    private string? _location;

    /// <summary>
    /// 시작 시간
    /// </summary>
    [ObservableProperty]
    private DateTime? _startDateTime;

    /// <summary>
    /// 종료 시간
    /// </summary>
    [ObservableProperty]
    private DateTime? _endDateTime;

    /// <summary>
    /// 종일 일정 여부
    /// </summary>
    [ObservableProperty]
    private bool _isAllDay;

    /// <summary>
    /// 중요도
    /// </summary>
    [ObservableProperty]
    private string _importance = "Normal";

    /// <summary>
    /// 주최자 이름
    /// </summary>
    [ObservableProperty]
    private string? _organizerName;

    /// <summary>
    /// 주최자 이메일
    /// </summary>
    [ObservableProperty]
    private string? _organizerEmail;

    /// <summary>
    /// 본문 미리보기
    /// </summary>
    [ObservableProperty]
    private string? _bodyPreview;

    /// <summary>
    /// 웹 링크
    /// </summary>
    [ObservableProperty]
    private string? _webLink;

    /// <summary>
    /// 온라인 미팅 여부
    /// </summary>
    [ObservableProperty]
    private bool _isOnlineMeeting;

    /// <summary>
    /// 온라인 미팅 URL
    /// </summary>
    [ObservableProperty]
    private string? _onlineMeetingUrl;

    /// <summary>
    /// 카테고리 목록
    /// </summary>
    [ObservableProperty]
    private System.Collections.Generic.List<string> _categories = new();

    /// <summary>
    /// 응답 상태 (accept, decline, tentative)
    /// </summary>
    [ObservableProperty]
    private string? _responseStatus;

    /// <summary>
    /// 마감일 일정인지 여부
    /// </summary>
    [ObservableProperty]
    private bool _isDeadline;

    /// <summary>
    /// 할일 일정인지 여부
    /// </summary>
    [ObservableProperty]
    private bool _isTodo;

    /// <summary>
    /// 반복 일정인지 여부
    /// </summary>
    [ObservableProperty]
    private bool _isRecurring;

    /// <summary>
    /// 시간 표시 문자열
    /// </summary>
    public string TimeDisplay
    {
        get
        {
            if (IsAllDay)
                return "종일";

            if (!StartDateTime.HasValue)
                return string.Empty;

            var start = StartDateTime.Value.ToString("HH:mm");

            if (EndDateTime.HasValue)
            {
                var end = EndDateTime.Value.ToString("HH:mm");
                return $"{start} - {end}";
            }

            return start;
        }
    }

    /// <summary>
    /// 날짜 표시 문자열
    /// </summary>
    public string DateDisplay
    {
        get
        {
            if (!StartDateTime.HasValue)
                return string.Empty;

            var today = DateTime.Today;
            var eventDate = StartDateTime.Value.Date;

            if (eventDate == today)
                return "오늘";
            if (eventDate == today.AddDays(1))
                return "내일";
            if (eventDate == today.AddDays(-1))
                return "어제";

            return StartDateTime.Value.ToString("M월 d일 (ddd)");
        }
    }

    /// <summary>
    /// 소요 시간 (분)
    /// </summary>
    public int DurationMinutes
    {
        get
        {
            if (!StartDateTime.HasValue || !EndDateTime.HasValue)
                return 0;

            return (int)(EndDateTime.Value - StartDateTime.Value).TotalMinutes;
        }
    }

    /// <summary>
    /// 소요 시간 표시 문자열
    /// </summary>
    public string DurationDisplay
    {
        get
        {
            if (IsAllDay)
                return "종일";

            var minutes = DurationMinutes;

            if (minutes < 60)
                return $"{minutes}분";

            var hours = minutes / 60;
            var remainingMinutes = minutes % 60;

            if (remainingMinutes == 0)
                return $"{hours}시간";

            return $"{hours}시간 {remainingMinutes}분";
        }
    }

    /// <summary>
    /// 중요 일정 여부
    /// </summary>
    public bool IsImportant => Importance?.Equals("High", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// 진행 중인 일정인지 여부
    /// </summary>
    public bool IsOngoing
    {
        get
        {
            if (!StartDateTime.HasValue || !EndDateTime.HasValue)
                return false;

            var now = DateTime.Now;
            return now >= StartDateTime.Value && now <= EndDateTime.Value;
        }
    }

    /// <summary>
    /// 임박한 일정인지 여부 (1시간 이내)
    /// </summary>
    public bool IsUpcoming
    {
        get
        {
            if (!StartDateTime.HasValue)
                return false;

            var now = DateTime.Now;
            var diff = StartDateTime.Value - now;

            return diff > TimeSpan.Zero && diff <= TimeSpan.FromHours(1);
        }
    }
}
