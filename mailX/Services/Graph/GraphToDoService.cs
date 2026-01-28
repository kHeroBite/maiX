using Microsoft.Graph;
using Microsoft.Graph.Models;
using Serilog;
using Serilog.Core;

namespace mailX.Services.Graph
{
    /// <summary>
    /// Microsoft To Do API 연동 서비스
    /// </summary>
    public class GraphToDoService
    {
        private readonly GraphAuthService _authService;
        private readonly ILogger _logger;
        private string? _defaultListId;

        public GraphToDoService(GraphAuthService authService)
        {
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _logger = Log.ForContext<GraphToDoService>();
        }

        /// <summary>
        /// 기본 To Do 목록 ID 가져오기
        /// </summary>
        /// <returns>기본 목록 ID</returns>
        public async Task<string?> GetDefaultListIdAsync()
        {
            if (!string.IsNullOrEmpty(_defaultListId))
                return _defaultListId;

            try
            {
                var client = _authService.GetGraphClient();

                // 모든 To Do 목록 조회
                var listsResponse = await client.Me.Todo.Lists.GetAsync();

                if (listsResponse?.Value == null || !listsResponse.Value.Any())
                {
                    _logger.Warning("[GraphToDoService] To Do 목록이 없습니다");
                    return null;
                }

                // wellknownListName이 defaultList인 목록 찾기
                var defaultList = listsResponse.Value.FirstOrDefault(l =>
                    l.WellknownListName == WellknownListName.DefaultList);

                if (defaultList != null)
                {
                    _defaultListId = defaultList.Id;
                    _logger.Debug("[GraphToDoService] 기본 목록 찾음: {ListName} ({ListId})",
                        defaultList.DisplayName, _defaultListId);
                    return _defaultListId;
                }

                // 기본 목록이 없으면 첫 번째 목록 사용
                _defaultListId = listsResponse.Value.First().Id;
                _logger.Debug("[GraphToDoService] 첫 번째 목록 사용: {ListId}", _defaultListId);
                return _defaultListId;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 기본 목록 ID 조회 실패");
                return null;
            }
        }

        /// <summary>
        /// Microsoft To Do에 작업 생성
        /// </summary>
        /// <param name="title">작업 제목</param>
        /// <param name="dueDate">마감일 (선택)</param>
        /// <param name="body">작업 본문 (선택)</param>
        /// <returns>생성된 작업 ID</returns>
        public async Task<string?> CreateTaskAsync(string title, DateTime? dueDate = null, string? body = null)
        {
            if (string.IsNullOrEmpty(title))
                throw new ArgumentNullException(nameof(title));

            try
            {
                var listId = await GetDefaultListIdAsync();
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.Error("[GraphToDoService] 기본 목록을 찾을 수 없어 작업을 생성할 수 없습니다");
                    return null;
                }

                var client = _authService.GetGraphClient();

                var newTask = new TodoTask
                {
                    Title = title,
                    Importance = Importance.Normal
                };

                // 마감일 설정
                if (dueDate.HasValue)
                {
                    newTask.DueDateTime = new DateTimeTimeZone
                    {
                        DateTime = dueDate.Value.ToString("yyyy-MM-ddT00:00:00"),
                        TimeZone = "Asia/Seoul"
                    };
                }

                // 본문 설정
                if (!string.IsNullOrEmpty(body))
                {
                    newTask.Body = new ItemBody
                    {
                        Content = body,
                        ContentType = BodyType.Text
                    };
                }

                var createdTask = await client.Me.Todo.Lists[listId].Tasks.PostAsync(newTask);

                if (createdTask != null)
                {
                    _logger.Information("[GraphToDoService] 작업 생성 완료: {Title} (ID: {TaskId})",
                        title, createdTask.Id);
                    return createdTask.Id;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 작업 생성 실패: {Title}", title);
                throw;
            }
        }

        /// <summary>
        /// Microsoft To Do에서 작업 삭제
        /// </summary>
        /// <param name="taskId">삭제할 작업 ID</param>
        /// <returns>삭제 성공 여부</returns>
        public async Task<bool> DeleteTaskAsync(string taskId)
        {
            if (string.IsNullOrEmpty(taskId))
                throw new ArgumentNullException(nameof(taskId));

            try
            {
                var listId = await GetDefaultListIdAsync();
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.Error("[GraphToDoService] 기본 목록을 찾을 수 없어 작업을 삭제할 수 없습니다");
                    return false;
                }

                var client = _authService.GetGraphClient();
                await client.Me.Todo.Lists[listId].Tasks[taskId].DeleteAsync();

                _logger.Information("[GraphToDoService] 작업 삭제 완료: {TaskId}", taskId);
                return true;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
                when (odataEx.ResponseStatusCode == 404)
            {
                // 이미 삭제된 작업 - 성공으로 처리
                _logger.Warning("[GraphToDoService] 작업이 이미 삭제됨: {TaskId}", taskId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 작업 삭제 실패: {TaskId}", taskId);
                return false;
            }
        }

        /// <summary>
        /// Microsoft To Do 작업 완료 상태 업데이트
        /// </summary>
        /// <param name="taskId">작업 ID</param>
        /// <param name="isCompleted">완료 여부</param>
        /// <returns>업데이트 성공 여부</returns>
        public async Task<bool> UpdateTaskCompletionAsync(string taskId, bool isCompleted)
        {
            if (string.IsNullOrEmpty(taskId))
                throw new ArgumentNullException(nameof(taskId));

            try
            {
                var listId = await GetDefaultListIdAsync();
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.Error("[GraphToDoService] 기본 목록을 찾을 수 없습니다");
                    return false;
                }

                var client = _authService.GetGraphClient();

                var updateTask = new TodoTask
                {
                    Status = isCompleted ? Microsoft.Graph.Models.TaskStatus.Completed : Microsoft.Graph.Models.TaskStatus.NotStarted
                };

                if (isCompleted)
                {
                    updateTask.CompletedDateTime = new DateTimeTimeZone
                    {
                        DateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"),
                        TimeZone = "UTC"
                    };
                }

                await client.Me.Todo.Lists[listId].Tasks[taskId].PatchAsync(updateTask);

                _logger.Information("[GraphToDoService] 작업 상태 업데이트: {TaskId}, 완료={IsCompleted}",
                    taskId, isCompleted);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 작업 상태 업데이트 실패: {TaskId}", taskId);
                return false;
            }
        }

        /// <summary>
        /// 모든 To Do 목록 조회
        /// </summary>
        /// <returns>To Do 목록 리스트</returns>
        public async Task<List<TodoTaskListInfo>> GetAllListsAsync()
        {
            try
            {
                var client = _authService.GetGraphClient();
                var listsResponse = await client.Me.Todo.Lists.GetAsync();

                if (listsResponse?.Value == null)
                    return new List<TodoTaskListInfo>();

                return listsResponse.Value.Select(l => new TodoTaskListInfo
                {
                    Id = l.Id ?? string.Empty,
                    DisplayName = l.DisplayName ?? "이름 없음",
                    IsDefaultList = l.WellknownListName == WellknownListName.DefaultList
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] To Do 목록 조회 실패");
                return new List<TodoTaskListInfo>();
            }
        }

        /// <summary>
        /// 기본 목록의 모든 작업 조회 (미완료 작업만)
        /// </summary>
        /// <returns>작업 목록</returns>
        public async Task<List<TodoTaskItem>> GetTasksAsync(bool includeCompleted = false)
        {
            try
            {
                var listId = await GetDefaultListIdAsync();
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.Warning("[GraphToDoService] 기본 목록을 찾을 수 없습니다");
                    return new List<TodoTaskItem>();
                }

                var client = _authService.GetGraphClient();

                // 작업 조회 (최근 생성 순으로 정렬)
                var tasksResponse = await client.Me.Todo.Lists[listId].Tasks.GetAsync(config =>
                {
                    config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
                    config.QueryParameters.Top = 50; // 최대 50개
                    if (!includeCompleted)
                    {
                        config.QueryParameters.Filter = "status ne 'completed'";
                    }
                });

                if (tasksResponse?.Value == null)
                    return new List<TodoTaskItem>();

                var tasks = tasksResponse.Value.Select(t => new TodoTaskItem
                {
                    Id = t.Id ?? string.Empty,
                    Title = t.Title ?? string.Empty,
                    Body = t.Body?.Content,
                    IsCompleted = t.Status == Microsoft.Graph.Models.TaskStatus.Completed,
                    Importance = t.Importance?.ToString() ?? "Normal",
                    DueDate = t.DueDateTime?.DateTime != null
                        ? DateTime.TryParse(t.DueDateTime.DateTime, out var dt) ? dt : (DateTime?)null
                        : null,
                    CreatedAt = t.CreatedDateTime?.DateTime ?? DateTime.Now
                }).ToList();

                _logger.Debug("[GraphToDoService] 작업 {Count}개 조회됨", tasks.Count);
                return tasks;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 작업 목록 조회 실패");
                return new List<TodoTaskItem>();
            }
        }
    }

    /// <summary>
    /// To Do 목록 정보
    /// </summary>
    public class TodoTaskListInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsDefaultList { get; set; }
    }

    /// <summary>
    /// To Do 작업 항목
    /// </summary>
    public class TodoTaskItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private bool _isCompleted;

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Body { get; set; }
        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }
        public string Importance { get; set; } = "Normal";
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 마감일 표시 문자열
        /// </summary>
        public string DueDateDisplay => DueDate.HasValue
            ? DueDate.Value.ToString("MM/dd")
            : string.Empty;

        /// <summary>
        /// 중요도 아이콘 표시 여부
        /// </summary>
        public bool IsImportant => Importance == "High";
    }
}
