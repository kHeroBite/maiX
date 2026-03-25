using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph.Models;
using Serilog;
using MaiX.Data;
using MaiX.Models;
using MaiX.Services.Analysis;
using MaiX.Services.Graph;
using MaiX.Services.Notification;
using MaiX.Utils;

namespace MaiX.Services.Sync;

/// <summary>
/// 백그라운드 동기화 서비스 - IHostedService 구현
/// 5분 주기 자동 동기화, Delta Query 지원, 분석 파이프라인 연동
/// </summary>
public class BackgroundSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    // JSON 직렬화 옵션 (한글/특수문자 이스케이프 방지)
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // 동기화 설정 (기존 - 하위 호환용)
    private int _syncIntervalSeconds = 300;  // 기본값: 5분 (300초)
    private const int MaxMessagesPerSync = 5;
    private const int MaxRetryCount = 3;
    private CancellationTokenSource? _intervalChangeCts;  // 주기 변경 시 타이머 재시작용

    // 즐겨찾기/전체 동기화 분리 설정
    private int _favoriteSyncIntervalSeconds = 30;   // 기본값: 30초
    private int _fullSyncIntervalSeconds = 300;      // 기본값: 5분 (300초)
    private int _calendarSyncIntervalSeconds = 60;   // 기본값: 1분 (60초)
    private int _chatSyncIntervalSeconds = 120;      // 기본값: 2분 (120초)
    private CancellationTokenSource? _favoriteIntervalChangeCts;  // 즐겨찾기 주기 변경용
    private CancellationTokenSource? _fullIntervalChangeCts;      // 전체 주기 변경용
    private CancellationTokenSource? _calendarIntervalChangeCts;  // 캘린더 주기 변경용
    private CancellationTokenSource? _chatIntervalChangeCts;      // 채팅 주기 변경용

    // 상태
    private DateTime _lastSyncTime = DateTime.MinValue;
    private DateTime _lastFavoriteSyncTime = DateTime.MinValue;
    private DateTime _lastFullSyncTime = DateTime.MinValue;
    private DateTime _lastCalendarSyncTime = DateTime.MinValue;
    private DateTime _lastChatSyncTime = DateTime.MinValue;
    // P2-02: Interlocked.CompareExchange 패턴 (0=비활성, 1=활성) — 레이스 컨디션 방지
    private int _isSyncing;
    private int _isFavoriteSyncing;
    private int _isCalendarSyncing;
    private int _isChatSyncing;
    private bool _isPaused;
    // P3-03: MailSyncCompleted Debounce 타이머 (500ms 내 중복 호출 병합)
    private System.Timers.Timer? _mailSyncDebounceTimer;
    private int _syncCount;
    private int _favoriteSyncCount;
    private int _fullSyncCount;
    private int _calendarSyncCount;
    private int _chatSyncCount;
    private int _errorCount;

    /// <summary>
    /// 동기화 일시정지/재개 이벤트
    /// </summary>
    public event Action<bool>? PausedChanged;

    /// <summary>
    /// 동기화 주기 변경 이벤트 (초 단위)
    /// </summary>
    public event Action<int>? SyncIntervalChanged;

    /// <summary>
    /// 즐겨찾기 동기화 주기 변경 이벤트 (초 단위)
    /// </summary>
    public event Action<int>? FavoriteSyncIntervalChanged;

    /// <summary>
    /// 전체 동기화 주기 변경 이벤트 (초 단위)
    /// </summary>
    public event Action<int>? FullSyncIntervalChanged;

    /// <summary>
    /// 캘린더 동기화 주기 변경 이벤트 (초 단위)
    /// </summary>
    public event Action<int>? CalendarSyncIntervalChanged;

    /// <summary>
    /// 채팅 동기화 주기 변경 이벤트 (초 단위)
    /// </summary>
    public event Action<int>? ChatSyncIntervalChanged;

    /// <summary>
    /// 채팅 동기화 완료 이벤트 (채팅방 개수 전달)
    /// </summary>
    public event Action<int>? ChatSynced;

    /// <summary>
    /// 현재 동기화 주기 (초)
    /// </summary>
    public int SyncIntervalSeconds => _syncIntervalSeconds;

    /// <summary>
    /// 즐겨찾기 동기화 주기 (초)
    /// </summary>
    public int FavoriteSyncIntervalSeconds => _favoriteSyncIntervalSeconds;

    /// <summary>
    /// 전체 동기화 주기 (초)
    /// </summary>
    public int FullSyncIntervalSeconds => _fullSyncIntervalSeconds;

    /// <summary>
    /// 캘린더 동기화 주기 (초)
    /// </summary>
    public int CalendarSyncIntervalSeconds => _calendarSyncIntervalSeconds;

    /// <summary>
    /// 채팅 동기화 주기 (초)
    /// </summary>
    public int ChatSyncIntervalSeconds => _chatSyncIntervalSeconds;

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

    /// <summary>
    /// 캘린더 이벤트 동기화 완료 이벤트 (추가, 수정, 삭제 개수 전달)
    /// </summary>
    public event Action<int, int, int>? CalendarEventsSynced;

    public BackgroundSyncService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = Log.ForContext<BackgroundSyncService>();
    }

    /// <summary>
    /// P3-03: MailSyncCompleted 이벤트를 Debounce하여 발생 (500ms 내 중복 호출 병합)
    /// </summary>
    private void RaiseMailSyncCompleted()
    {
        if (_mailSyncDebounceTimer == null)
        {
            _mailSyncDebounceTimer = new System.Timers.Timer(500) { AutoReset = false };
            _mailSyncDebounceTimer.Elapsed += (_, _) => MailSyncCompleted?.Invoke();
        }
        _mailSyncDebounceTimer.Stop();
        _mailSyncDebounceTimer.Start();
    }

    /// <summary>
    /// 마지막 동기화 시간
    /// </summary>
    public DateTime LastSyncTime => _lastSyncTime;

    /// <summary>
    /// 현재 동기화 중 여부
    /// </summary>
    public bool IsSyncing => _isSyncing == 1;

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
        Log4.Info($"[BackgroundSyncService] 백그라운드 동기화 서비스 시작 - 즐겨찾기: {_favoriteSyncIntervalSeconds}초, 전체: {_fullSyncIntervalSeconds}초, 캘린더: {_calendarSyncIntervalSeconds}초, 채팅: {_chatSyncIntervalSeconds}초");
        _logger.Information("백그라운드 동기화 서비스 시작 - 즐겨찾기: {Favorite}초, 전체: {Full}초, 캘린더: {Calendar}초, 채팅: {Chat}초",
            _favoriteSyncIntervalSeconds, _fullSyncIntervalSeconds, _calendarSyncIntervalSeconds, _chatSyncIntervalSeconds);

        try
        {
            // 시작 시 폴더 먼저 동기화
            Log4.Debug("[BackgroundSyncService] 초기 폴더 동기화 시작");
            await SyncFoldersAsync(stoppingToken);
            Log4.Debug("[BackgroundSyncService] 초기 폴더 동기화 완료");

            // 시작 시 즉시 1회 동기화
            Log4.Debug("[BackgroundSyncService] 초기 메일 동기화 시작");
            await SyncAllAccountsAsync(stoppingToken);
            Log4.Debug("[BackgroundSyncService] 초기 메일 동기화 완료");

            // 시작 시 캘린더 동기화
            Log4.Debug("[BackgroundSyncService] 초기 캘린더 동기화 시작");
            await SyncCalendarAsync(stoppingToken);
            Log4.Debug("[BackgroundSyncService] 초기 캘린더 동기화 완료");

            // 시작 시 채팅 동기화
            Log4.Debug("[BackgroundSyncService] 초기 채팅 동기화 시작");
            await SyncChatsAsync(stoppingToken);
            Log4.Debug("[BackgroundSyncService] 초기 채팅 동기화 완료");

            // 초기 동기화 시간 기록 (주기적 동기화 루프에서 중복 실행 방지)
            _lastFullSyncTime = DateTime.UtcNow;
            _lastFavoriteSyncTime = DateTime.UtcNow;
            _lastCalendarSyncTime = DateTime.UtcNow;
            _lastChatSyncTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log4.Error($"[BackgroundSyncService] 초기 동기화 실패: {ex.Message}");
            _logger.Error(ex, "초기 동기화 실패");
        }

        // 4개의 독립적인 동기화 루프 실행
        var favoriteTask = RunFavoriteSyncLoopAsync(stoppingToken);
        var fullTask = RunFullSyncLoopAsync(stoppingToken);
        var calendarTask = RunCalendarSyncLoopAsync(stoppingToken);
        var chatTask = RunChatSyncLoopAsync(stoppingToken);

        // 모든 Task가 완료될 때까지 대기
        await Task.WhenAll(favoriteTask, fullTask, calendarTask, chatTask);

        _logger.Information("백그라운드 동기화 서비스 중지됨");
    }

    /// <summary>
    /// 즐겨찾기 폴더 동기화 루프 (빠른 주기)
    /// </summary>
    private async Task RunFavoriteSyncLoopAsync(CancellationToken stoppingToken)
    {
        Log4.Info($"[BackgroundSyncService] 즐겨찾기 동기화 루프 시작 - 주기: {_favoriteSyncIntervalSeconds}초");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _favoriteIntervalChangeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_favoriteSyncIntervalSeconds));
                Log4.Debug($"[BackgroundSyncService] 즐겨찾기 타이머 생성 - 주기: {_favoriteSyncIntervalSeconds}초");

                while (await timer.WaitForNextTickAsync(_favoriteIntervalChangeCts.Token))
                {
                    if (_isPaused)
                    {
                        Log4.Debug("[BackgroundSyncService] 즐겨찾기 동기화 일시정지 상태 - 건너뜀");
                        continue;
                    }

                    Log4.Debug($"[BackgroundSyncService] 즐겨찾기 동기화 시작 (#{_favoriteSyncCount + 1})");

                    try
                    {
                        await SyncFavoriteFoldersAsync(stoppingToken);
                        _lastFavoriteSyncTime = DateTime.UtcNow;
                        Interlocked.Increment(ref _favoriteSyncCount);
                    }
                    catch (Exception ex)
                    {
                        Log4.Error($"[BackgroundSyncService] 즐겨찾기 동기화 실패: {ex.Message}");
                        _logger.Error(ex, "즐겨찾기 동기화 실패");
                    }

                    Log4.Debug($"[BackgroundSyncService] 즐겨찾기 동기화 완료 (#{_favoriteSyncCount})");
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                Log4.Info($"[BackgroundSyncService] 즐겨찾기 동기화 주기 변경으로 타이머 재시작 - 새 주기: {_favoriteSyncIntervalSeconds}초");
                _logger.Information("즐겨찾기 동기화 주기 변경으로 타이머 재시작 - 새 주기: {Interval}초", _favoriteSyncIntervalSeconds);
                continue;
            }
        }
    }

    /// <summary>
    /// 전체 폴더 동기화 루프 (느린 주기)
    /// </summary>
    private async Task RunFullSyncLoopAsync(CancellationToken stoppingToken)
    {
        Log4.Info($"[BackgroundSyncService] 전체 동기화 루프 시작 - 주기: {_fullSyncIntervalSeconds}초");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _fullIntervalChangeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                // 주기 변경으로 재시작된 경우, 마지막 실행 후 주기가 지났으면 즉시 실행
                // _lastFullSyncTime은 클래스 멤버이므로 타이머 재시작 후에도 유지됨
                var timeSinceLastSync = DateTime.UtcNow - _lastFullSyncTime;
                if (timeSinceLastSync.TotalSeconds >= _fullSyncIntervalSeconds)
                {
                    Log4.Debug($"[BackgroundSyncService] 전체 동기화 주기 경과 ({timeSinceLastSync.TotalSeconds:F0}초) - 즉시 실행");
                    await ExecuteFullSyncAsync(stoppingToken);
                }

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_fullSyncIntervalSeconds));
                Log4.Debug($"[BackgroundSyncService] 전체 타이머 생성 - 주기: {_fullSyncIntervalSeconds}초");

                while (await timer.WaitForNextTickAsync(_fullIntervalChangeCts.Token))
                {
                    if (_isPaused)
                    {
                        Log4.Debug("[BackgroundSyncService] 전체 동기화 일시정지 상태 - 건너뜀");
                        continue;
                    }

                    await ExecuteFullSyncAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                Log4.Info($"[BackgroundSyncService] 전체 동기화 주기 변경으로 타이머 재시작 - 새 주기: {_fullSyncIntervalSeconds}초");
                _logger.Information("전체 동기화 주기 변경으로 타이머 재시작 - 새 주기: {Interval}초", _fullSyncIntervalSeconds);
                continue;
            }
        }
    }

    /// <summary>
    /// 전체 동기화 실행 (폴더, 메일) - 캘린더는 별도 루프에서 실행
    /// </summary>
    private async Task ExecuteFullSyncAsync(CancellationToken stoppingToken)
    {
        Log4.Debug($"[BackgroundSyncService] 전체 동기화 시작 (#{_fullSyncCount + 1})");

        try
        {
            // 폴더 동기화
            await SyncFoldersAsync(stoppingToken);

            // 전체 메일 동기화 (Delta Query)
            await SyncAllAccountsAsync(stoppingToken);

            _lastFullSyncTime = DateTime.UtcNow;
            _lastSyncTime = DateTime.UtcNow;  // 하위 호환용
            Interlocked.Increment(ref _fullSyncCount);
            Interlocked.Increment(ref _syncCount);  // 하위 호환용
        }
        catch (Exception ex)
        {
            Log4.Error($"[BackgroundSyncService] 전체 동기화 실패: {ex.Message}");
            _logger.Error(ex, "전체 동기화 실패");
            Interlocked.Increment(ref _errorCount);
        }

        Log4.Debug($"[BackgroundSyncService] 전체 동기화 완료 (#{_fullSyncCount})");
    }

    /// <summary>
    /// 캘린더 동기화 루프 (별도 주기)
    /// </summary>
    private async Task RunCalendarSyncLoopAsync(CancellationToken stoppingToken)
    {
        Log4.Info($"[BackgroundSyncService] 캘린더 동기화 루프 시작 - 주기: {_calendarSyncIntervalSeconds}초");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _calendarIntervalChangeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                // 주기 변경으로 재시작된 경우, 마지막 실행 후 주기가 지났으면 즉시 실행
                var timeSinceLastSync = DateTime.UtcNow - _lastCalendarSyncTime;
                if (timeSinceLastSync.TotalSeconds >= _calendarSyncIntervalSeconds)
                {
                    Log4.Debug($"[BackgroundSyncService] 캘린더 동기화 주기 경과 ({timeSinceLastSync.TotalSeconds:F0}초) - 즉시 실행");
                    await ExecuteCalendarSyncAsync(stoppingToken);
                }

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_calendarSyncIntervalSeconds));
                Log4.Debug($"[BackgroundSyncService] 캘린더 타이머 생성 - 주기: {_calendarSyncIntervalSeconds}초");

                while (await timer.WaitForNextTickAsync(_calendarIntervalChangeCts.Token))
                {
                    if (_isPaused)
                    {
                        Log4.Debug("[BackgroundSyncService] 캘린더 동기화 일시정지 상태 - 건너뜀");
                        continue;
                    }

                    await ExecuteCalendarSyncAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                Log4.Info($"[BackgroundSyncService] 캘린더 동기화 주기 변경으로 타이머 재시작 - 새 주기: {_calendarSyncIntervalSeconds}초");
                _logger.Information("캘린더 동기화 주기 변경으로 타이머 재시작 - 새 주기: {Interval}초", _calendarSyncIntervalSeconds);
                continue;
            }
        }
    }

    /// <summary>
    /// 캘린더 동기화 실행
    /// </summary>
    private async Task ExecuteCalendarSyncAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.CompareExchange(ref _isCalendarSyncing, 1, 0) != 0)
        {
            Log4.Debug("[BackgroundSyncService] 이미 캘린더 동기화 진행 중 - 건너뜀");
            return;
        }
        Log4.Debug($"[BackgroundSyncService] 캘린더 동기화 시작 (#{_calendarSyncCount + 1})");

        try
        {
            // 캘린더 동기화
            await SyncCalendarAsync(stoppingToken);

            // 캘린더 알림 체크
            await CheckCalendarRemindersAsync(stoppingToken);

            _lastCalendarSyncTime = DateTime.UtcNow;
            Interlocked.Increment(ref _calendarSyncCount);
        }
        catch (Exception ex)
        {
            Log4.Error($"[BackgroundSyncService] 캘린더 동기화 실패: {ex.Message}");
            _logger.Error(ex, "캘린더 동기화 실패");
            Interlocked.Increment(ref _errorCount);
        }
        finally
        {
            Interlocked.Exchange(ref _isCalendarSyncing, 0);
        }

        Log4.Debug($"[BackgroundSyncService] 캘린더 동기화 완료 (#{_calendarSyncCount})");
    }

    /// <summary>
    /// 채팅 동기화 루프
    /// </summary>
    private async Task RunChatSyncLoopAsync(CancellationToken stoppingToken)
    {
        Log4.Info($"[BackgroundSyncService] 채팅 동기화 루프 시작 - 주기: {_chatSyncIntervalSeconds}초");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _chatIntervalChangeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

                // 주기 변경으로 재시작된 경우, 마지막 실행 후 주기가 지났으면 즉시 실행
                var timeSinceLastSync = DateTime.UtcNow - _lastChatSyncTime;
                if (timeSinceLastSync.TotalSeconds >= _chatSyncIntervalSeconds)
                {
                    Log4.Debug($"[BackgroundSyncService] 채팅 동기화 주기 경과 ({timeSinceLastSync.TotalSeconds:F0}초) - 즉시 실행");
                    await ExecuteChatSyncAsync(stoppingToken);
                }

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_chatSyncIntervalSeconds));
                Log4.Debug($"[BackgroundSyncService] 채팅 타이머 생성 - 주기: {_chatSyncIntervalSeconds}초");

                while (await timer.WaitForNextTickAsync(_chatIntervalChangeCts.Token))
                {
                    if (_isPaused)
                    {
                        Log4.Debug("[BackgroundSyncService] 채팅 동기화 일시정지 상태 - 건너뜀");
                        continue;
                    }

                    await ExecuteChatSyncAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                Log4.Info($"[BackgroundSyncService] 채팅 동기화 주기 변경으로 타이머 재시작 - 새 주기: {_chatSyncIntervalSeconds}초");
                _logger.Information("채팅 동기화 주기 변경으로 타이머 재시작 - 새 주기: {Interval}초", _chatSyncIntervalSeconds);
                continue;
            }
        }
    }

    /// <summary>
    /// 채팅 동기화 실행
    /// </summary>
    private async Task ExecuteChatSyncAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.CompareExchange(ref _isChatSyncing, 1, 0) != 0)
        {
            Log4.Debug("[BackgroundSyncService] 이미 채팅 동기화 진행 중 - 건너뜀");
            return;
        }
        Log4.Debug($"[BackgroundSyncService] 채팅 동기화 시작 (#{_chatSyncCount + 1})");

        try
        {
            await SyncChatsAsync(stoppingToken);

            _lastChatSyncTime = DateTime.UtcNow;
            Interlocked.Increment(ref _chatSyncCount);
        }
        catch (Exception ex)
        {
            Log4.Error($"[BackgroundSyncService] 채팅 동기화 실패: {ex.Message}");
            _logger.Error(ex, "채팅 동기화 실패");
            Interlocked.Increment(ref _errorCount);
        }
        finally
        {
            Interlocked.Exchange(ref _isChatSyncing, 0);
        }

        Log4.Debug($"[BackgroundSyncService] 채팅 동기화 완료 (#{_chatSyncCount})");
    }

    /// <summary>
    /// 채팅 동기화 (Teams API)
    /// </summary>
    public async Task SyncChatsAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var teamsService = scope.ServiceProvider.GetRequiredService<GraphTeamsService>();

            // Teams 채팅 목록 가져오기
            var chats = await teamsService.GetChatsAsync();
            var chatCount = chats.Count();

            Log4.Debug($"[BackgroundSyncService] 채팅 동기화: {chatCount}개 채팅방");

            // 이벤트 발생
            ChatSynced?.Invoke(chatCount);
        }
        catch (Exception ex)
        {
            Log4.Error($"[BackgroundSyncService] 채팅 동기화 실패: {ex.Message}");
            _logger.Error(ex, "채팅 동기화 실패");
        }
    }

    /// <summary>
    /// 즐겨찾기 폴더만 동기화 (IsFavorite == true인 폴더)
    /// </summary>
    public async Task SyncFavoriteFoldersAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isFavoriteSyncing, 1, 0) != 0)
        {
            _logger.Warning("이미 즐겨찾기 동기화 진행 중 - 건너뜀");
            return;
        }
        _logger.Debug("즐겨찾기 폴더 동기화 시작");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<MaiXDbContext>();
            var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();
            var graphMailService = scope.ServiceProvider.GetRequiredService<GraphMailService>();
            var emailAnalyzer = scope.ServiceProvider.GetRequiredService<EmailAnalyzer>();
            var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();

            if (!graphAuthService.IsLoggedIn || string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
            {
                _logger.Debug("로그인된 계정 없음 - 즐겨찾기 동기화 생략");
                return;
            }

            var accountEmail = graphAuthService.CurrentUserEmail;

            // 즐겨찾기 폴더만 조회
            var favoriteFolders = await dbContext.Folders
                .Where(f => f.AccountEmail == accountEmail && f.IsFavorite)
                .ToListAsync(ct);

            if (favoriteFolders.Count == 0)
            {
                _logger.Debug("즐겨찾기 폴더 없음 - 동기화 생략");
                return;
            }

            _logger.Information("즐겨찾기 폴더 동기화: {Count}개 폴더", favoriteFolders.Count);

            int totalChanged = 0;
            int totalDeleted = 0;
            var allSavedEmails = new List<Email>();

            foreach (var folder in favoriteFolders)
            {
                try
                {
                    // 1단계: 직접 최신 메일 조회 (Delta API 지연 보완)
                    // since 필터 없이 항상 최신 10개 메일을 가져옴
                    // (DB 중복 체크로 새 메일만 저장됨)
                    // 최근 50개 메일 조회 (새 메일 감지 + 읽음 상태 변경 감지)
                    var latestMessages = await graphMailService.GetLatestMessagesAsync(
                        folder.Id,
                        count: 50,
                        since: null);

                    var latestMessageList = latestMessages.ToList();
                    if (latestMessageList.Count > 0)
                    {
                        _logger.Debug("직접 조회로 {Count}건 발견 (폴더: {FolderName})",
                            latestMessageList.Count, folder.DisplayName);

                        // 디버그: 조회된 메일 제목, 수신시간, 읽음 상태 출력
                        foreach (var msg in latestMessageList.Take(5))
                        {
                            _logger.Debug("  - [{ReceivedAt}] {Subject} (IsRead={IsRead})",
                                msg.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                                msg.Subject?.Substring(0, Math.Min(msg.Subject?.Length ?? 0, 30)) ?? "(제목없음)",
                                msg.IsRead ?? false);
                        }

                        // 직접 조회한 메일 저장 (중복은 SaveEmailsAsync에서 처리)
                        var directSavedEmails = await SaveEmailsAsync(
                            dbContext, latestMessageList, accountEmail, folder.Id, ct);

                        allSavedEmails.AddRange(directSavedEmails);
                        totalChanged += directSavedEmails.Count;
                    }

                    // 2단계: Delta Query 실행 (상태 변경 감지 및 삭제 처리)
                    var (changed, deleted, savedEmails) = await SyncFolderAsync(
                        dbContext, graphMailService, accountEmail, folder, ct);

                    // Delta Query에서 저장된 메일 중 직접 조회와 중복 제거
                    var newFromDelta = savedEmails
                        .Where(e => !allSavedEmails.Any(a => a.InternetMessageId == e.InternetMessageId))
                        .ToList();

                    totalChanged += newFromDelta.Count;
                    totalDeleted += deleted;
                    allSavedEmails.AddRange(newFromDelta);

                    // 3단계: 읽음 상태 동기화 (최근 7일간 메일)
                    var readStatusUpdated = await SyncReadStatusAsync(
                        dbContext, graphMailService, folder.Id, ct);
                    if (readStatusUpdated > 0)
                    {
                        _logger.Information("읽음 상태 동기화: {Count}건 업데이트 (폴더: {FolderName})",
                            readStatusUpdated, folder.DisplayName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "즐겨찾기 폴더 동기화 실패: {FolderName}", folder.DisplayName);
                }
            }

            // 새 메일이 있으면 이벤트 발생
            if (allSavedEmails.Count > 0)
            {
                EmailsSynced?.Invoke(allSavedEmails.Count);

                // 분석 파이프라인 실행 (받은편지함 메일만)
                var inboxEmails = allSavedEmails.Where(e =>
                    e.ParentFolderId != null &&
                    favoriteFolders.Any(f => f.Id == e.ParentFolderId &&
                        (f.DisplayName == "받은 편지함" || f.DisplayName.ToLower() == "inbox")))
                    .ToList();

                if (inboxEmails.Count > 0)
                {
                    await AnalyzeAndNotifyAsync(emailAnalyzer, notificationService, inboxEmails, ct);
                }
            }

            _lastFavoriteSyncTime = DateTime.UtcNow;
            _logger.Information("즐겨찾기 폴더 동기화 완료: 변경 {Changed}건, 삭제 {Deleted}건",
                totalChanged, totalDeleted);

            // 동기화 완료 이벤트 (UI 갱신용) — Debounce 적용
            RaiseMailSyncCompleted();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "즐겨찾기 폴더 동기화 실패");
        }
        finally
        {
            Interlocked.Exchange(ref _isFavoriteSyncing, 0);
        }
    }

    /// <summary>
    /// 모든 계정 동기화
    /// </summary>
    public async Task SyncAllAccountsAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isSyncing, 1, 0) != 0)
        {
            _logger.Warning("이미 동기화 진행 중 - 건너뜀");
            return;
        }
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

            // 동기화 완료 이벤트 발생 (UI 갱신용) — Debounce 적용
            RaiseMailSyncCompleted();
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger.Error(ex, "전체 계정 동기화 실패");
        }
        finally
        {
            Interlocked.Exchange(ref _isSyncing, 0);
        }
    }

    /// <summary>
    /// 단일 계정 동기화
    /// </summary>
    public async Task SyncAccountAsync(string accountEmail, CancellationToken ct = default)
    {
        _logger.Debug("계정 동기화 시작: {Email}", accountEmail);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MaiXDbContext>();
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

        // 우선순위 폴더: 받은 편지함, 보낸 편지함을 먼저 동기화
        var priorityFolderNames = new[] { "받은 편지함", "Inbox", "보낸 편지함", "Sent Items" };
        var orderedFolders = folders
            .OrderByDescending(f => priorityFolderNames.Contains(f.DisplayName, StringComparer.OrdinalIgnoreCase))
            .ThenBy(f => f.DisplayName)
            .ToList();

        _logger.Information("전체 폴더 동기화: {Count}개 폴더 (받은 편지함 우선)", orderedFolders.Count);

        int totalChanged = 0;
        int totalDeleted = 0;
        var allSavedEmails = new List<Email>();

        // 각 폴더별 동기화 (우선순위 폴더부터)
        foreach (var folder in orderedFolders)
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

        // 동기화 완료 이벤트 (UI 갱신용) — Debounce 적용
        RaiseMailSyncCompleted();
    }


    /// <summary>
    /// 단일 폴더 동기화
    /// </summary>
    private async Task<(int Changed, int Deleted, List<Email> SavedEmails)> SyncFolderAsync(
        MaiXDbContext dbContext,
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
            // deltaLink가 있고, 마지막 동기화로부터 1시간 이상 지났으면 deltaLink 리셋
            // (stale deltaLink 문제 해결 - 오래 실행 시 새 메일 감지 안됨 방지)
            var deltaLinkToUse = syncState.DeltaLink;
            if (!string.IsNullOrEmpty(deltaLinkToUse) &&
                syncState.LastSyncedAt.HasValue &&
                DateTime.UtcNow - syncState.LastSyncedAt.Value > TimeSpan.FromHours(1))
            {
                _logger.Warning("deltaLink가 1시간 이상 변경 없음 - 초기 동기화로 리셋 (폴더: {FolderId})", folderId);
                deltaLinkToUse = null;
                syncState.DeltaLink = null;  // DB에서도 리셋
            }

            // Delta Query로 변경분만 조회
            var (messages, newDeltaLink, deletedIds) = await mailService.GetMessagesDeltaAsync(
                folderId,
                deltaLinkToUse);

            // 새 deltaLink 저장
            if (!string.IsNullOrEmpty(newDeltaLink))
            {
                syncState.DeltaLink = newDeltaLink;
            }

            _logger.Debug("Delta Query 완료: 변경 {Count}건, 삭제 {Deleted}건 (폴더: {FolderId})",
                messages.Count(), deletedIds.Count(), folderId);

            return (messages.ToList(), deletedIds.ToList());
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError odataEx)
            when (odataEx.ResponseStatusCode == 410 ||
                  odataEx.Error?.Code?.Contains("syncStateNotFound", StringComparison.OrdinalIgnoreCase) == true ||
                  odataEx.Error?.Code?.Contains("resyncRequired", StringComparison.OrdinalIgnoreCase) == true)
        {
            // P3-04: DeltaLink 410 Gone — deltaLink 리셋 후 전체 재동기화
            _logger.Warning("DeltaLink 만료(410 Gone) 감지 — deltaLink 초기화 후 재시도 (폴더: {FolderId})", folderId);
            syncState.DeltaLink = null;
            var (messages, newDeltaLink, deletedIds) = await mailService.GetMessagesDeltaAsync(folderId, null);
            if (!string.IsNullOrEmpty(newDeltaLink))
                syncState.DeltaLink = newDeltaLink;
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
        MaiXDbContext dbContext,
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
        MaiXDbContext dbContext,
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
                // 기존 메일 확인 (EntryId 우선, 없으면 InternetMessageId + ParentFolderId로 검색)
                // 같은 InternetMessageId라도 다른 폴더(보낸편지함/받은편지함)에 있으면 별도 메일로 처리
                Email? existingEmail = null;
                string foundBy = "";

                // 1. EntryId로 먼저 검색 (가장 정확한 식별자)
                if (!string.IsNullOrEmpty(message.Id))
                {
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.EntryId == message.Id, ct);
                    if (existingEmail != null) foundBy = "EntryId";
                }

                // 2. EntryId로 못 찾으면 InternetMessageId + ParentFolderId로 검색
                //    (같은 메일이 다른 폴더에 있을 수 있음: 보낸편지함/받은편지함)
                if (existingEmail == null && !string.IsNullOrEmpty(message.InternetMessageId))
                {
                    var parentFolderId = message.ParentFolderId ?? folderId;
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.InternetMessageId == message.InternetMessageId
                            && e.ParentFolderId == parentFolderId, ct);
                    if (existingEmail != null) foundBy = "InternetMessageId+FolderId";
                }

                // 디버그: 새 메일 감지 여부 로깅
                if (existingEmail == null)
                {
                    _logger.Debug("새 메일 감지: [{ReceivedAt}] {Subject} (InternetMessageId: {InternetMessageId})",
                        message.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                        message.Subject?.Substring(0, Math.Min(message.Subject?.Length ?? 0, 30)) ?? "(제목없음)",
                        message.InternetMessageId ?? "(null)");
                }
                else
                {
                    // 디버그: 중복 메일 발견 시 상세 로깅
                    _logger.Debug("기존 메일 발견: [{NewReceivedAt}] {NewSubject} → 기존: [{OldReceivedAt}] {OldSubject} (FoundBy: {FoundBy})",
                        message.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                        message.Subject?.Substring(0, Math.Min(message.Subject?.Length ?? 0, 20)) ?? "(제목없음)",
                        existingEmail.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
                        existingEmail.Subject?.Substring(0, Math.Min(existingEmail.Subject?.Length ?? 0, 20)) ?? "(제목없음)",
                        foundBy);
                }

                if (existingEmail != null)
                {
                    // 기존 메일의 상태 업데이트 (IsRead, FlagStatus, Importance, Categories, ParentFolderId, Subject, ReceivedDateTime 등)
                    bool updated = false;

                    // 디버그: Graph API에서 받아온 flag 값 로그
                    var graphFlagStatus = message.Flag?.FlagStatus;
                    _logger.Debug("플래그 비교: {Subject} - Graph API: {GraphFlag} ({GraphFlagRaw}), DB: {DbFlag}",
                        existingEmail.Subject?.Substring(0, Math.Min(existingEmail.Subject?.Length ?? 0, 20)) ?? "(제목없음)",
                        graphFlagStatus?.ToString()?.ToLower() ?? "null",
                        graphFlagStatus?.ToString() ?? "null",
                        existingEmail.FlagStatus ?? "null");

                    // Subject 동기화 (초안에서 제목이 변경된 경우)
                    // 서버에서 Subject가 null이면 기존 제목 유지 (API에서 subject 필드를 select하지 않은 경우)
                    if (!string.IsNullOrEmpty(message.Subject) && existingEmail.Subject != message.Subject)
                    {
                        _logger.Debug("메일 제목 변경: {OldSubject} -> {NewSubject}",
                            existingEmail.Subject, message.Subject);
                        existingEmail.Subject = message.Subject;
                        updated = true;
                    }

                    // ReceivedDateTime 동기화 (서버에서 수정된 경우)
                    var newReceivedDateTime = message.ReceivedDateTime?.UtcDateTime;
                    if (newReceivedDateTime != null && existingEmail.ReceivedDateTime != newReceivedDateTime)
                    {
                        _logger.Debug("메일 수신시간 변경: {OldTime} -> {NewTime}",
                            existingEmail.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                            newReceivedDateTime?.ToString("yyyy-MM-dd HH:mm:ss"));
                        existingEmail.ReceivedDateTime = newReceivedDateTime;
                        updated = true;
                    }

                    // From 필드 동기화 (구 형식 "이름"을 새 형식 "이름 <이메일>"로 업데이트)
                    var newFrom = GetDisplayName(message.From?.EmailAddress);
                    if (!string.IsNullOrEmpty(newFrom) && existingEmail.From != newFrom)
                    {
                        // 기존 From이 이메일을 포함하지 않고 새 From이 이메일을 포함하는 경우에만 업데이트
                        if (!string.IsNullOrEmpty(existingEmail.From) &&
                            !existingEmail.From.Contains("@") &&
                            newFrom.Contains("@"))
                        {
                            _logger.Debug("메일 발신자 업데이트: {OldFrom} -> {NewFrom}",
                                existingEmail.From, newFrom);
                            existingEmail.From = newFrom;
                            updated = true;
                        }
                    }

                    // 읽음 상태 동기화
                    if (existingEmail.IsRead != (message.IsRead ?? false))
                    {
                        _logger.Debug("메일 읽음 상태 변경: {Subject} ({OldValue} -> {NewValue})",
                            existingEmail.Subject, existingEmail.IsRead, message.IsRead ?? false);
                        existingEmail.IsRead = message.IsRead ?? false;
                        updated = true;
                    }

                    // 플래그 상태 동기화 (null이면 notFlagged로 처리)
                    var newFlagStatus = message.Flag?.FlagStatus?.ToString()?.ToLower() ?? "notflagged";
                    var oldFlagStatus = existingEmail.FlagStatus ?? "notflagged";
                    if (oldFlagStatus != newFlagStatus)
                    {
                        _logger.Information("메일 플래그 상태 변경: {Subject} ({OldValue} -> {NewValue})",
                            existingEmail.Subject, oldFlagStatus, newFlagStatus);
                        existingEmail.FlagStatus = newFlagStatus;
                        updated = true;
                    }

                    // 중요도 동기화
                    var newImportance = message.Importance?.ToString()?.ToLower();
                    if (existingEmail.Importance != newImportance)
                    {
                        _logger.Debug("메일 중요도 변경: {Subject} ({OldValue} -> {NewValue})",
                            existingEmail.Subject, existingEmail.Importance, newImportance);
                        existingEmail.Importance = newImportance;
                        updated = true;
                    }

                    // 카테고리 동기화
                    var newCategories = message.Categories?.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(message.Categories)
                        : null;
                    if (existingEmail.Categories != newCategories)
                    {
                        _logger.Debug("메일 카테고리 변경: {Subject} ({OldValue} -> {NewValue})",
                            existingEmail.Subject, existingEmail.Categories, newCategories);
                        existingEmail.Categories = newCategories;
                        updated = true;
                    }

                    // To/Cc/Bcc 필드 재직렬화 (유니코드 이스케이프 문제 해결)
                    // 기존 DB에 저장된 유니코드 이스케이프 문자열을 정상 형식으로 업데이트
                    var newTo = SerializeRecipients(message.ToRecipients);
                    if (!string.IsNullOrEmpty(newTo) && existingEmail.To != newTo)
                    {
                        // 유니코드 이스케이프가 포함된 경우에만 업데이트 (\\u로 시작하는 패턴 감지)
                        if (existingEmail.To?.Contains("\\u") == true || existingEmail.To != newTo)
                        {
                            _logger.Debug("메일 To 필드 업데이트: {Subject}", existingEmail.Subject);
                            existingEmail.To = newTo;
                            updated = true;
                        }
                    }

                    var newCc = SerializeRecipients(message.CcRecipients);
                    if (existingEmail.Cc != newCc)
                    {
                        if (existingEmail.Cc?.Contains("\\u") == true || existingEmail.Cc != newCc)
                        {
                            _logger.Debug("메일 Cc 필드 업데이트: {Subject}", existingEmail.Subject);
                            existingEmail.Cc = newCc;
                            updated = true;
                        }
                    }

                    var newBcc = SerializeRecipients(message.BccRecipients);
                    if (existingEmail.Bcc != newBcc)
                    {
                        if (existingEmail.Bcc?.Contains("\\u") == true || existingEmail.Bcc != newBcc)
                        {
                            _logger.Debug("메일 Bcc 필드 업데이트: {Subject}", existingEmail.Subject);
                            existingEmail.Bcc = newBcc;
                            updated = true;
                        }
                    }

                    // 폴더 이동 동기화 (parentFolderId가 변경된 경우)
                    var newParentFolderId = message.ParentFolderId;
                    if (!string.IsNullOrEmpty(newParentFolderId) && existingEmail.ParentFolderId != newParentFolderId)
                    {
                        _logger.Debug("메일 폴더 변경: {Subject} ({OldFolder} -> {NewFolder})",
                            existingEmail.Subject, existingEmail.ParentFolderId, newParentFolderId);
                        existingEmail.ParentFolderId = newParentFolderId;
                        
                        // 폴더 이동 시 EntryId도 변경될 수 있음
                        if (!string.IsNullOrEmpty(message.Id) && existingEmail.EntryId != message.Id)
                        {
                            existingEmail.EntryId = message.Id;
                        }
                        updated = true;
                    }

                    if (updated)
                    {
                        await dbContext.SaveChangesAsync(ct);
                        _logger.Debug("기존 메일 상태 업데이트: {Subject} (IsRead={IsRead}, Flag={Flag}, Categories={Categories})",
                            existingEmail.Subject, existingEmail.IsRead, existingEmail.FlagStatus, existingEmail.Categories);
                    }
                    continue;
                }

                // 새 메일 생성
                var email = new Email
                {
                    InternetMessageId = message.InternetMessageId,
                    EntryId = message.Id,
                    ConversationId = message.ConversationId,
                    Subject = message.Subject ?? "(제목 없음)",
                    Body = message.Body?.Content,
                    IsHtml = message.Body?.ContentType == Microsoft.Graph.Models.BodyType.Html,
                    From = GetDisplayName(message.From?.EmailAddress),
                    To = SerializeRecipients(message.ToRecipients),
                    Cc = SerializeRecipients(message.CcRecipients),
                    Bcc = SerializeRecipients(message.BccRecipients),
                    ReceivedDateTime = message.ReceivedDateTime?.UtcDateTime,
                    IsRead = message.IsRead ?? false,
                    FlagStatus = message.Flag?.FlagStatus?.ToString()?.ToLower(),
                    Importance = message.Importance?.ToString()?.ToLower(),
                    HasAttachments = message.HasAttachments ?? false,
                    Categories = message.Categories?.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(message.Categories)
                        : null,
                    ParentFolderId = !string.IsNullOrEmpty(message.ParentFolderId) 
                        ? message.ParentFolderId 
                        : folderId,  // 서버에서 받은 폴더 ID 사용, 없으면 현재 동기화 중인 폴더 ID
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
    /// 읽음 상태 동기화 (최근 N일간 메일)
    /// Delta Query가 읽음 상태 변경을 반환하지 않는 경우를 보완
    /// </summary>
    /// <param name="dbContext">DB 컨텍스트</param>
    /// <param name="graphMailService">Graph 메일 서비스</param>
    /// <param name="folderId">폴더 ID</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>업데이트된 메일 수</returns>
    private async Task<int> SyncReadStatusAsync(
        MaiXDbContext dbContext,
        GraphMailService graphMailService,
        string folderId,
        CancellationToken ct)
    {
        try
        {
            // 서버에서 최근 7일간 메일의 읽음 상태 조회
            var serverReadStatus = await graphMailService.GetMessagesReadStatusAsync(folderId, days: 7);
            var serverStatusList = serverReadStatus.ToList();
            var serverStatusDict = serverStatusList.ToDictionary(x => x.Id, x => x.IsRead);

            _logger.Debug("읽음 상태 조회: 서버 {ServerCount}건 (폴더: {FolderId})",
                serverStatusDict.Count, folderId.Substring(0, Math.Min(folderId.Length, 20)));

            if (serverStatusDict.Count == 0)
            {
                return 0;
            }

            // DB에서 해당 폴더의 메일 조회 (EntryId 기준)
            var dbEmails = await dbContext.Emails
                .Where(e => e.ParentFolderId == folderId && e.EntryId != null)
                .Select(e => new { e.Id, e.EntryId, e.IsRead, e.Subject })
                .ToListAsync(ct);

            _logger.Debug("읽음 상태 비교: DB {DbCount}건 vs 서버 {ServerCount}건",
                dbEmails.Count, serverStatusDict.Count);

            int updatedCount = 0;
            int matchedCount = 0;

            foreach (var dbEmail in dbEmails)
            {
                if (dbEmail.EntryId == null) continue;

                // 서버에서 해당 메일의 읽음 상태 확인
                if (serverStatusDict.TryGetValue(dbEmail.EntryId, out var serverIsRead))
                {
                    matchedCount++;
                    // 읽음 상태가 다르면 업데이트
                    if (dbEmail.IsRead != serverIsRead)
                    {
                        var email = await dbContext.Emails.FindAsync(new object[] { dbEmail.Id }, ct);
                        if (email != null)
                        {
                            _logger.Debug("읽음 상태 동기화: {Subject} ({OldValue} -> {NewValue})",
                                dbEmail.Subject, dbEmail.IsRead, serverIsRead);
                            email.IsRead = serverIsRead;
                            updatedCount++;
                        }
                    }
                }
            }

            _logger.Debug("읽음 상태 매칭: {MatchedCount}건 중 {UpdatedCount}건 변경",
                matchedCount, updatedCount);

            if (updatedCount > 0)
            {
                await dbContext.SaveChangesAsync(ct);
            }

            return updatedCount;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "읽음 상태 동기화 실패 (폴더: {FolderId})", folderId);
            return 0;
        }
    }

    /// <summary>
    /// 메일 저장 (진행률 이벤트 포함)
    /// </summary>
    private async Task SaveEmailsWithProgressAsync(
        MaiXDbContext dbContext,
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
                // 기존 메일 확인 (EntryId 우선, 없으면 InternetMessageId + ParentFolderId로 검색)
                Email? existingEmail = null;

                // 1. EntryId로 먼저 검색 (가장 정확한 식별자)
                if (!string.IsNullOrEmpty(message.Id))
                {
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.EntryId == message.Id, ct);
                }

                // 2. EntryId로 못 찾으면 InternetMessageId + ParentFolderId로 검색
                if (existingEmail == null && !string.IsNullOrEmpty(message.InternetMessageId))
                {
                    var parentFolderId = message.ParentFolderId ?? folderId;
                    existingEmail = await dbContext.Emails
                        .FirstOrDefaultAsync(e => e.InternetMessageId == message.InternetMessageId
                            && e.ParentFolderId == parentFolderId, ct);
                }

                if (existingEmail != null)
                {
                    // 기존 메일의 상태 업데이트 (IsRead, FlagStatus, Importance, From 등)
                    bool updated = false;

                    // From 필드 동기화 (구 형식 "이름"을 새 형식 "이름 <이메일>"로 업데이트)
                    var newFrom = GetDisplayName(message.From?.EmailAddress);
                    if (!string.IsNullOrEmpty(newFrom) && existingEmail.From != newFrom)
                    {
                        if (!string.IsNullOrEmpty(existingEmail.From) &&
                            !existingEmail.From.Contains("@") &&
                            newFrom.Contains("@"))
                        {
                            _logger.Debug("메일 발신자 업데이트 (Delta): {OldFrom} -> {NewFrom}",
                                existingEmail.From, newFrom);
                            existingEmail.From = newFrom;
                            updated = true;
                        }
                    }

                    if (existingEmail.IsRead != (message.IsRead ?? false))
                    {
                        existingEmail.IsRead = message.IsRead ?? false;
                        updated = true;
                    }

                    var newFlagStatus = message.Flag?.FlagStatus?.ToString()?.ToLower() ?? "notflagged";
                    var oldFlagStatus = existingEmail.FlagStatus ?? "notflagged";
                    if (oldFlagStatus != newFlagStatus)
                    {
                        _logger.Information("메일 플래그 상태 변경 (Delta): {Subject} ({OldValue} -> {NewValue})",
                            existingEmail.Subject, oldFlagStatus, newFlagStatus);
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
                        From = GetDisplayName(message.From?.EmailAddress),
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

        var displayNames = recipients
            .Where(r => r.EmailAddress != null)
            .Select(r => GetDisplayName(r.EmailAddress))
            .ToList();

        return JsonSerializer.Serialize(displayNames, _jsonOptions);
    }

    /// <summary>
    /// 이메일 주소에서 "이름 &lt;주소&gt;" 형식 문자열 생성
    /// 이름이 없으면 주소만 반환
    /// </summary>
    private string GetDisplayName(EmailAddress? emailAddress)
    {
        if (emailAddress == null)
            return "unknown";

        var address = emailAddress.Address ?? "unknown";
        var name = emailAddress.Name;

        // 이름이 있으면 "이름 <주소>" 형식, 없으면 주소만
        if (!string.IsNullOrWhiteSpace(name) && name != address)
            return $"{name} <{address}>";

        return address;
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
        var dbContext = scope.ServiceProvider.GetRequiredService<MaiXDbContext>();

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
    /// 동기화 주기 설정 (초 단위) - 하위 호환용
    /// </summary>
    public void SetSyncInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;  // 최소 1초
        if (seconds > 3600) seconds = 3600;  // 최대 1시간

        var oldInterval = _syncIntervalSeconds;
        _syncIntervalSeconds = seconds;

        _logger.Information("동기화 주기 변경: {Old}초 → {New}초", oldInterval, seconds);

        // 주기 변경 시 현재 타이머 취소 (새 주기로 재시작하도록)
        _intervalChangeCts?.Cancel();

        // 이벤트 발생
        SyncIntervalChanged?.Invoke(seconds);
    }

    /// <summary>
    /// 즐겨찾기 동기화 주기 설정 (초 단위)
    /// </summary>
    public void SetFavoriteSyncInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;  // 최소 1초
        if (seconds > 3600) seconds = 3600;  // 최대 1시간

        var oldInterval = _favoriteSyncIntervalSeconds;
        _favoriteSyncIntervalSeconds = seconds;

        Log4.Info($"[BackgroundSyncService] 즐겨찾기 동기화 주기 변경: {oldInterval}초 → {seconds}초");
        _logger.Information("즐겨찾기 동기화 주기 변경: {Old}초 → {New}초", oldInterval, seconds);

        // 주기 변경 시 현재 타이머 취소 (새 주기로 재시작하도록)
        _favoriteIntervalChangeCts?.Cancel();

        // 이벤트 발생
        FavoriteSyncIntervalChanged?.Invoke(seconds);
    }

    /// <summary>
    /// 전체 동기화 주기 설정 (초 단위)
    /// </summary>
    public void SetFullSyncInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;  // 최소 1초
        if (seconds > 3600) seconds = 3600;  // 최대 1시간

        var oldInterval = _fullSyncIntervalSeconds;
        _fullSyncIntervalSeconds = seconds;

        Log4.Info($"[BackgroundSyncService] 전체 동기화 주기 변경: {oldInterval}초 → {seconds}초");
        _logger.Information("전체 동기화 주기 변경: {Old}초 → {New}초", oldInterval, seconds);

        // 주기 변경 시 현재 타이머 취소 (새 주기로 재시작하도록)
        _fullIntervalChangeCts?.Cancel();

        // 이벤트 발생
        FullSyncIntervalChanged?.Invoke(seconds);
    }

    /// <summary>
    /// 캘린더 동기화 주기 설정 (초 단위)
    /// </summary>
    public void SetCalendarSyncInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;  // 최소 1초
        if (seconds > 3600) seconds = 3600;  // 최대 1시간

        var oldInterval = _calendarSyncIntervalSeconds;
        _calendarSyncIntervalSeconds = seconds;

        Log4.Info($"[BackgroundSyncService] 캘린더 동기화 주기 변경: {oldInterval}초 → {seconds}초");
        _logger.Information("캘린더 동기화 주기 변경: {Old}초 → {New}초", oldInterval, seconds);

        // 주기 변경 시 현재 타이머 취소 (새 주기로 재시작하도록)
        _calendarIntervalChangeCts?.Cancel();

        // 이벤트 발생
        CalendarSyncIntervalChanged?.Invoke(seconds);
    }

    /// <summary>
    /// 채팅 동기화 주기 설정 (초 단위)
    /// </summary>
    public void SetChatSyncInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;  // 최소 1초
        if (seconds > 3600) seconds = 3600;  // 최대 1시간

        var oldInterval = _chatSyncIntervalSeconds;
        _chatSyncIntervalSeconds = seconds;

        Log4.Info($"[BackgroundSyncService] 채팅 동기화 주기 변경: {oldInterval}초 → {seconds}초");
        _logger.Information("채팅 동기화 주기 변경: {Old}초 → {New}초", oldInterval, seconds);

        // 주기 변경 시 현재 타이머 취소 (새 주기로 재시작하도록)
        _chatIntervalChangeCts?.Cancel();

        // 이벤트 발생
        ChatSyncIntervalChanged?.Invoke(seconds);
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
    /// 보낸편지함 즉시 동기화 (메일 발송 후 호출용)
    /// </summary>
    public async Task SyncSentItemsAsync(string accountEmail, CancellationToken ct = default)
    {
        _logger.Information("보낸편지함 동기화 요청: {Email}", accountEmail);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MaiXDbContext>();
        var graphMailService = scope.ServiceProvider.GetRequiredService<GraphMailService>();

        // 보낸편지함 폴더 찾기
        var sentFolder = await dbContext.Folders
            .FirstOrDefaultAsync(f => f.AccountEmail == accountEmail &&
                (f.DisplayName == "보낸 편지함" ||
                 f.DisplayName.Equals("Sent Items", StringComparison.OrdinalIgnoreCase) ||
                 f.DisplayName.Equals("SentItems", StringComparison.OrdinalIgnoreCase)), ct);

        if (sentFolder == null)
        {
            _logger.Warning("보낸편지함 폴더를 찾을 수 없음: {Email}", accountEmail);
            return;
        }

        try
        {
            var (changed, deleted, savedEmails) = await SyncFolderAsync(
                dbContext, graphMailService, accountEmail, sentFolder, ct);

            if (changed > 0 || deleted > 0)
            {
                EmailsSynced?.Invoke(savedEmails.Count);
                RaiseMailSyncCompleted();
            }

            _logger.Information("보낸편지함 동기화 완료: 변경 {Changed}건, 삭제 {Deleted}건", changed, deleted);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "보낸편지함 동기화 실패");
        }
    }

    /// <summary>
    /// 특정 폴더 강제 새로고침 (deltaLink 초기화 후 전체 조회)
    /// </summary>
    public async Task ForceRefreshFolderAsync(string accountEmail, string folderId, CancellationToken ct = default)
    {
        _logger.Information("폴더 강제 새로고침 요청: {FolderId}", folderId);

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MaiXDbContext>();
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

        // 동기화 완료 이벤트 발생 — Debounce 적용
        RaiseMailSyncCompleted();

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
            var dbContext = scope.ServiceProvider.GetRequiredService<MaiXDbContext>();
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

            // 동기화 완료 이벤트 발생 (UI에서 동기화 시간 표시용) — Debounce 적용
            RaiseMailSyncCompleted();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "폴더 동기화 실패");
        }
    }

    /// <summary>
    /// 캘린더 일정 동기화 (Delta Query + DB 저장)
    /// Graph API에서 일정 변경분을 가져와 DB에 저장
    /// </summary>
    public async Task SyncCalendarAsync(CancellationToken ct = default)
    {
        _logger.Information("캘린더 동기화 시작");

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var graphAuthService = scope.ServiceProvider.GetRequiredService<GraphAuthService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<MaiXDbContext>();

            // 로그인 상태 확인
            if (!graphAuthService.IsLoggedIn || string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
            {
                _logger.Information("로그인된 계정 없음 - 캘린더 동기화 생략");
                return;
            }

            var accountEmail = graphAuthService.CurrentUserEmail;
            var calendarService = scope.ServiceProvider.GetRequiredService<GraphCalendarService>();

            // 동기화 시작 이벤트
            CalendarSyncStarted?.Invoke();

            // 진행 상태 알림: 1/4 - 동기화 상태 조회
            CalendarSyncProgress?.Invoke(1, 4, "동기화 상태 조회 중...");

            // CalendarSyncState 조회/생성
            var syncState = await dbContext.CalendarSyncStates
                .FirstOrDefaultAsync(s => s.AccountEmail == accountEmail && s.CalendarId == null, ct);

            if (syncState == null)
            {
                syncState = new CalendarSyncState
                {
                    AccountEmail = accountEmail,
                    CalendarId = null,  // 기본 캘린더
                    SyncStartDate = DateTime.Today.AddMonths(-3),
                    SyncEndDate = DateTime.Today.AddMonths(6),
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.CalendarSyncStates.Add(syncState);
                await dbContext.SaveChangesAsync(ct);
            }

            // 진행 상태 알림: 2/4 - Delta Query 실행
            CalendarSyncProgress?.Invoke(2, 4, "일정 변경분 조회 중...");

            // Delta Query로 변경분 조회
            CalendarDeltaResult deltaResult;
            try
            {
                deltaResult = await calendarService.GetEventsDeltaAsync(
                    syncState.DeltaLink,
                    syncState.SyncStartDate,
                    syncState.SyncEndDate);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Delta Query 실패, 전체 동기화로 폴백");
                syncState.DeltaLink = null;  // Delta 링크 초기화
                deltaResult = await calendarService.GetEventsDeltaAsync(
                    null,
                    syncState.SyncStartDate,
                    syncState.SyncEndDate);
            }

            // 진행 상태 알림: 3/4 - DB 저장
            CalendarSyncProgress?.Invoke(3, 4, "일정 DB 저장 중...");

            int addedCount = 0;
            int updatedCount = 0;
            int deletedCount = 0;

            // 삭제된 이벤트 처리
            if (deltaResult.DeletedEventIds.Count > 0)
            {
                foreach (var deletedId in deltaResult.DeletedEventIds)
                {
                    var existingEvent = await dbContext.CalendarEvents
                        .FirstOrDefaultAsync(e => e.GraphId == deletedId && e.AccountEmail == accountEmail, ct);

                    if (existingEvent != null)
                    {
                        existingEvent.IsDeleted = true;
                        existingEvent.DeletedAt = DateTime.UtcNow;
                        deletedCount++;
                        _logger.Debug("캘린더 이벤트 삭제: {Subject}", existingEvent.Subject);
                    }
                }
            }

            // 추가/수정된 이벤트 처리
            foreach (var graphEvent in deltaResult.Events)
            {
                if (string.IsNullOrEmpty(graphEvent.Id))
                    continue;

                var existingEvent = await dbContext.CalendarEvents
                    .FirstOrDefaultAsync(e => e.GraphId == graphEvent.Id && e.AccountEmail == accountEmail, ct);

                if (existingEvent != null)
                {
                    // 기존 이벤트 업데이트
                    UpdateCalendarEventFromGraph(existingEvent, graphEvent);
                    existingEvent.SyncedAt = DateTime.UtcNow;
                    updatedCount++;
                    _logger.Debug("캘린더 이벤트 업데이트: {Subject}", existingEvent.Subject);
                }
                else
                {
                    // 새 이벤트 추가
                    var newEvent = calendarService.ConvertToCalendarEvent(graphEvent, accountEmail);
                    dbContext.CalendarEvents.Add(newEvent);
                    addedCount++;
                    _logger.Debug("캘린더 이벤트 추가: {Subject}", newEvent.Subject);
                }
            }

            // DB 저장
            await dbContext.SaveChangesAsync(ct);

            // 동기화 상태 업데이트
            syncState.DeltaLink = deltaResult.DeltaLink;
            syncState.LastSyncedAt = DateTime.UtcNow;
            syncState.LastSyncAddedCount = addedCount;
            syncState.LastSyncUpdatedCount = updatedCount;
            syncState.LastSyncDeletedCount = deletedCount;
            syncState.UpdatedAt = DateTime.UtcNow;
            syncState.LastErrorMessage = null;  // 성공 시 오류 메시지 초기화
            await dbContext.SaveChangesAsync(ct);

            // 진행 상태 알림: 4/4 - 동기화 완료
            CalendarSyncProgress?.Invoke(4, 4, "캘린더 동기화 완료");

            var totalCount = addedCount + updatedCount;
            _logger.Information("캘린더 동기화 완료: 추가 {Added}건, 수정 {Updated}건, 삭제 {Deleted}건",
                addedCount, updatedCount, deletedCount);

            // 동기화 완료 이벤트 (기존 호환용)
            CalendarSynced?.Invoke(totalCount);

            // 새 이벤트: 상세 동기화 결과
            CalendarEventsSynced?.Invoke(addedCount, updatedCount, deletedCount);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "캘린더 동기화 실패");
            CalendarSyncProgress?.Invoke(0, 0, "캘린더 동기화 실패");

            // 동기화 상태에 오류 기록
            try
            {
                using var errorScope = _serviceProvider.CreateScope();
                var errorDbContext = errorScope.ServiceProvider.GetRequiredService<MaiXDbContext>();
                var graphAuthService = errorScope.ServiceProvider.GetRequiredService<GraphAuthService>();

                if (graphAuthService.IsLoggedIn && !string.IsNullOrEmpty(graphAuthService.CurrentUserEmail))
                {
                    var syncState = await errorDbContext.CalendarSyncStates
                        .FirstOrDefaultAsync(s => s.AccountEmail == graphAuthService.CurrentUserEmail && s.CalendarId == null);

                    if (syncState != null)
                    {
                        syncState.LastErrorMessage = ex.Message;
                        syncState.LastErrorAt = DateTime.UtcNow;
                        await errorDbContext.SaveChangesAsync();
                    }
                }
            }
            catch
            {
                // 오류 기록 실패는 무시
            }
        }
    }

    /// <summary>
    /// Graph API Event에서 CalendarEvent 업데이트
    /// </summary>
    private void UpdateCalendarEventFromGraph(CalendarEvent existingEvent, Microsoft.Graph.Models.Event graphEvent)
    {
        existingEvent.Subject = graphEvent.Subject ?? existingEvent.Subject;
        existingEvent.Body = graphEvent.Body?.Content;
        existingEvent.BodyContentType = graphEvent.Body?.ContentType?.ToString();
        existingEvent.Location = graphEvent.Location?.DisplayName;

        // Graph API 시간대 변환: Graph API는 지정된 TimeZone의 로컬 시간을 반환
        // 이를 시스템 로컬 시간으로 변환해야 함
        if (graphEvent.Start?.DateTime != null && DateTime.TryParse(graphEvent.Start.DateTime, out var startDt))
        {
            existingEvent.StartDateTime = ConvertGraphTimeToLocal(startDt, graphEvent.Start.TimeZone);
        }
        if (graphEvent.End?.DateTime != null && DateTime.TryParse(graphEvent.End.DateTime, out var endDt))
        {
            existingEvent.EndDateTime = ConvertGraphTimeToLocal(endDt, graphEvent.End.TimeZone);
        }

        existingEvent.StartTimeZone = graphEvent.Start?.TimeZone;
        existingEvent.EndTimeZone = graphEvent.End?.TimeZone;
        existingEvent.IsAllDay = graphEvent.IsAllDay ?? existingEvent.IsAllDay;
        existingEvent.IsRecurring = graphEvent.Recurrence != null;
        existingEvent.ShowAs = graphEvent.ShowAs?.ToString();
        existingEvent.ResponseStatus = graphEvent.ResponseStatus?.Response?.ToString();
        existingEvent.Importance = graphEvent.Importance?.ToString();
        existingEvent.Sensitivity = graphEvent.Sensitivity?.ToString();
        existingEvent.IsOnlineMeeting = graphEvent.IsOnlineMeeting ?? existingEvent.IsOnlineMeeting;
        existingEvent.OnlineMeetingUrl = graphEvent.OnlineMeeting?.JoinUrl;
        existingEvent.OnlineMeetingProvider = graphEvent.OnlineMeetingProvider?.ToString();
        existingEvent.ReminderMinutesBeforeStart = graphEvent.ReminderMinutesBeforeStart ?? existingEvent.ReminderMinutesBeforeStart;
        existingEvent.IsReminderOn = graphEvent.IsReminderOn ?? existingEvent.IsReminderOn;
        existingEvent.OrganizerEmail = graphEvent.Organizer?.EmailAddress?.Address;
        existingEvent.OrganizerName = graphEvent.Organizer?.EmailAddress?.Name;
        existingEvent.WebLink = graphEvent.WebLink;
        existingEvent.LastModifiedDateTime = graphEvent.LastModifiedDateTime?.UtcDateTime;
        existingEvent.IsCancelled = graphEvent.IsCancelled ?? existingEvent.IsCancelled;
        existingEvent.EventType = graphEvent.Type?.ToString();

        // 반복 패턴 업데이트
        if (graphEvent.Recurrence != null)
        {
            existingEvent.RecurrencePattern = System.Text.Json.JsonSerializer.Serialize(graphEvent.Recurrence.Pattern);
            existingEvent.RecurrenceRange = System.Text.Json.JsonSerializer.Serialize(graphEvent.Recurrence.Range);
        }

        // 참석자 업데이트
        if (graphEvent.Attendees?.Any() == true)
        {
            var attendeesList = graphEvent.Attendees.Select(a => new
            {
                email = a.EmailAddress?.Address,
                name = a.EmailAddress?.Name,
                type = a.Type?.ToString(),
                status = a.Status?.Response?.ToString()
            });
            existingEvent.Attendees = System.Text.Json.JsonSerializer.Serialize(attendeesList);
        }

        // 카테고리 업데이트
        if (graphEvent.Categories?.Any() == true)
        {
            existingEvent.Categories = System.Text.Json.JsonSerializer.Serialize(graphEvent.Categories);
        }

        // 삭제 상태 복원 (재활성화된 이벤트)
        if (existingEvent.IsDeleted)
        {
            existingEvent.IsDeleted = false;
            existingEvent.DeletedAt = null;
        }
    }

    /// <summary>
    /// Graph API 시간을 로컬 시간으로 변환
    /// Graph API는 지정된 TimeZone의 로컬 시간을 반환하므로,
    /// 해당 TimeZone에서 시스템 로컬 시간으로 변환 필요
    /// </summary>
    /// <param name="dateTime">Graph API에서 파싱한 DateTime (Kind=Unspecified)</param>
    /// <param name="timeZoneId">Graph API의 TimeZone ID (예: "Korea Standard Time", "UTC")</param>
    /// <returns>시스템 로컬 시간</returns>
    private DateTime ConvertGraphTimeToLocal(DateTime dateTime, string? timeZoneId)
    {
        try
        {
            // TimeZone이 없으면 UTC로 가정
            if (string.IsNullOrEmpty(timeZoneId))
            {
                // UTC로 지정 후 로컬 시간으로 변환
                var utcTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                return utcTime.ToLocalTime();
            }

            // TimeZoneInfo 가져오기
            TimeZoneInfo sourceTimeZone;
            try
            {
                sourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Windows 시간대 ID가 아닌 경우 (IANA 형식 등)
                // UTC로 폴백
                _logger.Warning("알 수 없는 시간대: {TimeZone}, UTC로 처리", timeZoneId);
                var utcTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                return utcTime.ToLocalTime();
            }

            // 소스 시간대의 시간을 UTC로 변환 후 로컬로 변환
            var sourceTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(sourceTime, sourceTimeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, TimeZoneInfo.Local);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "시간대 변환 실패: {DateTime}, {TimeZone}", dateTime, timeZoneId);
            return dateTime; // 변환 실패 시 원본 반환
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
            IsSyncing = _isSyncing == 1,
            IsPaused = _isPaused,
            LastSyncTime = _lastSyncTime,
            SyncCount = _syncCount,
            ErrorCount = _errorCount,
            NextSyncTime = _lastSyncTime.AddSeconds(_syncIntervalSeconds)
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
