using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mailX.Services.Graph;
using Serilog;

namespace mailX.ViewModels;

/// <summary>
/// OneDrive ViewModel - 파일/폴더 관리
/// </summary>
public partial class OneDriveViewModel : ViewModelBase
{
    private readonly GraphOneDriveService _oneDriveService;
    private readonly ILogger _logger;

    /// <summary>
    /// 현재 폴더의 아이템 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DriveItemViewModel> _items = new();

    /// <summary>
    /// 현재 경로 (Breadcrumb용)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<BreadcrumbItem> _breadcrumbs = new();

    /// <summary>
    /// 현재 폴더 ID
    /// </summary>
    [ObservableProperty]
    private string? _currentFolderId;

    /// <summary>
    /// 현재 폴더 경로
    /// </summary>
    [ObservableProperty]
    private string _currentPath = "/";

    /// <summary>
    /// 선택된 아이템
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedItem))]
    private DriveItemViewModel? _selectedItem;

    /// <summary>
    /// 선택된 아이템 목록 (다중 선택)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DriveItemViewModel> _selectedItems = new();

    /// <summary>
    /// 검색어
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// 검색 결과
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DriveItemViewModel> _searchResults = new();

    /// <summary>
    /// 드라이브 정보
    /// </summary>
    [ObservableProperty]
    private DriveInfoViewModel? _driveInfo;

    /// <summary>
    /// 뷰 모드 (list, grid)
    /// </summary>
    [ObservableProperty]
    private string _viewMode = "list";

    /// <summary>
    /// 선택된 아이템이 있는지 여부
    /// </summary>
    public bool HasSelectedItem => SelectedItem != null;

    public OneDriveViewModel(GraphOneDriveService oneDriveService)
    {
        _oneDriveService = oneDriveService ?? throw new ArgumentNullException(nameof(oneDriveService));
        _logger = Log.ForContext<OneDriveViewModel>();

        // 초기 Breadcrumb 설정
        Breadcrumbs.Add(new BreadcrumbItem { Name = "내 파일", Path = "/", Id = null });
    }

    /// <summary>
    /// 루트 폴더 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadRootAsync()
    {
        await ExecuteAsync(async () =>
        {
            CurrentFolderId = null;
            CurrentPath = "/";

            // Breadcrumb 초기화
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem { Name = "내 파일", Path = "/", Id = null });

            var items = await _oneDriveService.GetRootItemsAsync();

            Items.Clear();
            foreach (var item in items.OrderByDescending(i => i.Folder != null).ThenBy(i => i.Name))
            {
                Items.Add(CreateDriveItemViewModel(item));
            }

            _logger.Information("OneDrive 루트 폴더 로드: {Count}개 아이템", Items.Count);
        }, "폴더 로드 실패");
    }

    /// <summary>
    /// 특정 폴더로 이동
    /// </summary>
    [RelayCommand]
    public async Task NavigateToFolderAsync(string folderId)
    {
        if (string.IsNullOrEmpty(folderId))
        {
            await LoadRootAsync();
            return;
        }

        await ExecuteAsync(async () =>
        {
            CurrentFolderId = folderId;

            var items = await _oneDriveService.GetFolderItemsAsync(folderId);

            Items.Clear();
            foreach (var item in items.OrderByDescending(i => i.Folder != null).ThenBy(i => i.Name))
            {
                Items.Add(CreateDriveItemViewModel(item));
            }

            _logger.Debug("폴더 {FolderId} 로드: {Count}개 아이템", folderId, Items.Count);
        }, "폴더 로드 실패");
    }

    /// <summary>
    /// 아이템 더블클릭 (폴더면 이동, 파일이면 열기)
    /// </summary>
    [RelayCommand]
    public async Task OpenItemAsync(DriveItemViewModel item)
    {
        if (item == null) return;

        if (item.IsFolder)
        {
            // 폴더로 이동
            await NavigateToFolderAsync(item.Id);

            // Breadcrumb 업데이트
            Breadcrumbs.Add(new BreadcrumbItem
            {
                Name = item.Name,
                Path = CurrentPath + item.Name + "/",
                Id = item.Id
            });
            CurrentPath = Breadcrumbs.Last().Path;
        }
        else
        {
            // 파일 열기 (다운로드 또는 브라우저에서 열기)
            _logger.Information("파일 열기: {Name}", item.Name);
            // TODO: 파일 다운로드 또는 미리보기 구현
        }
    }

    /// <summary>
    /// Breadcrumb 클릭으로 특정 경로로 이동
    /// </summary>
    [RelayCommand]
    public async Task NavigateToBreadcrumbAsync(BreadcrumbItem breadcrumb)
    {
        if (breadcrumb == null) return;

        // 클릭한 Breadcrumb 이후 항목 제거
        var index = Breadcrumbs.IndexOf(breadcrumb);
        if (index >= 0)
        {
            while (Breadcrumbs.Count > index + 1)
            {
                Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
            }
        }

        CurrentPath = breadcrumb.Path;
        await NavigateToFolderAsync(breadcrumb.Id ?? string.Empty);
    }

    /// <summary>
    /// 상위 폴더로 이동
    /// </summary>
    [RelayCommand]
    public async Task GoUpAsync()
    {
        if (Breadcrumbs.Count > 1)
        {
            Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
            var parent = Breadcrumbs.Last();
            CurrentPath = parent.Path;
            await NavigateToFolderAsync(parent.Id ?? string.Empty);
        }
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (string.IsNullOrEmpty(CurrentFolderId))
        {
            await LoadRootAsync();
        }
        else
        {
            await NavigateToFolderAsync(CurrentFolderId);
        }
    }

    /// <summary>
    /// 새 폴더 생성
    /// </summary>
    [RelayCommand]
    public async Task CreateFolderAsync(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return;

        await ExecuteAsync(async () =>
        {
            var newFolder = await _oneDriveService.CreateFolderAsync(CurrentFolderId, folderName);
            if (newFolder != null)
            {
                Items.Insert(0, CreateDriveItemViewModel(newFolder));
                _logger.Information("폴더 생성 완료: {Name}", folderName);
            }
        }, "폴더 생성 실패");
    }

    /// <summary>
    /// 아이템 삭제
    /// </summary>
    [RelayCommand]
    public async Task DeleteItemAsync(DriveItemViewModel item)
    {
        if (item == null) return;

        await ExecuteAsync(async () =>
        {
            var success = await _oneDriveService.DeleteItemAsync(item.Id);
            if (success)
            {
                Items.Remove(item);
                _logger.Information("아이템 삭제 완료: {Name}", item.Name);
            }
        }, "삭제 실패");
    }

    /// <summary>
    /// 아이템 이름 변경
    /// </summary>
    public async Task RenameItemAsync(DriveItemViewModel item, string newName)
    {
        if (item == null || string.IsNullOrWhiteSpace(newName))
            return;

        await ExecuteAsync(async () =>
        {
            var updated = await _oneDriveService.RenameItemAsync(item.Id, newName);
            if (updated != null)
            {
                item.Name = updated.Name ?? newName;
                _logger.Information("아이템 이름 변경 완료: {Name}", newName);
            }
        }, "이름 변경 실패");
    }

    /// <summary>
    /// 검색
    /// </summary>
    [RelayCommand]
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        await ExecuteAsync(async () =>
        {
            var results = await _oneDriveService.SearchAsync(SearchQuery);

            SearchResults.Clear();
            foreach (var item in results)
            {
                SearchResults.Add(CreateDriveItemViewModel(item));
            }

            _logger.Information("검색 '{Query}': {Count}개 결과", SearchQuery, SearchResults.Count);
        }, "검색 실패");
    }

    /// <summary>
    /// 드라이브 정보 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadDriveInfoAsync()
    {
        await ExecuteAsync(async () =>
        {
            var drive = await _oneDriveService.GetDriveInfoAsync();
            if (drive != null)
            {
                DriveInfo = new DriveInfoViewModel
                {
                    Total = drive.Quota?.Total ?? 0,
                    Used = drive.Quota?.Used ?? 0,
                    Remaining = drive.Quota?.Remaining ?? 0,
                    TotalDisplay = GraphOneDriveService.FormatFileSize(drive.Quota?.Total),
                    UsedDisplay = GraphOneDriveService.FormatFileSize(drive.Quota?.Used),
                    RemainingDisplay = GraphOneDriveService.FormatFileSize(drive.Quota?.Remaining)
                };
            }
        }, "드라이브 정보 로드 실패");
    }

    /// <summary>
    /// 뷰 모드 전환
    /// </summary>
    [RelayCommand]
    public void ToggleViewMode()
    {
        ViewMode = ViewMode == "list" ? "grid" : "list";
    }

    /// <summary>
    /// DriveItem을 DriveItemViewModel로 변환
    /// </summary>
    private DriveItemViewModel CreateDriveItemViewModel(DriveItem item)
    {
        return new DriveItemViewModel
        {
            Id = item.Id ?? string.Empty,
            Name = item.Name ?? "Untitled",
            IsFolder = item.Folder != null,
            Size = item.Size ?? 0,
            SizeDisplay = GraphOneDriveService.FormatFileSize(item.Size),
            LastModifiedDateTime = item.LastModifiedDateTime?.DateTime,
            CreatedDateTime = item.CreatedDateTime?.DateTime,
            WebUrl = item.WebUrl,
            MimeType = item.File?.MimeType,
            ChildCount = item.Folder?.ChildCount ?? 0
        };
    }
}

/// <summary>
/// 드라이브 아이템 ViewModel
/// </summary>
public partial class DriveItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isFolder;

    [ObservableProperty]
    private long _size;

    [ObservableProperty]
    private string _sizeDisplay = string.Empty;

    [ObservableProperty]
    private DateTime? _lastModifiedDateTime;

    [ObservableProperty]
    private DateTime? _createdDateTime;

    [ObservableProperty]
    private string? _webUrl;

    [ObservableProperty]
    private string? _mimeType;

    [ObservableProperty]
    private int _childCount;

    /// <summary>
    /// 아이콘 타입 (폴더/파일 구분)
    /// </summary>
    public string IconType => IsFolder ? "Folder" : GetFileIconType();

    /// <summary>
    /// 수정 시간 표시
    /// </summary>
    public string LastModifiedDisplay
    {
        get
        {
            if (!LastModifiedDateTime.HasValue)
                return string.Empty;

            var diff = DateTime.Now - LastModifiedDateTime.Value;
            if (diff.TotalMinutes < 1)
                return "방금 전";
            if (diff.TotalHours < 1)
                return $"{(int)diff.TotalMinutes}분 전";
            if (diff.TotalDays < 1)
                return $"{(int)diff.TotalHours}시간 전";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}일 전";

            return LastModifiedDateTime.Value.ToString("yyyy-MM-dd HH:mm");
        }
    }

    /// <summary>
    /// 파일 확장자에 따른 아이콘 타입
    /// </summary>
    private string GetFileIconType()
    {
        var extension = System.IO.Path.GetExtension(Name)?.ToLowerInvariant();
        return extension switch
        {
            ".doc" or ".docx" => "Word",
            ".xls" or ".xlsx" => "Excel",
            ".ppt" or ".pptx" => "PowerPoint",
            ".pdf" => "PDF",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" => "Image",
            ".mp4" or ".avi" or ".mov" or ".wmv" => "Video",
            ".mp3" or ".wav" or ".wma" => "Audio",
            ".zip" or ".rar" or ".7z" => "Archive",
            ".txt" or ".md" => "Text",
            ".cs" or ".js" or ".ts" or ".py" or ".java" => "Code",
            _ => "File"
        };
    }
}

/// <summary>
/// Breadcrumb 아이템
/// </summary>
public class BreadcrumbItem
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? Id { get; set; }
}

/// <summary>
/// 드라이브 정보 ViewModel
/// </summary>
public partial class DriveInfoViewModel : ObservableObject
{
    [ObservableProperty]
    private long _total;

    [ObservableProperty]
    private long _used;

    [ObservableProperty]
    private long _remaining;

    [ObservableProperty]
    private string _totalDisplay = string.Empty;

    [ObservableProperty]
    private string _usedDisplay = string.Empty;

    [ObservableProperty]
    private string _remainingDisplay = string.Empty;

    /// <summary>
    /// 사용률 (0-100)
    /// </summary>
    public double UsagePercentage => Total > 0 ? (double)Used / Total * 100 : 0;
}
