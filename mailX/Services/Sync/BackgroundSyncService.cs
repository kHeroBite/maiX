using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph.Models;
using Serilog;
using mailX.Data;
using mailX.Models;
using mailX.Services.Analysis;
using mailX.Services.Graph;
using mailX.Services.Notification;

namespace mailX.Services.Sync;

/// <summary>
/// 백그라운드 동기화 서비스 - IHostedService 구현
/// 5분 주기 자동 동기화, Delta Query 지원, 분석 파이프라인 연동
/// </summary>
public class BackgroundSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    // 동기화 설정
    private const int SyncIntervalSeconds = 30;  // 30초 동기화 주기
    private const int MaxMessagesPerSync = 100;
    private const int MaxRetryCount = 3;

    // 상태
    private DateTime _lastSyncTime = DateTime.MinValue;
    private bool _isSyncing;
    private bool _isPaused;
    private int _syncCount;
    private int _errorCount;

    /// <summary>
    /// 동기화 일시정지/재개 이벤트
    /// </summary>
    public event Action<bool>? PausedChanged;

    /// <summary>
    /// 폴더 동기화 완료 이벤트
    /// </summary>
    public event Action? FoldersSynced;

    /// <summary>
    /// 메일 동기화 완료 이벤트 (새 메일 개수 전달)
    /// </summary>
    public event Action<int>? EmailsSynced;

    /// <summary>
    /// 메일 동기화 시작 이벤트 (전체 건수 전달)
    /// </summary>
    public event Action<int>? MailSyncStarted;

    /// <summary>
    /// 메일 동기화 진행 이벤트 (완료 건수 전달)
    /// </summary>
    public event Action<int>? MailSyncProgress;

    /// <summary>
    /// 메일 동기화 완료 이벤트
    /// </summary>
    public event Action? MailSyncCompleted;

    /// <summary>
    /// 캘린더 동기화 시작 이벤트
    /// </summary>
    public event Action? CalendarSyncStarted;

    /// <summary>
    /// 캘린더 동기화 진행 이벤트 (현재, 전체, 단계명)
    /// </summary>
    public event Action<int, int, string>? CalendarSyncProgress;

    /// <summary>
    /// 캘린더 동기화 완료 이벤트 (일정 개수 전달)
    /// </summary>
    public event Action<int>? CalendarSynced;

    public BackgroundSyncService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = Log.ForContext<BackgroundSyncService>();
    }

    /// <summary>
    /// 마지막 동기화 시간
    /// </summary>
    public DateTime LastSyncTime => _lastSyncTime;

    /// <summary>
    /// 현재 동기화 중 여부
    /// </summary>
    public bool IsSyncing => _isSyncing;

    /// <summary>
    /// 동기화 일시정지 여부
    /// </summary>
    public bool IsPaused => _isPaused;

    /// <summary>
    /// 총 동기화 횟수
    /// </summary>
    public int SyncCount => _syncCount;

    /// <summary>
    /// 오류 횟수
    /// </summary>
    public int ErrorCount => _errorCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Information("백그라운드 동기화 서비스 시작 - 주기: {Interval}초 (Delta Query)", SyncIntervalSeconds);

        // 시작 시 폴더 먼저 동기화
        await SyncFoldersAsync(stoppingToken);

        // 시작 시 즉시 1회 동기화
        await SyncAllAccountsAsync(stoppingToken);

        // 시작 시 캘린더 동기화
        await SyncCalendarAsync(stoppingToken);

        // 주기적 동기화 (30초)
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SyncIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // 일시정지 상태면 건너뜀
                if (_isPaused)
                {
                    _logger.Debug("동기화 일시정지 상태 - 건너뜀");
                    continue;
                }

                // 폴더 동기화 (5분마다 한 번)
                if (_syncCount % 10 == 0)
                {
                    await SyncFoldersAsync(stoppingToken);
                }

                // 메일 동기화 (Delta Query로 변경분만)
                await SyncAllAccountsAsync(stoppingToken);

                // 캘린더 동기화 (5분마다 한 번)
                if (_syncCount % 10 == 0)
                {
                    await SyncCalendarAsync(stoppingToken);

                    // 캘린더 알림 체크
                    await CheckCalendarRemindersAsync(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Information("백그라운드 동기화 서비스 중지 요청됨");
        }
    }

    /// <summary>
    /// 모든 계정 동기화
    /// </summary>
    public async Task SyncAllAccountsAsync(CancellationToken ct = default)
    {
        if (_isSyncing)
        {
            _logger.Warning("이미 동기화 진행 중 - 건너뜀");
            return;
        }

        _isSyncing = true;
        _logger.Information("전체 계정 동기화 시작");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();

            // MSAL로 로그인된 계정 확인
            if (!graphAuthService.IsLoggedIn || string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
            {
                _logger.Information("로그인된 계정 없음 - 동기화 생략");
                return;
            }

            try
            {
                await SyncAccountAsync(graphAuthService.CurrentUserEmail, ct);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errorCount);
                _logger.Error(ex, "계정 동기화 실패: {Email}", graphAuthService.CurrentUserEmail);
            }

            _lastSyncTime = DateTime.UtcNow;
            Interlocked.Increment(ref _syncCount);
            _logger.Information("계정 동기화 완료: {Email}", graphAuthService.CurrentUserEmail);

            // 동기화 완료 이벤트 발생 (UI 갱신용)
            MailSyncCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger.Error(ex, "전체 계정 동기화 실패");
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>
    /// 단일 계정 동기화
    /// </summary>
    public async Task SyncAccountAsync(string accountEmail, CancellationToken ct = default)
    {
        _logger.Debug("계정 동기화 시작: {Email}", accountEmail);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MailXDbContext>();
        var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();
        var graphMailService = scope.ServiceProvider.GetRequiredService<GraphMailService>();
        var emailAnalyzer = scope.ServiceProvider.GetRequiredService<EmailAnalyzer>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        // DB에서 해당 계정의 모든 폴더 조회
        var folders = await dbContext.Folders
            .Where(f => f.AccountEmail == accountEmail)
            .ToListAsync(ct);

        if (folders.Count == 0)
        {
            _logger.Warning("동기화할 폴더가 없음 - 동기화 생략");
            return;
        }

        _logger.Information("전체 폴더 동기화: {Count}개 폴더", folders.Count);

        int totalChanged = 0;
        int totalDeleted = 0;
        var allSavedEmails = new List<Email>();

        // 각 폴더별 동기화
        foreach (var folder in folders)
        {
            try
            {
                var (changed, deleted, savedEmails) = await SyncFolderAsync(
                    dbContext, graphMailService, accountEmail, folder, ct);

                totalChanged += changed;
                totalDeleted += deleted;
                allSavedEmails.AddRange(savedEmails);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "폴더 동기화 실패: {FolderName}", folder.DisplayName);
                // 개별 폴더 실패는 계속 진행
            }
        }

        // 마지막 동기화 시간 업데이트
        _lastSyncTime = DateTime.UtcNow;

        // 새 메일이 있으면 이벤트 발생
        if (allSavedEmails.Count > 0)
        {
            EmailsSynced?.Invoke(allSavedEmails.Count);

            // 분석 파이프라인 실행 (받은편지함 메일만)
            var inboxEmails = allSavedEmails.Where(e =>
                e.ParentFolderId != null &&
                folders.Any(f => f.Id == e.ParentFolderId &&
                    (f.DisplayName == "받은 편지함" || f.DisplayName.ToLower() == "inbox")))
                .ToList();

            if (inboxEmails.Count > 0)
            {
                await AnalyzeAndNotifyAsync(emailAnalyzer, notificationService, inboxEmails, ct);
            }
        }

        _logger.Information("전체 폴더 동기화 완료: 변경 {Changed}건, 삭제 {Deleted}건",
            totalChanged, totalDeleted);

        // 동기화 완료 이벤트 (UI 갱신용)
        MailSyncCompleted?.Invoke();
    }


    /// <summary>
    /// 단일 폴더 동기화
    /// </summary>
    private async Task<(int Changed, int Deleted, List<Email> SavedEmails)> SyncFolderAsync(
        MailXDbContext dbContext,
        GraphMailService graphMailService,
        string accountEmail,
        Models.Folder folder,
        CancellationToken ct)
    {
        _logger.Debug("폴더 동기화: {FolderName} ({FolderId})", folder.DisplayName, folder.Id);

        // 폴더별 동기화 상태 조회/생성
        var syncState = await dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.AccountEmail == accountEmail && s.FolderId == folder.Id, ct);

        if (syncState == null)
        {
            syncState = new SyncState
            {
                AccountEmail = accountEmail,
                FolderId = folder.Id
            };
            dbContext.SyncStates.Add(syncState);
            await dbContext.SaveChangesAsync(ct);
        }

        // Delta Query로 변경된 메일 조회
        var (changedMessages, deletedIds) = await FetchNewEmailsAsync(
            graphMailService, syncState, folder.Id, ct);

        // 삭제된 메일 처리
        if (deletedIds.Count > 0)
        {
            await ProcessDeletedEmailsAsync(dbContext, deletedIds, ct);
        }

        var savedEmails = new List<Email>();

        if (changedMessages.Count > 0)
        {
            _logger.Debug("폴더 '{FolderName}' 변경 감지: {Count}건",
                folder.DisplayName, changedMessages.Count);

            savedEmails = await SaveEmailsAsync(
                dbContext, changedMessages, accountEmail, folder.Id, ct);
        }

        // 동기화 상태 업데이트
        syncState.LastSyncedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        return (changedMessages.Count, deletedIds.Count, savedEmails);
    }

    /// <summary>
    /// 이메일 가져오기 (새 메일 + 기존 메일 상태 업데이트)
    /// </summary>
    private async Task<(List<Message> Messages, List<string> DeletedIds)> FetchNewEmailsAsync(
        GraphMailService mailService,
        SyncState syncState,
        string folderId,
        CancellationToken ct)
    {
        try
        {
            // Delta Query로 변경분만 조회
            var (messages, newDeltaLink, deletedIds) = await mailService.GetMessagesDeltaAsync(
                folderId,
                syncState.DeltaLink);

            // 새 deltaLink 저장
            if (!string.IsNullOrEmpty(newDeltaLink))
            {
                syncState.DeltaLink = newDeltaLink;
            }

            _logger.Debug("Delta Query 완료: 변경 {Count}건, 삭제 {Deleted}건 (폴더: {FolderId})",
                messages.Count(), deletedIds.Count(), folderId);

            return (messages.ToList(), deletedIds.ToList());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Delta Query 메일 가져오기 실패");
            throw;
        }
    }

    /// <summary>
    /// Delta Query에서 삭제된 메일 처리
    /// </summary>
    private async Task ProcessDeletedEmailsAsync(
        MailXDbContext dbContext,
        List<string> deletedIds,
        CancellationToken ct)
    {
        foreach (var entryId in deletedIds)
        {
            var email = await dbContext.Emails
                .FirstOrDefaultAsync(e => e.EntryId == entryId, ct);

            if (email != null)
            {
                dbContext.Emails.Remove(email);
                _logger.Debug("삭제된 메일 제거: {Subject}", email.Subject);
            }
        }

        if (deletedIds.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
            _logger.Information("삭제된 메일 처리 완료: {Count}건", deletedIds.Count);
        }
    }

    /// <summary>
    /// 이메일 DB 저장
    /// </summary>
    private async Task<List<Email>> SaveEmailsAsync(
        MailXDbContext dbContext,
        List<Message> messages,
        string accountEmail,
        string folderId,
        CancellationToken ct)
    {
        var savedEmails = new List<Email>();

        foreach (var message in messages)
        {
            try
            {
                // 기존 메일 확인 (InternetMessageId 또는 EntryId로 검색)
                Email? existingEmail = null;
                
                if (!string.IsNullOrEmpty(message.InternetMessageId))
                {
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.InternetMessageId == message.InternetMessageId, ct);
                }
                
                // InternetMessageId로 못 찾으면 EntryId로 검색
                if (existingEmail == null && !string.IsNullOrEmpty(message.Id))
                {
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.EntryId == message.Id, ct);
                }

                if (existingEmail != null)
                {
                    // 기존 메일의 상태 업데이트 (IsRead, FlagStatus, Importance 등)
                    bool updated = false;

                    if (existingEmail.IsRead != (message.IsRead ?? false))
                    {
                        _logger.Debug("메일 읽음 상태 변경: {Subject} ({OldValue} -> {NewValue})",
                            existingEmail.Subject, existingEmail.IsRead, message.IsRead ?? false);
                        existingEmail.IsRead = message.IsRead ?? false;
                        updated = true;
                    }

                    var newFlagStatus = message.Flag?.FlagStatus?.ToString()?.ToLower();
                    if (existingEmail.FlagStatus != newFlagStatus)
                    {
                        existingEmail.FlagStatus = newFlagStatus;
                        updated = true;
                    }

                    var newImportance = message.Importance?.ToString()?.ToLower();
                    if (existingEmail.Importance != newImportance)
                    {
                        existingEmail.Importance = newImportance;
                        updated = true;
                    }

                    if (updated)
                    {
                        await dbContext.SaveChangesAsync(ct);
                        _logger.Debug("기존 메일 상태 업데이트: {Subject} (IsRead={IsRead})",
                            existingEmail.Subject, existingEmail.IsRead);
                    }
                    continue;
                }

                var email = new Email
                {
                    InternetMessageId = message.InternetMessageId,
                    EntryId = message.Id,
                    ConversationId = message.ConversationId,
                    Subject = message.Subject ?? "(제목 없음)",
                    Body = message.Body?.Content,
                    IsHtml = message.Body?.ContentType == Microsoft.Graph.Models.BodyType.Html,
                    From = message.From?.EmailAddress?.Address ?? "unknown",
                    To = SerializeRecipients(message.ToRecipients),
                    Cc = SerializeRecipients(message.CcRecipients),
                    ReceivedDateTime = message.ReceivedDateTime?.UtcDateTime,
                    IsRead = message.IsRead ?? false,
                    FlagStatus = message.Flag?.FlagStatus?.ToString()?.ToLower(),
                    Importance = message.Importance?.ToString()?.ToLower(),
                    HasAttachments = message.HasAttachments ?? false,
                    ParentFolderId = folderId,  // 실제 폴더 ID 사용
                    AccountEmail = accountEmail,
                    AnalysisStatus = "pending"
                };

                dbContext.Emails.Add(email);
                await dbContext.SaveChangesAsync(ct);

                savedEmails.Add(email);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "메일 저장 실패: {Subject}", message.Subject);
            }
        }

        _logger.Information("메일 저장 완료: {Count}건", savedEmails.Count);
        return savedEmails;
    }

    /// <summary>
    /// 메일 저장 (진행률 이벤트 포함)
    /// </summary>
    private async Task SaveEmailsWithProgressAsync(
        MailXDbContext dbContext,
        List<Message> messages,
        string accountEmail,
        string folderId,
        CancellationToken ct)
    {
        int completed = 0;
        int total = messages.Count;

        foreach (var message in messages)
        {
            try
            {
                // 기존 메일 확인 (InternetMessageId 또는 EntryId로 검색)
                Email? existingEmail = null;

                if (!string.IsNullOrEmpty(message.InternetMessageId))
                {
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.InternetMessageId == message.InternetMessageId, ct);
                }

                // InternetMessageId로 못 찾으면 EntryId로 검색
                if (existingEmail == null && !string.IsNullOrEmpty(message.Id))
                {
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.EntryId == message.Id, ct);
                }

                if (existingEmail != null)
                {
                    // 기존 메일의 상태 업데이트 (IsRead, FlagStatus, Importance 등)
                    bool updated = false;

                    if (existingEmail.IsRead != (message.IsRead ?? false))
                    {
                        existingEmail.IsRead = message.IsRead ?? false;
                        updated = true;
                    }

                    var newFlagStatus = message.Flag?.FlagStatus?.ToString()?.ToLower();
                    if (existingEmail.FlagStatus != newFlagStatus)
                    {
                        existingEmail.FlagStatus = newFlagStatus;
                        updated = true;
                    }

                    var newImportance = message.Importance?.ToString()?.ToLower();
                    if (existingEmail.Importance != newImportance)
                    {
                        existingEmail.Importance = newImportance;
                        updated = true;
                    }

                    if (updated)
                    {
                        await dbContext.SaveChangesAsync(ct);
                    }
                }
                else
                {
                    var email = new Email
                    {
                        InternetMessageId = message.InternetMessageId,
                        EntryId = message.Id,
                        ConversationId = message.ConversationId,
                        Subject = message.Subject ?? "(제목 없음)",
                        Body = message.Body?.Content,
                        IsHtml = message.Body?.ContentType == Microsoft.Graph.Models.BodyType.Html,
                        From = message.From?.EmailAddress?.Address ?? "unknown",
                        To = SerializeRecipients(message.ToRecipients),
                        Cc = SerializeRecipients(message.CcRecipients),
                        ReceivedDateTime = message.ReceivedDateTime?.UtcDateTime,
                        IsRead = message.IsRead ?? false,
                        FlagStatus = message.Flag?.FlagStatus?.ToString()?.ToLower(),
                        Importance = message.Importance?.ToString()?.ToLower(),
                        HasAttachments = message.HasAttachments ?? false,
                        ParentFolderId = folderId,
                        AccountEmail = accountEmail,
                        AnalysisStatus = "pending"
                    };

                    dbContext.Emails.Add(email);
                    await dbContext.SaveChangesAsync(ct);
                }

                // 진행률 업데이트
                completed++;
                MailSyncProgress?.Invoke(completed);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "메일 저장 실패: {Subject}", message.Subject);
                completed++;
                MailSyncProgress?.Invoke(completed);
            }
        }

        _logger.Information("메일 동기화 완료: {Count}건 처리", total);
    }

    /// <summary>
    /// 수신자 목록 JSON 직렬화
    /// </summary>
    private string? SerializeRecipients(IEnumerable<Recipient>? recipients)
    {
        if (recipients == null || !recipients.Any())
            return null;

        var addresses = recipients
            .Where(r => r.EmailAddress != null)
            .Select(r => r.EmailAddress!.Address)
            .ToList();

        return JsonSerializer.Serialize(addresses);
    }

    /// <summary>
    /// 분석 및 알림 처리
    /// </summary>
    private async Task AnalyzeAndNotifyAsync(
        EmailAnalyzer analyzer,
        NotificationService notificationService,
        List<Email> emails,
        CancellationToken ct)
    {
        foreach (var email in emails)
        {
            try
            {
                // TODO: 메일 알림 임시 비활성화 - ntfy rate limit 문제로 인해 주석 처리
                // await notificationService.NotifyNewEmailAsync(email);

                // AI 분석
                var result = await analyzer.AnalyzeEmailAsync(email, ct);

                if (result.IsSuccess)
                {
                    // 분석 결과 적용
                    email.SummaryOneline = result.SummaryOneline;
                    email.Summary = result.SummaryDetail;
                    email.PriorityScore = result.PriorityScore;
                    email.PriorityLevel = result.PriorityLevel;
                    email.UrgencyLevel = result.UrgencyLevel;
                    email.Deadline = result.Deadline;
                    email.AnalysisStatus = "completed";

                    // TODO: 중요 메일 알림 임시 비활성화
                    // if (email.PriorityScore >= 70)
                    // {
                    //     await notificationService.NotifyImportantEmailAsync(email);
                    // }

                    // TODO: 마감일 알림 임시 비활성화
                    // if (email.Deadline.HasValue)
                    // {
                    //     await notificationService.NotifyDeadlineReminderAsync(email);
                    // }
                }
                else
                {
                    email.AnalysisStatus = "failed";
                    _logger.Warning("분석 실패: {Id} - {Error}", email.Id, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                email.AnalysisStatus = "failed";
                _logger.Error(ex, "분석/알림 처리 실패: {Id}", email.Id);
            }
        }

        // 변경사항 저장
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MailXDbContext>();

        foreach (var email in emails)
        {
            dbContext.Emails.Update(email);
        }

        await dbContext.SaveChangesAsync(ct);

        // 분석 완료 후 UI 업데이트를 위해 이벤트 발생 (0건으로 새 메일 없음을 알림)
        EmailsSynced?.Invoke(0);
    }

    /// <summary>
    /// 동기화 일시정지
    /// </summary>

    /// <summary>
    /// 마지막 동기화 시간 조회
    /// </summary>
    public DateTime GetLastSyncTime() => _lastSyncTime;

    public void Pause()
    {
        if (!_isPaused)
        {
            _isPaused = true;
            _logger.Information("동기화 일시정지됨");
            PausedChanged?.Invoke(true);
        }
    }

    /// <summary>
    /// 동기화 재개
    /// </summary>
    public void Resume()
    {
        if (_isPaused)
        {
            _isPaused = false;
            _logger.Information("동기화 재개됨");
            PausedChanged?.Invoke(false);
        }
    }

    /// <summary>
    /// 동기화 일시정지/재개 토글
    /// </summary>
    public void TogglePause()
    {
        if (_isPaused)
            Resume();
        else
            Pause();
    }

    /// <summary>
    /// 수동 즉시 동기화 트리거
    /// </summary>
    public async Task TriggerSyncAsync(CancellationToken ct = default)
    {
        _logger.Information("수동 동기화 요청됨");
        await SyncAllAccountsAsync(ct);
    }

    /// <summary>
    /// 특정 계정 즉시 동기화
    /// </summary>
    public async Task TriggerSyncForAccountAsync(string accountEmail, CancellationToken ct = default)
    {
        _logger.Information("수동 동기화 요청됨: {Email}", accountEmail);
        await SyncAccountAsync(accountEmail, ct);
    }

    /// <summary>
    /// 특정 폴더 강제 새로고침 (deltaLink 초기화 후 전체 조회)
    /// </summary>
    public async Task ForceRefreshFolderAsync(string accountEmail, string folderId, CancellationToken ct = default)
    {
        _logger.Information("폴더 강제 새로고침 요청: {FolderId}", folderId);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MailXDbContext>();
        var graphMailService = scope.ServiceProvider.GetRequiredService<GraphMailService>();

        // 해당 폴더의 deltaLink 초기화
        var syncState = await dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.AccountEmail == accountEmail && s.FolderId == folderId, ct);

        if (syncState != null)
        {
            syncState.DeltaLink = null;  // deltaLink 초기화
            await dbContext.SaveChangesAsync(ct);
            _logger.Debug("DeltaLink 초기화 완료: {FolderId}", folderId);
        }

        // 서버에서 전체 메일 목록 조회 (deltaLink 없이)
        var (serverMessages, newDeltaLink, _) = await graphMailService.GetMessagesDeltaAsync(folderId, null);
        var serverMessageList = serverMessages.ToList();
        var serverMessageIds = serverMessageList.Select(m => m.Id).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();

        _logger.Debug("서버 메일 수: {Count}", serverMessageIds.Count);

        // 동기화 시작 이벤트 발생
        MailSyncStarted?.Invoke(serverMessageList.Count);

        // DB에서 해당 폴더의 메일 목록 조회
        var dbEmails = await dbContext.Emails
            .Where(e => e.ParentFolderId == folderId)
            .ToListAsync(ct);

        _logger.Debug("DB 메일 수: {Count}", dbEmails.Count);

        // DB에는 있지만 서버에 없는 메일 삭제 (폴더 이동 또는 삭제된 메일)
        var emailsToDelete = dbEmails.Where(e => !serverMessageIds.Contains(e.EntryId)).ToList();

        if (emailsToDelete.Count > 0)
        {
            foreach (var email in emailsToDelete)
            {
                _logger.Debug("서버에 없는 메일 삭제: {Subject}", email.Subject);
                dbContext.Emails.Remove(email);
            }
            await dbContext.SaveChangesAsync(ct);
            _logger.Information("폴더에서 제거된 메일 삭제 완료: {Count}건", emailsToDelete.Count);
        }

        // 새 메일 저장 및 기존 메일 업데이트 (진행률 이벤트 포함)
        await SaveEmailsWithProgressAsync(dbContext, serverMessageList, accountEmail, folderId, ct);

        // deltaLink 업데이트
        if (syncState != null && !string.IsNullOrEmpty(newDeltaLink))
        {
            syncState.DeltaLink = newDeltaLink;
            syncState.LastSyncedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(ct);
        }

        // 마지막 동기화 시간 업데이트 (UI 표시용)
        _lastSyncTime = DateTime.UtcNow;

        // 동기화 완료 이벤트 발생
        MailSyncCompleted?.Invoke();

        _logger.Information("폴더 강제 새로고침 완료: {FolderId}", folderId);
    }

    /// <summary>
    /// 폴더 목록 동기화 (Graph API → DB)
    /// </summary>
    public async Task SyncFoldersAsync(CancellationToken ct = default)
    {
        _logger.Information("폴더 동기화 시작");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var graphMailService = scope.ServiceProvider.GetRequiredService<GraphMailService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<MailXDbContext>();
            var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();

            if (!graphAuthService.IsLoggedIn || string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
            {
                _logger.Information("로그인된 계정 없음 - 폴더 동기화 생략");
                return;
            }

            var folders = (await graphMailService.GetFoldersAsync()).ToList();
            _logger.Information("Graph API에서 {Count}개 폴더 조회됨", folders.Count);

            var accountEmail = graphAuthService.CurrentUserEmail;
            var syncedCount = 0;

            foreach (var folder in folders)
            {
                if (string.IsNullOrEmpty(folder.Id))
                    continue;

                var existing = await dbContext.Folders.FindAsync([folder.Id], ct);

                if (existing == null)
                {
                    var newFolder = new Models.Folder
                    {
                        Id = folder.Id,
                        DisplayName = folder.DisplayName ?? "(이름 없음)",
                        ParentFolderId = folder.ParentFolderId,
                        TotalItemCount = folder.TotalItemCount ?? 0,
                        UnreadItemCount = folder.UnreadItemCount ?? 0,
                        AccountEmail = accountEmail
                    };
                    dbContext.Folders.Add(newFolder);
                    syncedCount++;
                }
                else
                {
                    existing.DisplayName = folder.DisplayName ?? "(이름 없음)";
                    existing.TotalItemCount = folder.TotalItemCount ?? 0;
                    existing.UnreadItemCount = folder.UnreadItemCount ?? 0;
                }
            }

            await dbContext.SaveChangesAsync(ct);
            _logger.Information("폴더 동기화 완료: {Count}개 신규 폴더", syncedCount);

            // 마지막 동기화 시간 업데이트 (UI 표시용 - 폴더 동기화도 동기화 작업임)
            _lastSyncTime = DateTime.UtcNow;

            // 폴더 동기화 완료 이벤트 발생
            FoldersSynced?.Invoke();

            // 동기화 완료 이벤트 발생 (UI에서 동기화 시간 표시용)
            MailSyncCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "폴더 동기화 실패");
        }
    }

    /// <summary>
    /// 캘린더 일정 동기화
    /// Graph API에서 이번 달 + 다음 달 일정 가져오기
    /// </summary>
    public async Task SyncCalendarAsync(CancellationToken ct = default)
    {
        _logger.Information("캘린더 동기화 시작");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();

            // 로그인 상태 확인
            if (!graphAuthService.IsLoggedIn || string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
            {
                _logger.Information("로그인된 계정 없음 - 캘린더 동기화 생략");
                return;
            }

            var calendarService = scope.ServiceProvider.GetRequiredService<GraphCalendarService>();

            // 동기화 시작 이벤트
            CalendarSyncStarted?.Invoke();

            // 진행 상태 알림: 1/3 - 이번 달 일정 조회
            CalendarSyncProgress?.Invoke(1, 3, "이번 달 일정 조회 중...");

            var today = DateTime.Today;
            var firstDayThisMonth = new DateTime(today.Year, today.Month, 1);
            var lastDayThisMonth = firstDayThisMonth.AddMonths(1).AddDays(-1);

            var thisMonthEvents = await calendarService.GetEventsAsync(firstDayThisMonth, lastDayThisMonth.AddDays(1));
            var thisMonthCount = thisMonthEvents?.Count() ?? 0;
            _logger.Debug("이번 달 일정: {Count}건", thisMonthCount);

            // 진행 상태 알림: 2/3 - 다음 달 일정 조회
            CalendarSyncProgress?.Invoke(2, 3, "다음 달 일정 조회 중...");

            var firstDayNextMonth = firstDayThisMonth.AddMonths(1);
            var lastDayNextMonth = firstDayNextMonth.AddMonths(1).AddDays(-1);

            var nextMonthEvents = await calendarService.GetEventsAsync(firstDayNextMonth, lastDayNextMonth.AddDays(1));
            var nextMonthCount = nextMonthEvents?.Count() ?? 0;
            _logger.Debug("다음 달 일정: {Count}건", nextMonthCount);

            // 진행 상태 알림: 3/3 - 동기화 완료
            CalendarSyncProgress?.Invoke(3, 3, "캘린더 동기화 완료");

            var totalCount = thisMonthCount + nextMonthCount;
            _logger.Information("캘린더 동기화 완료: 총 {Count}건 (이번 달 {This}건, 다음 달 {Next}건)",
                totalCount, thisMonthCount, nextMonthCount);

            // 동기화 완료 이벤트
            CalendarSynced?.Invoke(totalCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "캘린더 동기화 실패");
            CalendarSyncProgress?.Invoke(0, 0, "캘린더 동기화 실패");
        }
    }

    /// <summary>
    /// 캘린더 알림 체크
    /// 임박한 일정에 대해 ntfy 푸시 알림 발송
    /// </summary>
    private async Task CheckCalendarRemindersAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();

            // 로그인 상태 확인
            if (!graphAuthService.IsLoggedIn || string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
            {
                return;
            }

            var calendarService = scope.ServiceProvider.GetRequiredService<GraphCalendarService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

            // 향후 2시간 이내 일정 조회
            var now = DateTime.Now;
            var until = now.AddHours(2);

            _logger.Debug("캘린더 알림 체크: {Start} ~ {End}", now, until);

            var events = await calendarService.GetEventsAsync(now, until);
            if (events == null || !events.Any())
            {
                return;
            }

            _logger.Debug("임박한 일정 {Count}건 발견", events.Count());

            // 알림 발송
            await notificationService.CheckAndNotifyUpcomingEventsAsync(events);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "캘린더 알림 체크 실패");
        }
    }

    /// <summary>
    /// 동기화 상태 조회
    /// </summary>
    public SyncStatus GetStatus()
    {
        return new SyncStatus
        {
            IsSyncing = _isSyncing,
            IsPaused = _isPaused,
            LastSyncTime = _lastSyncTime,
            SyncCount = _syncCount,
            ErrorCount = _errorCount,
            NextSyncTime = _lastSyncTime.AddSeconds(SyncIntervalSeconds)
        };
    }
}

/// <summary>
/// 동기화 상태 모델
/// </summary>
public class SyncStatus
{
    /// <summary>
    /// 현재 동기화 중 여부
    /// </summary>
    public bool IsSyncing { get; set; }

    /// <summary>
    /// 동기화 일시정지 여부
    /// </summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// 마지막 동기화 시간
    /// </summary>
    public DateTime LastSyncTime { get; set; }

    /// <summary>
    /// 다음 동기화 예정 시간
    /// </summary>
    public DateTime NextSyncTime { get; set; }

    /// <summary>
    /// 총 동기화 횟수
    /// </summary>
    public int SyncCount { get; set; }

    /// <summary>
    /// 오류 횟수
    /// </summary>
    public int ErrorCount { get; set; }

    /// <summary>
    /// 마지막 동기화로부터 경과 시간
    /// </summary>
    public TimeSpan TimeSinceLastSync => DateTime.UtcNow - LastSyncTime;
}
