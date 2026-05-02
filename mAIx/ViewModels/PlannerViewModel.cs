using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mAIx.Services.Graph;
using mAIx.Utils;
using NLog;

namespace mAIx.ViewModels;

/// <summary>
/// Planner ViewModel - 플랜/버킷/작업 관리
/// </summary>
public partial class PlannerViewModel : ViewModelBase
{
    private readonly GraphPlannerService _plannerService;
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 핀 고정 상태 저장 파일 경로
    /// </summary>
    private static readonly string PinnedPlansFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mAIx", "pinned_plans.json");

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
    /// 현재 뷰 모드 (board, list, myDay, myTasks, timeline)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKanbanView))]
    [NotifyPropertyChangedFor(nameof(IsTimelineView))]
    private string _viewMode = "board";

    /// <summary>
    /// 칸반 뷰 여부
    /// </summary>
    public bool IsKanbanView => ViewMode == "board";

    /// <summary>
    /// 타임라인 뷰 여부
    /// </summary>
    public bool IsTimelineView => ViewMode == "timeline";

    /// <summary>
    /// 플랜이 선택되었는지
    /// </summary>
    public bool HasSelectedPlan => SelectedPlan != null;

    /// <summary>
    /// 커스텀 필드 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<CustomFieldViewModel> _customFields = new();

    /// <summary>
    /// 현재 플랜의 카테고리(라벨) 정의
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PlanCategoryViewModel> _planCategories = new();

    /// <summary>
    /// 고정된 플랜 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PlanItemViewModel> _pinnedPlans = new();

    /// <summary>
    /// 나의 하루 작업 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TaskItemViewModel> _myDayTasks = new();

    public PlannerViewModel(GraphPlannerService plannerService)
    {
        _plannerService = plannerService ?? throw new ArgumentNullException(nameof(plannerService));
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

            // 저장된 핀 목록 로드
            var pinnedPlanIds = LoadPinnedPlanIds();

            var newPlans = new List<PlanItemViewModel>();
            var newPinnedPlans = new List<PlanItemViewModel>();

            foreach (var plan in plans.OrderBy(p => p.Title))
            {
                var isPinned = pinnedPlanIds.Contains(plan.Id ?? string.Empty);
                var planVm = new PlanItemViewModel
                {
                    Id = plan.Id ?? string.Empty,
                    Title = plan.Title ?? "Untitled",
                    CreatedDateTime = plan.CreatedDateTime?.DateTime,
                    OwnerId = plan.Owner,
                    IsPinned = isPinned
                };

                newPlans.Add(planVm);

                if (isPinned)
                {
                    newPinnedPlans.Add(planVm);
                }
            }

            Plans = new ObservableCollection<PlanItemViewModel>(newPlans);
            PinnedPlans = new ObservableCollection<PlanItemViewModel>(newPinnedPlans);

            _logger.Info("플랜 목록 로드 완료: {Count}개, 핀 고정: {PinnedCount}개", Plans.Count, PinnedPlans.Count);
        }, "플랜 목록 로드 실패");
    }

    /// <summary>
    /// 핀 고정된 플랜 ID 목록 로드
    /// </summary>
    private HashSet<string> LoadPinnedPlanIds()
    {
        try
        {
            if (File.Exists(PinnedPlansFilePath))
            {
                var json = File.ReadAllText(PinnedPlansFilePath);
                var ids = JsonSerializer.Deserialize<List<string>>(json);
                return ids != null ? new HashSet<string>(ids) : new HashSet<string>();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "핀 고정 목록 로드 실패");
        }
        return new HashSet<string>();
    }

    /// <summary>
    /// 핀 고정된 플랜 ID 목록 저장
    /// </summary>
    public void SavePinnedPlanIds()
    {
        try
        {
            var directory = Path.GetDirectoryName(PinnedPlansFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var pinnedIds = PinnedPlans.Select(p => p.Id).ToList();
            var json = JsonSerializer.Serialize(pinnedIds, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PinnedPlansFilePath, json);
            _logger.Debug("핀 고정 목록 저장: {Count}개", pinnedIds.Count);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "핀 고정 목록 저장 실패");
        }
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

            MyTasks = new ObservableCollection<TaskItemViewModel>(
                tasks.OrderBy(t => t.DueDateTime).ThenBy(t => t.Title)
                     .Select(CreateTaskViewModel));

            _logger.Info("내 작업 로드 완료: {Count}개", MyTasks.Count);
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
            // 카테고리·버킷·작업 병렬 로드
            var categoriesTask = _plannerService.GetPlanCategoriesAsync(planId);
            var bucketsTask = _plannerService.GetBucketsAsync(planId);
            var tasksTask = _plannerService.GetTasksAsync(planId);

            await Task.WhenAll(categoriesTask, bucketsTask, tasksTask).ConfigureAwait(false);

            // WhenAll 완료 후 .Result 접근 — 이미 완료된 태스크이므로 블로킹 없음
            var categories = categoriesTask.Result.ToList();
            var buckets = bucketsTask.Result.ToList();
            var taskList = tasksTask.Result.ToList();

            PlanCategories = new ObservableCollection<PlanCategoryViewModel>(categories);

            // 모든 작업에서 담당자 userId 수집
            var allUserIds = taskList
                .SelectMany(t => GetTaskAssigneeIds(t))
                .Distinct()
                .ToList();

            // 일괄로 사용자 이름 조회 (Graph API)
            var userNames = allUserIds.Count > 0
                ? await _plannerService.GetUserDisplayNamesAsync(allUserIds)
                : new Dictionary<string, string>();

            // 모든 담당자 ViewModel 목록 (사진 로드용)
            var allAssigneeVms = new List<TaskAssigneeViewModel>();

            // 버킷별로 작업 그룹화
            var newBuckets = new List<BucketViewModel>();

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
                var bucketTasks = taskList.Where(t => t.BucketId == bucket.Id)
                    .OrderBy(t => t.OrderHint);

                foreach (var task in bucketTasks)
                {
                    var taskVm = CreateTaskViewModel(task);
                    // 라벨 정보 매핑
                    MapTaskCategories(taskVm, task, categories);
                    // 담당자 정보 매핑 (조회한 이름 사용)
                    var taskUserIds = GetTaskAssigneeIds(task);
                    ApplyAssigneesToTask(taskVm, taskUserIds, userNames);
                    // 담당자 ViewModel 수집
                    allAssigneeVms.AddRange(taskVm.Assignees);
                    bucketVm.Tasks.Add(taskVm);
                }

                newBuckets.Add(bucketVm);
            }

            Buckets = new ObservableCollection<BucketViewModel>(newBuckets);

            var tasksWithDueDateCount = taskList.Count(t => t.DueDateTime != null);
            _logger.Debug($"[PlannerViewModel] 플랜 로드: {Buckets.Count}개 버킷, {taskList.Count}개 작업, {userNames.Count}명 담당자, 기한설정 {tasksWithDueDateCount}개");

            // 백그라운드에서 프로필 사진 로드
            if (allUserIds.Count > 0)
            {
                _ = LoadAssigneePhotosAsync(allUserIds, allAssigneeVms);
            }
        }, "버킷/작업 로드 실패");
    }

    /// <summary>
    /// 담당자 프로필 사진 비동기 로드
    /// </summary>
    private async Task LoadAssigneePhotosAsync(List<string> userIds, List<TaskAssigneeViewModel> assigneeVms)
    {
        try
        {
            if (userIds == null || userIds.Count == 0 || assigneeVms == null || assigneeVms.Count == 0)
                return;

            var photos = await _plannerService.GetUserPhotosAsync(userIds);

            if (photos == null || photos.Count == 0)
                return;

            // UI 스레드에서 사진 업데이트
            var app = System.Windows.Application.Current;
            if (app == null)
                return;

            await app.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    foreach (var assignee in assigneeVms)
                    {
                        if (assignee != null &&
                            !string.IsNullOrEmpty(assignee.UserId) &&
                            photos.TryGetValue(assignee.UserId, out var photo) &&
                            !string.IsNullOrEmpty(photo))
                        {
                            assignee.PhotoBase64 = photo;
                        }
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.Warn(innerEx, "UI 스레드에서 사진 업데이트 실패");
                }
            });

            _logger.Debug("담당자 프로필 사진 로드 완료: {PhotoCount}개", photos.Values.Count(p => !string.IsNullOrEmpty(p)));
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "담당자 프로필 사진 로드 실패");
        }
    }

    /// <summary>
    /// 작업에 적용된 카테고리(라벨) 매핑
    /// </summary>
    private void MapTaskCategories(TaskItemViewModel taskVm, PlannerTask task, List<PlanCategoryViewModel> categories)
    {
        if (task.AppliedCategories == null)
        {
            return;
        }

        var appliedCategories = task.AppliedCategories.AdditionalData;
        if (appliedCategories == null || appliedCategories.Count == 0)
        {
            return;
        }

        foreach (var kvp in appliedCategories)
        {
            // bool 또는 JsonElement로 올 수 있음
            bool applied = false;
            if (kvp.Value is bool boolValue)
            {
                applied = boolValue;
            }
            else if (kvp.Value is System.Text.Json.JsonElement jsonElement)
            {
                applied = jsonElement.ValueKind == System.Text.Json.JsonValueKind.True;
            }

            if (applied)
            {
                // 플랜에 정의된 카테고리 찾기
                var category = categories.FirstOrDefault(c =>
                    c.CategoryId.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase));

                if (category != null)
                {
                    // 플랜에 정의된 카테고리 사용
                    taskVm.AppliedCategories.Add(new AppliedCategoryViewModel
                    {
                        CategoryId = category.CategoryId,
                        Name = category.Name,
                        Color = category.Color
                    });
                }
                else
                {
                    // 플랜에 정의되지 않은 카테고리도 기본 색상으로 표시
                    var categoryId = kvp.Key;
                    taskVm.AppliedCategories.Add(new AppliedCategoryViewModel
                    {
                        CategoryId = categoryId,
                        Name = GetCategoryDisplayName(categoryId),
                        Color = PlanCategoryViewModel.GetDefaultColor(categoryId)
                    });
                }
            }
        }
    }

    /// <summary>
    /// 카테고리 ID에서 표시 이름 생성
    /// </summary>
    private static string GetCategoryDisplayName(string categoryId)
    {
        // "category7" -> "라벨 7"
        if (categoryId.StartsWith("category", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(categoryId.Substring(8), out int num))
        {
            return $"라벨 {num}";
        }
        return categoryId;
    }

    /// <summary>
    /// 작업 담당자 매핑 (userId만 수집)
    /// </summary>
    private List<string> GetTaskAssigneeIds(PlannerTask task)
    {
        var userIds = new List<string>();
        if (task.Assignments == null) return userIds;

        var assignments = task.Assignments.AdditionalData;
        if (assignments == null) return userIds;

        foreach (var kvp in assignments)
        {
            // userId가 키로 들어옴
            userIds.Add(kvp.Key);
        }
        return userIds;
    }

    /// <summary>
    /// 작업 담당자 매핑 (이름 적용)
    /// </summary>
    private void ApplyAssigneesToTask(TaskItemViewModel taskVm, List<string> userIds, Dictionary<string, string> userNames)
    {
        foreach (var userId in userIds)
        {
            var displayName = userNames.TryGetValue(userId, out var name) ? name : userId[..Math.Min(8, userId.Length)];
            taskVm.Assignees.Add(new TaskAssigneeViewModel
            {
                UserId = userId,
                DisplayName = displayName
            });
        }
    }

    /// <summary>
    /// 작업을 다른 버킷으로 이동
    /// </summary>
    public async Task<bool> MoveTaskToBucketAsync(TaskItemViewModel task, string targetBucketId)
    {
        if (task == null || string.IsNullOrEmpty(task.ETag) || string.IsNullOrEmpty(targetBucketId))
            return false;

        try
        {
            var updated = await _plannerService.MoveTaskToBucketAsync(task.Id, task.ETag, targetBucketId);
            if (updated != null)
            {
                task.BucketId = targetBucketId;
                task.ETag = updated.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : task.ETag;
                _logger.Info("작업 버킷 이동 완료: {Title} -> {BucketId}", task.Title, targetBucketId);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "작업 버킷 이동 실패: {TaskId}", task.Id);
        }
        return false;
    }

    /// <summary>
    /// 작업 순서 변경
    /// </summary>
    public async Task<bool> ReorderTaskAsync(TaskItemViewModel task, string newOrderHint)
    {
        if (task == null || string.IsNullOrEmpty(task.ETag))
            return false;

        try
        {
            var updated = await _plannerService.UpdateTaskOrderHintAsync(task.Id, task.ETag, newOrderHint);
            if (updated != null)
            {
                task.OrderHint = newOrderHint;
                task.ETag = updated.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : task.ETag;
                _logger.Debug("작업 순서 변경 완료: {Title}", task.Title);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "작업 순서 변경 실패: {TaskId}", task.Id);
        }
        return false;
    }

    /// <summary>
    /// 나의 하루 작업 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadMyDayTasksAsync()
    {
        await ExecuteAsync(async () =>
        {
            var today = DateTime.Today;
            var tasks = await _plannerService.GetMyTasksAsync();

            MyDayTasks = new ObservableCollection<TaskItemViewModel>(
                tasks.Where(t => t.DueDateTime?.DateTime.Date == today || t.StartDateTime?.DateTime.Date == today)
                     .OrderBy(t => t.DueDateTime)
                     .Select(CreateTaskViewModel));

            ViewMode = "myDay";
            _logger.Info("나의 하루 작업 로드 완료: {Count}개", MyDayTasks.Count);
        }, "나의 하루 작업 로드 실패");
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
                _logger.Info("버킷 생성 완료: {Name}", name);
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
                _logger.Info("작업 생성 완료: {Title}", title);
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
    /// 작업 완료로 표시 (100%)
    /// </summary>
    [RelayCommand]
    public async Task CompleteTaskAsync(TaskItemViewModel task)
    {
        if (task == null || string.IsNullOrEmpty(task.ETag)) return;
        if (task.PercentComplete == 100) return; // 이미 완료됨

        await ExecuteAsync(async () =>
        {
            var updated = await _plannerService.UpdateTaskPercentCompleteAsync(task.Id, task.ETag, 100);
            if (updated != null)
            {
                task.PercentComplete = 100;
                task.ETag = updated.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : task.ETag;
                _logger.Info("작업 완료 처리: {Title}", task.Title);
            }
        }, "작업 완료 처리 실패");
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
                _logger.Info("작업 삭제 완료: {Title}", task.Title);
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
    /// 칸반 뷰로 전환
    /// </summary>
    [RelayCommand]
    public void SwitchToKanban()
    {
        ViewMode = "board";
    }

    /// <summary>
    /// 타임라인 뷰로 전환
    /// </summary>
    [RelayCommand]
    public void SwitchToTimeline()
    {
        ViewMode = "timeline";
    }

    /// <summary>
    /// 카드 이동 (드래그 완료 시)
    /// </summary>
    [RelayCommand]
    public async Task MoveCardAsync(TaskItemViewModel task)
    {
        if (task == null || string.IsNullOrEmpty(task.BucketId)) return;

        // 실제 이동은 MoveTaskToBucketAsync를 통해 처리
        await Task.CompletedTask;
    }

    /// <summary>
    /// 커스텀 필드 로드
    /// </summary>
    public void LoadCustomFields(IEnumerable<CustomFieldViewModel> fields)
    {
        CustomFields.Clear();
        foreach (var field in fields)
        {
            CustomFields.Add(field);
        }
    }

    /// <summary>
    /// PlannerTask를 TaskItemViewModel로 변환
    /// </summary>
    private TaskItemViewModel CreateTaskViewModel(PlannerTask task)
    {
        // DateTimeOffset? 타입을 DateTime?으로 명시적 변환
        DateTime? dueDateTime = task.DueDateTime.HasValue ? task.DueDateTime.Value.DateTime : (DateTime?)null;
        DateTime? startDateTime = task.StartDateTime.HasValue ? task.StartDateTime.Value.DateTime : (DateTime?)null;
        DateTime? createdDateTime = task.CreatedDateTime.HasValue ? task.CreatedDateTime.Value.DateTime : (DateTime?)null;

        var vm = new TaskItemViewModel
        {
            Id = task.Id ?? string.Empty,
            Title = task.Title ?? "Untitled",
            BucketId = task.BucketId ?? string.Empty,
            PlanId = task.PlanId ?? string.Empty,
            PercentComplete = task.PercentComplete ?? 0,
            Priority = task.Priority ?? 5,
            DueDateTime = dueDateTime,
            StartDateTime = startDateTime,
            CreatedDateTime = createdDateTime,
            HasDescription = task.HasDescription ?? false,
            ChecklistItemCount = task.ChecklistItemCount ?? 0,
            ActiveChecklistItemCount = task.ActiveChecklistItemCount ?? 0,
            ETag = task.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : null
        };

        return vm;
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

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isSelected;
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
    private DateTime? _startDateTime;

    [ObservableProperty]
    private DateTime? _createdDateTime;

    [ObservableProperty]
    private bool _hasDescription;

    /// <summary>
    /// 메모 내용
    /// </summary>
    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private int _checklistItemCount;

    [ObservableProperty]
    private int _activeChecklistItemCount;

    [ObservableProperty]
    private string? _eTag;

    /// <summary>
    /// 작업 순서 힌트 (드래그앤드롭용)
    /// </summary>
    [ObservableProperty]
    private string _orderHint = string.Empty;

    /// <summary>
    /// 적용된 라벨(카테고리) 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AppliedCategoryViewModel> _appliedCategories = new();

    /// <summary>
    /// 담당자 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TaskAssigneeViewModel> _assignees = new();

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

    private static readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    private static SolidColorBrush GetCachedBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[color] = brush;
        }
        return brush;
    }

    /// <summary>
    /// 우선순위 색상 (Brush)
    /// </summary>
    public SolidColorBrush PriorityColor => Priority switch
    {
        1 => GetCachedBrush("#D13438"), // 긴급
        3 => GetCachedBrush("#C239B3"), // 중요
        5 => GetCachedBrush("#0078D4"), // 중간
        9 => GetCachedBrush("#808080"), // 낮음
        _ => GetCachedBrush("#0078D4")
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

    /// <summary>
    /// 첫 번째 담당자 이니셜 (아바타용)
    /// </summary>
    public string FirstAssigneeInitial =>
        Assignees.FirstOrDefault()?.DisplayName?.Length > 0
            ? Assignees.First().DisplayName[..1].ToUpper()
            : string.Empty;

    /// <summary>
    /// 담당자 수 표시 (2명 이상일 때)
    /// </summary>
    public string AssigneeCountDisplay =>
        Assignees.Count > 1 ? $"+{Assignees.Count - 1}" : string.Empty;

    /// <summary>
    /// 담당자 있음 여부
    /// </summary>
    public bool HasAssignees => Assignees.Count > 0;

    /// <summary>
    /// 라벨 있음 여부
    /// </summary>
    public bool HasCategories => AppliedCategories.Count > 0;

    /// <summary>
    /// 선택 상태
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// 플랜 카테고리(라벨) 정의 ViewModel
/// </summary>
public partial class PlanCategoryViewModel : ObservableObject
{
    /// <summary>
    /// 카테고리 ID (category1~category6)
    /// </summary>
    [ObservableProperty]
    private string _categoryId = string.Empty;

    /// <summary>
    /// 라벨 이름
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 라벨 색상 (Hex)
    /// </summary>
    [ObservableProperty]
    private string _color = string.Empty;

    /// <summary>
    /// 기본 라벨 색상 (Microsoft Planner 25개 카테고리)
    /// </summary>
    public static readonly Dictionary<string, string> DefaultColors = new()
    {
        { "category1", "#E74856" },   // 빨강
        { "category2", "#FF8C00" },   // 주황
        { "category3", "#FFCC00" },   // 노랑
        { "category4", "#6BB700" },   // 초록
        { "category5", "#0078D4" },   // 파랑
        { "category6", "#8764B8" },   // 보라
        { "category7", "#E3008C" },   // 핑크
        { "category8", "#038387" },   // 청록
        { "category9", "#8E562E" },   // 갈색
        { "category10", "#00B294" },  // 민트
        { "category11", "#D13438" },  // 진한 빨강
        { "category12", "#CA5010" },  // 진한 주황
        { "category13", "#4A154B" },  // 진한 보라
        { "category14", "#107C10" },  // 진한 초록
        { "category15", "#002050" },  // 진한 파랑
        { "category16", "#5C2D91" },  // 바이올렛
        { "category17", "#EE3F60" },  // 코랄
        { "category18", "#00AD56" },  // 에메랄드
        { "category19", "#0063B1" },  // 로얄 블루
        { "category20", "#744DA9" },  // 라벤더
        { "category21", "#C239B3" },  // 마젠타
        { "category22", "#16A085" },  // 터쿼이즈
        { "category23", "#E67E22" },  // 캐럿
        { "category24", "#3498DB" },  // 스카이 블루
        { "category25", "#9B59B6" },  // 아메시스트
    };

    /// <summary>
    /// 카테고리 ID로 기본 색상 가져오기
    /// </summary>
    public static string GetDefaultColor(string categoryId)
    {
        return DefaultColors.TryGetValue(categoryId.ToLower(), out var color) ? color : "#808080";
    }
}

/// <summary>
/// 작업에 적용된 카테고리(라벨) ViewModel
/// </summary>
public partial class AppliedCategoryViewModel : ObservableObject
{
    /// <summary>
    /// 카테고리 ID
    /// </summary>
    [ObservableProperty]
    private string _categoryId = string.Empty;

    /// <summary>
    /// 라벨 이름
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 라벨 색상 (Hex)
    /// </summary>
    [ObservableProperty]
    private string _color = string.Empty;

    private static readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    private static SolidColorBrush GetCachedBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[color] = brush;
        }
        return brush;
    }

    /// <summary>
    /// 브러시로 변환
    /// </summary>
    public SolidColorBrush ColorBrush
    {
        get
        {
            try
            {
                return GetCachedBrush((Color)ColorConverter.ConvertFromString(Color));
            }
            catch
            {
                return GetCachedBrush(Colors.Gray);
            }
        }
    }
}

/// <summary>
/// 작업 담당자 ViewModel
/// </summary>
public partial class TaskAssigneeViewModel : ObservableObject
{
    /// <summary>
    /// 사용자 ID
    /// </summary>
    [ObservableProperty]
    private string _userId = string.Empty;

    /// <summary>
    /// 표시 이름
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// 프로필 사진 (Base64)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private string? _photoBase64;

    /// <summary>
    /// 프로필 사진 유무
    /// </summary>
    public bool HasPhoto => !string.IsNullOrEmpty(PhotoBase64);

    /// <summary>
    /// 이니셜 (아바타용)
    /// </summary>
    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpper() : "?";

    private static readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    private static SolidColorBrush GetCachedBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            brush.Freeze();
            _brushCache[color] = brush;
        }
        return brush;
    }

    /// <summary>
    /// 아바타 배경색 (이름 기반)
    /// </summary>
    public SolidColorBrush AvatarColor
    {
        get
        {
            // 이름 해시값으로 색상 결정
            var hash = DisplayName.GetHashCode();
            var colors = new[]
            {
                "#0078D4", "#107C10", "#E74856", "#8764B8",
                "#FF8C00", "#008575", "#D83B01", "#5C2D91"
            };
            var index = Math.Abs(hash) % colors.Length;
            return GetCachedBrush(colors[index]);
        }
    }
}

/// <summary>
/// 커스텀 필드 ViewModel
/// </summary>
public partial class CustomFieldViewModel : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _taskId = string.Empty;

    [ObservableProperty]
    private string _fieldName = string.Empty;

    [ObservableProperty]
    private string _fieldValue = string.Empty;

    /// <summary>
    /// 필드 타입: text, number, date, checkbox
    /// </summary>
    [ObservableProperty]
    private string _fieldType = "text";

    /// <summary>
    /// 체크박스 타입일 때 bool 변환
    /// </summary>
    public bool IsChecked
    {
        get => FieldType == "checkbox" && FieldValue == "true";
        set
        {
            if (FieldType == "checkbox")
            {
                FieldValue = value ? "true" : "false";
            }
        }
    }
}
