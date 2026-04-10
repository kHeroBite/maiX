using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using mAIx.Services.Graph;
using NLog;

namespace mAIx.ViewModels.Teams;

/// <summary>
/// 채널 파일 탭 Sub-ViewModel — Hub(TeamsViewModel)에서 채널별 Lazy 생성
/// </summary>
public partial class ChannelFilesViewModel : ObservableObject
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly GraphTeamsService _teamsService;
    private readonly string _teamId;
    private readonly string _channelId;

    /// <summary>채널 파일 목록</summary>
    [ObservableProperty]
    private ObservableCollection<ChannelFileViewModel> _files = new();

    /// <summary>로딩 중 여부</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>오프라인(연결 실패) 여부</summary>
    [ObservableProperty]
    private bool _isOffline;

    /// <summary>오류 발생 여부</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>오류 메시지</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>선택된 파일</summary>
    [ObservableProperty]
    private ChannelFileViewModel? _selectedFile;

    /// <summary>버전 기록 표시 여부</summary>
    [ObservableProperty]
    private bool _isVersionFlyoutOpen;

    /// <summary>버전 기록 목록</summary>
    [ObservableProperty]
    private ObservableCollection<FileVersionViewModel> _fileVersions = new();

    /// <summary>업로드 진행 중 여부</summary>
    [ObservableProperty]
    private bool _isUploading;

    public IRelayCommand<ChannelFileViewModel?> OpenFileCommand { get; }
    public IRelayCommand UploadFileCommand { get; }
    public IRelayCommand<string?> UploadFileFromPathCommand { get; }
    public IRelayCommand<ChannelFileViewModel?> DownloadFileCommand { get; }
    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand<ChannelFileViewModel?> ShowVersionsCommand { get; }

    /// <summary>파일 파이리 요청 이벤트 (View에서 파일 다이얼로그 표시)</summary>
    public event Func<Task<string?>>? FilePickRequested;

    /// <summary>
    /// 생성자
    /// </summary>
    public ChannelFilesViewModel(GraphTeamsService teamsService, string teamId, string channelId)
    {
        _teamsService = teamsService ?? throw new ArgumentNullException(nameof(teamsService));
        _teamId = teamId ?? throw new ArgumentNullException(nameof(teamId));
        _channelId = channelId ?? throw new ArgumentNullException(nameof(channelId));

        OpenFileCommand = new RelayCommand<ChannelFileViewModel?>(OpenFile);
        UploadFileCommand = new AsyncRelayCommand(UploadFileAsync);
        UploadFileFromPathCommand = new AsyncRelayCommand<string?>(UploadFileFromPathAsync);
        DownloadFileCommand = new RelayCommand<ChannelFileViewModel?>(DownloadFile);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowVersionsCommand = new AsyncRelayCommand<ChannelFileViewModel?>(ShowVersionsAsync);
    }

    /// <summary>채널 파일 로드</summary>
    public async Task LoadAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            var driveItems = await _teamsService.GetChannelFilesAsync(_teamId, _channelId);

            Files.Clear();
            foreach (var item in driveItems)
            {
                Files.Add(new ChannelFileViewModel
                {
                    Id = item.Id ?? string.Empty,
                    Name = item.Name ?? "(파일명 없음)",
                    Size = item.Size ?? 0,
                    LastModified = item.LastModifiedDateTime?.DateTime ?? DateTime.Now,
                    WebUrl = item.WebUrl ?? string.Empty,
                    IsFolder = item.Folder != null,
                    CreatedBy = item.CreatedBy?.User?.DisplayName ?? string.Empty,
                    DriveId = item.ParentReference?.DriveId ?? string.Empty
                });
            }

            IsOffline = false;
            _log.Debug("채널 파일 로드 완료: teamId={TeamId}, channelId={ChannelId}, count={Count}",
                _teamId, _channelId, Files.Count);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = "파일 목록을 불러오지 못했습니다.";
            IsOffline = ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase)
                     || ex.Message.Contains("connect", StringComparison.OrdinalIgnoreCase);
            _log.Error(ex, "채널 파일 로드 실패: teamId={TeamId}, channelId={ChannelId}", _teamId, _channelId);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>파일 열기 (브라우저)</summary>
    private void OpenFile(ChannelFileViewModel? file)
    {
        if (file == null || string.IsNullOrEmpty(file.WebUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(file.WebUrl) { UseShellExecute = true });
            _log.Debug("파일 열기: {Name}", file.Name);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "파일 열기 실패: {Url}", file.WebUrl);
        }
    }

    /// <summary>파일 다운로드 (브라우저로 webUrl 열기)</summary>
    private void DownloadFile(ChannelFileViewModel? file)
    {
        if (file == null || string.IsNullOrEmpty(file.WebUrl)) return;
        try
        {
            // webUrl에 다운로드 파라미터 추가
            var downloadUrl = file.WebUrl.Contains('?')
                ? file.WebUrl + "&download=1"
                : file.WebUrl + "?download=1";
            Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
            _log.Info("파일 다운로드 시작: {Name}", file.Name);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "파일 다운로드 실패: {Name}", file.Name);
        }
    }

    /// <summary>파일 업로드 (파일 선택 다이얼로그)</summary>
    private async Task UploadFileAsync()
    {
        if (FilePickRequested == null) return;
        var path = await FilePickRequested.Invoke();
        if (string.IsNullOrEmpty(path)) return;
        await UploadFileFromPathAsync(path);
    }

    /// <summary>경로로 파일 업로드</summary>
    private async Task UploadFileFromPathAsync(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        IsUploading = true;
        try
        {
            var fileName = Path.GetFileName(filePath);
            await using var stream = File.OpenRead(filePath);
            var result = await _teamsService.UploadChannelFileAsync(_teamId, _channelId, fileName, stream);
            if (result != null)
            {
                _log.Info("파일 업로드 완료: {FileName}", fileName);
                await LoadAsync();
            }
            else
            {
                _log.Error("파일 업로드 실패 (null 반환): {FileName}", fileName);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "파일 업로드 중 오류: {FilePath}", filePath);
        }
        finally
        {
            IsUploading = false;
        }
    }

    /// <summary>버전 기록 표시</summary>
    private async Task ShowVersionsAsync(ChannelFileViewModel? file)
    {
        if (file == null || string.IsNullOrEmpty(file.DriveId) || string.IsNullOrEmpty(file.Id)) return;

        SelectedFile = file;
        FileVersions.Clear();
        IsVersionFlyoutOpen = true;

        try
        {
            var versions = await _teamsService.GetFileVersionsAsync(file.DriveId, file.Id);
            foreach (var v in versions)
            {
                FileVersions.Add(new FileVersionViewModel
                {
                    VersionId = v.Id ?? string.Empty,
                    ModifiedBy = v.LastModifiedBy?.User?.DisplayName ?? string.Empty,
                    ModifiedAt = v.LastModifiedDateTime?.DateTime ?? DateTime.MinValue,
                    Size = v.Size ?? 0
                });
            }
            _log.Debug("파일 버전 로드 완료: {FileName}, 버전 수={Count}", file.Name, FileVersions.Count);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "파일 버전 로드 실패: {FileName}", file?.Name);
        }
    }
}

/// <summary>파일 버전 ViewModel</summary>
public class FileVersionViewModel
{
    public string VersionId { get; set; } = string.Empty;
    public string ModifiedBy { get; set; } = string.Empty;
    public DateTime ModifiedAt { get; set; }
    public long Size { get; set; }

    public string SizeDisplay
    {
        get
        {
            if (Size < 1024) return $"{Size} B";
            if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
            return $"{Size / (1024.0 * 1024):F1} MB";
        }
    }
}
