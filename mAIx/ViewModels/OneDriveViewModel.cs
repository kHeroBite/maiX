using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mAIx.Models;
using mAIx.Services;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.ViewModels;

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
    /// 그리드 뷰 여부
    /// </summary>
    [ObservableProperty]
    private bool _isGridView;

    /// <summary>
    /// 현재 선택 아이템 즐겨찾기 여부
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// 업로드 진행률 (0~100)
    /// </summary>
    [ObservableProperty]
    private double _uploadProgress;

    /// <summary>
    /// 업로드 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isUploading;

    /// <summary>
    /// 미리보기 패널 표시 여부
    /// </summary>
    [ObservableProperty]
    private bool _isPreviewVisible;

    /// <summary>
    /// 현재 사이드바 뷰 (home, myfiles, shared, recent)
    /// </summary>
    [ObservableProperty]
    private string _currentView = "myfiles";

    /// <summary>
    /// 현재 파일 필터 (all, word, excel, ppt, pdf)
    /// </summary>
    [ObservableProperty]
    private string _currentFilter = "all";

    /// <summary>
    /// 필터링된 아이템 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DriveItemViewModel> _filteredItems = new();

    /// <summary>
    /// 빠른 액세스 폴더 목록 (최근 접근한 폴더)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<QuickAccessFolderViewModel> _quickAccessFolders = new();

    /// <summary>
    /// 최근 파일 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DriveItemViewModel> _recentItems = new();

    /// <summary>
    /// 공유된 파일 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DriveItemViewModel> _sharedItems = new();

    /// <summary>
    /// 폴더 트리 (사이드바용)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FolderTreeItemViewModel> _folderTree = new();

    /// <summary>
    /// 빠른 액세스 아이템 목록 (우측 사이드 패널용)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<QuickAccessItemViewModel> _quickAccessItems = new();

    /// <summary>
    /// 휴지통 아이템 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<OneDriveRecycleBinItem> _trashItems = new();

    /// <summary>
    /// 선택된 휴지통 아이템
    /// </summary>
    [ObservableProperty]
    private OneDriveRecycleBinItem? _selectedTrashItem;

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

            // 폴더 트리도 함께 로드
            await LoadFolderTreeAsync();
        }, "폴더 로드 실패");
    }

    /// <summary>
    /// 폴더 트리 로드 (사이드바용)
    /// </summary>
    public async Task LoadFolderTreeAsync()
    {
        try
        {
            var items = await _oneDriveService.GetRootItemsAsync();
            var folders = items.Where(i => i.Folder != null).OrderBy(i => i.Name).ToList();

            FolderTree.Clear();
            var treeItems = new List<FolderTreeItemViewModel>();

            foreach (var folder in folders)
            {
                var treeItem = new FolderTreeItemViewModel
                {
                    Id = folder.Id ?? string.Empty,
                    Name = folder.Name ?? "알 수 없음",
                    HasChildren = false // 초기값 false, 실제 자식 폴더 확인 후 설정
                };
                treeItems.Add(treeItem);
                FolderTree.Add(treeItem);
            }

            // 병렬로 각 폴더의 자식 폴더 존재 여부 확인
            var tasks = treeItems.Select(async treeItem =>
            {
                try
                {
                    var childItems = await _oneDriveService.GetFolderItemsAsync(treeItem.Id);
                    var childFolders = childItems.Where(i => i.Folder != null).ToList();

                    if (childFolders.Count > 0)
                    {
                        treeItem.HasChildren = true;
                        // 더미 아이템 추가 (확장 아이콘 표시용)
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            treeItem.Children.Add(new FolderTreeItemViewModel { Name = "로딩 중...", Id = "__dummy__" });
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning("폴더 '{Name}' 자식 확인 실패: {Error}", treeItem.Name, ex.Message);
                }
            });

            await Task.WhenAll(tasks);

            _logger.Debug("폴더 트리 로드: {Count}개 폴더", FolderTree.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "폴더 트리 로드 실패");
        }
    }

    /// <summary>
    /// 폴더 트리 자식 폴더 로드 (지연 로딩)
    /// </summary>
    public async Task LoadFolderChildrenAsync(FolderTreeItemViewModel parentFolder)
    {
        if (parentFolder.IsLoaded || parentFolder.IsLoading) return;

        try
        {
            parentFolder.IsLoading = true;

            var items = await _oneDriveService.GetFolderItemsAsync(parentFolder.Id);
            var folders = items.Where(i => i.Folder != null).OrderBy(i => i.Name);

            parentFolder.Children.Clear();
            foreach (var folder in folders)
            {
                var treeItem = new FolderTreeItemViewModel
                {
                    Id = folder.Id ?? string.Empty,
                    Name = folder.Name ?? "알 수 없음",
                    ParentId = parentFolder.Id,
                    HasChildren = folder.Folder?.ChildCount > 0
                };
                // 자식이 있으면 더미 아이템 추가 (확장 아이콘 표시용)
                if (treeItem.HasChildren)
                {
                    treeItem.Children.Add(new FolderTreeItemViewModel { Name = "로딩 중...", Id = "__dummy__" });
                }
                parentFolder.Children.Add(treeItem);
            }

            parentFolder.IsLoaded = true;

            // 자식 폴더가 없으면 HasChildren을 false로 설정하고 접기
            if (parentFolder.Children.Count == 0)
            {
                parentFolder.HasChildren = false;
                parentFolder.IsExpanded = false;
            }

            _logger.Debug("폴더 '{Name}' 자식 로드: {Count}개", parentFolder.Name, parentFolder.Children.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "폴더 자식 로드 실패: {FolderId}", parentFolder.Id);
        }
        finally
        {
            parentFolder.IsLoading = false;
        }
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
        IsGridView = ViewMode == "grid";
    }

    /// <summary>
    /// 파일 미리보기 토글
    /// </summary>
    [RelayCommand]
    public void FilePreview()
    {
        if (SelectedItem != null && !SelectedItem.IsFolder)
        {
            IsPreviewVisible = !IsPreviewVisible;
        }
    }

    /// <summary>
    /// 공유 다이얼로그 열기
    /// </summary>
    [RelayCommand]
    public void Share()
    {
        // MainWindow.OneDrive.cs에서 처리 (다이얼로그 생성)
        _logger.Debug("공유 요청: {Item}", SelectedItem?.Name);
    }

    /// <summary>
    /// 버전 히스토리 다이얼로그 열기
    /// </summary>
    [RelayCommand]
    public void VersionHistory()
    {
        // MainWindow.OneDrive.cs에서 처리 (다이얼로그 생성)
        _logger.Debug("버전 히스토리 요청: {Item}", SelectedItem?.Name);
    }

    /// <summary>
    /// 즐겨찾기 토글
    /// </summary>
    [RelayCommand]
    public void AddToFavorites()
    {
        if (SelectedItem == null) return;
        IsFavorite = !IsFavorite;
        _logger.Information("즐겨찾기 {Action}: {Name}",
            IsFavorite ? "추가" : "제거", SelectedItem.Name);
    }

    /// <summary>
    /// 대용량 파일 업로드 (청크)
    /// </summary>
    [RelayCommand]
    public async Task UploadLargeFileAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            IsUploading = true;
            UploadProgress = 0;

            var fileName = Path.GetFileName(filePath);
            _logger.Information("대용량 파일 업로드 시작: {FileName}", fileName);

            var progress = new Progress<double>(p =>
            {
                UploadProgress = p;
            });

            await using var stream = File.OpenRead(filePath);
            await _oneDriveService.UploadLargeFileAsync(stream, CurrentFolderId, fileName, progress);

            _logger.Information("대용량 파일 업로드 완료: {FileName}", fileName);

            // 현재 폴더 새로고침
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "대용량 파일 업로드 실패");
        }
        finally
        {
            IsUploading = false;
            UploadProgress = 0;
        }
    }

    /// <summary>
    /// 현재 뷰 변경 (사이드바 네비게이션)
    /// </summary>
    [RelayCommand]
    public async Task ChangeViewAsync(string view)
    {
        CurrentView = view;
        _logger.Debug("OneDrive 뷰 변경: {View}", view);

        switch (view)
        {
            case "home":
            case "myfiles":
                await LoadRootAsync();
                break;
            case "recent":
                await LoadRecentItemsAsync();
                break;
            case "shared":
                await LoadSharedItemsAsync();
                break;
            case "favorites":
                await LoadFavoritesAsync();
                break;
            case "trash":
                await LoadTrashAsync();
                break;
        }
    }

    /// <summary>
    /// 즐겨찾기 파일 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadFavoritesAsync()
    {
        await ExecuteAsync(async () =>
        {
            // Graph API에서는 즐겨찾기를 직접 지원하지 않으므로,
            // 최근 파일 중 자주 접근한 파일을 표시 (추후 로컬 DB에 즐겨찾기 저장 기능 추가 가능)
            var items = await _oneDriveService.GetRecentItemsAsync(20);

            Items.Clear();
            foreach (var item in items.Take(10)) // 상위 10개만
            {
                Items.Add(CreateDriveItemViewModel(item));
            }

            // Breadcrumb 업데이트
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem { Name = "즐겨찾기", Path = "/favorites", Id = null });

            _logger.Information("즐겨찾기 로드: {Count}개 아이템", Items.Count);
        }, "즐겨찾기 로드 실패");
    }

    /// <summary>
    /// 휴지통 파일 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadTrashAsync()
    {
        _logger.Information("[OneDriveViewModel] LoadTrashAsync 호출");
        await ExecuteAsync(async () =>
        {
            _logger.Information("[OneDriveViewModel] ExecuteAsync 시작");
            Items.Clear();
            TrashItems.Clear();

            // Breadcrumb 업데이트
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem { Name = "휴지통", Path = "/trash", Id = null });

            // SharePoint REST API로 휴지통 아이템 조회
            var trashItems = await _oneDriveService.GetTrashItemsAsync(100);

            foreach (var item in trashItems)
            {
                TrashItems.Add(item);
            }

            _logger.Information("휴지통 로드: {Count}개 아이템", TrashItems.Count);
        }, "휴지통 로드 실패");
    }

    /// <summary>
    /// 휴지통 아이템 복원
    /// </summary>
    [RelayCommand]
    public async Task RestoreTrashItemAsync(OneDriveRecycleBinItem? item)
    {
        if (item == null) return;

        await ExecuteAsync(async () =>
        {
            var success = await _oneDriveService.RestoreTrashItemAsync(item.Id);
            if (success)
            {
                TrashItems.Remove(item);
                _logger.Information("휴지통 아이템 복원 성공: {Name}", item.LeafName);
            }
        }, "휴지통 아이템 복원 실패");
    }

    /// <summary>
    /// 휴지통 아이템 영구 삭제
    /// </summary>
    [RelayCommand]
    public async Task DeleteTrashItemPermanentlyAsync(OneDriveRecycleBinItem? item)
    {
        if (item == null) return;

        await ExecuteAsync(async () =>
        {
            var success = await _oneDriveService.DeleteTrashItemPermanentlyAsync(item.Id);
            if (success)
            {
                TrashItems.Remove(item);
                _logger.Information("휴지통 아이템 영구 삭제 성공: {Name}", item.LeafName);
            }
        }, "휴지통 아이템 영구 삭제 실패");
    }

    /// <summary>
    /// 빠른 액세스 아이템 로드 (최근 파일 기반)
    /// </summary>
    public async Task LoadQuickAccessItemsAsync()
    {
        try
        {
            _logger.Debug("빠른 액세스 로드 시작");
            var items = await _oneDriveService.GetRecentItemsAsync(15);
            _logger.Debug("최근 파일 API 응답: {Count}개", items?.Count() ?? 0);

            QuickAccessItems.Clear();
            
            var fileItems = items.Where(i => i.File != null).ToList();
            _logger.Debug("파일만 필터링: {Count}개", fileItems.Count);
            
            foreach (var item in fileItems)
            {
                var quickItem = new QuickAccessItemViewModel
                {
                    Id = item.Id ?? string.Empty,
                    Name = item.Name ?? "알 수 없음",
                    WebUrl = item.WebUrl ?? string.Empty,
                    IsFolder = item.Folder != null
                };

                // 설명 설정 (상위 폴더 경로)
                if (item.ParentReference?.Path != null)
                {
                    var pathParts = item.ParentReference.Path.Split('/');
                    quickItem.Description = pathParts.Length > 0 ? pathParts[^1] : "내 파일";
                }

                // 아이콘 설정
                quickItem.SetIconByFileType(item.Name ?? string.Empty);

                QuickAccessItems.Add(quickItem);
            }

            _logger.Debug("빠른 액세스 로드 완료: {Count}개 아이템", QuickAccessItems.Count);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "빠른 액세스 로드 실패");
        }
    }

    /// <summary>
    /// 파일 필터 적용
    /// </summary>
    [RelayCommand]
    public void ApplyFilter(string filter)
    {
        CurrentFilter = filter;
        FilteredItems.Clear();

        var source = CurrentView switch
        {
            "recent" => RecentItems,
            "shared" => SharedItems,
            _ => Items
        };

        IEnumerable<DriveItemViewModel> filtered = filter switch
        {
            "word" => source.Where(i => i.IconType == "Word"),
            "excel" => source.Where(i => i.IconType == "Excel"),
            "ppt" => source.Where(i => i.IconType == "PowerPoint"),
            "pdf" => source.Where(i => i.IconType == "PDF"),
            _ => source // "all"
        };

        foreach (var item in filtered)
        {
            FilteredItems.Add(item);
        }

        _logger.Debug("OneDrive 필터 적용: {Filter}, {Count}개 아이템", filter, FilteredItems.Count);
    }

    /// <summary>
    /// 최근 파일 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadRecentItemsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var items = await _oneDriveService.GetRecentItemsAsync(30);

            RecentItems.Clear();
            Items.Clear();
            foreach (var item in items)
            {
                var vm = CreateDriveItemViewModel(item);
                RecentItems.Add(vm);
                Items.Add(vm);
            }

            // Breadcrumb 업데이트
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem { Name = "최근", Path = "/recent", Id = null });

            _logger.Information("OneDrive 최근 파일 로드: {Count}개", RecentItems.Count);
        }, "최근 파일 로드 실패");
    }

    /// <summary>
    /// 공유된 파일 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadSharedItemsAsync()
    {
        await ExecuteAsync(async () =>
        {
            var items = await _oneDriveService.GetSharedWithMeAsync(50);

            SharedItems.Clear();
            Items.Clear();
            foreach (var item in items)
            {
                var vm = CreateDriveItemViewModel(item);
                // 공유자 정보 설정
                if (item.RemoteItem?.CreatedBy?.User != null)
                {
                    vm.SharedByDisplayName = item.RemoteItem.CreatedBy.User.DisplayName;
                    vm.OwnerDisplayName = item.RemoteItem.CreatedBy.User.DisplayName ?? "알 수 없음";
                }
                SharedItems.Add(vm);
                Items.Add(vm);
            }

            // Breadcrumb 업데이트
            Breadcrumbs.Clear();
            Breadcrumbs.Add(new BreadcrumbItem { Name = "공유됨", Path = "/shared", Id = null });

            _logger.Information("OneDrive 공유 파일 로드: {Count}개", SharedItems.Count);
        }, "공유 파일 로드 실패");
    }

    /// <summary>
    /// 빠른 액세스에 폴더 추가 (최근 방문 폴더 기록)
    /// </summary>
    public void AddToQuickAccess(string folderId, string folderName, string path)
    {
        // 이미 있으면 업데이트
        var existing = QuickAccessFolders.FirstOrDefault(f => f.Id == folderId);
        if (existing != null)
        {
            existing.LastAccessedAt = DateTime.Now;
        }
        else
        {
            // 최대 5개 유지
            if (QuickAccessFolders.Count >= 5)
            {
                var oldest = QuickAccessFolders.OrderBy(f => f.LastAccessedAt).First();
                QuickAccessFolders.Remove(oldest);
            }

            QuickAccessFolders.Add(new QuickAccessFolderViewModel
            {
                Id = folderId,
                Name = folderName,
                Path = path,
                LastAccessedAt = DateTime.Now
            });
        }
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
    /// 소유자 표시 이름
    /// </summary>
    [ObservableProperty]
    private string _ownerDisplayName = "나";

    /// <summary>
    /// 공유자 표시 이름 (공유된 파일의 경우)
    /// </summary>
    [ObservableProperty]
    private string? _sharedByDisplayName;

    /// <summary>
    /// 부모 폴더 경로 (검색/최근 파일에서 사용)
    /// </summary>
    [ObservableProperty]
    private string? _parentPath;

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
/// <summary>
/// 빠른 액세스 폴더 ViewModel
/// </summary>
public class QuickAccessFolderViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastAccessedAt { get; set; }
}

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

/// <summary>
/// 폴더 트리 아이템 ViewModel (Windows 탐색기 스타일)
/// </summary>
public partial class FolderTreeItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _parentId;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasChildren = true;

    [ObservableProperty]
    private ObservableCollection<FolderTreeItemViewModel> _children = new();

    /// <summary>
    /// 자식 폴더가 로드되었는지 여부
    /// </summary>
    public bool IsLoaded { get; set; }
}

/// <summary>
/// 빠른 액세스 아이템 ViewModel (우측 사이드 패널용)
/// </summary>
public partial class QuickAccessItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _iconText = string.Empty;

    [ObservableProperty]
    private string _iconBackground = "#6264A7";

    [ObservableProperty]
    private string _webUrl = string.Empty;

    [ObservableProperty]
    private bool _isFolder;

    [ObservableProperty]
    private string _fileType = string.Empty;

    /// <summary>
    /// 파일 타입에 따른 아이콘 배경색 설정
    /// </summary>
    public void SetIconByFileType(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName)?.ToLowerInvariant();
        (IconText, IconBackground) = ext switch
        {
            ".docx" or ".doc" => ("W", "#2B579A"),
            ".xlsx" or ".xls" => ("X", "#217346"),
            ".pptx" or ".ppt" => ("P", "#D24726"),
            ".pdf" => ("PDF", "#F40F02"),
            ".txt" => ("T", "#666666"),
            ".jpg" or ".jpeg" or ".png" or ".gif" => ("IMG", "#0078D4"),
            _ => ("F", "#6264A7")
        };
        FileType = ext ?? string.Empty;
    }
}


/// <summary>
/// 사람별 파일 그룹 ViewModel (Teams OneDrive 스타일)
/// </summary>
public partial class PersonFilesGroupViewModel : ObservableObject
{
    /// <summary>
    /// 사람 이름
    /// </summary>
    [ObservableProperty]
    private string _personName = string.Empty;

    /// <summary>
    /// 사람 이니셜 (프로필 아이콘용)
    /// </summary>
    public string PersonInitials
    {
        get
        {
            if (string.IsNullOrEmpty(PersonName)) return "?";
            var parts = PersonName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
            return PersonName.Length >= 2 ? PersonName[..2].ToUpper() : PersonName.ToUpper();
        }
    }

    /// <summary>
    /// 이 사람이 공유한 파일 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PersonFileItemViewModel> _files = new();

    /// <summary>
    /// 표시되지 않은 추가 파일 수
    /// </summary>
    [ObservableProperty]
    private int _moreFilesCount;

    /// <summary>
    /// 더 많은 파일이 있는지 여부
    /// </summary>
    public bool HasMoreFiles => MoreFilesCount > 0;

    /// <summary>
    /// "+N개 더보기" 텍스트
    /// </summary>
    public string MoreFilesText => $"+{MoreFilesCount}개";
}

/// <summary>
/// 사람별 파일 아이템 ViewModel
/// </summary>
public partial class PersonFileItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _webUrl = string.Empty;

    [ObservableProperty]
    private string _iconText = "F";

    [ObservableProperty]
    private string _iconBackground = "#6264A7";

    /// <summary>
    /// 파일 타입에 따른 아이콘 설정
    /// </summary>
    public void SetIconByFileName(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName)?.ToLowerInvariant();
        (IconText, IconBackground) = ext switch
        {
            ".docx" or ".doc" => ("W", "#2B579A"),
            ".xlsx" or ".xls" => ("X", "#217346"),
            ".pptx" or ".ppt" => ("P", "#D24726"),
            ".pdf" => ("PDF", "#F40F02"),
            ".txt" => ("T", "#666666"),
            ".jpg" or ".jpeg" or ".png" or ".gif" => ("IMG", "#0078D4"),
            ".mp4" or ".avi" or ".mov" => ("VID", "#9A0089"),
            _ => ("F", "#6264A7")
        };
    }
}

/// <summary>
/// 모임별 파일 그룹 ViewModel (Teams OneDrive 스타일)
/// </summary>
public partial class MeetingFilesGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private string _meetingId = string.Empty;

    [ObservableProperty]
    private string _meetingTitle = string.Empty;

    [ObservableProperty]
    private string _meetingTime = string.Empty; // "오전 9:00"

    [ObservableProperty]
    private string _meetingDate = string.Empty; // "2025년 10월 31일"

    [ObservableProperty]
    private DateTime _meetingDateTime;

    [ObservableProperty]
    private ObservableCollection<MeetingAttendeeViewModel> _attendees = new();

    [ObservableProperty]
    private int _moreAttendeesCount;

    public bool HasMoreAttendees => MoreAttendeesCount > 0;

    public string MoreAttendeesText => $"+{MoreAttendeesCount}";

    [ObservableProperty]
    private string _organizerText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PersonFileItemViewModel> _files = new();
}

/// <summary>
/// 모임 참석자 ViewModel
/// </summary>
public partial class MeetingAttendeeViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    public string Initials
    {
        get
        {
            if (string.IsNullOrEmpty(Name)) return "?";
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
            return Name.Length >= 2 ? Name[..2].ToUpper() : Name.ToUpper();
        }
    }
}

/// <summary>
/// 미디어 날짜별 그룹 ViewModel
/// </summary>
public partial class MediaDateGroupViewModel : ObservableObject
{
    [ObservableProperty]
    private DateTime _date;

    /// <summary>
    /// 날짜 헤더 표시 ("1월 23일")
    /// </summary>
    public string DateHeader => Date.ToString("M월 d일");

    [ObservableProperty]
    private ObservableCollection<MediaItemViewModel> _items = new();
}

/// <summary>
/// 미디어 아이템 ViewModel
/// </summary>
public partial class MediaItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _webUrl = string.Empty;

    [ObservableProperty]
    private string? _thumbnailUrl;

    [ObservableProperty]
    private bool _isVideo;

    [ObservableProperty]
    private DateTime _createdDateTime;

    /// <summary>
    /// 썸네일이 있는지 여부
    /// </summary>
    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailUrl);
}
