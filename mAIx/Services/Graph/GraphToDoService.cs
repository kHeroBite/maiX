using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Serilog;
using Serilog.Core;

namespace mAIx.Services.Graph
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
                var listsResponse = await client.Me.Todo.Lists.GetAsync().ConfigureAwait(false);

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
                var listId = await GetDefaultListIdAsync().ConfigureAwait(false);
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

                var createdTask = await client.Me.Todo.Lists[listId].Tasks.PostAsync(newTask).ConfigureAwait(false);

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
                var listId = await GetDefaultListIdAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(listId))
                {
                    _logger.Error("[GraphToDoService] 기본 목록을 찾을 수 없어 작업을 삭제할 수 없습니다");
                    return false;
                }

                var client = _authService.GetGraphClient();
                await client.Me.Todo.Lists[listId].Tasks[taskId].DeleteAsync().ConfigureAwait(false);

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
                var listId = await GetDefaultListIdAsync().ConfigureAwait(false);
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

                await client.Me.Todo.Lists[listId].Tasks[taskId].PatchAsync(updateTask).ConfigureAwait(false);

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
                var listsResponse = await client.Me.Todo.Lists.GetAsync().ConfigureAwait(false);

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
                var listId = await GetDefaultListIdAsync().ConfigureAwait(false);
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
                }).ConfigureAwait(false);

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

        /// <summary>
        /// 특정 목록에서 작업 조회
        /// </summary>
        public async Task<List<TodoTaskItem>> GetTasksFromListAsync(string listId, bool includeCompleted = false)
        {
            try
            {
                var client = _authService.GetGraphClient();

                var tasksResponse = await client.Me.Todo.Lists[listId].Tasks.GetAsync(config =>
                {
                    config.QueryParameters.Orderby = new[] { "createdDateTime desc" };
                    config.QueryParameters.Top = 100;
                    if (!includeCompleted)
                    {
                        config.QueryParameters.Filter = "status ne 'completed'";
                    }
                }).ConfigureAwait(false);

                if (tasksResponse?.Value == null)
                    return new List<TodoTaskItem>();

                return tasksResponse.Value.Select(t => new TodoTaskItem
                {
                    Id = t.Id ?? string.Empty,
                    Title = t.Title ?? string.Empty,
                    Body = t.Body?.Content,
                    IsCompleted = t.Status == Microsoft.Graph.Models.TaskStatus.Completed,
                    Importance = t.Importance?.ToString() ?? "Normal",
                    DueDate = t.DueDateTime?.DateTime != null
                        ? DateTime.TryParse(t.DueDateTime.DateTime, out var dt) ? dt : (DateTime?)null
                        : null,
                    CreatedAt = t.CreatedDateTime?.DateTime ?? DateTime.Now,
                    IsMyDay = t.CreatedDateTime.HasValue
                        && t.CreatedDateTime.Value.Date == DateTime.Today,
                    RecurrencePattern = t.Recurrence?.Pattern?.Type?.ToString()
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 목록 {ListId}의 작업 조회 실패", listId);
                return new List<TodoTaskItem>();
            }
        }

        /// <summary>
        /// 특정 목록에 작업 생성
        /// </summary>
        public async Task<string?> CreateTaskInListAsync(string listId, string title, DateTime? dueDate = null, string? body = null)
        {
            if (string.IsNullOrEmpty(title))
                throw new ArgumentNullException(nameof(title));

            try
            {
                var client = _authService.GetGraphClient();

                var newTask = new TodoTask
                {
                    Title = title,
                    Importance = Importance.Normal
                };

                if (dueDate.HasValue)
                {
                    newTask.DueDateTime = new DateTimeTimeZone
                    {
                        DateTime = dueDate.Value.ToString("yyyy-MM-ddT00:00:00"),
                        TimeZone = "Asia/Seoul"
                    };
                }

                if (!string.IsNullOrEmpty(body))
                {
                    newTask.Body = new ItemBody
                    {
                        Content = body,
                        ContentType = BodyType.Text
                    };
                }

                var createdTask = await client.Me.Todo.Lists[listId].Tasks.PostAsync(newTask).ConfigureAwait(false);

                if (createdTask != null)
                {
                    _logger.Information("[GraphToDoService] 작업 생성: {Title} (목록: {ListId})", title, listId);
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
        /// 특정 목록에서 작업 완료 상태 업데이트
        /// </summary>
        public async Task<bool> UpdateTaskCompletionInListAsync(string listId, string taskId, bool isCompleted)
        {
            try
            {
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

                await client.Me.Todo.Lists[listId].Tasks[taskId].PatchAsync(updateTask).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 작업 상태 업데이트 실패: {TaskId}", taskId);
                return false;
            }
        }

        /// <summary>
        /// 작업 중요도 업데이트
        /// </summary>
        public async Task<bool> UpdateTaskImportanceAsync(string listId, string taskId, bool isImportant)
        {
            try
            {
                var client = _authService.GetGraphClient();

                var updateTask = new TodoTask
                {
                    Importance = isImportant ? Importance.High : Importance.Normal
                };

                await client.Me.Todo.Lists[listId].Tasks[taskId].PatchAsync(updateTask).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 중요도 업데이트 실패: {TaskId}", taskId);
                return false;
            }
        }

        /// <summary>
        /// 특정 목록에서 작업 삭제
        /// </summary>
        public async Task<bool> DeleteTaskFromListAsync(string listId, string taskId)
        {
            try
            {
                var client = _authService.GetGraphClient();
                await client.Me.Todo.Lists[listId].Tasks[taskId].DeleteAsync().ConfigureAwait(false);
                _logger.Information("[GraphToDoService] 작업 삭제: {TaskId} (목록: {ListId})", taskId, listId);
                return true;
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
                when (odataEx.ResponseStatusCode == 404)
            {
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
        /// 목록 생성
        /// </summary>
        public async Task<string?> CreateListAsync(string displayName)
        {
            try
            {
                var client = _authService.GetGraphClient();
                var newList = new TodoTaskList { DisplayName = displayName };
                var created = await client.Me.Todo.Lists.PostAsync(newList).ConfigureAwait(false);
                _logger.Information("[GraphToDoService] 목록 생성: {Name}", displayName);
                return created?.Id;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 목록 생성 실패: {Name}", displayName);
                return null;
            }
        }

        /// <summary>
        /// 목록 삭제
        /// </summary>
        public async Task<bool> DeleteListAsync(string listId)
        {
            try
            {
                var client = _authService.GetGraphClient();
                await client.Me.Todo.Lists[listId].DeleteAsync().ConfigureAwait(false);
                _logger.Information("[GraphToDoService] 목록 삭제: {ListId}", listId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 목록 삭제 실패: {ListId}", listId);
                return false;
            }
        }

        /// <summary>
        /// 반복 패턴 설정
        /// </summary>
        public async Task<bool> SetRecurrenceAsync(string listId, string taskId, string pattern)
        {
            try
            {
                var client = _authService.GetGraphClient();

                var recurrence = new PatternedRecurrence
                {
                    Pattern = ParseRecurrencePattern(pattern),
                    Range = new RecurrenceRange
                    {
                        Type = RecurrenceRangeType.NoEnd,
                        StartDate = new Microsoft.Kiota.Abstractions.Date(DateTime.Today)
                    }
                };

                var updateTask = new TodoTask { Recurrence = recurrence };
                await client.Me.Todo.Lists[listId].Tasks[taskId].PatchAsync(updateTask).ConfigureAwait(false);

                _logger.Information("[GraphToDoService] 반복 설정: {TaskId} → {Pattern}", taskId, pattern);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 반복 설정 실패: {TaskId}", taskId);
                return false;
            }
        }

        /// <summary>
        /// 반복 패턴 문자열을 Graph API 패턴으로 변환
        /// </summary>
        private RecurrencePattern ParseRecurrencePattern(string pattern)
        {
            if (pattern.StartsWith("weekly:"))
            {
                var daysPart = pattern.Substring(7);
                var days = new List<DayOfWeek>();
                foreach (var d in daysPart.Split(','))
                {
                    var day = d.Trim().ToLower() switch
                    {
                        "mon" => DayOfWeek.Monday,
                        "tue" => DayOfWeek.Tuesday,
                        "wed" => DayOfWeek.Wednesday,
                        "thu" => DayOfWeek.Thursday,
                        "fri" => DayOfWeek.Friday,
                        "sat" => DayOfWeek.Saturday,
                        "sun" => DayOfWeek.Sunday,
                        _ => (DayOfWeek?)null
                    };
                    if (day.HasValue) days.Add(day.Value);
                }

                return new RecurrencePattern
                {
                    Type = RecurrencePatternType.Weekly,
                    Interval = 1,
                    DaysOfWeek = days.Select(d => (Microsoft.Graph.Models.DayOfWeekObject?)(d switch
                    {
                        DayOfWeek.Monday => Microsoft.Graph.Models.DayOfWeekObject.Monday,
                        DayOfWeek.Tuesday => Microsoft.Graph.Models.DayOfWeekObject.Tuesday,
                        DayOfWeek.Wednesday => Microsoft.Graph.Models.DayOfWeekObject.Wednesday,
                        DayOfWeek.Thursday => Microsoft.Graph.Models.DayOfWeekObject.Thursday,
                        DayOfWeek.Friday => Microsoft.Graph.Models.DayOfWeekObject.Friday,
                        DayOfWeek.Saturday => Microsoft.Graph.Models.DayOfWeekObject.Saturday,
                        DayOfWeek.Sunday => Microsoft.Graph.Models.DayOfWeekObject.Sunday,
                        _ => Microsoft.Graph.Models.DayOfWeekObject.Monday
                    })).ToList()
                };
            }

            return pattern switch
            {
                "daily" => new RecurrencePattern { Type = RecurrencePatternType.Daily, Interval = 1 },
                "weekly" => new RecurrencePattern { Type = RecurrencePatternType.Weekly, Interval = 1 },
                "monthly" => new RecurrencePattern { Type = RecurrencePatternType.AbsoluteMonthly, Interval = 1, DayOfMonth = DateTime.Today.Day },
                _ => new RecurrencePattern { Type = RecurrencePatternType.Daily, Interval = 1 }
            };
        }

        /// <summary>
        /// 플래그된 이메일을 ToDo와 동기화
        /// </summary>
        public async Task SyncFlaggedEmailsAsync()
        {
            try
            {
                var client = _authService.GetGraphClient();

                // 플래그된 이메일 조회
                var flaggedEmails = await client.Me.Messages.GetAsync(config =>
                {
                    config.QueryParameters.Filter = "flag/flagStatus eq 'flagged'";
                    config.QueryParameters.Select = new[] { "id", "subject", "receivedDateTime" };
                    config.QueryParameters.Top = 50;
                    config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                }).ConfigureAwait(false);

                if (flaggedEmails?.Value == null || !flaggedEmails.Value.Any())
                {
                    _logger.Debug("[GraphToDoService] 플래그된 이메일 없음");
                    return;
                }

                // 기본 목록에 플래그 이메일을 할일로 생성
                var listId = await GetDefaultListIdAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(listId)) return;

                var existingTasks = await GetTasksFromListAsync(listId, true).ConfigureAwait(false);
                var existingTitles = new HashSet<string>(existingTasks.Select(t => t.Title));

                foreach (var email in flaggedEmails.Value)
                {
                    var title = $"📧 {email.Subject}";
                    if (existingTitles.Contains(title)) continue;

                    await CreateTaskInListAsync(listId, title, body: $"이메일에서 가져옴 (수신: {email.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm")})").ConfigureAwait(false);
                }

                _logger.Information("[GraphToDoService] 플래그 이메일 동기화 완료: {Count}건 확인", flaggedEmails.Value.Count);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[GraphToDoService] 플래그 이메일 동기화 실패");
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
        private bool _isMyDay;
        private string _importance = "Normal";

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Body { get; set; }
        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }
        public string Importance
        {
            get => _importance;
            set
            {
                if (SetProperty(ref _importance, value))
                    OnPropertyChanged(nameof(IsImportant));
            }
        }
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 소속 목록 ID
        /// </summary>
        public string? ListId { get; set; }

        /// <summary>
        /// 소속 목록 이름
        /// </summary>
        public string? ListName { get; set; }

        /// <summary>
        /// My Day 포함 여부
        /// </summary>
        public bool IsMyDay
        {
            get => _isMyDay;
            set => SetProperty(ref _isMyDay, value);
        }

        /// <summary>
        /// 반복 패턴
        /// </summary>
        public string? RecurrencePattern { get; set; }

        /// <summary>
        /// 반복 작업 여부
        /// </summary>
        public bool IsRecurring => !string.IsNullOrEmpty(RecurrencePattern);

        /// <summary>
        /// 마감일 표시 문자열
        /// </summary>
        public string DueDateDisplay
        {
            get
            {
                if (!DueDate.HasValue) return string.Empty;
                var date = DueDate.Value.Date;
                var today = DateTime.Today;
                if (date == today) return "오늘";
                if (date == today.AddDays(1)) return "내일";
                if (date < today) return $"기한 지남 ({date:MM/dd})";
                return date.ToString("MM/dd");
            }
        }

        /// <summary>
        /// 마감일 색상 (지난 날짜는 빨간색)
        /// </summary>
        public bool IsOverdue => DueDate.HasValue && DueDate.Value.Date < DateTime.Today && !IsCompleted;

        /// <summary>
        /// 중요도 아이콘 표시 여부
        /// </summary>
        public bool IsImportant => Importance == "High";
    }
}
