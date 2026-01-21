using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mailX.Services.Graph;
using Serilog;

namespace mailX.ViewModels;

/// <summary>
/// Activity ViewModel - 활동 피드 관리
/// </summary>
public partial class ActivityViewModel : ViewModelBase
{
    private readonly GraphActivityService _activityService;
    private readonly ILogger _logger;

    /// <summary>
    /// 활동 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ActivityItemViewModel> _activities = new();

    /// <summary>
    /// 필터된 활동 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ActivityItemViewModel> _filteredActivities = new();

    /// <summary>
    /// 현재 필터
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFilter))]
    [NotifyPropertyChangedFor(nameof(IsMailFilter))]
    [NotifyPropertyChangedFor(nameof(IsChatFilter))]
    [NotifyPropertyChangedFor(nameof(IsFileFilter))]
    private string _currentFilter = "all";

    /// <summary>
    /// 선택된 활동
    /// </summary>
    [ObservableProperty]
    private ActivityItemViewModel? _selectedActivity;

    public bool IsAllFilter => CurrentFilter == "all";
    public bool IsMailFilter => CurrentFilter == "mail";
    public bool IsChatFilter => CurrentFilter == "chat";
    public bool IsFileFilter => CurrentFilter == "file";

    public ActivityViewModel(GraphActivityService activityService)
    {
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
        _logger = Log.ForContext<ActivityViewModel>();
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

            ApplyFilter();
            _logger.Information("활동 피드 로드 완료: {Count}개", Activities.Count);
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
            _logger.Information("메일 활동 로드 완료: {Count}개", Activities.Count);
        }, "메일 활동 로드 실패");
    }

    /// <summary>
    /// 필터 적용
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
                (CurrentFilter == "mail" && a.Type == "Email") ||
                (CurrentFilter == "chat" && a.Type == "Chat") ||
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
    /// 활동 클릭 (해당 소스로 이동)
    /// </summary>
    [RelayCommand]
    public void OpenActivity(ActivityItemViewModel activity)
    {
        if (activity == null) return;

        // 활동 타입에 따라 해당 화면으로 이동
        _logger.Debug("활동 열기: {Type} - {Title}", activity.Type, activity.Title);
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
}
