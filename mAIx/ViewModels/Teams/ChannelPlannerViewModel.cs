using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using mAIx.Services.Graph;

namespace mAIx.ViewModels.Teams;

/// <summary>
/// Planner 작업 항목 모델
/// </summary>
public partial class PlannerTask : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string BucketId { get; set; } = string.Empty;

    [ObservableProperty]
    private string _bucketName = string.Empty;

    public string? AssigneeName { get; set; }
    public string? AssigneeInitials { get; set; }
    public DateTime? DueDate { get; set; }

    /// <summary>우선순위: urgent / important / medium / low</summary>
    public string Priority { get; set; } = "medium";

    /// <summary>우선순위 표시 레이블</summary>
    public string PriorityLabel => Priority switch
    {
        "urgent"    => "긴급",
        "important" => "중요",
        "medium"    => "보통",
        "low"       => "낮음",
        _           => "보통"
    };

    /// <summary>우선순위 색상 (16진수 문자열) — XAML에서 SolidColorBrush.Color에 바인딩 불가, 직접 Color로 변환 필요</summary>
    public string PriorityColor => Priority switch
    {
        "urgent"    => "#D13438",
        "important" => "#CA5010",
        "medium"    => "#0078D4",
        _           => "#767676"
    };

    [ObservableProperty]
    private int _percentComplete;

    public bool IsCompleted => PercentComplete == 100;
}

/// <summary>
/// Planner 버킷(열) 모델 — 할 일 / 진행 중 / 완료 등
/// </summary>
public partial class PlannerBucket : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public ObservableCollection<PlannerTask> Tasks { get; } = new();

    /// <summary>작업 수 배지용 (Tasks 변경 시 자동 갱신되지 않으므로 명시적 호출 필요)</summary>
    public int TaskCount => Tasks.Count;

    /// <summary>TaskCount 바인딩 갱신 트리거</summary>
    public void RefreshCount() => OnPropertyChanged(nameof(TaskCount));
}

/// <summary>
/// 채널 Planner 탭 ViewModel — 칸반 보드 + 목록 보기
/// </summary>
public partial class ChannelPlannerViewModel : ObservableObject
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();
    private readonly GraphTeamsService _teamsService;

    // 필터 상태
    private string _assigneeFilter = "all";
    private string _priorityFilter = "all";

    // 원본 데이터 (필터 적용 전)
    private readonly ObservableCollection<PlannerBucket> _allBuckets = new();

    [ObservableProperty] private string _channelId = string.Empty;
    [ObservableProperty] private string _teamId = string.Empty;
    [ObservableProperty] private string _planId = string.Empty;

    [ObservableProperty] private ObservableCollection<PlannerBucket> _buckets = new();
    [ObservableProperty] private ObservableCollection<PlannerTask> _allTasks = new();

    [ObservableProperty] private PlannerTask? _selectedTask;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isBoardView = true;
    [ObservableProperty] private bool _isEmpty;

    public ChannelPlannerViewModel(GraphTeamsService teamsService)
    {
        _teamsService = teamsService;
    }

    /// <summary>탭 진입 시 초기화</summary>
    public async Task InitializeAsync(string teamId, string channelId)
    {
        TeamId = teamId;
        ChannelId = channelId;
        await LoadPlannerAsync();
    }

    /// <summary>새 작업 추가 — bucket이 null이면 기본 버킷(할 일)에 추가</summary>
    [RelayCommand]
    private async Task AddTask(PlannerBucket? bucket)
    {
        var target = bucket ?? _allBuckets.FirstOrDefault();
        if (target == null) return;

        _log.Info("새 작업 추가: bucket={Bucket}", target.Name);

        var newTask = new PlannerTask
        {
            Id        = Guid.NewGuid().ToString(),
            Title     = "새 작업",
            BucketId  = target.Id,
            BucketName = target.Name,
            Priority  = "medium",
        };

        target.Tasks.Add(newTask);
        target.RefreshCount();
        AllTasks.Add(newTask);
        IsEmpty = AllTasks.Count == 0;

        // TODO: Graph Planner API 연동 (향후)
        await Task.CompletedTask;
    }

    /// <summary>드래그앤드롭으로 작업을 다른 열로 이동</summary>
    [RelayCommand]
    private async Task MoveTask((PlannerTask task, PlannerBucket targetBucket) args)
    {
        if (args.task == null || args.targetBucket == null) return;

        // 기존 버킷에서 제거
        foreach (var b in _allBuckets)
        {
            if (b.Tasks.Remove(args.task))
            {
                b.RefreshCount();
                break;
            }
        }

        // 대상 버킷에 추가
        args.task.BucketId   = args.targetBucket.Id;
        args.task.BucketName = args.targetBucket.Name;
        args.targetBucket.Tasks.Add(args.task);
        args.targetBucket.RefreshCount();

        _log.Info("작업 이동: {Task} → {Bucket}", args.task.Title, args.targetBucket.Name);

        // TODO: Graph Planner API 연동 (향후)
        await Task.CompletedTask;
    }

    /// <summary>보드 ↔ 목록 뷰 전환</summary>
    [RelayCommand]
    private void ToggleView()
    {
        IsBoardView = !IsBoardView;
        _log.Debug("뷰 전환: IsBoardView={View}", IsBoardView);
    }

    /// <summary>담당자 필터 적용</summary>
    public void ApplyAssigneeFilter(string filter)
    {
        _assigneeFilter = filter;
        ApplyFilters();
    }

    /// <summary>우선순위 필터 적용</summary>
    public void ApplyPriorityFilter(string filter)
    {
        _priorityFilter = filter;
        ApplyFilters();
    }

    // ── 내부 ──────────────────────────────────────────────

    private async Task LoadPlannerAsync()
    {
        IsLoading = true;
        try
        {
            _allBuckets.Clear();
            AllTasks.Clear();

            // 기본 버킷 생성 (Graph Planner API 연동 전 기본값)
            var todoBucket = new PlannerBucket { Id = "todo",       Name = "할 일" };
            var inprogBucket = new PlannerBucket { Id = "inprogress", Name = "진행 중" };
            var doneBucket = new PlannerBucket { Id = "done",       Name = "완료" };

            _allBuckets.Add(todoBucket);
            _allBuckets.Add(inprogBucket);
            _allBuckets.Add(doneBucket);

            _log.Info("Planner 로드 완료: teamId={TeamId}, channelId={ChannelId}", TeamId, ChannelId);

            ApplyFilters();

            IsEmpty = AllTasks.Count == 0;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Planner 로드 실패");
        }
        finally
        {
            IsLoading = false;
        }

        await Task.CompletedTask;
    }

    /// <summary>현재 필터 조건에 맞게 Buckets/AllTasks 갱신</summary>
    private void ApplyFilters()
    {
        Buckets.Clear();
        AllTasks.Clear();

        foreach (var bucket in _allBuckets)
        {
            var filtered = bucket.Tasks.Where(MatchesFilter).ToList();

            var displayBucket = new PlannerBucket { Id = bucket.Id, Name = bucket.Name };
            foreach (var t in filtered)
                displayBucket.Tasks.Add(t);

            Buckets.Add(displayBucket);

            foreach (var t in filtered)
                AllTasks.Add(t);
        }

        IsEmpty = AllTasks.Count == 0 && !IsLoading;
    }

    private bool MatchesFilter(PlannerTask task)
    {
        // 담당자 필터
        var passAssignee = _assigneeFilter switch
        {
            "me"         => task.AssigneeName != null, // 실제로는 현재 사용자 ID 비교 필요
            "unassigned" => string.IsNullOrEmpty(task.AssigneeName),
            _            => true
        };

        // 우선순위 필터
        var passPriority = _priorityFilter == "all" || task.Priority == _priorityFilter;

        return passAssignee && passPriority;
    }
}
