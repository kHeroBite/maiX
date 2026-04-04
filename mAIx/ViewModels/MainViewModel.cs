using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.Graph;
using mAIx.Services.Search;
using mAIx.Services.Sync;
using mAIx.Utils;

namespace mAIx.ViewModels;

/// <summary>
/// 메인 화면 ViewModel - 폴더/이메일 목록 관리
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly mAIxDbContext _dbContext;
    private readonly BackgroundSyncService _syncService;
    private readonly GraphMailService _graphMailService;
    private CancellationTokenSource? _loadEmailsCts;

    /// <summary>
    /// 캘린더 ViewModel (외부에서 설정, 캘린더 동기화 이벤트 연동용)
    /// </summary>
    public CalendarViewModel? CalendarViewModel { get; set; }

    public MainViewModel(mAIxDbContext dbContext, BackgroundSyncService syncService, GraphMailService graphMailService)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[MainViewModel] 생성자 시작");
            _dbContext = dbContext;
            _syncService = syncService;
            _graphMailService = graphMailService;

            Log4.Info($"[MainViewModel] 생성됨, syncService 해시코드: {syncService.GetHashCode()}");

        // 동기화 상태 변경 이벤트 구독
        _syncService.PausedChanged += OnSyncPausedChanged;

        // 폴더/메일 동기화 완료 이벤트 구독
        _syncService.FoldersSynced += OnFoldersSynced;
        _syncService.EmailsSynced += OnEmailsSynced;

        // 메일 동기화 진행률 이벤트 구독
        _syncService.MailSyncStarted += OnMailSyncStarted;
        _syncService.MailSyncProgress += OnMailSyncProgress;
        _syncService.MailSyncCompleted += OnMailSyncCompleted;

        Log4.Info("[MainViewModel] MailSyncCompleted 이벤트 구독 완료");

        // 캘린더 동기화 이벤트 구독
        _syncService.CalendarSyncStarted += OnCalendarSyncStarted;
        _syncService.CalendarSyncProgress += OnCalendarSyncProgress;
        _syncService.CalendarSynced += OnCalendarSynced;

        // 역방향 동기화 이벤트 구독
        _syncService.HistoricalSyncProgress += OnHistoricalSyncProgress;
        _syncService.HistoricalSyncCompleted += OnHistoricalSyncCompleted;

        // 초기 상태 동기화
        _isSyncPaused = _syncService.IsPaused;
        UpdateSyncStatus();
        }
        catch (Exception ex)
        {
            Log4.Error($"[MainViewModel] 생성자 예외: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 메일 동기화 시작 이벤트 핸들러
    /// </summary>
    private void OnMailSyncStarted(int total)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ShowSyncProgress(total);
        });
    }

    /// <summary>
    /// 메일 동기화 진행 이벤트 핸들러
    /// </summary>
    private void OnMailSyncProgress(int completed)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            UpdateSyncProgress(completed);
        });
    }

    /// <summary>
    /// 메일 동기화 완료 이벤트 핸들러
    /// </summary>
    private void OnMailSyncCompleted()
    {
        Log4.Info("[MainViewModel] OnMailSyncCompleted 이벤트 수신");

        var app = System.Windows.Application.Current;
        if (app == null)
        {
            Log4.Warn("[MainViewModel] Application.Current is null");
            return;
        }

        app.Dispatcher.InvokeAsync(async () =>
        {
            Log4.Info("[MainViewModel] Dispatcher에서 읽음 상태 갱신 시작");
            HideSyncProgressAfterDelay();
            UpdateSyncStatus();

            // 현재 표시 중인 메일 목록의 읽음 상태 갱신
            await RefreshEmailReadStatusAsync();

            // 읽음 상태 변경 여부와 무관하게 폴더 카운트 항상 갱신
            // (다른 앱에서 메일을 읽은 경우 UI 메일 목록 변경 없어도 카운트 반영 필요)
            await RefreshFolderUnreadCountsAsync();
        });
    }

    private void OnHistoricalSyncProgress(int fetched)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsHistoricalSyncRunning = true;
            HistoricalSyncStatusText = $"이전 메일 동기화 중... {fetched}건";
        });
    }

    private void OnHistoricalSyncCompleted()
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsHistoricalSyncRunning = false;
            HistoricalSyncStatusText = "";
        });
    }

    [RelayCommand]
    private void TriggerHistoricalSync()
    {
        _syncService.TriggerHistoricalSync();
        IsHistoricalSyncRunning = true;
        HistoricalSyncStatusText = "이전 메일 동기화 시작...";
    }

    /// <summary>
    /// 현재 표시 중인 메일 목록의 읽음 상태만 DB에서 다시 로드
    /// MainWindow에서 직접 호출 가능하도록 public으로 변경
    /// </summary>
    public async Task RefreshEmailReadStatusAsync()
    {
        if (Emails == null || Emails.Count == 0) return;

        try
        {
            // 현재 표시 중인 메일의 EntryId 목록 (ID보다 EntryId가 더 안정적인 식별자)
            var entryIds = Emails
                .Where(e => !string.IsNullOrEmpty(e.EntryId))
                .Select(e => e.EntryId)
                .ToList();

            // DB에서 메일 상태 조회 (AsNoTracking으로 캐시 무시하고 최신 값 조회)
            var dbEmails = await _dbContext.Emails
                .AsNoTracking()
                .Where(e => e.EntryId != null && entryIds.Contains(e.EntryId))
                .Select(e => new { e.EntryId, e.IsRead, e.FlagStatus, e.IsPinned, e.Subject })
                .ToListAsync();

            var dbEntryIds = dbEmails.Select(e => e.EntryId).ToHashSet();
            var dbEmailStatus = dbEmails.ToDictionary(e => e.EntryId!, e => new { e.IsRead, e.FlagStatus, e.IsPinned });

            // DB에서 삭제된 메일 찾기 (UI에는 있지만 DB에는 없는 메일)
            var deletedEmails = Emails
                .Where(e => !string.IsNullOrEmpty(e.EntryId) && !dbEntryIds.Contains(e.EntryId))
                .ToList();

            // UI 메일 목록에서 삭제된 메일 제거
            int deletedCount = 0;
            if (deletedEmails.Count > 0)
            {
                foreach (var deleted in deletedEmails)
                {
                    Log4.Debug($"[RefreshEmailReadStatus] 삭제된 메일 UI에서 제거: {deleted.Subject}");
                }
                Emails = Emails.Where(e => string.IsNullOrEmpty(e.EntryId) || dbEntryIds.Contains(e.EntryId)).ToList();
                deletedCount = deletedEmails.Count;
                Log4.Info($"[RefreshEmailReadStatus] UI에서 삭제된 메일 {deletedCount}건 제거됨");
            }

            // 메일 상태 업데이트 (읽음, 플래그, 핀)
            int updatedCount = 0;
            for (int i = 0; i < Emails.Count; i++)
            {
                var email = Emails[i];
                if (!string.IsNullOrEmpty(email.EntryId) &&
                    dbEmailStatus.TryGetValue(email.EntryId, out var status))
                {
                    bool changed = false;

                    // 읽음 상태 비교
                    if (email.IsRead != status.IsRead)
                    {
                        Log4.Debug($"[RefreshEmailStatus] 읽음 상태 변경: {email.Subject} (UI={email.IsRead} → DB={status.IsRead})");
                        email.IsRead = status.IsRead;
                        changed = true;
                    }

                    // 플래그 상태 비교
                    if (email.FlagStatus != status.FlagStatus)
                    {
                        Log4.Debug($"[RefreshEmailStatus] 플래그 상태 변경: {email.Subject} (UI={email.FlagStatus} → DB={status.FlagStatus})");
                        email.FlagStatus = status.FlagStatus;
                        changed = true;
                    }

                    // 핀 상태 비교
                    if (email.IsPinned != status.IsPinned)
                    {
                        Log4.Debug($"[RefreshEmailStatus] 핀 상태 변경: {email.Subject} (UI={email.IsPinned} → DB={status.IsPinned})");
                        email.IsPinned = status.IsPinned;
                        changed = true;
                    }

                    if (changed) updatedCount++;
                }
            }

            // 변경이 있으면 UI 갱신
            if (updatedCount > 0 || deletedCount > 0)
            {
                Log4.Info($"[RefreshEmailReadStatus] UI 갱신: 읽음상태 변경 {updatedCount}건, 삭제 {deletedCount}건");

                // 개별 항목의 INPC가 이미 UI를 갱신하므로 view.Refresh() 불필요
                // view.Refresh()는 전체 리스트를 다시 렌더링하여 번쩍임 유발

                // 폴더의 안읽은 메일 수도 함께 갱신
                await RefreshFolderUnreadCountsAsync();
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"[MainViewModel] 메일 읽음 상태 갱신 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 폴더의 안읽은 메일 수를 DB에서 다시 계산하여 갱신
    /// </summary>
    private async Task RefreshFolderUnreadCountsAsync()
    {
        try
        {
            // DB에서 폴더별 안읽은 메일 수 계산
            var unreadCounts = await _dbContext.Emails
                .AsNoTracking()
                .Where(e => !e.IsRead)
                .GroupBy(e => e.ParentFolderId)
                .Select(g => new { FolderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.FolderId ?? "", x => x.Count);

            // 폴더 목록 갱신
            foreach (var folder in Folders)
            {
                folder.UnreadItemCount = unreadCounts.TryGetValue(folder.Id, out var count) ? count : 0;
            }

            // 즐겨찾기 폴더 목록도 갱신
            foreach (var folder in FavoriteFolders)
            {
                folder.UnreadItemCount = unreadCounts.TryGetValue(folder.Id, out var count) ? count : 0;
            }

            // UI 갱신을 위해 컬렉션을 새로 할당 (Folder가 INotifyPropertyChanged 미구현)
            Folders = new List<Folder>(Folders);
            FavoriteFolders = new ObservableCollection<Folder>(FavoriteFolders);

            Log4.Debug("[RefreshFolderUnreadCounts] 폴더 안읽은 메일 수 갱신 완료");
        }
        catch (Exception ex)
        {
            Log4.Warn($"[RefreshFolderUnreadCounts] 폴더 갱신 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 캘린더 동기화 시작 이벤트 핸들러
    /// </summary>
    private void OnCalendarSyncStarted()
    {
        IsCalendarSyncing = true;
        CalendarSyncCompleted = 0;
        CalendarSyncTotal = 0;
        CalendarSyncStatusText = "일정 동기화 시작...";
    }

    /// <summary>
    /// 캘린더 동기화 진행 이벤트 핸들러
    /// </summary>
    private void OnCalendarSyncProgress(int current, int total, string stepName)
    {
        CalendarSyncCompleted = current;
        CalendarSyncTotal = total;
        CalendarSyncStatusText = stepName;
    }

    /// <summary>
    /// 캘린더 동기화 완료 이벤트 핸들러
    /// </summary>
    private void OnCalendarSynced(int eventCount)
    {
        IsCalendarSyncing = false;
        LastCalendarEventCount = eventCount;
        CalendarSyncStatusText = eventCount > 0
            ? $"일정 {eventCount}건 동기화됨"
            : "일정 변경 없음";
        
        // 캘린더 동기화 시간 업데이트
        LastCalendarSyncTime = DateTime.UtcNow;

        // 변경 있을 때만 캘린더 뷰 새로고침 이벤트 발생
        if (eventCount > 0)
            CalendarDataUpdated?.Invoke();
    }

    /// <summary>
    /// 캘린더 데이터 업데이트 이벤트 (UI 새로고침용)
    /// </summary>
    public event Action? CalendarDataUpdated;

    /// <summary>
    /// 즐겨찾기 폴더 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Folder> _favoriteFolders = new();

    /// <summary>
    /// 폴더 트리 구조 (루트 폴더들)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Folder> _folderTree = new();

    /// <summary>
    /// 애플리케이션 타이틀
    /// </summary>
    [ObservableProperty]
    private string _title = "mAIx";

    /// <summary>
    /// 상태 메시지
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "준비";

    /// <summary>
    /// 폴더 목록
    /// </summary>
    [ObservableProperty]
    private List<Folder> _folders = new();

    /// <summary>
    /// 선택된 폴더
    /// </summary>
    [ObservableProperty]
    private Folder? _selectedFolder;

    /// <summary>
    /// 이메일 목록
    /// </summary>
    [ObservableProperty]
    private List<Email> _emails = new();

    /// <summary>
    /// 이메일 수 (상태바 표시용)
    /// </summary>
    public int EmailCount => Emails?.Count ?? 0;

    /// <summary>
    /// Emails 변경 시 EmailCount와 StatusBarCountText도 갱신
    /// </summary>
    partial void OnEmailsChanged(List<Email> value)
    {
        OnPropertyChanged(nameof(EmailCount));
        OnPropertyChanged(nameof(StatusBarCountText));
    }

    /// <summary>
    /// <summary>
    /// 페이지네이션: 현재까지 로드된 이메일 수 (Skip 오프셋)
    /// </summary>
    private int _emailSkip;

    /// <summary>
    /// 추가 이메일 로딩 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingMore;

    /// <summary>
    /// 더 로드할 이메일이 있는지 여부
    /// </summary>
    [ObservableProperty]
    private bool _hasMoreEmails;

    /// <summary>
    /// 선택된 이메일
    /// </summary>
    [ObservableProperty]
    private Email? _selectedEmail;

    /// <summary>
    /// 현재 선택된 메일 건수 (다중 선택 액션바 표시 조건)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultipleEmailsSelected))]
    private int _selectedEmailCount;

    /// <summary>
    /// 2건 이상 선택 여부 (BulkActionBar 표시 트리거)
    /// </summary>
    public bool IsMultipleEmailsSelected => _selectedEmailCount >= 2;

    #region 임시보관함 편집 모드 프로퍼티

    /// <summary>
    /// 임시보관함 메일 편집 모드 여부
    /// </summary>
    [ObservableProperty]
    private bool _isEditingDraft;

    /// <summary>
    /// 임시보관함 편집 - 받는 사람
    /// </summary>
    [ObservableProperty]
    private string _draftTo = "";

    /// <summary>
    /// 임시보관함 편집 - 참조
    /// </summary>
    [ObservableProperty]
    private string _draftCc = "";

    /// <summary>
    /// 임시보관함 편집 - 숨은 참조
    /// </summary>
    [ObservableProperty]
    private string _draftBcc = "";

    /// <summary>
    /// 임시보관함 편집 - 제목
    /// </summary>
    [ObservableProperty]
    private string _draftSubject = "";

    /// <summary>
    /// 임시보관함 편집 - 본문
    /// </summary>
    [ObservableProperty]
    private string _draftBody = "";

    /// <summary>
    /// 편집 중인 임시보관함 메일 ID
    /// </summary>
    private string? _editingDraftMessageId;

    #endregion

    /// <summary>
    /// 동기화 일시정지 상태
    /// </summary>
    [ObservableProperty]
    private bool _isSyncPaused;

    /// <summary>
    /// 동기화 진행 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isSyncing;

    /// <summary>
    /// 마지막 동기화 시간
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastSyncTime;

    /// <summary>
    /// 동기화 상태 텍스트
    /// </summary>
    [ObservableProperty]
    private string _syncStatusText = "대기 중";

    /// <summary>
    /// 동기화 버튼 아이콘 (▶ 또는 ■)
    /// </summary>
    public string SyncButtonIcon => IsSyncPaused ? "▶" : "■";

    /// <summary>
    /// 동기화 버튼 툴팁
    /// </summary>
    public string SyncButtonTooltip => IsSyncPaused ? "동기화 시작" : "동기화 일시정지";

    /// <summary>
    /// 마지막 동기화 시간 표시 텍스트
    /// </summary>
    /// <summary>
    /// 마지막 메일 동기화 시간 표시 텍스트
    /// </summary>
    public string LastMailSyncTimeText => LastSyncTime.HasValue
        ? $"마지막 메일 동기화: {LastSyncTime.Value.ToLocalTime():HH:mm:ss}"
        : "메일 동기화 기록 없음";

    /// <summary>
    /// 마지막 캘린더 동기화 시간
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastCalendarSyncTime;

    /// <summary>
    /// 마지막 캘린더 동기화 시간 표시 텍스트
    /// </summary>
    public string LastCalendarSyncTimeText => LastCalendarSyncTime.HasValue
        ? $"마지막 캘린더 동기화: {LastCalendarSyncTime.Value.ToLocalTime():HH:mm:ss}"
        : "캘린더 동기화 기록 없음";

    /// <summary>
    /// 캘린더 뷰 활성화 여부
    /// </summary>
    [ObservableProperty]
    private bool _isCalendarViewActive;

    /// <summary>
    /// 현재 탭에 맞는 동기화 시간 텍스트
    /// </summary>
    public string CurrentSyncTimeText => IsCalendarViewActive
        ? LastCalendarSyncTimeText
        : LastMailSyncTimeText;

    partial void OnIsCalendarViewActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentSyncTimeText));
    }

    partial void OnLastCalendarSyncTimeChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(LastCalendarSyncTimeText));
        OnPropertyChanged(nameof(CurrentSyncTimeText));
    }

    #region 메일 동기화 진행상황

    /// <summary>
    /// 메일 동기화 완료 건수
    /// </summary>
    [ObservableProperty]
    private int _mailSyncCompleted;

    /// <summary>
    /// 메일 동기화 총 건수
    /// </summary>
    [ObservableProperty]
    private int _mailSyncTotal;

    /// <summary>
    /// 메일 동기화 진행률 표시 여부
    /// </summary>
    [ObservableProperty]
    private bool _isMailSyncProgressVisible;

    /// <summary>
    /// 진행률 숨김 타이머
    /// </summary>
    private System.Timers.Timer? _syncProgressHideTimer;

    /// <summary>
    /// 메일 동기화 진행률 (0-100)
    /// </summary>
    public double MailSyncProgress => MailSyncTotal > 0 ? (double)MailSyncCompleted / MailSyncTotal * 100 : 0;

    /// <summary>
    /// 메일 동기화 진행률 텍스트
    /// </summary>
    public string MailSyncProgressText => MailSyncTotal > 0
        ? $"{MailSyncCompleted}/{MailSyncTotal} ({MailSyncProgress:F0}%)"
        : "";

    partial void OnMailSyncCompletedChanged(int value)
    {
        OnPropertyChanged(nameof(MailSyncProgress));
        OnPropertyChanged(nameof(MailSyncProgressText));
    }

    partial void OnMailSyncTotalChanged(int value)
    {
        OnPropertyChanged(nameof(MailSyncProgress));
        OnPropertyChanged(nameof(MailSyncProgressText));
    }

    /// <summary>
    /// 동기화 진행률 표시 시작
    /// </summary>
    public void ShowSyncProgress(int total)
    {
        // 타이머 중지
        _syncProgressHideTimer?.Stop();

        MailSyncCompleted = 0;
        MailSyncTotal = total;
        IsMailSyncProgressVisible = true;
    }

    /// <summary>
    /// 동기화 진행률 업데이트
    /// </summary>
    public void UpdateSyncProgress(int completed)
    {
        MailSyncCompleted = completed;
    }

    /// <summary>
    /// 동기화 완료 후 3초 뒤 숨김
    /// </summary>
    public void HideSyncProgressAfterDelay()
    {
        _syncProgressHideTimer?.Stop();
        _syncProgressHideTimer = new System.Timers.Timer(3000);
        _syncProgressHideTimer.Elapsed += (s, e) =>
        {
            _syncProgressHideTimer?.Stop();
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                IsMailSyncProgressVisible = false;
                MailSyncCompleted = 0;
                MailSyncTotal = 0;
            });
        };
        _syncProgressHideTimer.AutoReset = false;
        _syncProgressHideTimer.Start();
    }

    #endregion

    #region AI 동기화

    /// <summary>
    /// AI 동기화 일시정지 여부
    /// </summary>
    [ObservableProperty]
    private bool _isAISyncPaused = true;

    /// <summary>
    /// AI 동기화 진행 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isAISyncing;

    /// <summary>
    /// AI 동기화 상태 텍스트
    /// </summary>
    [ObservableProperty]
    private string _aiSyncStatusText = "일시정지됨";

    /// <summary>
    /// AI 동기화 버튼 아이콘 (🤖 = AI 활성, 🚫 = AI 일시정지)
    /// </summary>
    public string AISyncButtonIcon => IsAISyncPaused ? "🤖" : "🤖";

    /// <summary>
    /// AI 동기화 버튼 툴팁
    /// </summary>
    public string AISyncButtonTooltip => IsAISyncPaused ? "AI 분석 시작" : "AI 분석 일시정지";

    /// <summary>
    /// IsAISyncPaused 변경 시 관련 프로퍼티 알림
    /// </summary>
    partial void OnIsAISyncPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(AISyncButtonIcon));
        OnPropertyChanged(nameof(AISyncButtonTooltip));
        _aiSyncStatusText = value ? "일시정지됨" : "분석 중";
        OnPropertyChanged(nameof(AiSyncStatusText));
    }

    /// <summary>
    /// AI 분석 완료 건수
    /// </summary>
    [ObservableProperty]
    private int _aiAnalysisCompleted;

    /// <summary>
    /// AI 분석 총 건수
    /// </summary>
    [ObservableProperty]
    private int _aiAnalysisTotal;

    /// <summary>
    /// AI 분석 진행률 (0-100)
    /// </summary>
    public double AiAnalysisProgress => AiAnalysisTotal > 0 ? (double)AiAnalysisCompleted / AiAnalysisTotal * 100 : 0;

    /// <summary>
    /// AI 분석 진행률 텍스트
    /// </summary>
    public string AiAnalysisProgressText => AiAnalysisTotal > 0
        ? $"AI: {AiAnalysisCompleted}/{AiAnalysisTotal} ({AiAnalysisProgress:F0}%)"
        : "AI: 대기 중";

    partial void OnAiAnalysisCompletedChanged(int value)
    {
        OnPropertyChanged(nameof(AiAnalysisProgress));
        OnPropertyChanged(nameof(AiAnalysisProgressText));
    }

    partial void OnAiAnalysisTotalChanged(int value)
    {
        OnPropertyChanged(nameof(AiAnalysisProgress));
        OnPropertyChanged(nameof(AiAnalysisProgressText));
    }

    #endregion

    #region 캘린더 동기화

    /// <summary>
    /// 캘린더 동기화 진행 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isCalendarSyncing;

    /// <summary>
    /// 캘린더 동기화 완료 건수
    /// </summary>
    [ObservableProperty]
    private int _calendarSyncCompleted;

    /// <summary>
    /// 캘린더 동기화 총 건수
    /// </summary>
    [ObservableProperty]
    private int _calendarSyncTotal;

    /// <summary>
    /// 캘린더 동기화 상태 텍스트
    /// </summary>
    [ObservableProperty]
    private string _calendarSyncStatusText = "일정: 대기 중";

    /// <summary>역방향 동기화 진행 중 여부</summary>
    [ObservableProperty]
    private bool _isHistoricalSyncRunning;

    /// <summary>역방향 동기화 진행 텍스트</summary>
    [ObservableProperty]
    private string _historicalSyncStatusText = "";

    /// <summary>
    /// 캘린더 동기화 진행률 (0-100)
    /// </summary>
    public double CalendarSyncProgress => CalendarSyncTotal > 0 ? (double)CalendarSyncCompleted / CalendarSyncTotal * 100 : 0;

    /// <summary>
    /// 캘린더 동기화 진행률 텍스트
    /// </summary>
    public string CalendarSyncProgressText => IsCalendarSyncing
        ? (CalendarSyncTotal > 0
            ? $"일정: {CalendarSyncCompleted}/{CalendarSyncTotal}"
            : "일정: 동기화 중...")
        : CalendarSyncStatusText;

    /// <summary>
    /// 마지막 캘린더 동기화 일정 수
    /// </summary>
    [ObservableProperty]
    private int _lastCalendarEventCount;

    /// <summary>
    /// 캘린더 모드 여부
    /// </summary>
    [ObservableProperty]
    private bool _isCalendarMode;

    /// <summary>
    /// 이번 달 일정 수 (캘린더 모드에서 표시)
    /// </summary>
    [ObservableProperty]
    private int _currentMonthEventCount;

    /// <summary>
    /// 상태바 카운트 텍스트 (메일 모드/캘린더 모드 분기)
    /// </summary>
    public string StatusBarCountText => IsCalendarMode
        ? $" | 이번달 {CurrentMonthEventCount}개 일정"
        : $" | {EmailCount}개 메일";

    partial void OnIsCalendarModeChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusBarCountText));
    }

    partial void OnCurrentMonthEventCountChanged(int value)
    {
        OnPropertyChanged(nameof(StatusBarCountText));
    }

    partial void OnCalendarSyncCompletedChanged(int value)
    {
        OnPropertyChanged(nameof(CalendarSyncProgress));
        OnPropertyChanged(nameof(CalendarSyncProgressText));
    }

    partial void OnCalendarSyncTotalChanged(int value)
    {
        OnPropertyChanged(nameof(CalendarSyncProgress));
        OnPropertyChanged(nameof(CalendarSyncProgressText));
    }

    partial void OnIsCalendarSyncingChanged(bool value)
    {
        OnPropertyChanged(nameof(CalendarSyncProgressText));
    }

    partial void OnCalendarSyncStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(CalendarSyncProgressText));
    }

    #endregion

    #region 동기화 기간 설정

    /// <summary>
    /// 메일 동기화 기간 설정
    /// </summary>
    [ObservableProperty]
    private Models.Settings.SyncPeriodSettings _mailSyncPeriodSettings = Models.Settings.SyncPeriodSettings.Default;

    /// <summary>
    /// AI 분석 기간 설정
    /// </summary>
    [ObservableProperty]
    private Models.Settings.SyncPeriodSettings _aiAnalysisPeriodSettings = Models.Settings.SyncPeriodSettings.Default;

    /// <summary>
    /// 메일 동기화 기간 표시 텍스트
    /// </summary>
    public string MailSyncPeriodText => MailSyncPeriodSettings?.ToDisplayString() ?? "설정 없음";

    /// <summary>
    /// AI 분석 기간 표시 텍스트
    /// </summary>
    public string AiAnalysisPeriodText => AiAnalysisPeriodSettings?.ToDisplayString() ?? "설정 없음";

    partial void OnMailSyncPeriodSettingsChanged(Models.Settings.SyncPeriodSettings value)
    {
        OnPropertyChanged(nameof(MailSyncPeriodText));
    }

    partial void OnAiAnalysisPeriodSettingsChanged(Models.Settings.SyncPeriodSettings value)
    {
        OnPropertyChanged(nameof(AiAnalysisPeriodText));
    }

    /// <summary>
    /// 서명 설정
    /// </summary>
    [ObservableProperty]
    private Models.Settings.SignatureSettings? _signatureSettings = new();

    /// <summary>
    /// 동기화 기간 설정 업데이트
    /// </summary>
    public void UpdateSyncPeriodSettings(Models.Settings.SyncPeriodSettings mailSettings, Models.Settings.SyncPeriodSettings aiSettings)
    {
        MailSyncPeriodSettings = mailSettings;
        AiAnalysisPeriodSettings = aiSettings;
        Log4.Info($"동기화 기간 설정 업데이트 - 메일: {mailSettings.ToDisplayString()}, AI: {aiSettings.ToDisplayString()}");
    }

    #endregion

    /// <summary>
    /// 동기화 상태 변경 이벤트 핸들러
    /// </summary>
    private void OnSyncPausedChanged(bool isPaused)
    {
        IsSyncPaused = isPaused;
    }

    /// <summary>
    /// 폴더 동기화 완료 이벤트 핸들러
    /// </summary>
    private async void OnFoldersSynced()
    {
        await LoadFoldersAsync();
        UpdateSyncStatus();
    }

    /// <summary>
    /// 메일 동기화 완료 이벤트 핸들러
    /// </summary>
    private async void OnEmailsSynced(int newCount)
    {
        // 새 메일이 있을 때만 목록 전체 갱신 (번쩍임 방지)
        if (newCount > 0 && SelectedFolder != null)
        {
            var selectedEmailId = SelectedEmail?.Id;
            await LoadEmailsAsync();
            if (selectedEmailId.HasValue && SelectedEmail?.Id != selectedEmailId)
            {
                SelectedEmail = Emails?.FirstOrDefault(e => e.Id == selectedEmailId.Value);
            }
            await LoadFoldersAsync();
            StatusMessage = $"{newCount}개 새 메일 동기화됨";
        }
        else
        {
            // 새 메일 없으면 읽음 상태만 갱신 (OnMailSyncCompleted에서 처리)
            // 폴더 카운�� 갱신
            await RefreshFolderUnreadCountsAsync();
        }

        UpdateSyncStatus();
    }

    /// <summary>
    /// IsSyncPaused 변경 시 관련 프로퍼티 알림
    /// </summary>
    partial void OnIsSyncPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(SyncButtonIcon));
        OnPropertyChanged(nameof(SyncButtonTooltip));
        UpdateSyncStatus();
    }

    /// <summary>
    /// 마지막 동기화 시간 변경 시 텍스트 업데이트
    /// </summary>
    partial void OnLastSyncTimeChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(LastMailSyncTimeText));
        OnPropertyChanged(nameof(CurrentSyncTimeText));
    }

    /// <summary>
    /// 동기화 상태 업데이트
    /// </summary>
    private void UpdateSyncStatus()
    {
        var status = _syncService.GetStatus();
        IsSyncing = status.IsSyncing;
        LastSyncTime = status.LastSyncTime == DateTime.MinValue ? null : status.LastSyncTime;

        if (status.IsSyncing)
        {
            SyncStatusText = "동기화 중...";
        }
        else if (status.IsPaused)
        {
            SyncStatusText = "일시정지";
        }
        else
        {
            SyncStatusText = "대기 중";
        }
    }

    /// <summary>
    /// 선택된 폴더 변경 시 이메일 목록 자동 로드
    /// </summary>
    /// <param name="value">새로 선택된 폴더</param>
    partial void OnSelectedFolderChanged(Folder? value)
    {
        _loadEmailsCts?.Cancel();
        _loadEmailsCts?.Dispose();
        _loadEmailsCts = new CancellationTokenSource();
        if (value != null)
        {
            _ = LoadEmailsAsync(_loadEmailsCts.Token);
        }
        else
        {
            Emails = new List<Email>();
        }
    }

    /// <summary>
    /// 폴더 목록 로드 (트리 구조 구성)
    /// </summary>
    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        await ExecuteAsync(async () =>
        {
            StatusMessage = "폴더 로딩 중...";

            // 모든 폴더를 DB에서 로드
            Folders = await _dbContext.Folders
                .OrderBy(f => f.DisplayName)
                .ToListAsync();

            // 각 폴더의 UnreadItemCount를 Email 테이블에서 실시간 계산
            var unreadCounts = await _dbContext.Emails
                .Where(e => !e.IsRead)
                .GroupBy(e => e.ParentFolderId)
                .Select(g => new { FolderId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.FolderId ?? "", x => x.Count);

            foreach (var folder in Folders)
            {
                folder.UnreadItemCount = unreadCounts.TryGetValue(folder.Id, out var count) ? count : 0;
            }

            // 트리 구조 구성
            BuildFolderTree();

            // 즐겨찾기 폴더 로드
            LoadFavoriteFolders();

            // 받은편지함 자동 선택 (선택된 폴더가 없는 경우)
            // 시스템 Inbox("받은 편지함") 우선, 커스텀 폴더("#받은편지함") 제외
            if (SelectedFolder == null && Folders.Count > 0)
            {
                var inbox = Folders.FirstOrDefault(f =>
                    f.DisplayName == "받은 편지함" ||
                    f.DisplayName.Equals("Inbox", StringComparison.OrdinalIgnoreCase));

                SelectedFolder = inbox ?? Folders.First();
            }

            StatusMessage = $"{Folders.Count}개 폴더 로드됨";

            // 초기 로딩 시 동기화 상태 업데이트 (서비스에서 이미 동기화 완료된 경우 반영)
            UpdateSyncStatus();
        }, "폴더 로드 실패");
    }

    /// <summary>
    /// 폴더 트리 구조 구성
    /// </summary>
    private void BuildFolderTree()
    {
        var folderDict = Folders.ToDictionary(f => f.Id);
        var rootFolders = new List<Folder>();

        foreach (var folder in Folders)
        {
            folder.Children.Clear();
            folder.Depth = 0;
        }

        foreach (var folder in Folders)
        {
            if (string.IsNullOrEmpty(folder.ParentFolderId) ||
                !folderDict.ContainsKey(folder.ParentFolderId))
            {
                // 루트 폴더 (상위 폴더가 없거나, 상위 폴더가 DB에 없는 경우)
                rootFolders.Add(folder);
            }
            else
            {
                // 하위 폴더
                var parent = folderDict[folder.ParentFolderId];
                parent.Children.Add(folder);
                folder.Depth = parent.Depth + 1;
            }
        }

        // 깊이 재계산 (재귀)
        foreach (var root in rootFolders)
        {
            SetFolderDepth(root, 0);
        }

        // 정렬: 이름순
        rootFolders = rootFolders.OrderBy(f => f.DisplayName).ToList();

        FolderTree = new ObservableCollection<Folder>(rootFolders);
    }

    /// <summary>
    /// 폴더 깊이 재귀 설정
    /// </summary>
    private void SetFolderDepth(Folder folder, int depth)
    {
        folder.Depth = depth;
        foreach (var child in folder.Children.OrderBy(c => c.DisplayName))
        {
            SetFolderDepth(child, depth + 1);
        }
    }

    /// <summary>
    /// 선택된 폴더의 이메일 목록 로드 ([RelayCommand] 진입점 — CancellationToken 없는 오버로드)
    /// </summary>
    [RelayCommand]
    private Task LoadEmailsAsync() => LoadEmailsAsync(default);

    /// <summary>
    /// 선택된 폴더의 이메일 목록 로드 (CancellationToken 지원 — Race Condition 방지)
    /// </summary>
    private async Task LoadEmailsAsync(CancellationToken cancellationToken)
    {
        if (SelectedFolder == null)
        {
            Emails = new List<Email>();
            return;
        }

        await ExecuteAsync(async () =>
        {
            StatusMessage = "이메일 로딩 중...";

            // AsNoTracking()으로 캐시된 엔티티가 아닌 DB에서 최신 데이터 로드
            var query = _dbContext.Emails
                .AsNoTracking()
                .Where(e => e.ParentFolderId == SelectedFolder.Id);

            // 스누즈 필터: ShowSnoozedEmails=false이면 스누즈 중인 메일 숨김
            if (!ShowSnoozedEmails)
            {
                var now = DateTime.UtcNow;
                query = query.Where(e => e.SnoozedUntil == null || e.SnoozedUntil <= now);
            }

            var emails = await query
                .OrderByDescending(e => e.ReceivedDateTime)
                .Take(App.Settings.UserPreferences.InitialMailCount)
                .ToListAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // 페이지네이션 상태 초기화
            _emailSkip = emails.Count;
            HasMoreEmails = emails.Count >= App.Settings.UserPreferences.InitialMailCount;

            // 임시보관함인 경우 IsDraft 플래그 설정
            bool isDraftsFolder = IsDraftsFolder(SelectedFolder);
            if (isDraftsFolder)
            {
                foreach (var email in emails)
                {
                    email.IsDraft = true;
                }
            }

            Emails = emails;
            StatusMessage = HasMoreEmails
                ? $"{Emails.Count}개 이메일 로드됨 (더 있음)"
                : $"{Emails.Count}개 이메일 로드됨";
        }, "이메일 로드 실패");
    }

    /// <summary>
    /// 스크롤 끝 도달 시 추가 이메일 로드 (인피니티 스크롤)
    /// </summary>
    public async Task LoadMoreEmailsAsync()
    {
        if (IsLoadingMore || !HasMoreEmails || SelectedFolder == null)
            return;

        IsLoadingMore = true;
        try
        {
            var query = _dbContext.Emails
                .AsNoTracking()
                .Where(e => e.ParentFolderId == SelectedFolder.Id);

            if (!ShowSnoozedEmails)
            {
                var now = DateTime.UtcNow;
                query = query.Where(e => e.SnoozedUntil == null || e.SnoozedUntil <= now);
            }

            var moreEmails = await query
                .OrderByDescending(e => e.ReceivedDateTime)
                .Skip(_emailSkip)
                .Take(App.Settings.UserPreferences.InitialMailCount)
                .ToListAsync();

            if (moreEmails.Count > 0)
            {
                bool isDraftsFolder = IsDraftsFolder(SelectedFolder);
                if (isDraftsFolder)
                {
                    foreach (var email in moreEmails)
                    {
                        email.IsDraft = true;
                    }
                }

                var combined = new List<Email>(Emails);
                combined.AddRange(moreEmails);
                _emailSkip += moreEmails.Count;
                HasMoreEmails = moreEmails.Count >= App.Settings.UserPreferences.InitialMailCount;
                Emails = combined;
                StatusMessage = HasMoreEmails
                    ? $"{Emails.Count}개 이메일 로드됨 (더 있음)"
                    : $"{Emails.Count}개 이메일 로드됨";
            }
            else
            {
                HasMoreEmails = false;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[LoadMoreEmailsAsync] 추가 이메일 로드 실패: {ex.Message}");
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    /// <summary>
    /// 동기화 일시정지/재개 토글
    /// </summary>
    [RelayCommand]
    private void ToggleSync()
    {
        _syncService.TogglePause();
        StatusMessage = IsSyncPaused ? "동기화 재개됨" : "동기화 일시정지됨";
    }

    /// <summary>
    /// 동기화 일시정지
    /// </summary>
    [RelayCommand]
    private void PauseSync()
    {
        if (!IsSyncPaused)
        {
            _syncService.TogglePause();
            UpdateSyncStatus();
            StatusMessage = "동기화 일시정지됨";
        }
    }

    /// <summary>
    /// 동기화 재개
    /// </summary>
    [RelayCommand]
    private void ResumeSync()
    {
        if (IsSyncPaused)
        {
            _syncService.TogglePause();
            UpdateSyncStatus();
            StatusMessage = "동기화 재개됨";
        }
    }

    /// <summary>
    /// AI 동기화 일시정지
    /// </summary>
    [RelayCommand]
    private void PauseAISync()
    {
        if (!IsAISyncPaused)
        {
            IsAISyncPaused = true;
            StatusMessage = "AI 분석 일시정지됨";
        }
    }

    /// <summary>
    /// AI 동기화 재개
    /// </summary>
    [RelayCommand]
    private void ResumeAISync()
    {
        if (IsAISyncPaused)
        {
            IsAISyncPaused = false;
            StatusMessage = "AI 분석 시작됨";
        }
    }

    /// <summary>
    /// AI 동기화 토글
    /// </summary>
    [RelayCommand]
    private void ToggleAISync()
    {
        IsAISyncPaused = !IsAISyncPaused;
        StatusMessage = IsAISyncPaused ? "AI 분석 일시정지됨" : "AI 분석 시작됨";
    }

    /// <summary>
    /// 현재 동기화 주기 (초) - 하위 호환용
    /// </summary>
    public int SyncIntervalSeconds => _syncService.SyncIntervalSeconds;

    /// <summary>
    /// 동기화 주기 설정 - 하위 호환용
    /// </summary>
    public void SetSyncInterval(int seconds)
    {
        _syncService.SetSyncInterval(seconds);
    }

    /// <summary>
    /// 즐겨찾기 동기화 주기 (초)
    /// </summary>
    public int FavoriteSyncIntervalSeconds => _syncService.FavoriteSyncIntervalSeconds;

    /// <summary>
    /// 전체 동기화 주기 (초)
    /// </summary>
    public int FullSyncIntervalSeconds => _syncService.FullSyncIntervalSeconds;

    /// <summary>
    /// 즐겨찾기 동기화 주기 설정
    /// </summary>
    public void SetFavoriteSyncInterval(int seconds)
    {
        _syncService.SetFavoriteSyncInterval(seconds);
    }

    /// <summary>
    /// 전체 동기화 주기 설정
    /// </summary>
    public void SetFullSyncInterval(int seconds)
    {
        _syncService.SetFullSyncInterval(seconds);
    }

    /// <summary>
    /// 캘린더 동기화 주기 (초)
    /// </summary>
    public int CalendarSyncIntervalSeconds => _syncService.CalendarSyncIntervalSeconds;

    /// <summary>
    /// 캘린더 동기화 주기 설정
    /// </summary>
    public void SetCalendarSyncInterval(int seconds)
    {
        _syncService.SetCalendarSyncInterval(seconds);
    }

    /// <summary>
    /// 채팅 동기화 주기 (초)
    /// </summary>
    public int ChatSyncIntervalSeconds => _syncService.ChatSyncIntervalSeconds;

    /// <summary>
    /// 채팅 동기화 주기 설정
    /// </summary>
    public void SetChatSyncInterval(int seconds)
    {
        _syncService.SetChatSyncInterval(seconds);
    }

    /// <summary>
    /// 현재 AI 분석 주기 (초) - 하위 호환용
    /// </summary>
    private int _aiAnalysisIntervalSeconds = 300;  // 기본값: 5분
    public int AIAnalysisIntervalSeconds => _aiAnalysisIntervalSeconds;

    /// <summary>
    /// AI 분석 주기 설정 - 하위 호환용
    /// </summary>
    public void SetAIAnalysisInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;
        if (seconds > 3600) seconds = 3600;
        _aiAnalysisIntervalSeconds = seconds;
        Log4.Info($"AI 분석 주기 변경: {seconds}초");
    }

    /// <summary>
    /// 즐겨찾기 AI 분석 주기 (초)
    /// </summary>
    private int _favoriteAnalysisIntervalSeconds = 30;  // 기본값: 30초
    public int FavoriteAnalysisIntervalSeconds => _favoriteAnalysisIntervalSeconds;

    /// <summary>
    /// 전체 AI 분석 주기 (초)
    /// </summary>
    private int _fullAnalysisIntervalSeconds = 300;  // 기본값: 5분
    public int FullAnalysisIntervalSeconds => _fullAnalysisIntervalSeconds;

    /// <summary>
    /// 즐겨찾기 AI 분석 주기 설정
    /// </summary>
    public void SetFavoriteAnalysisInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;
        if (seconds > 3600) seconds = 3600;
        _favoriteAnalysisIntervalSeconds = seconds;
        Log4.Info($"즐겨찾기 AI 분석 주기 변경: {seconds}초");
    }

    /// <summary>
    /// 전체 AI 분석 주기 설정
    /// </summary>
    public void SetFullAnalysisInterval(int seconds)
    {
        if (seconds < 1) seconds = 1;
        if (seconds > 3600) seconds = 3600;
        _fullAnalysisIntervalSeconds = seconds;
        Log4.Info($"전체 AI 분석 주기 변경: {seconds}초");
    }

    /// <summary>
    /// 메일 목록 새로고침
    /// </summary>
    [RelayCommand]
    private async Task RefreshMailsAsync()
    {
        if (SelectedFolder == null)
        {
            StatusMessage = "폴더를 선택해주세요";
            return;
        }

        // 확인창 표시
        var result = MessageBox.Show(
            "전체 새로고침을 실행하면 선택된 폴더의 모든 메일을 다시 가져옵니다.\n" +
            "이 작업은 시간이 걸릴 수 있습니다.\n\n" +
            "계속하시겠습니까?",
            "전체 새로고침",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await ExecuteAsync(async () =>
        {
            StatusMessage = "전체 새로고침 중...";
            Log4.Info($"전체 새로고침 시작: {SelectedFolder.DisplayName}");
            
            await _syncService.ForceRefreshFolderAsync(
                SelectedFolder.AccountEmail,
                SelectedFolder.Id);
            await LoadEmailsAsync();
            
            UpdateSyncStatus();
            StatusMessage = "전체 새로고침 완료";
            Log4.Info($"전체 새로고침 완료: {SelectedFolder.DisplayName}");
        }, "전체 새로고침 실패");
    }

    /// <summary>
    /// 폴더 즐겨찾기 토글
    /// </summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync(Folder folder)
    {
        if (folder == null) return;

        await ExecuteAsync(async () =>
        {
            // DB에서 폴더 찾기
            var dbFolder = await _dbContext.Folders.FindAsync(folder.Id);
            if (dbFolder == null) return;

            // 즐겨찾기 상태 토글
            dbFolder.IsFavorite = !dbFolder.IsFavorite;
            folder.IsFavorite = dbFolder.IsFavorite;

            await _dbContext.SaveChangesAsync();

            // 즐겨찾기 목록 갱신
            LoadFavoriteFolders();

            StatusMessage = folder.IsFavorite
                ? $"'{folder.DisplayName}' 즐겨찾기에 추가됨"
                : $"'{folder.DisplayName}' 즐겨찾기에서 제거됨";
        }, "즐겨찾기 변경 실패");
    }

    /// <summary>
    /// 즐겨찾기 폴더 목록 로드 (FavoriteOrder 순서로 정렬)
    /// </summary>
    private void LoadFavoriteFolders()
    {
        var favorites = Folders.Where(f => f.IsFavorite)
            .OrderBy(f => f.FavoriteOrder)
            .ThenBy(f => f.DisplayName)
            .ToList();
        FavoriteFolders = new ObservableCollection<Folder>(favorites);
    }

    /// <summary>
    /// 즐겨찾기 순서 변경 (드래그&드롭)
    /// </summary>
    /// <param name="sourceFolder">이동할 폴더</param>
    /// <param name="targetFolder">드롭 위치의 폴더</param>
    public async void MoveFavoriteOrder(Folder sourceFolder, Folder targetFolder)
    {
        var favorites = FavoriteFolders.ToList();
        var sourceIndex = favorites.IndexOf(sourceFolder);
        var targetIndex = favorites.IndexOf(targetFolder);

        if (sourceIndex < 0 || targetIndex < 0)
            return;

        // 리스트에서 순서 변경
        favorites.RemoveAt(sourceIndex);
        favorites.Insert(targetIndex, sourceFolder);

        // FavoriteOrder 재설정
        for (int i = 0; i < favorites.Count; i++)
        {
            favorites[i].FavoriteOrder = i;
        }

        // DB 저장
        await _dbContext.SaveChangesAsync();

        // UI 갱신
        FavoriteFolders = new ObservableCollection<Folder>(favorites);

        Log4.Info($"즐겨찾기 순서 변경: '{sourceFolder.DisplayName}' → {targetIndex + 1}번째");
    }

    /// <summary>
    /// 선택된 메일 삭제 (휴지통으로 이동)
    /// </summary>
    [RelayCommand]
    private async Task DeleteEmailAsync(Email? email)
    {
        Log4.Info($"=== DeleteEmailAsync 호출됨 === email: {email?.Subject ?? "null"}, EntryId: {email?.EntryId ?? "null"}");

        if (email == null || string.IsNullOrEmpty(email.EntryId))
        {
            Log4.Warn($"삭제 실패 - email null: {email == null}, EntryId empty: {string.IsNullOrEmpty(email?.EntryId)}");
            StatusMessage = "삭제할 메일을 선택해주세요.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            StatusMessage = "메일 삭제 중...";
            Log4.Debug("ExecuteAsync 내부 시작");

            try
            {
                // 휴지통(DeletedItems) 폴더 ID 찾기
                var deletedItemsFolder = await _dbContext.Folders
                    .FirstOrDefaultAsync(f => f.DisplayName == "지운 편지함" ||
                                              f.DisplayName.ToLower() == "deleted items" ||
                                              f.DisplayName.ToLower() == "deleteditems");

                if (deletedItemsFolder != null && !string.IsNullOrEmpty(deletedItemsFolder.Id))
                {
                    // Graph API로 휴지통으로 이동 (새 메시지 ID 반환)
                    var movedMessage = await _graphMailService.MoveMessageAsync(email.EntryId, deletedItemsFolder.Id);

                    // 실행취소를 위해 새로운 EntryId로 업데이트 (휴지통에서의 ID)
                    if (movedMessage != null && !string.IsNullOrEmpty(movedMessage.Id))
                    {
                        email.EntryId = movedMessage.Id;
                        Log4.Debug($"메일 EntryId 업데이트: {movedMessage.Id}");
                    }

                    Log4.Info($"메일 휴지통으로 이동: {email.Subject}");
                }
                else
                {
                    // 휴지통을 찾지 못한 경우 영구 삭제
                    await _graphMailService.DeleteMessageAsync(email.EntryId);
                    Log4.Info($"메일 영구 삭제: {email.Subject}");
                }

                // 로컬 DB에서 삭제
                var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                if (dbEmail != null)
                {
                    _dbContext.Emails.Remove(dbEmail);
                    await _dbContext.SaveChangesAsync();
                }

                // 메일 목록 갱신
                Emails = Emails.Where(e => e.Id != email.Id).ToList();

                // 선택 해제
                if (SelectedEmail?.Id == email.Id)
                {
                    SelectedEmail = null;
                }

                StatusMessage = "메일이 삭제되었습니다.";
            }
            catch (Exception ex)
            {
                Log4.Error($"메일 삭제 실패: {ex.Message}");
                StatusMessage = $"메일 삭제 실패: {ex.Message}";
            }
        }, "메일 삭제 실패");
    }

    /// <summary>
    /// 여러 메일 일괄 삭제 (휴지통으로 이동)
    /// </summary>
    public async Task DeleteEmailsAsync(List<Email> emails)
    {
        if (emails == null || emails.Count == 0) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"메일 삭제 중... (0/{emails.Count})";

            var deletedItemsFolder = await _dbContext.Folders
                .FirstOrDefaultAsync(f => f.DisplayName == "지운 편지함" ||
                                          f.DisplayName.ToLower() == "deleted items" ||
                                          f.DisplayName.ToLower() == "deleteditems");

            // Graph API 병렬 처리 (세마포어 8)
            var semaphore = new SemaphoreSlim(8, 8);
            var succeededEmails = new ConcurrentBag<Email>();
            int apiFailed = 0;

            await Task.WhenAll(emails.Where(e => !string.IsNullOrEmpty(e.EntryId)).Select(async email =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (deletedItemsFolder != null && !string.IsNullOrEmpty(deletedItemsFolder.Id))
                        await _graphMailService.MoveMessageAsync(email.EntryId, deletedItemsFolder.Id);
                    else
                        await _graphMailService.DeleteMessageAsync(email.EntryId);
                    succeededEmails.Add(email);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref apiFailed);
                    Log4.Error($"메일 일괄 삭제 실패 [{email.Subject}]: {ex.Message}");
                }
                finally { semaphore.Release(); }
            }));

            // 성공한 메일만 DB에서 제거 후 1회 SaveChanges
            foreach (var email in succeededEmails)
            {
                var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                if (dbEmail != null) _dbContext.Emails.Remove(dbEmail);
            }
            await _dbContext.SaveChangesAsync();

            var deletedIds = succeededEmails.Select(e => e.Id).ToHashSet();
            Emails = Emails.Where(e => !deletedIds.Contains(e.Id)).ToList();

            if (SelectedEmail != null && deletedIds.Contains(SelectedEmail.Id))
                SelectedEmail = null;

            StatusMessage = $"메일 {succeededEmails.Count}건 삭제 완료";
            Log4.Info($"일괄 삭제 완료: {succeededEmails.Count}건");
        }, "메일 일괄 삭제 실패");
    }

    /// <summary>
    /// 삭제된 메일 복원 (휴지통에서 원래 폴더로 이동)
    /// </summary>
    /// <param name="email">복원할 메일</param>
    /// <param name="originalFolderId">원래 폴더 ID</param>
    public async Task RestoreDeletedEmailAsync(Email email, string originalFolderId)
    {
        if (email == null || string.IsNullOrEmpty(email.EntryId))
        {
            Log4.Warn("복원할 메일이 없거나 EntryId가 없습니다.");
            return;
        }

        if (string.IsNullOrEmpty(originalFolderId))
        {
            Log4.Warn("원래 폴더 ID가 없습니다.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            StatusMessage = "메일 복원 중...";

            try
            {
                // Graph API로 원래 폴더로 이동 (새 메시지 ID 반환)
                var restoredMessage = await _graphMailService.MoveMessageAsync(email.EntryId, originalFolderId);
                Log4.Info($"메일 복원 완료: {email.Subject} → 폴더 {originalFolderId}");

                // 새로운 EntryId로 업데이트
                if (restoredMessage != null && !string.IsNullOrEmpty(restoredMessage.Id))
                {
                    email.EntryId = restoredMessage.Id;
                }

                // 로컬 DB에 다시 추가 (새 레코드로)
                email.ParentFolderId = originalFolderId;
                email.Id = 0; // EF Core가 새 레코드로 인식하도록 ID 초기화
                _dbContext.Emails.Add(email);
                await _dbContext.SaveChangesAsync();

                // 현재 선택된 폴더가 원래 폴더면 메일 목록에 추가
                if (SelectedFolder?.Id == originalFolderId)
                {
                    var updatedList = new List<Email>(Emails) { email };
                    // 날짜순 정렬 유지
                    updatedList = updatedList.OrderByDescending(e => e.ReceivedDateTime).ToList();
                    Emails = updatedList;
                }

                StatusMessage = "메일이 복원되었습니다.";
            }
            catch (Exception ex)
            {
                Log4.Error($"메일 복원 실패: {ex.Message}");
                StatusMessage = $"메일 복원 실패: {ex.Message}";
                throw;
            }
        }, "메일 복원 실패");
    }

    /// <summary>
    /// 이동된 메일들 복원 (원래 폴더로 되돌리기)
    /// </summary>
    public async Task RestoreMovedEmailsAsync(List<Email> emails, Dictionary<int, string> originalFolderIds)
    {
        if (emails == null || emails.Count == 0 || originalFolderIds == null) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"이동 취소 중... (0/{emails.Count})";
            int completed = 0;

            foreach (var email in emails)
            {
                if (string.IsNullOrEmpty(email.EntryId)) continue;
                if (!originalFolderIds.TryGetValue(email.Id, out var originalFolderId)) continue;

                try
                {
                    // Graph API로 원래 폴더로 이동
                    var movedMessage = await _graphMailService.MoveMessageAsync(email.EntryId, originalFolderId);

                    // 로컬 DB 업데이트
                    var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                    if (dbEmail != null)
                    {
                        if (movedMessage != null && !string.IsNullOrEmpty(movedMessage.Id))
                        {
                            dbEmail.EntryId = movedMessage.Id;
                            email.EntryId = movedMessage.Id;
                        }
                        dbEmail.ParentFolderId = originalFolderId;
                        await _dbContext.SaveChangesAsync();
                    }

                    completed++;
                    StatusMessage = $"이동 취소 중... ({completed}/{emails.Count})";
                }
                catch (Exception ex)
                {
                    Log4.Error($"메일 이동 취소 실패 [{email.Subject}]: {ex.Message}");
                }
            }

            // 메일 목록 새로고침
            await LoadEmailsAsync();
            StatusMessage = $"이동 취소 완료: {completed}건";

        }, "메일 이동 취소 실패");
    }

    /// <summary>
    /// 선택된 메일들의 플래그 상태 업데이트
    /// </summary>
    /// <param name="emails">대상 메일 목록</param>
    /// <param name="flagStatus">플래그 상태 (flagged, complete, notFlagged)</param>
    public async Task UpdateFlagStatusAsync(List<Email> emails, string flagStatus)
    {
        if (emails == null || emails.Count == 0) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"플래그 업데이트 중... (0/{emails.Count})";

            var semaphore = new SemaphoreSlim(8, 8);
            var succeededEmails = new ConcurrentBag<Email>();
            int failed = 0;

            await Task.WhenAll(emails.Where(e => !string.IsNullOrEmpty(e.EntryId)).Select(async email =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await _graphMailService.UpdateMessageFlagAsync(email.EntryId, flagStatus);
                    succeededEmails.Add(email);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Log4.Error($"플래그 업데이트 실패 [{email.Subject}]: {ex.Message}");
                }
                finally { semaphore.Release(); }
            }));

            // DB 일괄 업데이트 (ExecuteUpdateAsync — SaveChanges 불필요)
            var succeededIds = succeededEmails.Select(e => e.Id).ToHashSet();
            if (succeededIds.Count > 0)
            {
                await _dbContext.Emails
                    .Where(e => succeededIds.Contains(e.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.FlagStatus, flagStatus));
            }

            // 메모리 객체 갱신 (INPC가 UI 자동 갱신)
            foreach (var email in succeededEmails)
                email.FlagStatus = flagStatus;

            // DataTemplate Trigger가 FlagStatus로 분기하는 경우를 위해 1회 재할당
            Emails = new List<Email>(Emails);

            // 선택된 메일의 플래그가 변경된 경우 상세 패널도 갱신
            if (SelectedEmail != null && emails.Any(e => e.Id == SelectedEmail.Id))
            {
                OnPropertyChanged(nameof(SelectedEmail));
            }

            StatusMessage = failed > 0
                ? $"플래그 업데이트 완료 ({succeededEmails.Count}건 성공, {failed}건 실패)"
                : $"플래그 업데이트 완료 ({succeededEmails.Count}건)";
            Log4.Info($"플래그 '{flagStatus}' 업데이트 완료: {succeededEmails.Count}건");
        }, "플래그 업데이트 실패");
    }

    /// <summary>
    /// 선택된 메일들의 읽음 상태 업데이트
    /// </summary>
    /// <param name="emails">대상 메일 목록</param>
    /// <param name="isRead">읽음 여부</param>
    public async Task UpdateReadStatusAsync(List<Email> emails, bool isRead)
    {
        if (emails == null || emails.Count == 0) return;

        await ExecuteAsync(async () =>
        {
            string statusText = isRead ? "읽음" : "읽지 않음";
            StatusMessage = $"{statusText} 표시 중... (0/{emails.Count})";

            var semaphore = new SemaphoreSlim(8, 8);
            var succeededEmails = new ConcurrentBag<Email>();
            int failed = 0;

            await Task.WhenAll(emails.Where(e => !string.IsNullOrEmpty(e.EntryId)).Select(async email =>
            {
                await semaphore.WaitAsync();
                try
                {
                    await _graphMailService.UpdateMessageReadStatusAsync(email.EntryId, isRead);
                    succeededEmails.Add(email);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Log4.Error($"{statusText} 표시 실패 [{email.Subject}]: {ex.Message}");
                }
                finally { semaphore.Release(); }
            }));

            // DB 일괄 업데이트 (ExecuteUpdateAsync — SaveChanges 불필요)
            var succeededIds = succeededEmails.Select(e => e.Id).ToHashSet();
            if (succeededIds.Count > 0)
            {
                await _dbContext.Emails
                    .Where(e => succeededIds.Contains(e.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsRead, isRead));
            }

            // 메모리 객체 갱신 (INPC가 UI 자동 갱신)
            foreach (var email in succeededEmails)
                email.IsRead = isRead;

            // 폴더별 안읽은 메일 수 업데이트 (성공한 메일만 카운트)
            var folderChanges = succeededEmails
                .Where(e => !string.IsNullOrEmpty(e.ParentFolderId))
                .GroupBy(e => e.ParentFolderId)
                .ToList();

            foreach (var group in folderChanges)
            {
                var folder = Folders.FirstOrDefault(f => f.Id == group.Key);
                if (folder != null)
                {
                    // 읽음으로 표시 → 안읽은 개수 감소, 읽지않음으로 표시 → 안읽은 개수 증가
                    int delta = isRead ? -group.Count() : group.Count();
                    folder.UnreadItemCount = Math.Max(0, folder.UnreadItemCount + delta); // INPC가 자동으로 UI 갱신
                    Log4.Debug($"폴더 안읽은 메일 수 업데이트: {folder.DisplayName} = {folder.UnreadItemCount} (delta: {delta})");
                }
            }

            // 즐겨찾기 폴더 목록 갱신 (폴더 카운트 변경 시)
            if (folderChanges.Count > 0)
                LoadFavoriteFolders();

            StatusMessage = failed > 0
                ? $"{statusText} 표시 완료 ({succeededEmails.Count}건 성공, {failed}건 실패)"
                : $"{statusText} 표시 완료 ({succeededEmails.Count}건)";
            Log4.Info($"{statusText} 표시 완료: {succeededEmails.Count}건");
        }, $"{(isRead ? "읽음" : "읽지 않음")} 표시 실패");
    }

    /// <summary>
    /// 선택된 메일들의 카테고리 업데이트
    /// </summary>
    /// <param name="emails">대상 메일 목록</param>
    /// <param name="categories">카테고리 목록</param>
    public async Task UpdateCategoriesAsync(List<Email> emails, List<string> categories)
    {
        if (emails == null || emails.Count == 0) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"카테고리 업데이트 중... (0/{emails.Count})";
            int completed = 0;

            foreach (var email in emails)
            {
                if (string.IsNullOrEmpty(email.EntryId)) continue;

                try
                {
                    // Graph API로 카테고리 업데이트
                    await _graphMailService.UpdateMessageCategoriesAsync(email.EntryId, categories);

                    // 로컬 DB 업데이트
                    var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                    if (dbEmail != null)
                    {
                        dbEmail.Categories = categories?.Count > 0
                            ? System.Text.Json.JsonSerializer.Serialize(categories)
                            : null;
                        await _dbContext.SaveChangesAsync();
                    }

                    // 메모리 상 데이터 업데이트
                    email.Categories = categories?.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(categories)
                        : null;

                    completed++;
                    StatusMessage = $"카테고리 업데이트 중... ({completed}/{emails.Count})";
                }
                catch (Exception ex)
                {
                    Log4.Error($"카테고리 업데이트 실패 [{email.Subject}]: {ex.Message}");
                }
            }

            // UI 갱신을 위해 목록 다시 설정
            Emails = new List<Email>(Emails);

            StatusMessage = $"카테고리 업데이트 완료 ({completed}/{emails.Count})";
            Log4.Info($"카테고리 업데이트 완료: {completed}건");
        }, "카테고리 업데이트 실패");
    }

    /// <summary>
    /// 삭제 가능 여부 (선택된 메일이 있을 때)
    /// </summary>
    public bool CanDeleteEmail => SelectedEmail != null;

    /// <summary>
    /// SelectedEmail 변경 시 CanDeleteEmail도 갱신 및 자동 읽음 처리
    /// </summary>
    partial void OnSelectedEmailChanged(Email? value)
    {
        OnPropertyChanged(nameof(CanDeleteEmail));

        // 메일 선택 시 읽지 않은 메일이면 자동으로 읽음 처리
        if (value != null && !value.IsRead)
        {
            _ = MarkAsReadOnSelectAsync(value);
        }
    }

    /// <summary>
    /// 메일 선택 시 자동 읽음 처리 (Outlook 실시간 동기화)
    /// </summary>
    private async Task MarkAsReadOnSelectAsync(Email email)
    {
        if (string.IsNullOrEmpty(email.EntryId)) return;

        try
        {
            // Graph API로 읽음 상태 업데이트 (Outlook 실시간 동기화)
            await _graphMailService.UpdateMessageReadStatusAsync(email.EntryId, true);

            // 로컬 DB 업데이트
            var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
            if (dbEmail != null)
            {
                dbEmail.IsRead = true;
                await _dbContext.SaveChangesAsync();
            }

            // 메모리 상 데이터 업데이트 (INPC가 자동으로 UI 갱신)
            email.IsRead = true;

            // 폴더 안읽은 메일 수 즉시 업데이트 (빠른 메모리 계산)
            if (!string.IsNullOrEmpty(email.ParentFolderId))
            {
                var folder = Folders.FirstOrDefault(f => f.Id == email.ParentFolderId);
                if (folder != null)
                {
                    folder.UnreadItemCount = Math.Max(0, folder.UnreadItemCount - 1); // INPC가 자동으로 UI 갱신
                    LoadFavoriteFolders(); // 즐겨찾기 폴더 목록도 갱신
                    Log4.Debug($"폴더 안읽은 메일 수 업데이트: {folder.DisplayName} = {folder.UnreadItemCount} (delta: -1)");
                }
            }

            Log4.Debug($"메일 자동 읽음 처리: {email.Subject}");
        }
        catch (Exception ex)
        {
            Log4.Error($"자동 읽음 처리 실패: {ex.Message}");
        }
    }

    #region 스누즈 메일 필터

    /// <summary>
    /// 스누즈 중인 메일 표시 여부 (false=숨김, true=표시)
    /// </summary>
    [ObservableProperty]
    private bool _showSnoozedEmails;

    /// <summary>
    /// 스누즈 메일 표시 토글
    /// </summary>
    [RelayCommand]
    private async Task ToggleShowSnoozedEmails()
    {
        ShowSnoozedEmails = !ShowSnoozedEmails;
        if (IsSearchMode)
            await SearchAsync();
        else
            await LoadEmailsAsync();
        StatusMessage = ShowSnoozedEmails ? "스누즈 메일 포함 표시" : "스누즈 메일 숨김";
    }

    #endregion

    #region Phase 1: 검색 기능

    /// <summary>
    /// 검색 결과 건수
    /// </summary>
    [ObservableProperty]
    private int _searchResultCount;

    /// <summary>
    /// 검색 키워드
    /// </summary>
    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    /// <summary>
    /// 검색 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>
    /// 검색 결과 모드 여부
    /// </summary>
    [ObservableProperty]
    private bool _isSearchMode;

    /// <summary>
    /// 검색 필터 패널 표시 여부
    /// </summary>
    [ObservableProperty]
    private bool _isFilterPanelVisible;

    /// <summary>
    /// 검색 필터: 읽지않음만
    /// </summary>
    [ObservableProperty]
    private bool _filterUnreadOnly;

    /// <summary>
    /// 검색 필터: 첨부파일 있음만
    /// </summary>
    [ObservableProperty]
    private bool _filterHasAttachments;

    /// <summary>
    /// 검색 필터: 플래그됨만
    /// </summary>
    [ObservableProperty]
    private bool _filterFlaggedOnly;

    /// <summary>
    /// 검색 필터: AI 액션 필요만
    /// </summary>
    [ObservableProperty]
    private bool _filterActionRequired;

    /// <summary>
    /// 선택된 AI 카테고리 필터 (전체/긴급/액션필요/FYI/일반)
    /// </summary>
    [ObservableProperty]
    private string _selectedAiCategory = "전체";

    /// <summary>
    /// 검색 필터: 시작 날짜
    /// </summary>
    [ObservableProperty]
    private DateTime? _filterFromDate;

    /// <summary>
    /// 검색 필터: 종료 날짜
    /// </summary>
    [ObservableProperty]
    private DateTime? _filterToDate;

    #region 고급 검색

    /// <summary>
    /// 고급 검색: 발신자
    /// </summary>
    [ObservableProperty]
    private string _advancedSearchFrom = string.Empty;

    /// <summary>
    /// 고급 검색: 수신자
    /// </summary>
    [ObservableProperty]
    private string _advancedSearchTo = string.Empty;

    /// <summary>
    /// 고급 검색: 제목
    /// </summary>
    [ObservableProperty]
    private string _advancedSearchSubject = string.Empty;

    /// <summary>
    /// 고급 검색: 본문
    /// </summary>
    [ObservableProperty]
    private string _advancedSearchBody = string.Empty;

    /// <summary>
    /// 고급 검색: 시작 날짜
    /// </summary>
    [ObservableProperty]
    private DateTime? _advancedSearchDateFrom;

    /// <summary>
    /// 고급 검색: 종료 날짜
    /// </summary>
    [ObservableProperty]
    private DateTime? _advancedSearchDateTo;

    /// <summary>
    /// 고급 검색: 첨부파일 여부
    /// </summary>
    [ObservableProperty]
    private bool _advancedSearchHasAttachment;

    /// <summary>
    /// 검색 대상 폴더
    /// </summary>
    [ObservableProperty]
    private Folder? _searchTargetFolder;

    /// <summary>
    /// 검색 폴더 옵션 (모든 폴더, 기본 폴더들, 사용자 폴더)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SearchFolderItem> _searchFolderOptions = new();

    /// <summary>
    /// 선택된 검색 폴더 옵션
    /// </summary>
    [ObservableProperty]
    private SearchFolderItem? _selectedSearchFolderOption;

    /// <summary>
    /// 검색 폴더 옵션 초기화
    /// </summary>
    public void InitializeSearchFolderOptions()
    {
        SearchFolderOptions.Clear();

        // 기본 옵션 (보관/정크메일 제외)
        SearchFolderOptions.Add(new SearchFolderItem { FolderId = null, DisplayName = "모든 폴더", Icon = "📂" });
        SearchFolderOptions.Add(new SearchFolderItem { FolderId = "inbox", DisplayName = "받은 편지함", Icon = "📥" });
        SearchFolderOptions.Add(new SearchFolderItem { FolderId = "drafts", DisplayName = "임시 보관함", Icon = "📝" });
        SearchFolderOptions.Add(new SearchFolderItem { FolderId = "sentitems", DisplayName = "보낸 편지함", Icon = "📤" });
        SearchFolderOptions.Add(new SearchFolderItem { FolderId = "outbox", DisplayName = "보낼 편지함", Icon = "📮" });
        SearchFolderOptions.Add(new SearchFolderItem { FolderId = "deleteditems", DisplayName = "지운 편지함", Icon = "🗑️" });

        // 기본 선택: 모든 폴더
        SelectedSearchFolderOption = SearchFolderOptions.FirstOrDefault();
    }

    partial void OnSelectedSearchFolderOptionChanged(SearchFolderItem? value)
    {
        if (value != null && value.Folder != null)
        {
            SearchTargetFolder = value.Folder;
        }
        else
        {
            SearchTargetFolder = null;
        }
    }

    /// <summary>
    /// 고급 검색 실행
    /// </summary>
    [RelayCommand]
    private async Task ExecuteAdvancedSearchAsync()
    {
        Log4.Info("고급 검색 실행");

        // 고급 검색 필드를 기본 필터로 적용
        if (!string.IsNullOrWhiteSpace(AdvancedSearchFrom))
            SearchKeyword = $"from:{AdvancedSearchFrom}";
        else if (!string.IsNullOrWhiteSpace(AdvancedSearchTo))
            SearchKeyword = $"to:{AdvancedSearchTo}";
        else if (!string.IsNullOrWhiteSpace(AdvancedSearchSubject))
            SearchKeyword = $"subject:{AdvancedSearchSubject}";
        else if (!string.IsNullOrWhiteSpace(AdvancedSearchBody))
            SearchKeyword = AdvancedSearchBody;

        FilterFromDate = AdvancedSearchDateFrom;
        FilterToDate = AdvancedSearchDateTo;
        FilterHasAttachments = AdvancedSearchHasAttachment;

        await SearchAsync();
    }

    /// <summary>
    /// 고급 검색 초기화
    /// </summary>
    [RelayCommand]
    private void ClearAdvancedSearch()
    {
        Log4.Info("고급 검색 초기화");
        AdvancedSearchFrom = string.Empty;
        AdvancedSearchTo = string.Empty;
        AdvancedSearchSubject = string.Empty;
        AdvancedSearchBody = string.Empty;
        AdvancedSearchDateFrom = null;
        AdvancedSearchDateTo = null;
        AdvancedSearchHasAttachment = false;
        SearchTargetFolder = null;
    }

    #endregion

    /// <summary>
    /// 필터 패널 토글
    /// </summary>
    [RelayCommand]
    private void ToggleFilterPanel()
    {
        IsFilterPanelVisible = !IsFilterPanelVisible;
    }

    /// <summary>
    /// AI 액션 필요 필터 토글
    /// </summary>
    [RelayCommand]
    private async Task ToggleFilterActionRequired()
    {
        FilterActionRequired = !FilterActionRequired;
        await SearchAsync();
    }

    /// <summary>
    /// 검색 실행
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword) && !HasActiveFilters())
        {
            // 검색어와 필터가 없으면 검색 모드 해제
            await ClearSearchAsync();
            return;
        }

        await ExecuteAsync(async () =>
        {
            IsSearching = true;
            IsSearchMode = true;
            StatusMessage = "검색 중...";

            // 검색 대상 폴더 ID 결정 (콤보박스 선택 기준)
            string? searchFolderId = null;
            if (SelectedSearchFolderOption != null && !string.IsNullOrEmpty(SelectedSearchFolderOption.FolderId))
            {
                // FolderId로 표시 이름 매핑하여 실제 폴더 찾기
                var folderDisplayName = SelectedSearchFolderOption.FolderId switch
                {
                    "inbox" => "받은 편지함",
                    "drafts" => "임시 보관함",
                    "sentitems" => "보낸 편지함",
                    "outbox" => "보낼 편지함",
                    "deleteditems" => "지운 편지함",
                    "archive" => "보관",
                    "junkemail" => "정크 메일",
                    _ => null
                };

                if (folderDisplayName != null)
                {
                    var targetFolder = await _dbContext.Folders
                        .FirstOrDefaultAsync(f => f.DisplayName == folderDisplayName);
                    searchFolderId = targetFolder?.Id;
                }
            }

            // 접두사(from:, to:, subject:) 파싱
            string? fromPrefix = null;
            string? toPrefix = null;
            string? subjectPrefix = null;
            string? generalKeyword = SearchKeyword;

            if (!string.IsNullOrWhiteSpace(SearchKeyword))
            {
                if (SearchKeyword.StartsWith("from:", StringComparison.OrdinalIgnoreCase))
                {
                    fromPrefix = SearchKeyword.Substring(5).Trim();
                    generalKeyword = null;
                }
                else if (SearchKeyword.StartsWith("to:", StringComparison.OrdinalIgnoreCase))
                {
                    toPrefix = SearchKeyword.Substring(3).Trim();
                    generalKeyword = null;
                }
                else if (SearchKeyword.StartsWith("subject:", StringComparison.OrdinalIgnoreCase))
                {
                    subjectPrefix = SearchKeyword.Substring(8).Trim();
                    generalKeyword = null;
                }
            }

            var query = new SearchQuery
            {
                Keywords = generalKeyword,
                FolderId = searchFolderId, // 콤보박스 선택 폴더 기준
                IsRead = FilterUnreadOnly ? false : null,
                HasAttachments = FilterHasAttachments ? true : null,
                FromDate = FilterFromDate,
                ToDate = FilterToDate,
                PageSize = 200
            };

            // 플래그 필터 (FlagStatus가 SearchQuery에 없으므로 후처리)
            var results = await _dbContext.Emails
                .Where(e => string.IsNullOrEmpty(query.FolderId) || e.ParentFolderId == query.FolderId)
                .Where(e => (string.IsNullOrEmpty(query.Keywords) && fromPrefix == null && toPrefix == null && subjectPrefix == null) ||
                           (fromPrefix != null && EF.Functions.Like(e.From ?? "", $"%{fromPrefix}%")) ||
                           (toPrefix != null && EF.Functions.Like(e.To ?? "", $"%{toPrefix}%")) ||
                           (subjectPrefix != null && EF.Functions.Like(e.Subject ?? "", $"%{subjectPrefix}%")) ||
                           (!string.IsNullOrEmpty(query.Keywords) && (
                               EF.Functions.Like(e.Subject ?? "", $"%{query.Keywords}%") ||
                               EF.Functions.Like(e.Body ?? "", $"%{query.Keywords}%") ||
                               EF.Functions.Like(e.From ?? "", $"%{query.Keywords}%"))))
                .Where(e => !query.IsRead.HasValue || e.IsRead == query.IsRead.Value)
                .Where(e => !query.HasAttachments.HasValue || e.HasAttachments == query.HasAttachments.Value)
                .Where(e => !query.FromDate.HasValue || e.ReceivedDateTime >= query.FromDate.Value)
                .Where(e => !query.ToDate.HasValue || e.ReceivedDateTime <= query.ToDate.Value)
                .OrderByDescending(e => e.ReceivedDateTime)
                .Take(query.PageSize)
                .ToListAsync();

            // 플래그 필터 후처리
            if (FilterFlaggedOnly)
            {
                results = results.Where(e => e.FlagStatus == "flagged").ToList();
            }

            // AI 액션 필요 필터 후처리
            if (FilterActionRequired)
            {
                results = results.Where(e => e.AiActionRequired).ToList();
            }

            // AI 카테고리 필터 후처리
            if (SelectedAiCategory != "전체" && !string.IsNullOrEmpty(SelectedAiCategory))
            {
                results = results.Where(e => e.AiCategory == SelectedAiCategory).ToList();
            }

            // 스누즈 필터 후처리
            if (!ShowSnoozedEmails)
            {
                var now = DateTime.UtcNow;
                results = results.Where(e => e.SnoozedUntil == null || e.SnoozedUntil <= now).ToList();
            }

            Emails = results;
            SearchResultCount = Emails.Count;
            StatusMessage = $"검색 결과: {Emails.Count}건";
            IsSearching = false;
        }, "검색 실패");
    }

    /// <summary>
    /// 검색 초기화
    /// </summary>
    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchKeyword = string.Empty;
        FilterUnreadOnly = false;
        FilterHasAttachments = false;
        FilterFlaggedOnly = false;
        FilterActionRequired = false;
        SelectedAiCategory = "전체";
        FilterFromDate = null;
        FilterToDate = null;
        IsSearchMode = false;
        IsFilterPanelVisible = false;
        SearchResultCount = 0;

        if (SelectedFolder != null)
        {
            await LoadEmailsAsync();
        }
    }

    /// <summary>
    /// 활성화된 필터가 있는지 확인
    /// </summary>
    private bool HasActiveFilters()
    {
        return FilterUnreadOnly || FilterHasAttachments || FilterFlaggedOnly || FilterActionRequired ||
               (SelectedAiCategory != "전체" && !string.IsNullOrEmpty(SelectedAiCategory)) ||
               FilterFromDate.HasValue || FilterToDate.HasValue;
    }

    #endregion

    #region Phase 1: 스레드 뷰 (대화별 그룹핑)

    /// <summary>
    /// 스레드 뷰 모드 여부
    /// </summary>
    [ObservableProperty]
    private bool _isThreadViewMode;

    /// <summary>
    /// 그룹화된 이메일 목록 (스레드 뷰용)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<EmailGroup> _groupedEmails = new();

    /// <summary>
    /// 스레드 뷰 토글
    /// </summary>
    [RelayCommand]
    private void ToggleThreadView()
    {
        IsThreadViewMode = !IsThreadViewMode;
        if (IsThreadViewMode)
        {
            GroupEmailsByConversation();
        }
        else
        {
            GroupedEmails.Clear();
        }
        StatusMessage = IsThreadViewMode ? "스레드 뷰 모드" : "단일 뷰 모드";
    }

    /// <summary>
    /// ConversationId로 메일 그룹핑
    /// </summary>
    private void GroupEmailsByConversation()
    {
        if (Emails == null || Emails.Count == 0)
        {
            GroupedEmails.Clear();
            return;
        }

        var groups = Emails
            .GroupBy(e => e.ConversationId ?? e.Id.ToString())
            .Select(g => new EmailGroup
            {
                ConversationId = g.Key,
                Subject = g.OrderByDescending(e => e.ReceivedDateTime).First().Subject ?? "(제목 없음)",
                Emails = new ObservableCollection<Email>(g.OrderByDescending(e => e.ReceivedDateTime)),
                LatestDate = g.Max(e => e.ReceivedDateTime),
                UnreadCount = g.Count(e => !e.IsRead),
                TotalCount = g.Count(),
                IsExpanded = false
            })
            .OrderByDescending(g => g.LatestDate)
            .ToList();

        GroupedEmails = new ObservableCollection<EmailGroup>(groups);
    }

    /// <summary>
    /// 스레드 그룹 확장/축소 토글
    /// </summary>
    public void ToggleGroupExpand(EmailGroup group)
    {
        if (group != null)
        {
            group.IsExpanded = !group.IsExpanded;
        }
    }

    #endregion

    #region Phase 1: 정렬 및 그룹화

    /// <summary>
    /// 현재 정렬 기준
    /// </summary>
    [ObservableProperty]
    private string _sortBy = "ReceivedDateTime";

    /// <summary>
    /// 정렬 방향 (true: 내림차순)
    /// </summary>
    [ObservableProperty]
    private bool _sortDescending = true;

    /// <summary>
    /// 정렬 기준 표시 텍스트
    /// </summary>
    public string SortByDisplayText => SortBy switch
    {
        "ReceivedDateTime" => "날짜",
        "Subject" => "제목",
        "From" => "발신자",
        "PriorityScore" => "중요도",
        "AiPriority" => "AI 우선순위",
        "IsRead" => "읽음 상태",
        "FlagStatus" => "플래그",
        "HasAttachments" => "첨부파일",
        _ => "날짜"
    };

    /// <summary>
    /// 정렬 순서 아이콘
    /// </summary>
    public Wpf.Ui.Controls.SymbolRegular SortOrderIcon =>
        SortDescending ? Wpf.Ui.Controls.SymbolRegular.ArrowSortDown24 : Wpf.Ui.Controls.SymbolRegular.ArrowSortUp24;

    /// <summary>
    /// 정렬 순서 툴팁
    /// </summary>
    public string SortOrderTooltip =>
        SortDescending ? "내림차순 (최신순)" : "오름차순 (오래된순)";

    /// <summary>
    /// 정렬 순서 토글 커맨드
    /// </summary>
    [RelayCommand]
    private void ToggleSortOrder()
    {
        SortDescending = !SortDescending;
        ApplySorting();
        OnPropertyChanged(nameof(SortOrderIcon));
        OnPropertyChanged(nameof(SortOrderTooltip));
    }

    /// <summary>
    /// SortBy가 변경될 때 DisplayText도 갱신
    /// </summary>
    partial void OnSortByChanged(string value)
    {
        OnPropertyChanged(nameof(SortByDisplayText));
    }

    /// <summary>
    /// SortDescending이 변경될 때 아이콘/툴팁도 갱신
    /// </summary>
    partial void OnSortDescendingChanged(bool value)
    {
        OnPropertyChanged(nameof(SortOrderIcon));
        OnPropertyChanged(nameof(SortOrderTooltip));
    }

    /// <summary>
    /// 날짜별 그룹화 모드 여부
    /// </summary>
    [ObservableProperty]
    private bool _isDateGroupingEnabled;

    /// <summary>
    /// 날짜별 그룹화된 메일 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DateEmailGroup> _dateGroupedEmails = new();

    /// <summary>
    /// 정렬 기준 변경
    /// </summary>
    [RelayCommand]
    private void SortEmails(string sortBy)
    {
        if (SortBy == sortBy)
        {
            // 같은 기준이면 방향 토글
            SortDescending = !SortDescending;
        }
        else
        {
            SortBy = sortBy;
            SortDescending = true;
        }

        ApplySorting();
    }

    /// <summary>
    /// 정렬 적용
    /// </summary>
    private void ApplySorting()
    {
        if (Emails == null || Emails.Count == 0) return;

        var sorted = SortBy switch
        {
            "Subject" => SortDescending
                ? Emails.OrderByDescending(e => e.Subject).ToList()
                : Emails.OrderBy(e => e.Subject).ToList(),
            "From" => SortDescending
                ? Emails.OrderByDescending(e => e.From).ToList()
                : Emails.OrderBy(e => e.From).ToList(),
            "HasAttachments" => SortDescending
                ? Emails.OrderByDescending(e => e.HasAttachments).ThenByDescending(e => e.ReceivedDateTime).ToList()
                : Emails.OrderBy(e => e.HasAttachments).ThenByDescending(e => e.ReceivedDateTime).ToList(),
            "PriorityScore" => SortDescending
                ? Emails.OrderByDescending(e => e.PriorityScore).ToList()
                : Emails.OrderBy(e => e.PriorityScore).ToList(),
            "AiPriority" => SortDescending
                ? Emails.OrderByDescending(e => e.AiPriority).ToList()
                : Emails.OrderBy(e => e.AiPriority).ToList(),
            _ => SortDescending
                ? Emails.OrderByDescending(e => e.ReceivedDateTime).ToList()
                : Emails.OrderBy(e => e.ReceivedDateTime).ToList()
        };

        Emails = sorted;

        // 스레드 뷰 모드면 다시 그룹핑
        if (IsThreadViewMode)
        {
            GroupEmailsByConversation();
        }

        // 날짜 그룹화 모드면 다시 그룹핑
        if (IsDateGroupingEnabled)
        {
            GroupEmailsByDate();
        }
    }

    /// <summary>
    /// 날짜별 그룹화 토글
    /// </summary>
    [RelayCommand]
    private void ToggleDateGrouping()
    {
        IsDateGroupingEnabled = !IsDateGroupingEnabled;
        if (IsDateGroupingEnabled)
        {
            GroupEmailsByDate();
        }
        else
        {
            DateGroupedEmails.Clear();
        }
    }

    /// <summary>
    /// 날짜별 메일 그룹핑
    /// </summary>
    private void GroupEmailsByDate()
    {
        if (Emails == null || Emails.Count == 0)
        {
            DateGroupedEmails.Clear();
            return;
        }

        var today = DateTime.Today;
        var yesterday = today.AddDays(-1);
        var thisWeekStart = today.AddDays(-(int)today.DayOfWeek);
        var lastWeekStart = thisWeekStart.AddDays(-7);
        var thisMonthStart = new DateTime(today.Year, today.Month, 1);

        var groups = Emails
            .GroupBy(e =>
            {
                var date = e.ReceivedDateTime?.Date ?? DateTime.MinValue;
                if (date == today) return "오늘";
                if (date == yesterday) return "어제";
                if (date >= thisWeekStart) return "이번 주";
                if (date >= lastWeekStart) return "지난 주";
                if (date >= thisMonthStart) return "이번 달";
                if (date.Year == today.Year) return date.ToString("yyyy년 M월");
                return date.ToString("yyyy년");
            })
            .Select(g => new DateEmailGroup
            {
                DateLabel = g.Key,
                Emails = new ObservableCollection<Email>(g.OrderByDescending(e => e.ReceivedDateTime)),
                Count = g.Count()
            })
            .ToList();

        // 정렬 순서 지정
        var order = new[] { "오늘", "어제", "이번 주", "지난 주", "이번 달" };
        var orderedGroups = groups
            .OrderBy(g =>
            {
                var idx = Array.IndexOf(order, g.DateLabel);
                return idx >= 0 ? idx : 100;
            })
            .ThenByDescending(g => g.DateLabel)
            .ToList();

        DateGroupedEmails = new ObservableCollection<DateEmailGroup>(orderedGroups);
    }

    #endregion

    #region Phase 2: 메일 드래그&드롭 (폴더 간 이동)

    /// <summary>
    /// 메일을 다른 폴더로 이동
    /// </summary>
    public async Task MoveEmailsToFolderAsync(List<Email> emails, Folder targetFolder)
    {
        if (emails == null || emails.Count == 0 || targetFolder == null) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"메일 이동 중... (0/{emails.Count})";

            var semaphore = new SemaphoreSlim(8, 8);
            var succeededMoves = new ConcurrentBag<(Email email, string newEntryId)>();
            int failed = 0;

            await Task.WhenAll(emails.Where(e => !string.IsNullOrEmpty(e.EntryId)).Select(async email =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var movedMessage = await _graphMailService.MoveMessageAsync(email.EntryId, targetFolder.Id);
                    var newId = movedMessage?.Id ?? email.EntryId;
                    succeededMoves.Add((email, newId));
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    Log4.Error($"메일 이동 실패 [{email.Subject}]: {ex.Message}");
                }
                finally { semaphore.Release(); }
            }));

            // DB 배치 업데이트 (EntryId 각각 다르므로 foreach + 1회 SaveChanges)
            foreach (var (email, newEntryId) in succeededMoves)
            {
                var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                if (dbEmail != null)
                {
                    dbEmail.EntryId = newEntryId;
                    dbEmail.ParentFolderId = targetFolder.Id;
                    email.EntryId = newEntryId; // 메모리 상 객체도 업데이트
                }
            }
            await _dbContext.SaveChangesAsync(); // 루프 밖 1회

            // UI: 이동된 메일 제거
            var movedIds = succeededMoves.Select(m => m.email.Id).ToHashSet();
            Emails = Emails.Where(e => !movedIds.Contains(e.Id)).ToList();

            StatusMessage = failed > 0
                ? $"메일 이동 완료 ({succeededMoves.Count}건 성공, {failed}건 실패)"
                : $"'{targetFolder.DisplayName}'으로 {succeededMoves.Count}건 이동 완료";
            Log4.Info($"메일 이동 완료: {succeededMoves.Count}건 → {targetFolder.DisplayName}");
        }, "메일 이동 실패");
    }

    /// <summary>
    /// 전체 폴더 강제 재동기화
    /// </summary>
    public async Task ForceResyncAllAsync()
    {
        Log4.Info("전체 재동기화 시작");
        StatusMessage = "전체 폴더 재동기화 중...";

        try
        {
            // BackgroundSyncService를 통해 강제 동기화
            await _syncService.SyncFoldersAsync();
            await _syncService.SyncAllAccountsAsync();

            // 현재 폴더 목록 새로고침
            await LoadFoldersCommand.ExecuteAsync(null);

            StatusMessage = "전체 재동기화 완료";
            Log4.Info("전체 재동기화 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"전체 재동기화 실패: {ex.Message}");
            StatusMessage = $"재동기화 실패: {ex.Message}";
            throw;
        }
    }

    /// <summary>
    /// 선택된 메일을 지정 폴더로 이동 명령
    /// </summary>
    [RelayCommand]
    private async Task MoveSelectedEmailsAsync(Folder targetFolder)
    {
        if (SelectedEmail == null || targetFolder == null) return;
        await MoveEmailsToFolderAsync(new List<Email> { SelectedEmail }, targetFolder);
    }

    #endregion

    #region Phase 2: 폴더 CRUD

    /// <summary>
    /// 폴더 생성
    /// </summary>
    [RelayCommand]
    private async Task CreateFolderAsync(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"폴더 생성 중: {folderName}";

            // Graph API로 폴더 생성 (현재 선택된 폴더 아래에 생성)
            var parentId = SelectedFolder?.Id;
            var newFolder = await _graphMailService.CreateFolderAsync(folderName, parentId);

            if (newFolder != null)
            {
                // 로컬 DB에 저장
                var folder = new Folder
                {
                    Id = newFolder.Id,
                    DisplayName = newFolder.DisplayName,
                    ParentFolderId = parentId,
                    TotalItemCount = 0,
                    UnreadItemCount = 0
                };
                _dbContext.Folders.Add(folder);
                await _dbContext.SaveChangesAsync();

                // 폴더 목록 갱신
                await LoadFoldersAsync();

                StatusMessage = $"폴더 '{folderName}' 생성 완료";
                Log4.Info($"폴더 생성: {folderName}");
            }
        }, "폴더 생성 실패");
    }

    /// <summary>
    /// 폴더 이름 변경
    /// </summary>
    [RelayCommand]
    private async Task RenameFolderAsync((Folder folder, string newName) args)
    {
        if (args.folder == null || string.IsNullOrWhiteSpace(args.newName)) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"폴더 이름 변경 중: {args.folder.DisplayName} → {args.newName}";

            // Graph API로 폴더 이름 변경
            var success = await _graphMailService.RenameFolderAsync(args.folder.Id, args.newName);

            if (success)
            {
                // 로컬 DB 업데이트
                var dbFolder = await _dbContext.Folders.FindAsync(args.folder.Id);
                if (dbFolder != null)
                {
                    dbFolder.DisplayName = args.newName;
                    await _dbContext.SaveChangesAsync();
                }

                args.folder.DisplayName = args.newName;

                // 폴더 목록 갱신
                await LoadFoldersAsync();

                StatusMessage = $"폴더 이름 변경 완료: {args.newName}";
                Log4.Info($"폴더 이름 변경: {args.folder.DisplayName} → {args.newName}");
            }
        }, "폴더 이름 변경 실패");
    }

    /// <summary>
    /// 폴더 삭제
    /// </summary>
    [RelayCommand]
    private async Task DeleteFolderAsync(Folder folder)
    {
        if (folder == null) return;

        await ExecuteAsync(async () =>
        {
            StatusMessage = $"폴더 삭제 중: {folder.DisplayName}";

            // Graph API로 폴더 삭제
            var success = await _graphMailService.DeleteFolderAsync(folder.Id);

            if (success)
            {
                // 로컬 DB에서 삭제
                var dbFolder = await _dbContext.Folders.FindAsync(folder.Id);
                if (dbFolder != null)
                {
                    _dbContext.Folders.Remove(dbFolder);
                    await _dbContext.SaveChangesAsync();
                }

                // 해당 폴더의 메일도 로컬 DB에서 삭제
                var folderEmails = await _dbContext.Emails
                    .Where(e => e.ParentFolderId == folder.Id)
                    .ToListAsync();
                _dbContext.Emails.RemoveRange(folderEmails);
                await _dbContext.SaveChangesAsync();

                // 폴더 목록 갱신
                await LoadFoldersAsync();

                StatusMessage = $"폴더 '{folder.DisplayName}' 삭제 완료";
                Log4.Info($"폴더 삭제: {folder.DisplayName}");
            }
        }, "폴더 삭제 실패");
    }

    /// <summary>
    /// 메일 고정/해제 토글
    /// </summary>
    [RelayCommand]
    private async Task TogglePinnedAsync(Email? email = null)
    {
        var targetEmail = email ?? SelectedEmail;
        if (targetEmail == null) return;

        await ExecuteAsync(async () =>
        {
            // Pin 상태 토글
            targetEmail.IsPinned = !targetEmail.IsPinned;
            targetEmail.PinnedAt = targetEmail.IsPinned ? DateTime.Now : null;

            // DB 업데이트
            var dbEmail = await _dbContext.Emails.FindAsync(targetEmail.Id);
            if (dbEmail != null)
            {
                dbEmail.IsPinned = targetEmail.IsPinned;
                dbEmail.PinnedAt = targetEmail.PinnedAt;
                await _dbContext.SaveChangesAsync();
            }

            // 리스트 정렬 다시 적용 (고정된 메일 상단)
            ApplySortingWithPin();

            var action = targetEmail.IsPinned ? "고정" : "고정 해제";
            StatusMessage = $"메일 {action}됨";
            Log4.Info($"메일 {action}: {targetEmail.Subject}");
        }, "메일 고정 상태 변경 실패");
    }

    /// <summary>
    /// 고정된 메일을 상단에 정렬
    /// </summary>
    private void ApplySortingWithPin()
    {
        if (Emails == null || Emails.Count == 0) return;

        // 고정 메일과 일반 메일 분리
        var pinned = Emails.Where(e => e.IsPinned)
                                  .OrderByDescending(e => e.PinnedAt)
                                  .ToList();
        var unpinned = Emails.Where(e => !e.IsPinned).ToList();

        // 현재 정렬 기준 적용 (일반 메일에만)
        IEnumerable<Email> sortedUnpinned = SortBy switch
        {
            "Subject" => SortDescending
                ? unpinned.OrderByDescending(e => e.Subject)
                : unpinned.OrderBy(e => e.Subject),
            "From" => SortDescending
                ? unpinned.OrderByDescending(e => e.From)
                : unpinned.OrderBy(e => e.From),
            "HasAttachments" => SortDescending
                ? unpinned.OrderByDescending(e => e.HasAttachments).ThenByDescending(e => e.ReceivedDateTime)
                : unpinned.OrderBy(e => e.HasAttachments).ThenByDescending(e => e.ReceivedDateTime),
            "PriorityScore" => SortDescending
                ? unpinned.OrderByDescending(e => e.PriorityScore)
                : unpinned.OrderBy(e => e.PriorityScore),
            "AiPriority" => SortDescending
                ? unpinned.OrderByDescending(e => e.AiPriority)
                : unpinned.OrderBy(e => e.AiPriority),
            _ => SortDescending
                ? unpinned.OrderByDescending(e => e.ReceivedDateTime)
                : unpinned.OrderBy(e => e.ReceivedDateTime)
        };

        // 고정 메일 + 정렬된 일반 메일 (새 리스트 할당으로 UI 업데이트)
        Emails = pinned.Concat(sortedUnpinned).ToList();

        Log4.Debug($"[ApplySortingWithPin] 정렬 완료: 고정 {pinned.Count}건, 일반 {unpinned.Count()}건");
    }

    #endregion

    #region 임시보관함 편집 메서드

    /// <summary>
    /// 임시보관함 폴더인지 확인
    /// </summary>
    public bool IsDraftsFolder(Folder? folder)
    {
        if (folder == null) return false;

        // 폴더 이름으로 임시보관함 확인
        return folder.DisplayName.Equals("Drafts", StringComparison.OrdinalIgnoreCase) ||
               folder.DisplayName.Equals("임시 보관함", StringComparison.OrdinalIgnoreCase) ||
               folder.DisplayName.Equals("초안", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 드래프트 메일을 편집 모드로 로드
    /// </summary>
    public void LoadDraftForEditing(Email draftEmail)
    {
        if (draftEmail == null) return;

        try
        {
            Log4.Info($"[LoadDraftForEditing] 임시보관함 메일 편집 시작: {draftEmail.Subject}");

            _editingDraftMessageId = draftEmail.EntryId;
            DraftTo = ParseJsonArrayToString(draftEmail.To);
            DraftCc = ParseJsonArrayToString(draftEmail.Cc);
            DraftBcc = ParseJsonArrayToString(draftEmail.Bcc);
            DraftSubject = draftEmail.Subject ?? "";
            DraftBody = draftEmail.Body ?? "";
            IsEditingDraft = true;

            Log4.Debug($"[LoadDraftForEditing] To={DraftTo}, Subject={DraftSubject}");
        }
        catch (Exception ex)
        {
            Log4.Error($"[LoadDraftForEditing] 드래프트 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 드래프트 편집 취소/닫기
    /// </summary>
    /// <param name="resetSelectedEmail">SelectedEmail을 null로 리셋할지 여부 (자동 저장 시에는 false)</param>
    [RelayCommand]
    public void CloseDraftEditor(bool resetSelectedEmail = true)
    {
        IsEditingDraft = false;
        _editingDraftMessageId = null;
        DraftTo = "";
        DraftCc = "";
        DraftBcc = "";
        DraftSubject = "";
        DraftBody = "";

        // 자동 저장 시에는 SelectedEmail을 리셋하지 않음 (다음 메일 선택 흐름 유지)
        if (resetSelectedEmail)
        {
            SelectedEmail = null;
        }

        Log4.Debug($"[CloseDraftEditor] 드래프트 편집 모드 종료 (resetSelectedEmail={resetSelectedEmail})");
    }

    /// <summary>
    /// 드래프트 메일 발송
    /// </summary>
    [RelayCommand]
    public async Task SendDraftAsync()
    {
        try
        {
            Log4.Info($"[SendDraftAsync] 드래프트 발송 시작: {DraftSubject}");

            // 받는 사람 확인
            var toRecipients = ParseEmailAddresses(DraftTo);
            if (toRecipients.Count == 0)
            {
                Log4.Warn("[SendDraftAsync] 받는 사람이 없습니다.");
                MessageBox.Show("받는 사람을 입력해주세요.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 참조, 숨은참조 파싱
            var ccRecipients = ParseEmailAddresses(DraftCc);
            var bccRecipients = ParseEmailAddresses(DraftBcc);

            // Message 객체 생성
            var message = new Microsoft.Graph.Models.Message
            {
                Subject = DraftSubject,
                Body = new Microsoft.Graph.Models.ItemBody
                {
                    ContentType = Microsoft.Graph.Models.BodyType.Html,
                    Content = DraftBody
                },
                ToRecipients = toRecipients,
                CcRecipients = ccRecipients,
                BccRecipients = bccRecipients
            };

            // Graph API로 발송
            await _graphMailService.SendMessageAsync(message);
            Log4.Info($"[SendDraftAsync] 메일 발송 성공: To={DraftTo}, Subject={DraftSubject}");

            // 기존 드래프트 삭제
            if (!string.IsNullOrEmpty(_editingDraftMessageId))
            {
                try
                {
                    await _graphMailService.DeleteMessageAsync(_editingDraftMessageId);
                    Log4.Debug($"[SendDraftAsync] 기존 드래프트 삭제 완료: {_editingDraftMessageId}");

                    // 로컬 DB에서도 삭제
                    var localEmail = await _dbContext.Emails.FirstOrDefaultAsync(e => e.EntryId == _editingDraftMessageId);
                    if (localEmail != null)
                    {
                        _dbContext.Emails.Remove(localEmail);
                        await _dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    Log4.Warn($"[SendDraftAsync] 기존 드래프트 삭제 실패 (무시): {ex.Message}");
                }
            }

            // 편집 모드 종료
            CloseDraftEditor();

            // 메일 목록 새로고침
            await LoadEmailsAsync();

            MessageBox.Show("메일이 발송되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log4.Error($"[SendDraftAsync] 메일 발송 실패: {ex.Message}");
            MessageBox.Show($"메일 발송 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 드래프트 저장
    /// </summary>
    [RelayCommand]
    public async Task SaveDraftAsync()
    {
        try
        {
            Log4.Info($"[SaveDraftAsync] 드래프트 저장 시작: {DraftSubject}");

            // 받는 사람 파싱
            var toRecipients = ParseEmailAddresses(DraftTo);
            var ccRecipients = ParseEmailAddresses(DraftCc);
            var bccRecipients = ParseEmailAddresses(DraftBcc);

            // Message 객체 생성
            var message = new Microsoft.Graph.Models.Message
            {
                Subject = string.IsNullOrWhiteSpace(DraftSubject) ? "" : DraftSubject,
                Body = new Microsoft.Graph.Models.ItemBody
                {
                    ContentType = Microsoft.Graph.Models.BodyType.Html,
                    Content = DraftBody
                },
                ToRecipients = toRecipients,
                CcRecipients = ccRecipients,
                BccRecipients = bccRecipients
            };

            // 기존 드래프트가 있으면 업데이트, 없으면 새로 생성
            if (!string.IsNullOrEmpty(_editingDraftMessageId))
            {
                // 기존 드래프트 업데이트 (EntryId 유지)
                await _graphMailService.UpdateDraftAsync(_editingDraftMessageId, message);
                Log4.Info($"[SaveDraftAsync] 드래프트 업데이트 성공: {_editingDraftMessageId}");

                // 로컬 DB도 업데이트
                var localEmail = await _dbContext.Emails.FirstOrDefaultAsync(e => e.EntryId == _editingDraftMessageId);
                if (localEmail != null)
                {
                    localEmail.Subject = message.Subject;
                    localEmail.Body = message.Body?.Content;
                    localEmail.To = toRecipients.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(toRecipients.Select(r => $"{r.EmailAddress?.Name} <{r.EmailAddress?.Address}>").ToArray())
                        : null;
                    localEmail.Cc = ccRecipients.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(ccRecipients.Select(r => $"{r.EmailAddress?.Name} <{r.EmailAddress?.Address}>").ToArray())
                        : null;
                    localEmail.Bcc = bccRecipients.Count > 0
                        ? System.Text.Json.JsonSerializer.Serialize(bccRecipients.Select(r => $"{r.EmailAddress?.Name} <{r.EmailAddress?.Address}>").ToArray())
                        : null;
                    await _dbContext.SaveChangesAsync();
                    Log4.Debug($"[SaveDraftAsync] 로컬 DB 업데이트 완료: {_editingDraftMessageId}");
                }
            }
            else
            {
                // 새 드래프트 생성
                await _graphMailService.SaveDraftAsync(message);
                Log4.Info($"[SaveDraftAsync] 새 드래프트 저장 성공: {DraftSubject}");
            }

            // 편집 모드 종료
            CloseDraftEditor();

            // 메일 목록 새로고침
            await LoadEmailsAsync();

            MessageBox.Show("임시 저장되었습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log4.Error($"[SaveDraftAsync] 드래프트 저장 실패: {ex.Message}");
            MessageBox.Show($"임시 저장 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 드래프트 자동 저장 (다른 메일 선택 시 - MessageBox 없음)
    /// 기존 드래프트를 업데이트하는 방식으로 EntryId 유지
    /// </summary>
    public async Task AutoSaveDraftAsync()
    {
        try
        {
            Log4.Info($"[AutoSaveDraftAsync] 드래프트 자동 저장 시작: {DraftSubject}");

            // 받는 사람 파싱
            var toRecipients = ParseEmailAddresses(DraftTo);
            var ccRecipients = ParseEmailAddresses(DraftCc);
            var bccRecipients = ParseEmailAddresses(DraftBcc);

            // Message 객체 생성
            var message = new Microsoft.Graph.Models.Message
            {
                Subject = string.IsNullOrWhiteSpace(DraftSubject) ? "" : DraftSubject,
                Body = new Microsoft.Graph.Models.ItemBody
                {
                    ContentType = Microsoft.Graph.Models.BodyType.Html,
                    Content = DraftBody
                },
                ToRecipients = toRecipients,
                CcRecipients = ccRecipients,
                BccRecipients = bccRecipients
            };

            // 기존 드래프트가 있으면 업데이트, 없으면 새로 생성
            if (!string.IsNullOrEmpty(_editingDraftMessageId))
            {
                // 기존 드래프트 업데이트 (EntryId 유지)
                await _graphMailService.UpdateDraftAsync(_editingDraftMessageId, message);
                Log4.Info($"[AutoSaveDraftAsync] 드래프트 업데이트 성공: {_editingDraftMessageId}");

                // To/Cc/Bcc 문자열 생성
                var toStr = toRecipients.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(toRecipients.Select(r => $"{r.EmailAddress?.Name} <{r.EmailAddress?.Address}>").ToArray())
                    : null;
                var ccStr = ccRecipients.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(ccRecipients.Select(r => $"{r.EmailAddress?.Name} <{r.EmailAddress?.Address}>").ToArray())
                    : null;
                var bccStr = bccRecipients.Count > 0
                    ? System.Text.Json.JsonSerializer.Serialize(bccRecipients.Select(r => $"{r.EmailAddress?.Name} <{r.EmailAddress?.Address}>").ToArray())
                    : null;

                // 로컬 DB 업데이트
                var localEmail = await _dbContext.Emails.FirstOrDefaultAsync(e => e.EntryId == _editingDraftMessageId);
                if (localEmail != null)
                {
                    localEmail.Subject = message.Subject;
                    localEmail.Body = message.Body?.Content;
                    localEmail.To = toStr;
                    localEmail.Cc = ccStr;
                    localEmail.Bcc = bccStr;
                    await _dbContext.SaveChangesAsync();
                    Log4.Debug($"[AutoSaveDraftAsync] 로컬 DB 업데이트 완료: {_editingDraftMessageId}");
                }

                // UI(Emails 컬렉션)도 업데이트하여 메일 목록에 즉시 반영
                var uiEmail = Emails.FirstOrDefault(e => e.EntryId == _editingDraftMessageId);
                if (uiEmail != null)
                {
                    uiEmail.Subject = message.Subject;
                    uiEmail.Body = message.Body?.Content;
                    uiEmail.To = toStr;
                    uiEmail.Cc = ccStr;
                    uiEmail.Bcc = bccStr;
                    Log4.Debug($"[AutoSaveDraftAsync] UI 메일 목록 업데이트 완료: {_editingDraftMessageId}");
                }
            }
            else
            {
                // 새 드래프트 생성
                await _graphMailService.SaveDraftAsync(message);
                Log4.Info($"[AutoSaveDraftAsync] 새 드래프트 저장 성공: {DraftSubject}");
            }

            // 편집 모드 종료 (자동 저장이므로 SelectedEmail 유지)
            CloseDraftEditor(resetSelectedEmail: false);

            // 메일 목록 새로고침하여 UI에 변경 사항 반영
            await LoadEmailsAsync();
            Log4.Debug("[AutoSaveDraftAsync] 메일 목록 새로고침 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"[AutoSaveDraftAsync] 드래프트 자동 저장 실패: {ex.Message}");
            // 자동 저장 실패 시에도 편집 모드 종료 (SelectedEmail 유지)
            CloseDraftEditor(resetSelectedEmail: false);
            // 예외를 throw하지 않고 조용히 실패 (다른 메일 선택 흐름 유지)
        }
    }

    /// <summary>
    /// JSON 배열 문자열을 세미콜론 구분 문자열로 변환
    /// </summary>
    private static string ParseJsonArrayToString(string? jsonArray)
    {
        if (string.IsNullOrWhiteSpace(jsonArray))
            return "";

        // JSON 배열이 아니면 그대로 반환
        if (!jsonArray.StartsWith("["))
            return jsonArray;

        try
        {
            var items = System.Text.Json.JsonSerializer.Deserialize<string[]>(jsonArray);
            if (items == null || items.Length == 0)
                return "";

            return string.Join("; ", items);
        }
        catch
        {
            return jsonArray;
        }
    }

    /// <summary>
    /// 이메일 주소 문자열을 Recipient 목록으로 파싱
    /// </summary>
    private static List<Microsoft.Graph.Models.Recipient> ParseEmailAddresses(string input)
    {
        var result = new List<Microsoft.Graph.Models.Recipient>();
        if (string.IsNullOrWhiteSpace(input)) return result;

        var addresses = input.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var addr in addresses)
        {
            var trimmed = addr.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string? name = null;
            string? email = null;

            // "이름 <주소>" 형식 파싱
            var bracketStart = trimmed.IndexOf('<');
            var bracketEnd = trimmed.IndexOf('>');

            if (bracketStart > 0 && bracketEnd > bracketStart)
            {
                // "이름 <주소>" 형식
                name = trimmed.Substring(0, bracketStart).Trim();
                email = trimmed.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
            }
            else if (trimmed.Contains('@'))
            {
                // 이메일 주소만 있는 경우
                email = trimmed;
            }
            else
            {
                // 이메일 형식이 아닌 경우 스킵
                Log4.Warn($"[ParseEmailAddresses] 유효하지 않은 이메일 주소 형식: {trimmed}");
                continue;
            }

            if (!string.IsNullOrEmpty(email))
            {
                result.Add(new Microsoft.Graph.Models.Recipient
                {
                    EmailAddress = new Microsoft.Graph.Models.EmailAddress
                    {
                        Address = email,
                        Name = name
                    }
                });
            }
        }

        return result;
    }

    #endregion

    #region IDisposable

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // BackgroundSyncService 이벤트 구독 해제
        _syncService.PausedChanged -= OnSyncPausedChanged;
        _syncService.FoldersSynced -= OnFoldersSynced;
        _syncService.EmailsSynced -= OnEmailsSynced;
        _syncService.MailSyncStarted -= OnMailSyncStarted;
        _syncService.MailSyncProgress -= OnMailSyncProgress;
        _syncService.MailSyncCompleted -= OnMailSyncCompleted;
        _syncService.CalendarSyncStarted -= OnCalendarSyncStarted;
        _syncService.CalendarSyncProgress -= OnCalendarSyncProgress;
        _syncService.CalendarSynced -= OnCalendarSynced;
        _syncService.HistoricalSyncProgress -= OnHistoricalSyncProgress;
        _syncService.HistoricalSyncCompleted -= OnHistoricalSyncCompleted;

        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// 이메일 스레드 그룹 (ConversationId 기준)
/// </summary>
public class EmailGroup : ObservableObject
{
    public string ConversationId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public ObservableCollection<Email> Emails { get; set; } = new();
    public DateTime? LatestDate { get; set; }
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// 그룹 헤더 표시 텍스트
    /// </summary>
    public string HeaderText => TotalCount > 1
        ? $"{Subject} ({TotalCount}건, {UnreadCount}건 안읽음)"
        : Subject;
}

/// <summary>
/// 날짜별 이메일 그룹
/// </summary>
public class DateEmailGroup : ObservableObject
{
    public string DateLabel { get; set; } = string.Empty;
    public ObservableCollection<Email> Emails { get; set; } = new();
    public int Count { get; set; }

    /// <summary>
    /// 그룹 헤더 표시 텍스트
    /// </summary>
    public string HeaderText => $"{DateLabel} ({Count}건)";
}
