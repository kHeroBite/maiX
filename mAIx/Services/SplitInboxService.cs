using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using mAIx.Data;
using mAIx.Models;

namespace mAIx.Services;

/// <summary>
/// Split Inbox 서비스 — 받은편지함 커스텀 탭 분류
/// </summary>
public class SplitInboxService
{
    private readonly IDbContextFactory<mAIxDbContext> _dbFactory;

    public SplitInboxService(IDbContextFactory<mAIxDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// 전체 Split Inbox 규칙 목록 조회 (SortOrder 순)
    /// </summary>
    public async Task<List<SplitInboxRule>> GetTabsAsync()
    {
        await using var db = _dbFactory.CreateDbContext();
        return await db.SplitInboxRules
            .OrderBy(r => r.SortOrder)
            .ToListAsync();
    }

    /// <summary>
    /// 규칙 저장 (신규/수정)
    /// </summary>
    public async Task SaveTabAsync(SplitInboxRule rule)
    {
        await using var db = _dbFactory.CreateDbContext();
        if (rule.Id == 0)
        {
            db.SplitInboxRules.Add(rule);
        }
        else
        {
            db.SplitInboxRules.Update(rule);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 규칙 삭제
    /// </summary>
    public async Task DeleteTabAsync(int id)
    {
        await using var db = _dbFactory.CreateDbContext();
        var rule = await db.SplitInboxRules.FindAsync(id);
        if (rule != null)
        {
            db.SplitInboxRules.Remove(rule);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 이메일을 규칙에 따라 분류. 첫 번째 매치 규칙의 탭 이름 반환, 없으면 null
    /// </summary>
    public string? ClassifyEmail(Email email, IEnumerable<SplitInboxRule> rules)
    {
        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.SortOrder))
        {
            if (MatchesRule(email, rule))
                return rule.TabName;
        }
        return null;
    }

    /// <summary>
    /// 특정 탭에 속하는 이메일 목록 반환
    /// </summary>
    public IEnumerable<Email> GetEmailsForTab(IEnumerable<Email> emails, string tabName, IEnumerable<SplitInboxRule> rules)
    {
        var rulesForTab = rules
            .Where(r => r.IsEnabled && r.TabName == tabName)
            .OrderBy(r => r.SortOrder)
            .ToList();

        return emails.Where(e => rulesForTab.Any(r => MatchesRule(e, r)));
    }

    // ===== 내부 =====

    private static bool MatchesRule(Email email, SplitInboxRule rule)
    {
        try
        {
            var matchers = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(rule.MatchersJson)
                           ?? new List<Dictionary<string, string>>();

            return matchers.Any(m => MatchesMatcher(email, m));
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesMatcher(Email email, Dictionary<string, string> matcher)
    {
        if (matcher.TryGetValue("SenderDomain", out var domain))
        {
            var senderDomain = ExtractDomain(email.From);
            if (!string.IsNullOrEmpty(domain) &&
                senderDomain.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (matcher.TryGetValue("SenderEmail", out var senderEmail))
        {
            if (!string.IsNullOrEmpty(senderEmail) &&
                email.From?.Contains(senderEmail, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        if (matcher.TryGetValue("SubjectContains", out var keyword))
        {
            if (!string.IsNullOrEmpty(keyword) &&
                email.Subject?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        if (matcher.TryGetValue("HasLabel", out var label))
        {
            if (!string.IsNullOrEmpty(label) &&
                email.Categories?.Contains(label, StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }

        return false;
    }

    private static string ExtractDomain(string? from)
    {
        if (string.IsNullOrEmpty(from)) return string.Empty;
        var start = from.IndexOf('<');
        var end = from.IndexOf('>');
        var emailPart = start >= 0 && end > start
            ? from.Substring(start + 1, end - start - 1).Trim()
            : from.Trim();

        var atIdx = emailPart.IndexOf('@');
        return atIdx >= 0 ? emailPart.Substring(atIdx + 1) : string.Empty;
    }
}
