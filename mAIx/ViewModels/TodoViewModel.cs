using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Services.Graph;
using mAIx.Utilities;
using mAIx.Utils;
using Serilog;

namespace mAIx.ViewModels;

/// <summary>
/// 스마트 목록 타입
/// </summary>
public enum SmartListType
{
    MyDay,
    Important,
    Planned,
    Tasks
}

/// <summary>
/// ToDo 뷰모델 - Microsoft To Do 연동 + 스마트 목록 + 자연어 입력
/// </summary>
public partial class TodoViewModel : ViewModelBase
{
    private readonly GraphToDoService _todoService;
    private readonly NaturalLanguageDateParser _dateParser;
    private readonly ILogger _logger;

    /// <summary>
    /// 모든 작업 목록(리스트)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TodoTaskListInfo> _taskLists = new();

    /// <summary>
    /// 선택된 목록
    /// </summary>
    [ObservableProperty]
    private TodoTaskListInfo? _selectedList;

    /// <summary>
    /// 현재 표시되는 작업 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TodoTaskItem> _tasks = new();

    /// <summary>
    /// My Day 작업 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TodoTaskItem> _myDayTasks = new();

    /// <summary>
    /// 새 작업 제목 입력
    /// </summary>
    [ObservableProperty]
    private string _newTaskTitle = "";

    /// <summary>
    /// My Day가 선택되었는지 여부
    /// </summary>
    [ObservableProperty]
    private bool _isMyDaySelected = true;

    /// <summary>
    /// 현재 스마트 목록 타입
    /// </summary>
    [ObservableProperty]
    private SmartListType _currentSmartList = SmartListType.MyDay;

    /// <summary>
    /// 완료된 항목 표시 여부
    /// </summary>
    [ObservableProperty]
    private bool _showCompleted;

    /// <summary>
    /// 현재 목록 제목 표시
    /// </summary>
    [ObservableProperty]
    private string _currentListTitle = "나의 하루";

    /// <summary>
    /// 전체 작업 (필터 전)
    /// </summary>
    private List<TodoTaskItem> _allTasks = new();

    /// <summary>
    /// 선택된 작업 (편집용)
    /// </summary>
    [ObservableProperty]
    private TodoTaskItem? _selectedTask;

    /// <summary>
    /// 작업 수 표시
    /// </summary>
    public int TaskCount => Tasks.Count;

    /// <summary>
    /// 작업 없음 표시
    /// </summary>
    public bool HasNoTasks => !IsLoading && Tasks.Count == 0;

    public TodoViewModel(GraphToDoService todoService, NaturalLanguageDateParser dateParser)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
        _dateParser = dateParser ?? throw new ArgumentNullException(nameof(dateParser));
        _logger = Log.ForContext<TodoViewModel>();
    }

    /// <summary>
    /// 전체 데이터 로드 (목록 + 작업)
    /// </summary>
    [RelayCommand]
    private async Task LoadDataAsync()
    {
        await ExecuteAsync(async () =>
        {
            _logger.Debug("[TodoViewModel] 데이터 로드 시작");

            // 목록 로드
            var lists = await _todoService.GetAllListsAsync();
            TaskLists = new ObservableCollection<TodoTaskListInfo>(lists);

            // 모든 목록의 작업 로드
            await LoadAllTasksAsync();

            // 현재 스마트 목록에 맞게 필터링
            ApplyFilter();

            _logger.Debug("[TodoViewModel] 데이터 로드 완료: 목록 {ListCount}개, 작업 {TaskCount}개",
                lists.Count, _allTasks.Count);
        }, "할일 데이터 로드");
    }

    /// <summary>
    /// 모든 목록의 작업을 로드
    /// </summary>
    private async Task LoadAllTasksAsync()
    {
        _allTasks.Clear();

        foreach (var list in TaskLists)
        {
            var tasks = await _todoService.GetTasksFromListAsync(list.Id, ShowCompleted);
            foreach (var task in tasks)
            {
                task.ListId = list.Id;
                task.ListName = list.DisplayName;
            }
            _allTasks.AddRange(tasks);
        }
    }

    /// <summary>
    /// 스마트 목록 선택
    /// </summary>
    [RelayCommand]
    private void SelectSmartList(string listType)
    {
        SelectedList = null;

        if (Enum.TryParse<SmartListType>(listType, out var smartList))
        {
            CurrentSmartList = smartList;
            IsMyDaySelected = smartList == SmartListType.MyDay;

            CurrentListTitle = smartList switch
            {
                SmartListType.MyDay => "나의 하루",
                SmartListType.Important => "중요",
                SmartListType.Planned => "계획된 항목",
                SmartListType.Tasks => "모든 작업",
                _ => "할일"
            };

            ApplyFilter();
        }
    }

    /// <summary>
    /// 사용자 목록 선택
    /// </summary>
    [RelayCommand]
    private void SelectList(TodoTaskListInfo? list)
    {
        if (list == null) return;

        SelectedList = list;
        IsMyDaySelected = false;
        CurrentListTitle = list.DisplayName;

        ApplyFilter();
    }

    /// <summary>
    /// 필터 적용
    /// </summary>
    private void ApplyFilter()
    {
        IEnumerable<TodoTaskItem> filtered;

        if (SelectedList != null)
        {
            // 특정 목록 선택
            filtered = _allTasks.Where(t => t.ListId == SelectedList.Id);
        }
        else
        {
            // 스마트 목록
            filtered = CurrentSmartList switch
            {
                SmartListType.MyDay => _allTasks.Where(t => t.IsMyDay),
                SmartListType.Important => _allTasks.Where(t => t.IsImportant),
                SmartListType.Planned => _allTasks.Where(t => t.DueDate.HasValue),
                SmartListType.Tasks => _allTasks,
                _ => _allTasks
            };
        }

        if (!ShowCompleted)
        {
            filtered = filtered.Where(t => !t.IsCompleted);
        }

        Tasks = new ObservableCollection<TodoTaskItem>(filtered.OrderBy(t => t.IsCompleted).ThenByDescending(t => t.IsImportant).ThenBy(t => t.DueDate ?? DateTime.MaxValue));

        // My Day 작업 업데이트
        MyDayTasks = new ObservableCollection<TodoTaskItem>(_allTasks.Where(t => t.IsMyDay && !t.IsCompleted));

        OnPropertyChanged(nameof(TaskCount));
        OnPropertyChanged(nameof(HasNoTasks));
    }

    /// <summary>
    /// 새 작업 추가 (자연어 파싱 포함)
    /// </summary>
    [RelayCommand]
    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTaskTitle)) return;

        await ExecuteAsync(async () =>
        {
            var input = NewTaskTitle.Trim();
            _logger.Debug("[TodoViewModel] 새 작업 추가: {Input}", input);

            // 자연어 파싱
            var parseResult = _dateParser.Parse(input);

            // 제목에서 날짜 관련 텍스트 제거 (원본 유지)
            var title = input;
            DateTime? dueDate = parseResult.Success ? parseResult.StartDateTime : null;

            // 대상 목록 결정
            string? targetListId = SelectedList?.Id;
            if (string.IsNullOrEmpty(targetListId))
            {
                targetListId = await _todoService.GetDefaultListIdAsync();
            }

            if (string.IsNullOrEmpty(targetListId))
            {
                _logger.Warning("[TodoViewModel] 대상 목록을 찾을 수 없습니다");
                return;
            }

            // Graph API로 작업 생성
            var taskId = await _todoService.CreateTaskInListAsync(targetListId, title, dueDate);

            if (!string.IsNullOrEmpty(taskId))
            {
                // My Day에 자동 추가 (나의 하루 모드일 때)
                if (IsMyDaySelected)
                {
                    // LinkedResource로 My Day 마킹 (Graph API는 My Day 전용 API가 없으므로 로컬 플래그 사용)
                }

                // 반복 패턴 설정
                if (parseResult.IsRecurring && !string.IsNullOrEmpty(parseResult.RecurrencePattern))
                {
                    await _todoService.SetRecurrenceAsync(targetListId, taskId, parseResult.RecurrencePattern);
                }

                // 목록 새로고침
                await LoadAllTasksAsync();
                ApplyFilter();

                NewTaskTitle = "";
                _logger.Information("[TodoViewModel] 작업 추가 완료: {Title}", title);
            }
        }, "작업 추가");
    }

    /// <summary>
    /// 작업 완료/미완료 토글
    /// </summary>
    [RelayCommand]
    private async Task ToggleTaskCompleteAsync(TodoTaskItem? task)
    {
        if (task == null) return;

        await ExecuteAsync(async () =>
        {
            var newStatus = !task.IsCompleted;
            var listId = task.ListId ?? await _todoService.GetDefaultListIdAsync();
            if (string.IsNullOrEmpty(listId)) return;

            var success = await _todoService.UpdateTaskCompletionInListAsync(listId, task.Id, newStatus);
            if (success)
            {
                task.IsCompleted = newStatus;
                _logger.Debug("[TodoViewModel] 작업 완료 토글: {Title} → {Status}", task.Title, newStatus);
                ApplyFilter();
            }
        }, "작업 상태 변경");
    }

    /// <summary>
    /// 중요도 토글
    /// </summary>
    [RelayCommand]
    private async Task ToggleImportantAsync(TodoTaskItem? task)
    {
        if (task == null) return;

        await ExecuteAsync(async () =>
        {
            var newImportant = !task.IsImportant;
            var listId = task.ListId ?? await _todoService.GetDefaultListIdAsync();
            if (string.IsNullOrEmpty(listId)) return;

            var success = await _todoService.UpdateTaskImportanceAsync(listId, task.Id, newImportant);
            if (success)
            {
                task.Importance = newImportant ? "High" : "Normal";
                _logger.Debug("[TodoViewModel] 중요도 토글: {Title} → {Important}", task.Title, newImportant);
                OnPropertyChanged(nameof(Tasks));
            }
        }, "중요도 변경");
    }

    /// <summary>
    /// My Day에 추가/제거
    /// </summary>
    [RelayCommand]
    private void ToggleMyDay(TodoTaskItem? task)
    {
        if (task == null) return;

        task.IsMyDay = !task.IsMyDay;
        _logger.Debug("[TodoViewModel] My Day 토글: {Title} → {IsMyDay}", task.Title, task.IsMyDay);
        ApplyFilter();
    }

    /// <summary>
    /// 작업 삭제
    /// </summary>
    [RelayCommand]
    private async Task DeleteTaskAsync(TodoTaskItem? task)
    {
        if (task == null) return;

        await ExecuteAsync(async () =>
        {
            var listId = task.ListId ?? await _todoService.GetDefaultListIdAsync();
            if (string.IsNullOrEmpty(listId)) return;

            var success = await _todoService.DeleteTaskFromListAsync(listId, task.Id);
            if (success)
            {
                _allTasks.Remove(task);
                ApplyFilter();
                _logger.Information("[TodoViewModel] 작업 삭제 완료: {Title}", task.Title);
            }
        }, "작업 삭제");
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }
}
