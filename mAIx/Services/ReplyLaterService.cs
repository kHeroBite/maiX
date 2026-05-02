using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using mAIx.Data;
using mAIx.Models;
using mAIx.Utils;

namespace mAIx.Services;

/// <summary>
/// Reply Later 큐 관리 서비스 — 나중에 답장할 이메일 관리
/// </summary>
public class ReplyLaterService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public ReplyLaterService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = Log.ForContext<ReplyLaterService>();
    }

    /// <summary>
    /// 미완료 Reply Later 항목 조회
    /// </summary>
    public async Task<List<ReplyLaterItem>> GetPendingAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        return await dbContext.ReplyLaterItems
            .Where(r => !r.IsCompleted)
            .OrderBy(r => r.RemindAt)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Reply Later 항목 추가
    /// </summary>
    public async Task AddAsync(string emailId, string subject, string senderEmail, DateTime? remindAt = null)
    {
        if (string.IsNullOrWhiteSpace(emailId)) return;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        dbContext.ReplyLaterItems.Add(new ReplyLaterItem
        {
            EmailId = emailId,
            Subject = subject ?? "",
            SenderEmail = senderEmail ?? "",
            RemindAt = remindAt,
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Log4.Info($"[ReplyLaterService] Reply Later 추가: {subject} (EmailId={emailId})");
    }

    /// <summary>
    /// Reply Later 항목 완료 처리
    /// </summary>
    public async Task CompleteAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var item = await dbContext.ReplyLaterItems.FindAsync(id).ConfigureAwait(false);

        if (item == null)
        {
            _logger.Warning("[ReplyLaterService] 완료 처리 대상 없음: Id={Id}", id);
            return;
        }

        item.IsCompleted = true;
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Log4.Info($"[ReplyLaterService] Reply Later 완료: Id={id}");
    }

    /// <summary>
    /// Reply Later 항목 삭제
    /// </summary>
    public async Task RemoveAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var item = await dbContext.ReplyLaterItems.FindAsync(id).ConfigureAwait(false);

        if (item == null)
        {
            _logger.Warning("[ReplyLaterService] 삭제 대상 없음: Id={Id}", id);
            return;
        }

        dbContext.ReplyLaterItems.Remove(item);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Log4.Info($"[ReplyLaterService] Reply Later 삭제: Id={id}");
    }

    /// <summary>
    /// 알림 시각이 된 Reply Later 항목 조회 (RemindAt &lt;= 현재 시각)
    /// </summary>
    public async Task<List<ReplyLaterItem>> GetDueItemsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        var now = DateTime.UtcNow;
        return await dbContext.ReplyLaterItems
            .Where(r => !r.IsCompleted && r.RemindAt.HasValue && r.RemindAt.Value <= now)
            .OrderBy(r => r.RemindAt)
            .ToListAsync().ConfigureAwait(false);
    }
}
