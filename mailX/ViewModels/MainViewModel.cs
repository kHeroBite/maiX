using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using mailX.Data;
using mailX.Models;
using mailX.Services.Graph;
using mailX.Services.Search;
using mailX.Services.Sync;
using mailX.Utils;

namespace mailX.ViewModels;

/// <summary>
/// 메인 화면 ViewModel - 폴더/이메일 목록 관리
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly MailXDbContext _dbContext;
    private readonly BackgroundSyncService _syncService;
    private readonly GraphMailService _graphMailService;

    public MainViewModel(MailXDbContext dbContext, BackgroundSyncService syncService, GraphMailService graphMailService)
    {
        _dbContext = dbContext;
        _syncService = syncService;
        _graphMailService = graphMailService;

        // 동기화 상태 변경 이벤트 구독
        _syncService.PausedChanged += OnSyncPausedChanged;

        // 폴더/메일 동기화 완료 이벤트 구독
        _syncService.FoldersSynced += OnFoldersSynced;
        _syncService.EmailsSynced += OnEmailsSynced;

        // 메일 동기화 진행률 이벤트 구독
        _syncService.MailSyncStarted += OnMailSyncStarted;
        _syncService.MailSyncProgress += OnMailSyncProgress;
        _syncService.MailSyncCompleted += OnMailSyncCompleted;

        // 캘린더 동기화 이벤트 구독
        _syncService.CalendarSyncStarted += OnCalendarSyncStarted;
        _syncService.CalendarSyncProgress += OnCalendarSyncProgress;
        _syncService.CalendarSynced += OnCalendarSynced;

        // 초기 상태 동기화
        _isSyncPaused = _syncService.IsPaused;
        UpdateSyncStatus();
    }

    /// <summary>
    /// 메일 동기화 시작 이벤트 핸들러
    /// </summary>
    private void OnMailSyncStarted(int total)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ShowSyncProgress(total);
        });
    }

    /// <summary>
    /// 메일 동기화 진행 이벤트 핸들러
    /// </summary>
    private void OnMailSyncProgress(int completed)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            UpdateSyncProgress(completed);
        });
    }

    /// <summary>
    /// 메일 동기화 완료 이벤트 핸들러
    /// </summary>
    private void OnMailSyncCompleted()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            HideSyncProgressAfterDelay();
            UpdateSyncStatus();
        });
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
        CalendarSyncStatusText = $"일정 {eventCount}건 동기화됨";
        
        // 캘린더 동기화 시간 업데이트
        LastCalendarSyncTime = DateTime.UtcNow;

        // 캘린더 뷰 새로고침 이벤트 발생 (UI에서 구독)
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
    private string _title = "mailX";

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
    /// 선택된 이메일
    /// </summary>
    [ObservableProperty]
    private Email? _selectedEmail;

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
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
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
        if (SelectedFolder != null)
        {
            await LoadEmailsAsync();
        }

        if (newCount > 0)
        {
            StatusMessage = $"{newCount}개 새 메일 동기화됨";
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
        if (value != null)
        {
            _ = LoadEmailsAsync();
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
    /// 선택된 폴더의 이메일 목록 로드
    /// </summary>
    [RelayCommand]
    private async Task LoadEmailsAsync()
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
            Emails = await _dbContext.Emails
                .AsNoTracking()
                .Where(e => e.ParentFolderId == SelectedFolder.Id)
                .OrderByDescending(e => e.ReceivedDateTime)
                .ToListAsync();

            StatusMessage = $"{Emails.Count}개 이메일 로드됨";
        }, "이메일 로드 실패");
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
        if (email == null || string.IsNullOrEmpty(email.EntryId))
        {
            StatusMessage = "삭제할 메일을 선택해주세요.";
            return;
        }

        await ExecuteAsync(async () =>
        {
            StatusMessage = "메일 삭제 중...";

            try
            {
                // 휴지통(DeletedItems) 폴더 ID 찾기
                var deletedItemsFolder = await _dbContext.Folders
                    .FirstOrDefaultAsync(f => f.DisplayName == "지운 편지함" ||
                                              f.DisplayName.Equals("Deleted Items", StringComparison.OrdinalIgnoreCase) ||
                                              f.DisplayName.Equals("DeletedItems", StringComparison.OrdinalIgnoreCase));

                if (deletedItemsFolder != null && !string.IsNullOrEmpty(deletedItemsFolder.Id))
                {
                    // Graph API로 휴지통으로 이동
                    await _graphMailService.MoveMessageAsync(email.EntryId, deletedItemsFolder.Id);
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
            int completed = 0;

            foreach (var email in emails)
            {
                if (string.IsNullOrEmpty(email.EntryId)) continue;

                try
                {
                    // Graph API로 플래그 업데이트
                    await _graphMailService.UpdateMessageFlagAsync(email.EntryId, flagStatus);

                    // 로컬 DB 업데이트
                    var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                    if (dbEmail != null)
                    {
                        dbEmail.FlagStatus = flagStatus;
                        await _dbContext.SaveChangesAsync();
                    }

                    // 메모리 상 데이터 업데이트
                    email.FlagStatus = flagStatus;

                    completed++;
                    StatusMessage = $"플래그 업데이트 중... ({completed}/{emails.Count})";
                }
                catch (Exception ex)
                {
                    Log4.Error($"플래그 업데이트 실패 [{email.Subject}]: {ex.Message}");
                }
            }

            // UI 갱신을 위해 목록 다시 설정
            Emails = new List<Email>(Emails);

            StatusMessage = $"플래그 업데이트 완료 ({completed}/{emails.Count})";
            Log4.Info($"플래그 '{flagStatus}' 업데이트 완료: {completed}건");
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
            int completed = 0;

            foreach (var email in emails)
            {
                if (string.IsNullOrEmpty(email.EntryId)) continue;

                try
                {
                    // Graph API로 읽음 상태 업데이트
                    await _graphMailService.UpdateMessageReadStatusAsync(email.EntryId, isRead);

                    // 로컬 DB 업데이트
                    var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                    if (dbEmail != null)
                    {
                        dbEmail.IsRead = isRead;
                        await _dbContext.SaveChangesAsync();
                    }

                    // 메모리 상 데이터 업데이트
                    email.IsRead = isRead;

                    completed++;
                    StatusMessage = $"{statusText} 표시 중... ({completed}/{emails.Count})";
                }
                catch (Exception ex)
                {
                    Log4.Error($"{statusText} 표시 실패 [{email.Subject}]: {ex.Message}");
                }
            }

            // UI 갱신을 위해 목록 다시 설정
            Emails = new List<Email>(Emails);

            // 폴더별 안읽은 메일 수 업데이트 (빠른 메모리 계산 방식)
            var folderChanges = emails
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
                    folder.UnreadItemCount = Math.Max(0, folder.UnreadItemCount + delta);
                    Log4.Debug($"폴더 안읽은 메일 수 업데이트: {folder.DisplayName} = {folder.UnreadItemCount} (delta: {delta})");
                }
            }

            // UI 갱신: 폴더 목록 한 번만 갱신
            if (folderChanges.Count > 0)
            {
                Folders = new List<Folder>(Folders);
            }

            StatusMessage = $"{statusText} 표시 완료 ({completed}/{emails.Count})";
            Log4.Info($"{statusText} 표시 완료: {completed}건");
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

            // 메모리 상 데이터 업데이트
            email.IsRead = true;

            // UI 즉시 갱신: 컬렉션을 새로 설정하여 바인딩 갱신
            Emails = new List<Email>(Emails);

            // 폴더 안읽은 메일 수 즉시 업데이트 (빠른 메모리 계산)
            if (!string.IsNullOrEmpty(email.ParentFolderId))
            {
                var folder = Folders.FirstOrDefault(f => f.Id == email.ParentFolderId);
                if (folder != null)
                {
                    folder.UnreadItemCount = Math.Max(0, folder.UnreadItemCount - 1);
                    Folders = new List<Folder>(Folders);
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

    #region Phase 1: 검색 기능

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
        else if (!string.IsNullOrWhiteSpace(AdvancedSearchSubject))
            SearchKeyword = AdvancedSearchSubject;
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

            var query = new SearchQuery
            {
                Keywords = SearchKeyword,
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
                .Where(e => string.IsNullOrEmpty(query.Keywords) ||
                           EF.Functions.Like(e.Subject ?? "", $"%{query.Keywords}%") ||
                           EF.Functions.Like(e.Body ?? "", $"%{query.Keywords}%") ||
                           EF.Functions.Like(e.From ?? "", $"%{query.Keywords}%"))
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

            Emails = results;
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
        FilterFromDate = null;
        FilterToDate = null;
        IsSearchMode = false;
        IsFilterPanelVisible = false;

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
        return FilterUnreadOnly || FilterHasAttachments || FilterFlaggedOnly ||
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
            int completed = 0;
            int failed = 0;

            foreach (var email in emails)
            {
                if (string.IsNullOrEmpty(email.EntryId)) continue;

                try
                {
                    // Graph API로 메일 이동 (이동 후 새 EntryId 반환됨)
                    var movedMessage = await _graphMailService.MoveMessageAsync(email.EntryId, targetFolder.Id);

                    // 로컬 DB 업데이트 (새 EntryId와 폴더 ID)
                    var dbEmail = await _dbContext.Emails.FindAsync(email.Id);
                    if (dbEmail != null)
                    {
                        // 이동하면 EntryId가 변경됨 - 새 ID로 업데이트
                        if (movedMessage != null && !string.IsNullOrEmpty(movedMessage.Id))
                        {
                            dbEmail.EntryId = movedMessage.Id;
                            email.EntryId = movedMessage.Id; // 메모리 상 객체도 업데이트
                        }
                        dbEmail.ParentFolderId = targetFolder.Id;
                        await _dbContext.SaveChangesAsync();
                    }

                    completed++;
                    StatusMessage = $"메일 이동 중... ({completed}/{emails.Count})";
                }
                catch (Exception ex)
                {
                    failed++;
                    Log4.Error($"메일 이동 실패 [{email.Subject}]: {ex.Message}");
                }
            }

            // 현재 폴더 목록에서 이동된 메일 제거
            var movedIds = emails.Select(e => e.Id).ToHashSet();
            Emails = Emails.Where(e => !movedIds.Contains(e.Id)).ToList();

            StatusMessage = failed > 0
                ? $"메일 이동 완료 ({completed}건 성공, {failed}건 실패)"
                : $"'{targetFolder.DisplayName}'으로 {completed}건 이동 완료";
            Log4.Info($"메일 이동 완료: {completed}건 → {targetFolder.DisplayName}");
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
            "date_desc" => unpinned.OrderByDescending(e => e.ReceivedDateTime),
            "date_asc" => unpinned.OrderBy(e => e.ReceivedDateTime),
            "sender_asc" => unpinned.OrderBy(e => e.From),
            "sender_desc" => unpinned.OrderByDescending(e => e.From),
            "subject_asc" => unpinned.OrderBy(e => e.Subject),
            "subject_desc" => unpinned.OrderByDescending(e => e.Subject),
            _ => unpinned.OrderByDescending(e => e.ReceivedDateTime)
        };

        // 고정 메일 + 정렬된 일반 메일
        var combined = pinned.Concat(sortedUnpinned).ToList();

        Emails.Clear();
        foreach (var email in combined)
        {
            Emails.Add(email);
        }
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
