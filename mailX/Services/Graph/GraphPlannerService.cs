using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using mailX.Utils;

namespace mailX.Services.Graph;

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
}
