using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.Graph;
using mAIx.Utils;

namespace mAIx.Services;

/// <summary>
/// Quick Steps CRUD + 실행 서비스
/// 반복 작업 자동화 규칙 관리 및 Graph API 순차 실행
/// </summary>
public class QuickStepService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public QuickStepService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = Log.ForContext<QuickStepService>();
    }

    /// <summary>
    /// 모든 Quick Step 조회 (SortOrder 오름차순)
    /// </summary>
    public async Task<List<QuickStep>> GetAllAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        return await dbContext.QuickSteps
            .OrderBy(q => q.SortOrder)
            .ThenBy(q => q.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Quick Step 저장 (신규/수정)
    /// </summary>
    public async Task SaveAsync(QuickStep quickStep)
    {
        if (quickStep == null) throw new ArgumentNullException(nameof(quickStep));

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        quickStep.UpdatedAt = DateTime.UtcNow;

        if (quickStep.Id == 0)
        {
            quickStep.CreatedAt = DateTime.UtcNow;
            dbContext.QuickSteps.Add(quickStep);
        }
        else
        {
            dbContext.QuickSteps.Update(quickStep);
        }

        await dbContext.SaveChangesAsync();
        Log4.Info($"[QuickStepService] QuickStep 저장 완료: {quickStep.Name} (Id={quickStep.Id})");
    }

    /// <summary>
    /// Quick Step 삭제
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var quickStep = await dbContext.QuickSteps.FindAsync(id);
        if (quickStep == null)
        {
            _logger.Warning("[QuickStepService] 삭제 대상 QuickStep 없음: Id={Id}", id);
            return;
        }

        dbContext.QuickSteps.Remove(quickStep);
        await dbContext.SaveChangesAsync();
        Log4.Info($"[QuickStepService] QuickStep 삭제 완료: Id={id}");
    }

    /// <summary>
    /// Quick Step 실행 — ActionsJson 파싱하여 Graph API 순차 실행
    /// 지원 액션: MoveToFolder, MarkAsRead, Delete, Flag, AddCategory
    /// </summary>
    public async Task ExecuteAsync(int quickStepId, string emailId)
    {
        if (string.IsNullOrEmpty(emailId))
        {
            _logger.Warning("[QuickStepService] emailId가 비어있어 실행 건너뜀");
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var quickStep = await dbContext.QuickSteps.FindAsync(quickStepId);
        if (quickStep == null)
        {
            _logger.Warning("[QuickStepService] QuickStep 없음: Id={Id}", quickStepId);
            return;
        }

        if (!quickStep.IsEnabled)
        {
            Log4.Debug($"[QuickStepService] QuickStep 비활성화 상태, 건너뜀: {quickStep.Name}");
            return;
        }

        List<JsonElement> actions;
        try
        {
            actions = JsonSerializer.Deserialize<List<JsonElement>>(quickStep.ActionsJson)
                      ?? new List<JsonElement>();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "[QuickStepService] ActionsJson 파싱 실패: {Json}", quickStep.ActionsJson);
            return;
        }

        var graphMailService = scope.ServiceProvider.GetService<GraphMailService>();
        if (graphMailService == null)
        {
            _logger.Warning("[QuickStepService] GraphMailService를 가져올 수 없음");
            return;
        }

        Log4.Info($"[QuickStepService] QuickStep 실행 시작: {quickStep.Name}, 액션 {actions.Count}개");

        foreach (var action in actions)
        {
            try
            {
                var actionType = action.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString() ?? ""
                    : "";

                switch (actionType)
                {
                    case "MarkAsRead":
                        await graphMailService.UpdateMessageReadStatusAsync(emailId, true);
                        Log4.Debug($"[QuickStepService] MarkAsRead 완료");
                        break;

                    case "MoveToFolder":
                        var folderId = action.TryGetProperty("folderId", out var folderProp)
                            ? folderProp.GetString() ?? ""
                            : "";
                        if (!string.IsNullOrEmpty(folderId))
                        {
                            await graphMailService.MoveMessageAsync(emailId, folderId);
                            Log4.Debug($"[QuickStepService] MoveToFolder 완료: {folderId}");
                        }
                        break;

                    case "Delete":
                        await graphMailService.DeleteMessageAsync(emailId);
                        Log4.Debug($"[QuickStepService] Delete 완료");
                        break;

                    case "Flag":
                        await graphMailService.UpdateMessageFlagAsync(emailId, "flagged");
                        Log4.Debug($"[QuickStepService] Flag 완료");
                        break;

                    case "AddCategory":
                        var category = action.TryGetProperty("category", out var catProp)
                            ? catProp.GetString() ?? ""
                            : "";
                        if (!string.IsNullOrEmpty(category))
                        {
                            await graphMailService.UpdateMessageCategoriesAsync(emailId, new List<string> { category });
                            Log4.Debug($"[QuickStepService] AddCategory 완료: {category}");
                        }
                        break;

                    default:
                        _logger.Warning("[QuickStepService] 알 수 없는 액션 타입: {Type}", actionType);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "[QuickStepService] 액션 실행 실패 (계속 진행): {Action}", action);
            }
        }

        Log4.Info($"[QuickStepService] QuickStep 실행 완료: {quickStep.Name}");
    }
}
