using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Services.Graph;
using NLog;

namespace mAIx.ViewModels.Teams;

/// <summary>
/// 캘린더 이벤트 아이템 모델
/// </summary>
public partial class CalendarEvent : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string Location { get; set; } = string.Empty;
    public string OrganizerName { get; set; } = string.Empty;
    public int AttendeeCount { get; set; }
    public string ResponseStatus { get; set; } = "none"; // accepted/declined/tentative/none
    public bool IsAllDay { get; set; }
    public string Color { get; set; } = "#0078D4";

    /// <summary>시작~종료 시간 표시 (종일 이벤트 포함)</summary>
    public string TimeDisplay => IsAllDay
        ? "종일"
        : $"{Start:HH:mm} – {End:HH:mm}";
}

/// <summary>
/// 캘린더 날짜 셀 모델
/// </summary>
public partial class CalendarDay : ObservableObject
{
    public DateTime Date { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsToday => Date.Date == DateTime.Today;
    public ObservableCollection<CalendarEvent> Events { get; } = new();

    /// <summary>셀에 표시할 이벤트 (최대 3개)</summary>
    public IEnumerable<CalendarEvent> TopEvents => Events.Take(3);

    /// <summary>3개 초과 이벤트가 있는지 여부</summary>
    public bool HasMoreEvents => Events.Count > 3;

    /// <summary>표시 한도(3개) 초과 이벤트 수</summary>
    public int ExtraEventCount => Math.Max(0, Events.Count - 3);

    /// <summary>날짜 숫자 (1~31)</summary>
    public int DayNumber => Date.Day;
}

/// <summary>
/// Teams 채널 일정 탭 ViewModel — 월간 캘린더 뷰
/// </summary>
public partial class ChannelCalendarViewModel : ViewModelBase
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();
    private readonly GraphCalendarService _calendarService;

    [ObservableProperty] private string _channelId = string.Empty;
    [ObservableProperty] private string _teamId = string.Empty;
    [ObservableProperty] private DateTime _currentMonth = DateTime.Today;
    [ObservableProperty] private ObservableCollection<CalendarDay> _calendarDays = new();
    [ObservableProperty] private CalendarDay? _selectedDay;
    [ObservableProperty] private ObservableCollection<CalendarEvent> _selectedDayEvents = new();
    [ObservableProperty] private string _monthTitle = string.Empty;

    public ChannelCalendarViewModel(GraphCalendarService calendarService)
    {
        _calendarService = calendarService;
        UpdateMonthTitle();
    }

    /// <summary>채널 초기화 — teamId/channelId 설정 후 캘린더 로드</summary>
    public async Task InitializeAsync(string teamId, string channelId)
    {
        TeamId = teamId;
        ChannelId = channelId;
        await LoadCalendarAsync();
    }

    [RelayCommand]
    private async Task PreviousMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(-1);
        UpdateMonthTitle();
        await LoadCalendarAsync();
    }

    [RelayCommand]
    private async Task NextMonth()
    {
        CurrentMonth = CurrentMonth.AddMonths(1);
        UpdateMonthTitle();
        await LoadCalendarAsync();
    }

    [RelayCommand]
    private void SelectDay(CalendarDay? day)
    {
        SelectedDay = day;
        SelectedDayEvents.Clear();
        if (day == null) return;
        foreach (var ev in day.Events)
            SelectedDayEvents.Add(ev);
    }

    private async Task LoadCalendarAsync()
    {
        if (string.IsNullOrEmpty(TeamId)) return;

        await ExecuteAsync(async () =>
        {
            BuildCalendarGrid();
            // Graph API 연동 지점 — GraphCalendarService.GetCalendarEventsAsync 구현 시 교체
            await Task.CompletedTask;
            _log.Info("채널 캘린더 로드 완료: teamId={TeamId}, month={Month}", TeamId, CurrentMonth.ToString("yyyy-MM"));
        }, "캘린더 로드 실패");
    }

    /// <summary>
    /// 현재 월 기준 42칸(6주) 달력 그리드 생성
    /// </summary>
    private void BuildCalendarGrid()
    {
        CalendarDays.Clear();
        var firstDay = new DateTime(CurrentMonth.Year, CurrentMonth.Month, 1);
        // 일요일 시작 기준
        var startDate = firstDay.AddDays(-(int)firstDay.DayOfWeek);
        for (var i = 0; i < 42; i++)
        {
            var date = startDate.AddDays(i);
            CalendarDays.Add(new CalendarDay
            {
                Date = date,
                IsCurrentMonth = date.Month == CurrentMonth.Month
            });
        }
    }

    private void UpdateMonthTitle()
    {
        MonthTitle = CurrentMonth.ToString("yyyy년 M월");
    }
}
