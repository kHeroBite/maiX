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
/// OneNote ViewModel - 노트북/섹션/페이지 관리
/// </summary>
public partial class OneNoteViewModel : ViewModelBase
{
    private readonly GraphOneNoteService _oneNoteService;
    private readonly ILogger _logger;

    /// <summary>
    /// 노트북 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<NotebookItemViewModel> _notebooks = new();

    /// <summary>
    /// 선택된 노트북
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedNotebook))]
    private NotebookItemViewModel? _selectedNotebook;

    /// <summary>
    /// 선택된 섹션
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedSection))]
    private SectionItemViewModel? _selectedSection;

    /// <summary>
    /// 선택된 페이지
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPage))]
    private PageItemViewModel? _selectedPage;

    /// <summary>
    /// 현재 페이지 HTML 콘텐츠
    /// </summary>
    [ObservableProperty]
    private string? _currentPageContent;

    /// <summary>
    /// 최근 페이지 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageItemViewModel> _recentPages = new();

    /// <summary>
    /// 검색어
    /// </summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>
    /// 검색 결과 페이지 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageItemViewModel> _searchResults = new();

    /// <summary>
    /// 페이지 콘텐츠 로딩 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingContent;

    /// <summary>
    /// 선택된 노트북이 있는지 여부
    /// </summary>
    public bool HasSelectedNotebook => SelectedNotebook != null;

    /// <summary>
    /// 선택된 섹션이 있는지 여부
    /// </summary>
    public bool HasSelectedSection => SelectedSection != null;

    /// <summary>
    /// 선택된 페이지가 있는지 여부
    /// </summary>
    public bool HasSelectedPage => SelectedPage != null;

    public OneNoteViewModel(GraphOneNoteService oneNoteService)
    {
        _oneNoteService = oneNoteService ?? throw new ArgumentNullException(nameof(oneNoteService));
        _logger = Log.ForContext<OneNoteViewModel>();
    }

    /// <summary>
    /// 노트북 목록 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadNotebooksAsync()
    {
        await ExecuteAsync(async () =>
        {
            var notebooks = await _oneNoteService.GetNotebooksAsync();

            Notebooks.Clear();
            foreach (var notebook in notebooks)
            {
                var notebookItem = new NotebookItemViewModel
                {
                    Id = notebook.Id ?? string.Empty,
                    DisplayName = notebook.DisplayName ?? "Untitled",
                    CreatedDateTime = notebook.CreatedDateTime?.DateTime,
                    LastModifiedDateTime = notebook.LastModifiedDateTime?.DateTime,
                    IsExpanded = false
                };

                // 섹션 로드
                var sections = await _oneNoteService.GetSectionsAsync(notebook.Id ?? string.Empty);
                foreach (var section in sections)
                {
                    notebookItem.Sections.Add(new SectionItemViewModel
                    {
                        Id = section.Id ?? string.Empty,
                        DisplayName = section.DisplayName ?? "Untitled",
                        NotebookId = notebook.Id ?? string.Empty,
                        NotebookName = notebook.DisplayName ?? string.Empty,
                        IsDefault = section.IsDefault ?? false
                    });
                }

                Notebooks.Add(notebookItem);
            }

            _logger.Information("노트북 {Count}개 로드 완료", Notebooks.Count);
        }, "노트북 목록 로드 실패");
    }

    /// <summary>
    /// 선택된 노트북 변경 시 섹션 로드
    /// </summary>
    partial void OnSelectedNotebookChanged(NotebookItemViewModel? value)
    {
        if (value != null)
        {
            value.IsExpanded = true;
        }
    }

    /// <summary>
    /// 선택된 섹션 변경 시 페이지 목록 로드
    /// </summary>
    partial void OnSelectedSectionChanged(SectionItemViewModel? value)
    {
        if (value != null)
        {
            _ = LoadPagesAsync(value.Id);
        }
    }

    /// <summary>
    /// 선택된 페이지 변경 시 콘텐츠 로드
    /// </summary>
    partial void OnSelectedPageChanged(PageItemViewModel? value)
    {
        if (value != null)
        {
            _ = LoadPageContentAsync(value.Id);
        }
        else
        {
            CurrentPageContent = null;
        }
    }

    /// <summary>
    /// 섹션의 페이지 목록 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadPagesAsync(string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId))
            return;

        await ExecuteAsync(async () =>
        {
            var pages = await _oneNoteService.GetPagesAsync(sectionId);

            if (SelectedSection != null)
            {
                SelectedSection.Pages.Clear();
                foreach (var page in pages)
                {
                    SelectedSection.Pages.Add(new PageItemViewModel
                    {
                        Id = page.Id ?? string.Empty,
                        Title = page.Title ?? "Untitled",
                        SectionId = sectionId,
                        CreatedDateTime = page.CreatedDateTime?.DateTime,
                        LastModifiedDateTime = page.LastModifiedDateTime?.DateTime
                    });
                }
            }

            _logger.Debug("섹션 {SectionId} 페이지 {Count}개 로드", sectionId, pages.Count());
        }, "페이지 목록 로드 실패");
    }

    /// <summary>
    /// 페이지 콘텐츠 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadPageContentAsync(string pageId)
    {
        if (string.IsNullOrEmpty(pageId))
            return;

        try
        {
            IsLoadingContent = true;
            CurrentPageContent = null;

            var content = await _oneNoteService.GetPageContentAsync(pageId);
            CurrentPageContent = content;

            _logger.Debug("페이지 {PageId} 콘텐츠 로드 완료", pageId);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "페이지 콘텐츠 로드 실패: PageId={PageId}", pageId);
            ErrorMessage = $"페이지 콘텐츠 로드 실패: {ex.Message}";
        }
        finally
        {
            IsLoadingContent = false;
        }
    }

    /// <summary>
    /// 최근 페이지 로드
    /// </summary>
    [RelayCommand]
    public async Task LoadRecentPagesAsync()
    {
        await ExecuteAsync(async () =>
        {
            var notebooks = await _oneNoteService.GetNotebooksAsync();
            var allPages = new System.Collections.Generic.List<PageItemViewModel>();

            foreach (var notebook in notebooks)
            {
                var sections = await _oneNoteService.GetSectionsAsync(notebook.Id ?? string.Empty);
                foreach (var section in sections)
                {
                    var pages = await _oneNoteService.GetPagesAsync(section.Id ?? string.Empty);
                    foreach (var page in pages)
                    {
                        allPages.Add(new PageItemViewModel
                        {
                            Id = page.Id ?? string.Empty,
                            Title = page.Title ?? "Untitled",
                            SectionId = section.Id ?? string.Empty,
                            SectionName = section.DisplayName ?? string.Empty,
                            NotebookName = notebook.DisplayName ?? string.Empty,
                            CreatedDateTime = page.CreatedDateTime?.DateTime,
                            LastModifiedDateTime = page.LastModifiedDateTime?.DateTime
                        });
                    }
                }
            }

            RecentPages.Clear();
            foreach (var page in allPages.OrderByDescending(p => p.LastModifiedDateTime).Take(20))
            {
                RecentPages.Add(page);
            }

            _logger.Debug("최근 페이지 {Count}개 로드", RecentPages.Count);
        }, "최근 페이지 로드 실패");
    }

    /// <summary>
    /// 페이지 검색
    /// </summary>
    [RelayCommand]
    public async Task SearchPagesAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return;

        await ExecuteAsync(async () =>
        {
            // 모든 노트북에서 페이지 검색
            var notebooks = await _oneNoteService.GetNotebooksAsync();
            var allPages = new System.Collections.Generic.List<PageItemViewModel>();

            foreach (var notebook in notebooks)
            {
                var sections = await _oneNoteService.GetSectionsAsync(notebook.Id ?? string.Empty);
                foreach (var section in sections)
                {
                    var pages = await _oneNoteService.GetPagesAsync(section.Id ?? string.Empty);
                    foreach (var page in pages)
                    {
                        if (page.Title?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            allPages.Add(new PageItemViewModel
                            {
                                Id = page.Id ?? string.Empty,
                                Title = page.Title ?? "Untitled",
                                SectionId = section.Id ?? string.Empty,
                                SectionName = section.DisplayName ?? string.Empty,
                                NotebookName = notebook.DisplayName ?? string.Empty,
                                CreatedDateTime = page.CreatedDateTime?.DateTime,
                                LastModifiedDateTime = page.LastModifiedDateTime?.DateTime
                            });
                        }
                    }
                }
            }

            SearchResults.Clear();
            foreach (var page in allPages)
            {
                SearchResults.Add(page);
            }

            _logger.Information("검색 '{Query}': {Count}개 결과", SearchQuery, SearchResults.Count);
        }, "페이지 검색 실패");
    }

    /// <summary>
    /// 새 노트북 생성
    /// </summary>
    [RelayCommand]
    public async Task CreateNotebookAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return;

        await ExecuteAsync(async () =>
        {
            var notebook = await _oneNoteService.CreateNotebookAsync(displayName);
            if (notebook != null)
            {
                var notebookItem = new NotebookItemViewModel
                {
                    Id = notebook.Id ?? string.Empty,
                    DisplayName = notebook.DisplayName ?? displayName,
                    CreatedDateTime = notebook.CreatedDateTime?.DateTime,
                    LastModifiedDateTime = notebook.LastModifiedDateTime?.DateTime
                };
                Notebooks.Add(notebookItem);
                _logger.Information("노트북 생성 완료: {Name}", displayName);
            }
        }, "노트북 생성 실패");
    }

    /// <summary>
    /// 새 섹션 생성
    /// </summary>
    [RelayCommand]
    public async Task CreateSectionAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || SelectedNotebook == null)
            return;

        await ExecuteAsync(async () =>
        {
            var section = await _oneNoteService.CreateSectionAsync(SelectedNotebook.Id, displayName);
            if (section != null)
            {
                var sectionItem = new SectionItemViewModel
                {
                    Id = section.Id ?? string.Empty,
                    DisplayName = section.DisplayName ?? displayName,
                    NotebookId = SelectedNotebook.Id,
                    NotebookName = SelectedNotebook.DisplayName
                };
                SelectedNotebook.Sections.Add(sectionItem);
                _logger.Information("섹션 생성 완료: {Name}", displayName);
            }
        }, "섹션 생성 실패");
    }

    /// <summary>
    /// 새 페이지 생성
    /// </summary>
    [RelayCommand]
    public async Task CreatePageAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || SelectedSection == null)
            return;

        await ExecuteAsync(async () =>
        {
            var page = await _oneNoteService.CreatePageAsync(SelectedSection.Id, title, null);
            if (page != null)
            {
                var pageItem = new PageItemViewModel
                {
                    Id = page.Id ?? string.Empty,
                    Title = page.Title ?? title,
                    SectionId = SelectedSection.Id,
                    CreatedDateTime = page.CreatedDateTime?.DateTime
                };
                SelectedSection.Pages.Add(pageItem);
                _logger.Information("페이지 생성 완료: {Title}", title);
            }
        }, "페이지 생성 실패");
    }
}

/// <summary>
/// 노트북 아이템 ViewModel
/// </summary>
public partial class NotebookItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private DateTime? _createdDateTime;

    [ObservableProperty]
    private DateTime? _lastModifiedDateTime;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<SectionItemViewModel> _sections = new();

    /// <summary>
    /// 표시용 날짜
    /// </summary>
    public string LastModifiedDisplay
    {
        get
        {
            if (!LastModifiedDateTime.HasValue)
                return string.Empty;

            var diff = DateTime.Now - LastModifiedDateTime.Value;
            if (diff.TotalDays < 1)
                return "오늘";
            if (diff.TotalDays < 7)
                return $"{(int)diff.TotalDays}일 전";
            return LastModifiedDateTime.Value.ToString("MM/dd");
        }
    }
}

/// <summary>
/// 섹션 아이템 ViewModel
/// </summary>
public partial class SectionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _notebookId = string.Empty;

    [ObservableProperty]
    private string _notebookName = string.Empty;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private ObservableCollection<PageItemViewModel> _pages = new();
}

/// <summary>
/// 페이지 아이템 ViewModel
/// </summary>
public partial class PageItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _sectionId = string.Empty;

    [ObservableProperty]
    private string _sectionName = string.Empty;

    [ObservableProperty]
    private string _notebookName = string.Empty;

    [ObservableProperty]
    private DateTime? _createdDateTime;

    [ObservableProperty]
    private DateTime? _lastModifiedDateTime;

    /// <summary>
    /// 시간 표시 문자열
    /// </summary>
    public string TimeDisplay
    {
        get
        {
            if (!LastModifiedDateTime.HasValue)
                return string.Empty;

            var today = DateTime.Today;
            var modifiedDate = LastModifiedDateTime.Value.Date;

            if (modifiedDate == today)
                return LastModifiedDateTime.Value.ToString("HH:mm");
            if (modifiedDate == today.AddDays(-1))
                return "어제";

            return LastModifiedDateTime.Value.ToString("MM/dd");
        }
    }

    /// <summary>
    /// 위치 표시 (노트북 > 섹션)
    /// </summary>
    public string LocationDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(NotebookName) && !string.IsNullOrEmpty(SectionName))
                return $"{NotebookName} > {SectionName}";
            if (!string.IsNullOrEmpty(SectionName))
                return SectionName;
            return string.Empty;
        }
    }
}
