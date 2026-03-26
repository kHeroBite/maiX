using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mAIx.Utils;
using mAIx.ViewModels;

namespace mAIx.Services.Graph;

/// <summary>
/// Microsoft Planner 연동 서비스
/// </summary>
public class GraphPlannerService
{
    private readonly GraphAuthService _authService;

    public GraphPlannerService(GraphAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// 사용자의 모든 플랜 목록 조회
    /// </summary>
    public async Task<IEnumerable<PlannerPlan>> GetAllPlansAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Planner.Plans.GetAsync();

            Log4.Debug($"[PlannerService] 플랜 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<PlannerPlan>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 플랜 목록 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 특정 플랜 정보 조회
    /// </summary>
    public async Task<PlannerPlan?> GetPlanAsync(string planId)
    {
        if (string.IsNullOrEmpty(planId))
            throw new ArgumentNullException(nameof(planId));

        try
        {
            var client = _authService.GetGraphClient();
            var plan = await client.Planner.Plans[planId].GetAsync();
            return plan;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 플랜 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 플랜의 버킷 목록 조회
    /// </summary>
    public async Task<IEnumerable<PlannerBucket>> GetBucketsAsync(string planId)
    {
        if (string.IsNullOrEmpty(planId))
            throw new ArgumentNullException(nameof(planId));

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Planner.Plans[planId].Buckets.GetAsync();

            Log4.Debug($"[PlannerService] 플랜 {planId} 버킷 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<PlannerBucket>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 버킷 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 플랜의 작업 목록 조회
    /// </summary>
    public async Task<IEnumerable<PlannerTask>> GetTasksAsync(string planId)
    {
        if (string.IsNullOrEmpty(planId))
            throw new ArgumentNullException(nameof(planId));

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Planner.Plans[planId].Tasks.GetAsync();

            Log4.Debug($"[PlannerService] 플랜 {planId} 작업 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<PlannerTask>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 사용자에게 할당된 모든 작업 조회
    /// </summary>
    public async Task<IEnumerable<PlannerTask>> GetMyTasksAsync()
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Planner.Tasks.GetAsync();

            Log4.Debug($"[PlannerService] 내 작업 {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<PlannerTask>();
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 내 작업 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 새 버킷 생성
    /// </summary>
    public async Task<PlannerBucket?> CreateBucketAsync(string planId, string name, string? orderHint = null)
    {
        if (string.IsNullOrEmpty(planId))
            throw new ArgumentNullException(nameof(planId));
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        try
        {
            var client = _authService.GetGraphClient();
            var bucket = new PlannerBucket
            {
                Name = name,
                PlanId = planId,
                OrderHint = orderHint ?? " !"
            };

            var response = await client.Planner.Buckets.PostAsync(bucket);
            Log4.Info($"[PlannerService] 버킷 생성 완료: {name}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 버킷 생성 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 새 작업 생성
    /// </summary>
    public async Task<PlannerTask?> CreateTaskAsync(string planId, string bucketId, string title, string? assignedToUserId = null)
    {
        if (string.IsNullOrEmpty(planId))
            throw new ArgumentNullException(nameof(planId));
        if (string.IsNullOrEmpty(title))
            throw new ArgumentNullException(nameof(title));

        try
        {
            var client = _authService.GetGraphClient();
            var task = new PlannerTask
            {
                PlanId = planId,
                BucketId = bucketId,
                Title = title
            };

            // 담당자 할당
            if (!string.IsNullOrEmpty(assignedToUserId))
            {
                task.Assignments = new PlannerAssignments
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        { assignedToUserId, new PlannerAssignment { OrderHint = " !" } }
                    }
                };
            }

            var response = await client.Planner.Tasks.PostAsync(task);
            Log4.Info($"[PlannerService] 작업 생성 완료: {title}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 생성 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 작업 상세 정보 조회
    /// </summary>
    public async Task<PlannerTaskDetails?> GetTaskDetailsAsync(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentNullException(nameof(taskId));

        try
        {
            var client = _authService.GetGraphClient();
            var details = await client.Planner.Tasks[taskId].Details.GetAsync();
            return details;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 상세 조회 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 작업 상세 정보 업데이트 (메모, 체크리스트 등)
    /// </summary>
    public async Task<PlannerTaskDetails?> UpdateTaskDetailsAsync(string taskId, string? description = null)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentNullException(nameof(taskId));

        try
        {
            var client = _authService.GetGraphClient();

            // 먼저 현재 details를 가져와서 etag 획득
            var currentDetails = await client.Planner.Tasks[taskId].Details.GetAsync();
            var etag = currentDetails?.AdditionalData?.TryGetValue("@odata.etag", out var etagValue) == true
                ? etagValue?.ToString()
                : null;

            if (string.IsNullOrEmpty(etag))
            {
                Log4.Error("[PlannerService] 작업 상세 ETag를 가져올 수 없습니다.");
                return null;
            }

            var updatedDetails = new PlannerTaskDetails();

            if (description != null)
            {
                updatedDetails.Description = description;
            }

            var response = await client.Planner.Tasks[taskId].Details.PatchAsync(updatedDetails, config =>
            {
                config.Headers.Add("If-Match", etag);
            });

            Log4.Info($"[PlannerService] 작업 상세 업데이트 완료: {taskId}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 상세 업데이트 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 작업 업데이트 (제목, 버킷 이동 등)
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskAsync(string taskId, string etag, PlannerTask updatedTask)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentNullException(nameof(taskId));
        if (string.IsNullOrEmpty(etag))
            throw new ArgumentNullException(nameof(etag));

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Planner.Tasks[taskId].PatchAsync(updatedTask, config =>
            {
                config.Headers.Add("If-Match", etag);
            });

            Log4.Info($"[PlannerService] 작업 업데이트 완료: {taskId}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 업데이트 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 작업 완료율 업데이트
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskPercentCompleteAsync(string taskId, string etag, int percentComplete)
    {
        var task = new PlannerTask
        {
            PercentComplete = percentComplete
        };

        return await UpdateTaskAsync(taskId, etag, task);
    }

    /// <summary>
    /// 작업 버킷 이동
    /// </summary>
    public async Task<PlannerTask?> MoveTaskToBucketAsync(string taskId, string etag, string newBucketId)
    {
        var task = new PlannerTask
        {
            BucketId = newBucketId
        };

        return await UpdateTaskAsync(taskId, etag, task);
    }

    /// <summary>
    /// 작업 삭제
    /// </summary>
    public async Task<bool> DeleteTaskAsync(string taskId, string etag)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentNullException(nameof(taskId));
        if (string.IsNullOrEmpty(etag))
            throw new ArgumentNullException(nameof(etag));

        try
        {
            var client = _authService.GetGraphClient();
            await client.Planner.Tasks[taskId].DeleteAsync(config =>
            {
                config.Headers.Add("If-Match", etag);
            });

            Log4.Info($"[PlannerService] 작업 삭제 완료: {taskId}");
            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 삭제 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 버킷 삭제
    /// </summary>
    public async Task<bool> DeleteBucketAsync(string bucketId, string etag)
    {
        if (string.IsNullOrEmpty(bucketId))
            throw new ArgumentNullException(nameof(bucketId));
        if (string.IsNullOrEmpty(etag))
            throw new ArgumentNullException(nameof(etag));

        try
        {
            var client = _authService.GetGraphClient();
            await client.Planner.Buckets[bucketId].DeleteAsync(config =>
            {
                config.Headers.Add("If-Match", etag);
            });

            Log4.Info($"[PlannerService] 버킷 삭제 완료: {bucketId}");
            return true;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 버킷 삭제 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 작업 진행률 표시 (0%, 50%, 100%)
    /// </summary>
    public static string GetPercentCompleteDisplay(int? percentComplete)
    {
        return percentComplete switch
        {
            null or 0 => "시작 안함",
            100 => "완료",
            _ => $"{percentComplete}%"
        };
    }

    /// <summary>
    /// 우선순위 표시
    /// </summary>
    public static string GetPriorityDisplay(int? priority)
    {
        return priority switch
        {
            1 => "긴급",
            3 => "중요",
            5 => "중간",
            9 => "낮음",
            _ => "중간"
        };
    }

    /// <summary>
    /// 플랜의 카테고리(라벨) 정의 조회
    /// Microsoft Planner는 category1~category25까지 지원
    /// </summary>
    public async Task<List<PlanCategoryViewModel>> GetPlanCategoriesAsync(string planId)
    {
        if (string.IsNullOrEmpty(planId))
            throw new ArgumentNullException(nameof(planId));

        var categories = new List<PlanCategoryViewModel>();

        try
        {
            // Beta API를 사용하여 category1~25 모두 가져오기
            var categoryNames = await GetPlanCategoryNamesFromBetaApiAsync(planId);

            // 이름이 있는 카테고리만 추가
            foreach (var kvp in categoryNames)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    categories.Add(new PlanCategoryViewModel
                    {
                        CategoryId = kvp.Key,
                        Name = kvp.Value,
                        Color = PlanCategoryViewModel.GetDefaultColor(kvp.Key)
                    });
                    Log4.Debug($"[PlannerService] 카테고리 로드: {kvp.Key} = {kvp.Value}");
                }
            }

            // 기본 카테고리 6개 추가 (API에서 못 가져왔을 경우)
            if (categories.Count == 0)
            {
                for (int i = 1; i <= 6; i++)
                {
                    var categoryId = $"category{i}";
                    categories.Add(new PlanCategoryViewModel
                    {
                        CategoryId = categoryId,
                        Name = $"라벨 {i}",
                        Color = PlanCategoryViewModel.GetDefaultColor(categoryId)
                    });
                }
            }

            Log4.Debug($"[PlannerService] 플랜 {planId} 카테고리 {categories.Count}개 조회");
            return categories;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 플랜 카테고리 조회 실패: {ex.Message}");
            // 기본 카테고리 반환
            for (int i = 1; i <= 6; i++)
            {
                var categoryId = $"category{i}";
                categories.Add(new PlanCategoryViewModel
                {
                    CategoryId = categoryId,
                    Name = $"라벨 {i}",
                    Color = PlanCategoryViewModel.GetDefaultColor(categoryId)
                });
            }
            return categories;
        }
    }

    /// <summary>
    /// Beta API를 사용하여 플랜의 카테고리 이름 조회 (category1~25)
    /// </summary>
    private async Task<Dictionary<string, string?>> GetPlanCategoryNamesFromBetaApiAsync(string planId)
    {
        var categoryNames = new Dictionary<string, string?>();

        try
        {
            // Beta API 엔드포인트 직접 호출
            var httpClient = await _authService.GetHttpClientAsync();
            var response = await httpClient.GetAsync($"https://graph.microsoft.com/beta/planner/plans/{planId}/details");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = JsonDocument.Parse(content);

                if (json.RootElement.TryGetProperty("categoryDescriptions", out var catDescElement))
                {
                    // category1~category25 순회
                    for (int i = 1; i <= 25; i++)
                    {
                        var categoryId = $"category{i}";
                        if (catDescElement.TryGetProperty(categoryId, out var categoryValue) &&
                            categoryValue.ValueKind == JsonValueKind.String)
                        {
                            var name = categoryValue.GetString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                categoryNames[categoryId] = name;
                            }
                        }
                    }
                }
            }
            else
            {
                Log4.Warn($"[PlannerService] Beta API 카테고리 조회 실패: {response.StatusCode}");
                // v1.0 API fallback
                return await GetPlanCategoryNamesFromV1ApiAsync(planId);
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[PlannerService] Beta API 카테고리 조회 예외: {ex.Message}");
            // v1.0 API fallback
            return await GetPlanCategoryNamesFromV1ApiAsync(planId);
        }

        return categoryNames;
    }

    /// <summary>
    /// v1.0 API를 사용하여 플랜의 카테고리 이름 조회 (category1~6 only)
    /// </summary>
    private async Task<Dictionary<string, string?>> GetPlanCategoryNamesFromV1ApiAsync(string planId)
    {
        var categoryNames = new Dictionary<string, string?>();

        try
        {
            var client = _authService.GetGraphClient();
            var details = await client.Planner.Plans[planId].Details.GetAsync();

            if (details?.CategoryDescriptions != null)
            {
                var catDesc = details.CategoryDescriptions;
                categoryNames["category1"] = catDesc.Category1;
                categoryNames["category2"] = catDesc.Category2;
                categoryNames["category3"] = catDesc.Category3;
                categoryNames["category4"] = catDesc.Category4;
                categoryNames["category5"] = catDesc.Category5;
                categoryNames["category6"] = catDesc.Category6;
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[PlannerService] v1.0 API 카테고리 조회 실패: {ex.Message}");
        }

        return categoryNames;
    }

    /// <summary>
    /// 플랜의 작업 목록 조회 (상세 정보 포함)
    /// </summary>
    public async Task<IEnumerable<PlannerTask>> GetTasksWithDetailsAsync(string planId)
    {
        if (string.IsNullOrEmpty(planId))
            throw new ArgumentNullException(nameof(planId));

        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Planner.Plans[planId].Tasks.GetAsync(config =>
            {
                // appliedCategories, assignments 포함
                config.QueryParameters.Expand = new[] { "details" };
            });

            Log4.Debug($"[PlannerService] 플랜 {planId} 작업(상세) {response?.Value?.Count ?? 0}개 조회");
            return response?.Value ?? new List<PlannerTask>();
        }
        catch (Exception ex)
        {
            Log4.Warn($"[PlannerService] 작업 상세 조회 실패, 기본 조회로 대체: {ex.Message}");
            // 상세 조회 실패 시 기본 조회
            return await GetTasksAsync(planId);
        }
    }

    /// <summary>
    /// 작업 카테고리(라벨) 업데이트
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskCategoriesAsync(string taskId, string etag, Dictionary<string, bool> categories)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentNullException(nameof(taskId));
        if (string.IsNullOrEmpty(etag))
            throw new ArgumentNullException(nameof(etag));

        try
        {
            var client = _authService.GetGraphClient();

            var task = new PlannerTask
            {
                AppliedCategories = new PlannerAppliedCategories
                {
                    AdditionalData = categories.ToDictionary(k => k.Key, v => (object)v.Value)
                }
            };

            var response = await client.Planner.Tasks[taskId].PatchAsync(task, config =>
            {
                config.Headers.Add("If-Match", etag);
            });

            Log4.Info($"[PlannerService] 작업 카테고리 업데이트 완료: {taskId}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 카테고리 업데이트 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 작업 순서 힌트 업데이트 (드래그앤드롭)
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskOrderHintAsync(string taskId, string etag, string orderHint)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentNullException(nameof(taskId));
        if (string.IsNullOrEmpty(etag))
            throw new ArgumentNullException(nameof(etag));

        try
        {
            var client = _authService.GetGraphClient();

            var task = new PlannerTask
            {
                OrderHint = orderHint
            };

            var response = await client.Planner.Tasks[taskId].PatchAsync(task, config =>
            {
                config.Headers.Add("If-Match", etag);
            });

            Log4.Info($"[PlannerService] 작업 순서 업데이트 완료: {taskId}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 순서 업데이트 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 작업 담당자 업데이트
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskAssignmentsAsync(string taskId, string etag, List<string> userIds)
    {
        if (string.IsNullOrEmpty(taskId))
            throw new ArgumentNullException(nameof(taskId));
        if (string.IsNullOrEmpty(etag))
            throw new ArgumentNullException(nameof(etag));

        try
        {
            var client = _authService.GetGraphClient();

            var assignments = new PlannerAssignments
            {
                AdditionalData = userIds.ToDictionary(
                    userId => userId,
                    userId => (object)new PlannerAssignment { OrderHint = " !" })
            };

            var task = new PlannerTask
            {
                Assignments = assignments
            };

            var response = await client.Planner.Tasks[taskId].PatchAsync(task, config =>
            {
                config.Headers.Add("If-Match", etag);
            });

            Log4.Info($"[PlannerService] 작업 담당자 업데이트 완료: {taskId}");
            return response;
        }
        catch (Exception ex)
        {
            Log4.Error($"[PlannerService] 작업 담당자 업데이트 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 작업 우선순위 업데이트
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskPriorityAsync(string taskId, string etag, int priority)
    {
        var task = new PlannerTask
        {
            Priority = priority
        };

        return await UpdateTaskAsync(taskId, etag, task);
    }

    /// <summary>
    /// 작업 마감일 업데이트
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskDueDateAsync(string taskId, string etag, DateTime? dueDate)
    {
        var task = new PlannerTask();

        if (dueDate.HasValue)
        {
            task.DueDateTime = new DateTimeOffset(DateTime.SpecifyKind(dueDate.Value, DateTimeKind.Utc));
        }

        return await UpdateTaskAsync(taskId, etag, task);
    }

    /// <summary>
    /// 작업 제목 업데이트
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskTitleAsync(string taskId, string etag, string title)
    {
        if (string.IsNullOrEmpty(title))
            throw new ArgumentNullException(nameof(title));

        var task = new PlannerTask
        {
            Title = title
        };

        return await UpdateTaskAsync(taskId, etag, task);
    }

    /// <summary>
    /// 작업 시작일 업데이트
    /// </summary>
    public async Task<PlannerTask?> UpdateTaskStartDateAsync(string taskId, string etag, DateTime? startDate)
    {
        var task = new PlannerTask();

        if (startDate.HasValue)
        {
            task.StartDateTime = new DateTimeOffset(DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc));
        }

        return await UpdateTaskAsync(taskId, etag, task);
    }

    // 사용자 이름 캐시 (세션 동안 유지)
    private static readonly Dictionary<string, string> _userNameCache = new();

    /// <summary>
    /// 사용자 ID로 표시 이름 조회 (캐시 사용)
    /// </summary>
    public async Task<string> GetUserDisplayNameAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return "Unknown";

        // 캐시 확인
        if (_userNameCache.TryGetValue(userId, out var cachedName))
            return cachedName;

        try
        {
            var client = _authService.GetGraphClient();
            var user = await client.Users[userId].GetAsync(config =>
            {
                config.QueryParameters.Select = new[] { "displayName" };
            });

            var displayName = user?.DisplayName ?? "Unknown";
            _userNameCache[userId] = displayName;
            return displayName;
        }
        catch (Exception ex)
        {
            Log4.Debug($"[PlannerService] 사용자 정보 조회 실패 ({userId}): {ex.Message}");
            // 실패 시 userId의 앞 8자를 반환
            var fallbackName = userId.Length > 8 ? userId[..8] : userId;
            _userNameCache[userId] = fallbackName;
            return fallbackName;
        }
    }

    /// <summary>
    /// 여러 사용자 ID의 표시 이름 일괄 조회
    /// </summary>
    public async Task<Dictionary<string, string>> GetUserDisplayNamesAsync(IEnumerable<string> userIds)
    {
        var result = new Dictionary<string, string>();
        var uncachedIds = new List<string>();

        foreach (var userId in userIds.Distinct())
        {
            if (_userNameCache.TryGetValue(userId, out var cachedName))
            {
                result[userId] = cachedName;
            }
            else
            {
                uncachedIds.Add(userId);
            }
        }

        // 캐시에 없는 사용자들 조회
        foreach (var userId in uncachedIds)
        {
            var displayName = await GetUserDisplayNameAsync(userId);
            result[userId] = displayName;
        }

        return result;
    }

    // 사용자 프로필 사진 캐시 (세션 동안 유지)
    private static readonly Dictionary<string, string?> _userPhotoCache = new();

    /// <summary>
    /// 사용자 ID로 프로필 사진 조회 (캐시 사용)
    /// </summary>
    /// <param name="userId">사용자 ID</param>
    /// <returns>Base64 인코딩된 사진 (없으면 null)</returns>
    public async Task<string?> GetUserPhotoAsync(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        // 캐시 확인
        if (_userPhotoCache.TryGetValue(userId, out var cachedPhoto))
            return cachedPhoto;

        try
        {
            var client = _authService.GetGraphClient();
            var photoStream = await client.Users[userId].Photo.Content.GetAsync();

            if (photoStream != null)
            {
                using var memoryStream = new System.IO.MemoryStream();
                await photoStream.CopyToAsync(memoryStream);
                var photoBase64 = Convert.ToBase64String(memoryStream.ToArray());
                _userPhotoCache[userId] = photoBase64;
                return photoBase64;
            }
        }
        catch
        {
            // 사진이 없는 경우 예외 발생 - 무시하고 null 캐시
        }

        _userPhotoCache[userId] = null;
        return null;
    }

    /// <summary>
    /// 여러 사용자 ID의 프로필 사진 일괄 조회
    /// </summary>
    public async Task<Dictionary<string, string?>> GetUserPhotosAsync(IEnumerable<string> userIds)
    {
        var result = new Dictionary<string, string?>();
        var uncachedIds = new List<string>();

        foreach (var userId in userIds.Distinct())
        {
            if (_userPhotoCache.TryGetValue(userId, out var cachedPhoto))
            {
                result[userId] = cachedPhoto;
            }
            else
            {
                uncachedIds.Add(userId);
            }
        }

        // 캐시에 없는 사용자들 조회 (병렬로 최대 5개씩)
        var tasks = uncachedIds.Select(async userId =>
        {
            var photo = await GetUserPhotoAsync(userId);
            return (userId, photo);
        });

        var results = await Task.WhenAll(tasks);
        foreach (var (userId, photo) in results)
        {
            result[userId] = photo;
        }

        return result;
    }
}
