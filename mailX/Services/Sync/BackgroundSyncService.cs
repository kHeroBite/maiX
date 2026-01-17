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
    private const int SyncIntervalMinutes = 5;
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
        _logger.Information("백그라운드 동기화 서비스 시작 - 주기: {Interval}분", SyncIntervalMinutes);

        // 시작 시 폴더 먼저 동기화
        await SyncFoldersAsync(stoppingToken);

        // 시작 시 즉시 1회 동기화
        await SyncAllAccountsAsync(stoppingToken);

        // 시작 시 캘린더 동기화
        await SyncCalendarAsync(stoppingToken);

        // 주기적 동기화
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(SyncIntervalMinutes));

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

                // 폴더 동기화
                await SyncFoldersAsync(stoppingToken);

                // 메일 동기화
                await SyncAllAccountsAsync(stoppingToken);

                // 캘린더 동기화
                await SyncCalendarAsync(stoppingToken);

                // 캘린더 알림 체크
                await CheckCalendarRemindersAsync(stoppingToken);
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
        _logger.Information("계정 동기화 시작: {Email}", accountEmail);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MailXDbContext>();
        var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();
        var graphMailService = scope.ServiceProvider.GetRequiredService<GraphMailService>();
        var emailAnalyzer = scope.ServiceProvider.GetRequiredService<EmailAnalyzer>();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

        // DB에서 받은편지함 폴더 ID 조회 (시스템 Inbox 폴더 우선)
        // "받은 편지함" (시스템) vs "#받은편지함" (커스텀) 구분
        var inboxFolder = await dbContext.Folders
            .FirstOrDefaultAsync(f => f.AccountEmail == accountEmail &&
                (f.DisplayName == "받은 편지함" || f.DisplayName.ToLower() == "inbox"), ct);

        if (inboxFolder == null)
        {
            _logger.Warning("받은편지함 폴더를 찾을 수 없음 - 동기화 생략");
            return;
        }

        var inboxFolderId = inboxFolder.Id;
        _logger.Debug("받은편지함 폴더 ID: {FolderId}", inboxFolderId);

        // 폴더별 동기화 상태 조회
        var inboxSyncState = await dbContext.SyncStates
            .FirstOrDefaultAsync(s => s.AccountEmail == accountEmail && s.FolderId == inboxFolderId, ct);

        if (inboxSyncState == null)
        {
            inboxSyncState = new SyncState
            {
                AccountEmail = accountEmail,
                FolderId = inboxFolderId
            };
            dbContext.SyncStates.Add(inboxSyncState);
            await dbContext.SaveChangesAsync(ct);
        }

        // 실제 폴더 ID로 메일 조회
        var newEmails = await FetchNewEmailsAsync(
            graphMailService,
            inboxSyncState,
            inboxFolderId,
            ct);

        if (newEmails.Count == 0)
        {
            _logger.Debug("새 메일 없음: {Email}", accountEmail);
            return;
        }

        _logger.Information("새 메일 발견: {Email} - {Count}건", accountEmail, newEmails.Count);

        // DB에 저장 (폴더 ID 전달)
        var savedEmails = await SaveEmailsAsync(dbContext, newEmails, accountEmail, inboxFolderId, ct);

        // 동기화 상태 업데이트
        inboxSyncState.LastSyncedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);

        // 메일 동기화 완료 이벤트 발생 (분석 전 UI 업데이트를 위해)
        EmailsSynced?.Invoke(savedEmails.Count);

        // 분석 파이프라인 실행
        await AnalyzeAndNotifyAsync(emailAnalyzer, notificationService, savedEmails, ct);
    }

    /// <summary>
    /// 새 이메일 가져오기 (Delta Query 활용)
    /// </summary>
    private async Task<List<Message>> FetchNewEmailsAsync(
        GraphMailService mailService,
        SyncState syncState,
        string folderId,
        CancellationToken ct)
    {
        var messages = new List<Message>();

        try
        {
            // 실제 폴더 ID로 메일 조회
            var allMessages = await mailService.GetMessagesAsync(folderId, MaxMessagesPerSync);

            if (syncState.LastSyncedAt.HasValue)
            {
                messages = allMessages
                    .Where(m => m.ReceivedDateTime > syncState.LastSyncedAt)
                    .ToList();
            }
            else
            {
                // 첫 동기화 - 최근 메일만
                messages = allMessages.Take(MaxMessagesPerSync).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "메일 가져오기 실패");
            throw;
        }

        return messages;
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
                // 중복 확인
                var exists = await dbContext.Emails
                    .AnyAsync(e => e.InternetMessageId == message.InternetMessageId, ct);

                if (exists)
                {
                    _logger.Debug("중복 메일 건너뜀: {MessageId}", message.InternetMessageId);
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

            // 폴더 동기화 완료 이벤트 발생
            FoldersSynced?.Invoke();
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
            NextSyncTime = _lastSyncTime.AddMinutes(SyncIntervalMinutes)
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
