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
/// 발신자 차단/허용 스크리너 서비스
/// </summary>
public class ScreenerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public ScreenerService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = Log.ForContext<ScreenerService>();
    }

    /// <summary>
    /// 모든 스크리너 항목 조회
    /// </summary>
    public async Task<List<ScreenerEntry>> GetAllAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        return await dbContext.ScreenerEntries
            .OrderBy(s => s.Action)
            .ThenBy(s => s.SenderEmail)
            .ToListAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 발신자 차단 추가
    /// </summary>
    public async Task BlockSenderAsync(string email, string name = "")
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var existing = await dbContext.ScreenerEntries
            .FirstOrDefaultAsync(s => s.SenderEmail == email).ConfigureAwait(false);

        if (existing != null)
        {
            existing.Action = "blocked";
            existing.SenderName = name;
        }
        else
        {
            dbContext.ScreenerEntries.Add(new ScreenerEntry
            {
                SenderEmail = email,
                SenderName = name,
                Action = "blocked",
                CreatedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Log4.Info($"[ScreenerService] 발신자 차단 추가: {email}");
    }

    /// <summary>
    /// 발신자 허용 추가
    /// </summary>
    public async Task AllowSenderAsync(string email, string name = "")
    {
        if (string.IsNullOrWhiteSpace(email)) return;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var existing = await dbContext.ScreenerEntries
            .FirstOrDefaultAsync(s => s.SenderEmail == email).ConfigureAwait(false);

        if (existing != null)
        {
            existing.Action = "allowed";
            existing.SenderName = name;
        }
        else
        {
            dbContext.ScreenerEntries.Add(new ScreenerEntry
            {
                SenderEmail = email,
                SenderName = name,
                Action = "allowed",
                CreatedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Log4.Info($"[ScreenerService] 발신자 허용 추가: {email}");
    }

    /// <summary>
    /// 스크리너 항목 삭제
    /// </summary>
    public async Task RemoveAsync(int id)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();

        var entry = await dbContext.ScreenerEntries.FindAsync(id).ConfigureAwait(false);

        if (entry == null)
        {
            _logger.Warning("[ScreenerService] 삭제 대상 항목 없음: Id={Id}", id);
            return;
        }

        dbContext.ScreenerEntries.Remove(entry);
        await dbContext.SaveChangesAsync().ConfigureAwait(false);

        Log4.Info($"[ScreenerService] 스크리너 항목 삭제: Id={id}");
    }

    /// <summary>
    /// 발신자가 차단 목록에 있는지 확인 (동기)
    /// </summary>
    public bool IsBlocked(string senderEmail)
    {
        if (string.IsNullOrWhiteSpace(senderEmail)) return false;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        return dbContext.ScreenerEntries
            .Any(s => s.SenderEmail == senderEmail && s.Action == "blocked");
    }

    /// <summary>
    /// 발신자가 허용 목록에 있는지 확인 (동기)
    /// </summary>
    public bool IsAllowed(string senderEmail)
    {
        if (string.IsNullOrWhiteSpace(senderEmail)) return false;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        return dbContext.ScreenerEntries
            .Any(s => s.SenderEmail == senderEmail && s.Action == "allowed");
    }

    /// <summary>
    /// 스크리닝 필요 여부 판단 — 새 발신자이고 허용/차단 목록 없으면 true
    /// </summary>
    public bool ShouldScreen(Email email)
    {
        if (email == null) return false;

        var senderEmail = email.From;
        if (string.IsNullOrWhiteSpace(senderEmail)) return false;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<mAIxDbContext>();
        var hasEntry = dbContext.ScreenerEntries
            .Any(s => s.SenderEmail == senderEmail);

        return !hasEntry;
    }
}
