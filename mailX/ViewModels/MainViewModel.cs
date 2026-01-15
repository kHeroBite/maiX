using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using mailX.Data;
using mailX.Models;
using mailX.Services.Sync;

namespace mailX.ViewModels;

/// <summary>
/// 메인 화면 ViewModel - 폴더/이메일 목록 관리
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly MailXDbContext _dbContext;
    private readonly BackgroundSyncService _syncService;

    public MainViewModel(MailXDbContext dbContext, BackgroundSyncService syncService)
    {
        _dbContext = dbContext;
        _syncService = syncService;

        // 동기화 상태 변경 이벤트 구독
        _syncService.PausedChanged += OnSyncPausedChanged;

        // 초기 상태 동기화
        _isSyncPaused = _syncService.IsPaused;
    }

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
    /// 동기화 버튼 아이콘 (▶ 또는 ■)
    /// </summary>
    public string SyncButtonIcon => IsSyncPaused ? "▶" : "■";

    /// <summary>
    /// 동기화 버튼 툴팁
    /// </summary>
    public string SyncButtonTooltip => IsSyncPaused ? "동기화 시작" : "동기화 중지";

    /// <summary>
    /// 동기화 상태 변경 이벤트 핸들러
    /// </summary>
    private void OnSyncPausedChanged(bool isPaused)
    {
        IsSyncPaused = isPaused;
    }

    /// <summary>
    /// IsSyncPaused 변경 시 관련 프로퍼티 알림
    /// </summary>
    partial void OnIsSyncPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(SyncButtonIcon));
        OnPropertyChanged(nameof(SyncButtonTooltip));
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
    /// 폴더 목록 로드
    /// </summary>
    [RelayCommand]
    private async Task LoadFoldersAsync()
    {
        await ExecuteAsync(async () =>
        {
            StatusMessage = "폴더 로딩 중...";

            Folders = await _dbContext.Folders
                .OrderBy(f => f.DisplayName)
                .ToListAsync();

            StatusMessage = $"{Folders.Count}개 폴더 로드됨";
        }, "폴더 로드 실패");
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

            Emails = await _dbContext.Emails
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
        StatusMessage = IsSyncPaused ? "동기화 재개됨" : "동기화 중지됨";
    }
}
