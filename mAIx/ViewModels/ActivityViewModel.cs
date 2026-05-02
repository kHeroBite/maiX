using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Services;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.ViewModels;

/// <summary>
/// Activity ViewModel - 활동 피드 관리 + 5분 폴링 + 탭 활성화 새로고침
/// </summary>
public partial class ActivityViewModel : ViewModelBase
{
    private readonly ILogger _log = Log.ForContext<ActivityViewModel>();
    private readonly GraphActivityService _activityService;
    private CrossTabIntegrationService? _crossTabService;
    private DispatcherTimer? _pollingTimer;
    private bool _isTabActive;

    [ObservableProperty]
    private ObservableCollection<ActivityItemViewModel> _activities = new();

    [ObservableProperty]
    private ObservableCollection<ActivityItemViewModel> _filteredActivities = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFilter))]
    [NotifyPropertyChangedFor(nameof(IsMailFilter))]
    [NotifyPropertyChangedFor(nameof(IsChatFilter))]
    [NotifyPropertyChangedFor(nameof(IsFileFilter))]
    private string _currentFilter = "all";

    [ObservableProperty]
    private ActivityItemViewModel? _selectedActivity;

    [ObservableProperty]
    private int _unreadCount;

    public bool IsAllFilter => CurrentFilter == "all";
    public bool IsMailFilter => CurrentFilter == "mail";
    public bool IsChatFilter => CurrentFilter == "chat";
    public bool IsFileFilter => CurrentFilter == "file";

    public ActivityViewModel(GraphActivityService activityService)
    {
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
    }

    /// <summary>
    /// 크로스탭 서비스 설정 (MainWindow에서 주입)
    /// </summary>
    public void SetCrossTabService(CrossTabIntegrationService service)
    {
        _crossTabService = service;
    }

    /// <summary>
    /// 활동 피드 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadActivitiesAsync()
    {
        await ExecuteAsync(async () =>
        {
            var activities = await _activityService.GetAllActivitiesAsync(50);

            Activities.Clear();
            foreach (var activity in activities)
            {
                Activities.Add(new ActivityItemViewModel
                {
                    Id = activity.Id,
                    Type = activity.Type.ToString(),
                    Title = activity.Title,
                    Description = activity.Description,
                    Timestamp = activity.Timestamp,
                    TimestampDisplay = activity.TimestampDisplay,
                    IsRead = activity.IsRead,
                    SourceId = activity.SourceId,
                    TypeIcon = activity.TypeIcon,
                    TypeColor = activity.TypeColor
                });
            }

            UnreadCount = Activities.Count(a => !a.IsRead);
            ApplyFilter();
            _log.Information("활동 피드 로드 완료: {Count}개 (미읽음: {UnreadCount})", Activities.Count, UnreadCount);
        }, "활동 피드 로드 실패");
    }

    /// <summary>
    /// 메일 활동만 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadMailActivitiesAsync()
    {
        await ExecuteAsync(async () =>
        {
            var activities = await _activityService.GetRecentMailActivityAsync(30);

            Activities.Clear();
            foreach (var activity in activities)
            {
                Activities.Add(new ActivityItemViewModel
                {
                    Id = activity.Id,
                    Type = activity.Type.ToString(),
                    Title = activity.Title,
                    Description = activity.Description,
                    Timestamp = activity.Timestamp,
                    TimestampDisplay = activity.TimestampDisplay,
                    IsRead = activity.IsRead,
                    SourceId = activity.SourceId,
                    TypeIcon = activity.TypeIcon,
                    TypeColor = activity.TypeColor
                });
            }

            FilteredActivities = new ObservableCollection<ActivityItemViewModel>(Activities);
            _log.Information("메일 활동 로드 완료: {Count}개", Activities.Count);
        }, "메일 활동 로드 실패");
    }

    /// <summary>
    /// 필터 설정
    /// </summary>
    [RelayCommand]
    public void SetFilter(string filter)
    {
        CurrentFilter = filter;
        ApplyFilter();
    }

    /// <summary>
    /// 필터 적용
    /// </summary>
    private void ApplyFilter()
    {
        if (CurrentFilter == "all")
        {
            FilteredActivities = new ObservableCollection<ActivityItemViewModel>(Activities);
        }
        else
        {
            var filtered = Activities.Where(a =>
                a.Type.Equals(CurrentFilter, StringComparison.OrdinalIgnoreCase) ||
                (CurrentFilter == "mail" && (a.Type == "Email" || a.Type == "Reply")) ||
                (CurrentFilter == "chat" && (a.Type == "Chat" || a.Type == "Mention" || a.Type == "Reaction")) ||
                (CurrentFilter == "file" && a.Type == "File"));
            FilteredActivities = new ObservableCollection<ActivityItemViewModel>(filtered);
        }
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadActivitiesAsync();
    }

    /// <summary>
    /// 활동 클릭 → 원본 탭 이동
    /// </summary>
    [RelayCommand]
    public void OpenActivity(ActivityItemViewModel activity)
    {
        if (activity == null) return;
        _log.Debug("활동 열기: {Type} - {Title}", activity.Type, activity.Title);
    }

    /// <summary>
    /// 활동 피드에서 원본 항목으로 이동 (크로스탭)
    /// </summary>
    public async Task NavigateToSourceAsync(ActivityItemViewModel activity, Action<string> navigateCallback)
    {
        if (activity == null || _crossTabService == null) return;

        var sourceItem = new ActivityItem
        {
            Id = activity.Id,
            Type = Enum.TryParse<ActivityType>(activity.Type, out var t) ? t : ActivityType.Other,
            Title = activity.Title,
            SourceId = activity.SourceId
        };

        await _crossTabService.NavigateToActivitySourceAsync(sourceItem, navigateCallback);
    }

    /// <summary>
    /// 탭 활성화 시 호출 — 자동 새로고침
    /// </summary>
    public async Task OnTabActivatedAsync()
    {
        _isTabActive = true;
        if (Activities.Count == 0)
        {
            await LoadActivitiesAsync();
        }
        StartPolling();
    }

    /// <summary>
    /// 탭 비활성화 시 호출
    /// </summary>
    public void OnTabDeactivated()
    {
        _isTabActive = false;
        StopPolling();
    }

    /// <summary>
    /// 5분 폴링 시작
    /// </summary>
    private void StartPolling()
    {
        if (_pollingTimer != null) return;

        _pollingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _pollingTimer.Tick += async (_, _) =>
        {
            try
            {
                if (_isTabActive)
                {
                    _log.Debug("활동 피드 폴링 새로고침");
                    await LoadActivitiesAsync();
                }
            }
            catch (Exception ex)
            {
                _log.Error($"[ActivityViewModel] 폴링 타이머 핸들러 실패: {ex}");
            }
        };
        _pollingTimer.Start();
        _log.Debug("활동 피드 폴링 시작 (5분 주기)");
    }

    /// <summary>
    /// 폴링 중지
    /// </summary>
    private void StopPolling()
    {
        _pollingTimer?.Stop();
        _pollingTimer = null;
        _log.Debug("활동 피드 폴링 중지");
    }
}

/// <summary>
/// 활동 아이템 ViewModel
/// </summary>
public partial class ActivityItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _type = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private DateTime _timestamp;

    [ObservableProperty]
    private string _timestampDisplay = string.Empty;

    [ObservableProperty]
    private bool _isRead;

    [ObservableProperty]
    private string? _sourceId;

    [ObservableProperty]
    private string _typeIcon = "Alert24";

    [ObservableProperty]
    private string _typeColor = "#808080";

    /// <summary>
    /// 날짜 그룹 표시 (오늘/어제/이번 주)
    /// </summary>
    public string DateGroup
    {
        get
        {
            var today = DateTime.Today;
            if (Timestamp.Date == today) return "오늘";
            if (Timestamp.Date == today.AddDays(-1)) return "어제";
            if (Timestamp.Date >= today.AddDays(-7)) return "이번 주";
            return Timestamp.ToString("yyyy년 M월 d일");
        }
    }
}
