using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mailX.Services.Graph;
using Serilog;

namespace mailX.ViewModels;

/// <summary>
/// Planner ViewModel - 플랜/버킷/작업 관리
/// </summary>
public partial class PlannerViewModel : ViewModelBase
{
    private readonly GraphPlannerService _plannerService;
    private readonly ILogger _logger;

    /// <summary>
    /// 플랜 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PlanItemViewModel> _plans = new();

    /// <summary>
    /// 선택된 플랜
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPlan))]
    private PlanItemViewModel? _selectedPlan;

    /// <summary>
    /// 버킷 목록 (칸반 보드용)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BucketViewModel> _buckets = new();

    /// <summary>
    /// 내 작업 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TaskItemViewModel> _myTasks = new();

    /// <summary>
    /// 선택된 작업
    /// </summary>
    [ObservableProperty]
    private TaskItemViewModel? _selectedTask;

    /// <summary>
    /// 현재 뷰 모드 (board, list)
    /// </summary>
    [ObservableProperty]
    private string _viewMode = "board";

    /// <summary>
    /// 플랜이 선택되었는지
    /// </summary>
    public bool HasSelectedPlan => SelectedPlan != null;

    public PlannerViewModel(GraphPlannerService plannerService)
    {
        _plannerService = plannerService ?? throw new ArgumentNullException(nameof(plannerService));
        _logger = Log.ForContext<PlannerViewModel>();
    }

    /// <summary>
    /// 플랜 목록 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadPlansAsync()
    {
        await ExecuteAsync(async () =>
        {
            var plans = await _plannerService.GetAllPlansAsync();

            Plans.Clear();
            foreach (var plan in plans.OrderBy(p => p.Title))
            {
                Plans.Add(new PlanItemViewModel
                {
                    Id = plan.Id ?? string.Empty,
                    Title = plan.Title ?? "Untitled",
                    CreatedDateTime = plan.CreatedDateTime?.DateTime,
                    OwnerId = plan.Owner
                });
            }

            _logger.Information("플랜 목록 로드 완료: {Count}개", Plans.Count);
        }, "플랜 목록 로드 실패");
    }

    /// <summary>
    /// 내 작업 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadMyTasksAsync()
    {
        await ExecuteAsync(async () =>
        {
            var tasks = await _plannerService.GetMyTasksAsync();

            MyTasks.Clear();
            foreach (var task in tasks.OrderBy(t => t.DueDateTime).ThenBy(t => t.Title))
            {
                MyTasks.Add(CreateTaskViewModel(task));
            }

            _logger.Information("내 작업 로드 완료: {Count}개", MyTasks.Count);
        }, "내 작업 로드 실패");
    }

    /// <summary>
    /// 플랜 선택 시 버킷과 작업 로드
    /// </summary>
    [RelayCommand]
    public async Task SelectPlanAsync(PlanItemViewModel plan)
    {
        if (plan == null) return;

        SelectedPlan = plan;
        await LoadBucketsAndTasksAsync(plan.Id);
    }

    /// <summary>
    /// 버킷과 작업 로드
    /// </summary>
    private async Task LoadBucketsAndTasksAsync(string planId)
    {
        await ExecuteAsync(async () =>
        {
            // 버킷 로드
            var buckets = await _plannerService.GetBucketsAsync(planId);
            // 작업 로드
            var tasks = await _plannerService.GetTasksAsync(planId);

            Buckets.Clear();

            // 버킷별로 작업 그룹화
            foreach (var bucket in buckets.OrderBy(b => b.OrderHint))
            {
                var bucketVm = new BucketViewModel
                {
                    Id = bucket.Id ?? string.Empty,
                    Name = bucket.Name ?? "Untitled",
                    PlanId = bucket.PlanId ?? string.Empty,
                    ETag = bucket.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : null
                };

                // 해당 버킷의 작업 추가
                var bucketTasks = tasks.Where(t => t.BucketId == bucket.Id)
                    .OrderBy(t => t.OrderHint);

                foreach (var task in bucketTasks)
                {
                    bucketVm.Tasks.Add(CreateTaskViewModel(task));
                }

                Buckets.Add(bucketVm);
            }

            _logger.Debug("플랜 {PlanId} 로드: {BucketCount}개 버킷, {TaskCount}개 작업",
                planId, Buckets.Count, tasks.Count());
        }, "버킷/작업 로드 실패");
    }

    /// <summary>
    /// 새 버킷 생성
    /// </summary>
    [RelayCommand]
    public async Task CreateBucketAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || SelectedPlan == null)
            return;

        await ExecuteAsync(async () =>
        {
            var bucket = await _plannerService.CreateBucketAsync(SelectedPlan.Id, name);
            if (bucket != null)
            {
                Buckets.Add(new BucketViewModel
                {
                    Id = bucket.Id ?? string.Empty,
                    Name = bucket.Name ?? name,
                    PlanId = bucket.PlanId ?? string.Empty
                });
                _logger.Information("버킷 생성 완료: {Name}", name);
            }
        }, "버킷 생성 실패");
    }

    /// <summary>
    /// 새 작업 생성
    /// </summary>
    [RelayCommand]
    public async Task CreateTaskAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || SelectedPlan == null)
            return;

        // 첫 번째 버킷에 작업 추가 (기본)
        var firstBucket = Buckets.FirstOrDefault();
        if (firstBucket == null)
            return;

        await ExecuteAsync(async () =>
        {
            var task = await _plannerService.CreateTaskAsync(SelectedPlan.Id, firstBucket.Id, title);
            if (task != null)
            {
                firstBucket.Tasks.Insert(0, CreateTaskViewModel(task));
                _logger.Information("작업 생성 완료: {Title}", title);
            }
        }, "작업 생성 실패");
    }

    /// <summary>
    /// 작업 완료 토글
    /// </summary>
    [RelayCommand]
    public async Task ToggleTaskCompleteAsync(TaskItemViewModel task)
    {
        if (task == null || string.IsNullOrEmpty(task.ETag)) return;

        var newPercent = task.PercentComplete == 100 ? 0 : 100;

        await ExecuteAsync(async () =>
        {
            var updated = await _plannerService.UpdateTaskPercentCompleteAsync(task.Id, task.ETag, newPercent);
            if (updated != null)
            {
                task.PercentComplete = newPercent;
                task.ETag = updated.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : task.ETag;
                _logger.Debug("작업 완료 상태 변경: {Title} -> {Percent}%", task.Title, newPercent);
            }
        }, "작업 상태 변경 실패");
    }

    /// <summary>
    /// 작업 삭제
    /// </summary>
    [RelayCommand]
    public async Task DeleteTaskAsync(TaskItemViewModel task)
    {
        if (task == null || string.IsNullOrEmpty(task.ETag)) return;

        await ExecuteAsync(async () =>
        {
            var success = await _plannerService.DeleteTaskAsync(task.Id, task.ETag);
            if (success)
            {
                // 버킷에서 작업 제거
                foreach (var bucket in Buckets)
                {
                    if (bucket.Tasks.Remove(task))
                        break;
                }
                _logger.Information("작업 삭제 완료: {Title}", task.Title);
            }
        }, "작업 삭제 실패");
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadPlansAsync();
        if (SelectedPlan != null)
        {
            await LoadBucketsAndTasksAsync(SelectedPlan.Id);
        }
    }

    /// <summary>
    /// 뷰 모드 전환
    /// </summary>
    [RelayCommand]
    public void ToggleViewMode()
    {
        ViewMode = ViewMode == "board" ? "list" : "board";
    }

    /// <summary>
    /// PlannerTask를 TaskItemViewModel로 변환
    /// </summary>
    private TaskItemViewModel CreateTaskViewModel(PlannerTask task)
    {
        return new TaskItemViewModel
        {
            Id = task.Id ?? string.Empty,
            Title = task.Title ?? "Untitled",
            BucketId = task.BucketId ?? string.Empty,
            PlanId = task.PlanId ?? string.Empty,
            PercentComplete = task.PercentComplete ?? 0,
            Priority = task.Priority ?? 5,
            DueDateTime = task.DueDateTime?.DateTime,
            CreatedDateTime = task.CreatedDateTime?.DateTime,
            HasDescription = task.HasDescription ?? false,
            ChecklistItemCount = task.ChecklistItemCount ?? 0,
            ActiveChecklistItemCount = task.ActiveChecklistItemCount ?? 0,
            ETag = task.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : null
        };
    }
}

/// <summary>
/// 플랜 아이템 ViewModel
/// </summary>
public partial class PlanItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private DateTime? _createdDateTime;

    [ObservableProperty]
    private string? _ownerId;

    [ObservableProperty]
    private int _taskCount;
}

/// <summary>
/// 버킷 ViewModel (칸반 컬럼)
/// </summary>
public partial class BucketViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _planId = string.Empty;

    [ObservableProperty]
    private string? _eTag;

    /// <summary>
    /// 버킷 내 작업 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TaskItemViewModel> _tasks = new();
}

/// <summary>
/// 작업 아이템 ViewModel
/// </summary>
public partial class TaskItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _bucketId = string.Empty;

    [ObservableProperty]
    private string _planId = string.Empty;

    [ObservableProperty]
    private int _percentComplete;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private DateTime? _dueDateTime;

    [ObservableProperty]
    private DateTime? _createdDateTime;

    [ObservableProperty]
    private bool _hasDescription;

    [ObservableProperty]
    private int _checklistItemCount;

    [ObservableProperty]
    private int _activeChecklistItemCount;

    [ObservableProperty]
    private string? _eTag;

    /// <summary>
    /// 완료 여부
    /// </summary>
    public bool IsComplete => PercentComplete == 100;

    /// <summary>
    /// 진행률 표시
    /// </summary>
    public string ProgressDisplay => GraphPlannerService.GetPercentCompleteDisplay(PercentComplete);

    /// <summary>
    /// 우선순위 표시
    /// </summary>
    public string PriorityDisplay => GraphPlannerService.GetPriorityDisplay(Priority);

    /// <summary>
    /// 우선순위 색상
    /// </summary>
    public string PriorityColor => Priority switch
    {
        1 => "#D13438", // 긴급
        3 => "#C239B3", // 중요
        5 => "#0078D4", // 중간
        9 => "#808080", // 낮음
        _ => "#0078D4"
    };

    /// <summary>
    /// 마감일 표시
    /// </summary>
    public string DueDateDisplay
    {
        get
        {
            if (!DueDateTime.HasValue)
                return string.Empty;

            var today = DateTime.Today;
            var due = DueDateTime.Value.Date;

            if (due == today)
                return "오늘";
            if (due == today.AddDays(1))
                return "내일";
            if (due < today)
                return $"지남 ({(today - due).Days}일)";
            if ((due - today).Days <= 7)
                return $"{(due - today).Days}일 후";

            return DueDateTime.Value.ToString("MM/dd");
        }
    }

    /// <summary>
    /// 마감일 상태 (지남/임박/여유)
    /// </summary>
    public string DueStatus
    {
        get
        {
            if (!DueDateTime.HasValue || IsComplete)
                return "Normal";

            var today = DateTime.Today;
            var due = DueDateTime.Value.Date;

            if (due < today)
                return "Overdue";
            if ((due - today).Days <= 1)
                return "Urgent";
            if ((due - today).Days <= 3)
                return "Soon";

            return "Normal";
        }
    }

    /// <summary>
    /// 체크리스트 표시
    /// </summary>
    public string ChecklistDisplay
    {
        get
        {
            if (ChecklistItemCount == 0)
                return string.Empty;

            var completed = ChecklistItemCount - ActiveChecklistItemCount;
            return $"{completed}/{ChecklistItemCount}";
        }
    }
}
