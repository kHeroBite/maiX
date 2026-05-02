using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.Graph;
using mAIx.Utils;

namespace mAIx.Services.Rules;

/// <summary>
/// 메일 규칙 서비스 - 조건 기반 자동 메일 처리 규칙 엔진
/// Priority 순 규칙 조회 → 조건 평가 → 액션 실행
/// </summary>
public class MailRuleService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public MailRuleService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = Log.ForContext<MailRuleService>();
    }

    /// <summary>
    /// 메일 목록에 활성화된 규칙을 우선순위 순으로 적용
    /// </summary>
    public async Task ApplyRulesAsync(List<Email> emails, string accountEmail)
    {
        if (emails.Count == 0) return;

        var rules = await GetRulesAsync(accountEmail).ConfigureAwait(false);

        if (rules.Count == 0) return;

        Log4.Debug($"[MailRuleService] 규칙 {rules.Count}개 로드, 메일 {emails.Count}건에 적용 시작");

        using var scope = _serviceProvider.CreateScope();
        var graphMailService = scope.ServiceProvider.GetService<GraphMailService>();

        foreach (var email in emails)
        {
            foreach (var rule in rules)
            {
                try
                {
                    if (EvaluateCondition(email, rule))
                    {
                        await ExecuteActionAsync(email, rule, graphMailService).ConfigureAwait(false);

                        Log4.Debug($"[MailRuleService] 규칙 '{rule.Name}' 적용 → 메일: {email.Subject}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "규칙 '{RuleName}' 적용 중 오류 (메일: {Subject})", rule.Name, email.Subject);
                }
            }
        }

        // 변경사항 DB 저장
        try
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
            await dbContext.SaveChangesAsync().ConfigureAwait(false);

        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "규칙 적용 후 DB 저장 실패");
        }
    }

    /// <summary>
    /// 메일이 규칙 조건을 만족하는지 평가
    /// </summary>
    public bool EvaluateCondition(Email email, MailRule rule)
    {
        return rule.ConditionType switch
        {
            "FromContains" => !string.IsNullOrEmpty(rule.ConditionValue) &&
                              email.From.Contains(rule.ConditionValue, StringComparison.OrdinalIgnoreCase),

            "SubjectContains" => !string.IsNullOrEmpty(rule.ConditionValue) &&
                                 email.Subject.Contains(rule.ConditionValue, StringComparison.OrdinalIgnoreCase),

            "HasAttachment" => email.HasAttachments,

            "AiCategoryEquals" => !string.IsNullOrEmpty(rule.ConditionValue) &&
                                  string.Equals(email.AiCategory, rule.ConditionValue, StringComparison.OrdinalIgnoreCase),

            "ToContains" => !string.IsNullOrEmpty(rule.ConditionValue) &&
                            !string.IsNullOrEmpty(email.To) &&
                            email.To.Contains(rule.ConditionValue, StringComparison.OrdinalIgnoreCase),

            _ => false
        };
    }

    /// <summary>
    /// 규칙 액션 실행
    /// </summary>
    public async Task ExecuteActionAsync(Email email, MailRule rule, GraphMailService? graphMailService)
    {
        switch (rule.ActionType)
        {
            case "MoveToFolder":
                if (graphMailService != null && !string.IsNullOrEmpty(email.EntryId) && !string.IsNullOrEmpty(rule.ActionValue))
                {
                    // ActionValue에 폴더 ID 또는 폴더명이 담겨 있음
                    // 폴더 ID로 이동 시도
                    await graphMailService.MoveMessageAsync(email.EntryId, rule.ActionValue).ConfigureAwait(false);

                    Log4.Debug($"[MailRuleService] 메일 이동: {email.Subject} → {rule.ActionValue}");
                }
                break;

            case "SetCategory":
                if (!string.IsNullOrEmpty(rule.ActionValue))
                {
                    email.AiCategory = rule.ActionValue;
                }
                break;

            case "SetFlag":
                email.FlagStatus = "flagged";
                break;

            case "MarkAsRead":
                email.IsRead = true;
                break;

            case "Delete":
                if (graphMailService != null && !string.IsNullOrEmpty(email.EntryId))
                {
                    await graphMailService.DeleteMessageAsync(email.EntryId).ConfigureAwait(false);

                    Log4.Debug($"[MailRuleService] 메일 삭제: {email.Subject}");
                }
                break;

            default:
                _logger.Warning("알 수 없는 액션 타입: {ActionType}", rule.ActionType);
                break;
        }
    }

    /// <summary>
    /// 계정별 활성화된 규칙 목록 조회 (Priority 오름차순)
    /// </summary>
    public async Task<List<MailRule>> GetRulesAsync(string accountEmail)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        return await dbContext.MailRules
            .Where(r => r.IsEnabled && (r.AccountEmail == null || r.AccountEmail == accountEmail))
            .OrderBy(r => r.Priority)
            .ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 규칙 저장 (신규 생성 또는 수정)
    /// </summary>
    public async Task SaveRuleAsync(MailRule rule)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        if (rule.Id == 0)
        {
            rule.CreatedAt = DateTime.UtcNow;
            rule.UpdatedAt = DateTime.UtcNow;
            dbContext.MailRules.Add(rule);
        }
        else
        {
            rule.UpdatedAt = DateTime.UtcNow;
            dbContext.MailRules.Update(rule);
        }

        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Log4.Debug($"[MailRuleService] 규칙 저장 완료: {rule.Name} (Id={rule.Id})");
    }

    /// <summary>
    /// 규칙 삭제
    /// </summary>
    public async Task DeleteRuleAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var rule = await dbContext.MailRules.FindAsync(id).ConfigureAwait(false);

        if (rule != null)
        {
            dbContext.MailRules.Remove(rule);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            Log4.Debug($"[MailRuleService] 규칙 삭제: Id={id}");
        }
    }
}
