using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Graph.Models;
using mailX.Utils;
using mailX.Services.Graph;
using Newtonsoft.Json;
using Serilog;
using STJ = System.Text.Json;

namespace mailX.ViewModels;

/// <summary>
/// OneNote ViewModel - 노트북/섹션/페이지 관리
/// </summary>
public partial class OneNoteViewModel : ViewModelBase
{
    private readonly GraphOneNoteService _oneNoteService;
    private readonly ILogger _logger;
    private readonly Services.Speech.SpeechRecognitionService _speechService;

    // 캐시 관련
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mailX", "cache");
    private static readonly string NotebooksCacheFile = Path.Combine(CacheDir, "onenote_notebooks.json");
    private static readonly string CustomSitesFile = Path.Combine(CacheDir, "onenote_custom_sites.json");
    private bool _isInitialLoadFromCache = false;
    private bool _isBackgroundSyncRunning = false;

    // 사용자가 수동으로 추가한 사이트 경로 목록
    private List<string> _customSitePaths = new();

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
    /// 즐겨찾기 페이지 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageItemViewModel> _favoritePages = new();

    /// <summary>
    /// 즐겨찾기 저장 파일 경로
    /// </summary>
    private static readonly string FavoritesFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mailX", "onenote_favorites.json");

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
    /// 저장되지 않은 변경사항 있음 여부
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusDisplay))]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// 제목 변경 대기 중 (아직 서버에 저장 안 된 제목)
    /// </summary>
    public string? PendingTitleChange { get; set; }

    /// <summary>
    /// 저장 중 여부
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusDisplay))]
    private bool _isSaving;

    /// <summary>
    /// 저장 상태 (저장됨, 수정됨, 저장 중..., 저장 실패)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusDisplay))]
    private string _saveStatus = "저장됨";

    /// <summary>
    /// 저장 상태 표시 문자열
    /// </summary>
    public string SaveStatusDisplay => SaveStatus;

    /// <summary>
    /// 자동저장 디바운스 타이머
    /// </summary>
    private System.Timers.Timer? _autoSaveTimer;
    private const int AutoSaveDelayMs = 3000; // 3초

    /// <summary>
    /// 현재 편집 중인 콘텐츠 (에디터에서 업데이트)
    /// </summary>
    private string? _editingContent;

    /// <summary>
    /// 이전 페이지 ID (페이지 전환 시 자동저장용)
    /// </summary>
    private string? _previousPageId;

    /// <summary>
    /// 녹음 파일 목록
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Models.RecordingInfo> _recordings = new();

    /// <summary>
    /// 녹음 파일 저장 경로
    /// </summary>
    private static readonly string RecordingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mailX", "recordings");

    /// <summary>
    /// 녹음 서비스
    /// </summary>
    private Services.Audio.AudioRecordingService? _recordingService;

    /// <summary>
    /// 실시간 STT 취소 토큰
    /// </summary>
    private CancellationTokenSource? _realtimeSTTCts;

    /// <summary>
    /// 실시간 요약 업데이트 타이머
    /// </summary>
    private System.Timers.Timer? _realtimeSummaryTimer;

    /// <summary>
    /// 실시간 요약 마지막 업데이트 세그먼트 수
    /// </summary>
    private int _lastSummarySegmentCount = 0;

    /// <summary>
    /// 오디오 플레이어 서비스
    /// </summary>
    private Services.Audio.AudioPlayerService? _audioPlayerService;

    /// <summary>
    /// 현재 재생 중인 녹음
    /// </summary>
    [ObservableProperty]
    private Models.RecordingInfo? _currentPlayingRecording;

    /// <summary>
    /// 현재 페이지의 녹음만 필터링 (페이지 연결된 녹음)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<Models.RecordingInfo> _currentPageRecordings = new();

    /// <summary>
    /// 현재 선택된 녹음 (상세 패널 표시용)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRecording))]
    private Models.RecordingInfo? _selectedRecording;

    /// <summary>
    /// 선택된 녹음의 STT 세그먼트 목록
    /// </summary>
    public ObservableCollection<Models.TranscriptSegment> STTSegments { get; } = new();

    /// <summary>
    /// 선택된 녹음의 요약 결과
    /// </summary>
    [ObservableProperty]
    private Models.RecordingSummary? _currentSummary;

    /// <summary>
    /// 선택된 녹음이 있는지 여부
    /// </summary>
    public bool HasSelectedRecording => SelectedRecording != null;

    /// <summary>
    /// AI 분석 활성화 여부 (녹음 시 STT/요약 자동 수행)
    /// </summary>
    [ObservableProperty]
    private bool _isAIAnalysisEnabled = true;

    /// <summary>
    /// STT 진행 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isSTTInProgress;

    /// <summary>
    /// STT 진행률 (0.0 ~ 1.0)
    /// </summary>
    [ObservableProperty]
    private double _sttProgress;

    /// <summary>
    /// STT 진행률 텍스트 (예: "분석 중...")
    /// </summary>
    [ObservableProperty]
    private string _sttProgressText = string.Empty;

    /// <summary>
    /// STT 예상 남은 시간
    /// </summary>
    [ObservableProperty]
    private string _sttTimeRemaining = string.Empty;

    /// <summary>
    /// STT 분석 시작 시간 (예상 시간 계산용)
    /// </summary>
    private DateTime? _sttStartTime;

    /// <summary>
    /// 요약 진행 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isSummaryInProgress;

    /// <summary>
    /// 요약 진행 상태 텍스트
    /// </summary>
    [ObservableProperty]
    private string _summaryProgressText = string.Empty;

    /// <summary>
    /// 현재 활성 콘텐츠 탭 (note/recording)
    /// </summary>
    [ObservableProperty]
    private string _activeContentTab = "note";

    /// <summary>
    /// 실시간 STT 세그먼트 (녹음 중 점진적 추가)
    /// </summary>
    public ObservableCollection<Models.TranscriptSegment> LiveSTTSegments { get; } = new();

    /// <summary>
    /// 실시간 요약 텍스트 (녹음 중 점진적 업데이트)
    /// </summary>
    [ObservableProperty]
    private string _liveSummaryText = string.Empty;

    /// <summary>
    /// 현재 STT 결과
    /// </summary>
    [ObservableProperty]
    private Models.TranscriptResult? _currentSTTResult;

    /// <summary>
    /// 현재 요약 결과
    /// </summary>
    [ObservableProperty]
    private Models.RecordingSummary? _currentSummaryResult;

    /// <summary>
    /// 녹음 중 여부
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingStatusText))]
    private bool _isRecording;

    /// <summary>
    /// 녹음 일시정지 여부
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingStatusText))]
    private bool _isRecordingPaused;

    /// <summary>
    /// 녹음 경과 시간
    /// </summary>
    [ObservableProperty]
    private TimeSpan _recordingDuration;

    /// <summary>
    /// 녹음 볼륨 레벨 (0.0 ~ 1.0)
    /// </summary>
    [ObservableProperty]
    private float _recordingVolume;

    /// <summary>
    /// 녹음 상태 텍스트
    /// </summary>
    public string RecordingStatusText
    {
        get
        {
            if (!IsRecording) return "대기 중";
            if (IsRecordingPaused) return "일시정지";
            return "녹음 중...";
        }
    }

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
        _speechService = new Services.Speech.SpeechRecognitionService();
    }

    /// <summary>
    /// 노트북 목록 로드 (캐시 우선, 백그라운드 동기화)
    /// </summary>
    [RelayCommand]
    public async Task LoadNotebooksAsync()
    {
        Log4.Info("[OneNote] ★★★ LoadNotebooksAsync 시작 ★★★");

        // 1. 캐시에서 먼저 로드 (빠른 UI 표시) - 로딩 인디케이터 없이 즉시 표시
        if (Notebooks.Count == 0 && !_isInitialLoadFromCache)
        {
            _isInitialLoadFromCache = true;
            var cached = LoadNotebooksFromCache();
            if (cached != null && cached.Count > 0)
            {
                foreach (var nb in cached)
                {
                    // 캐시에서 로드된 노트북도 더미 섹션 추가 (확장 화살표 표시용)
                    nb.HasSectionsLoaded = false;
                    nb.Sections.Clear();
                    nb.Sections.Add(new SectionItemViewModel
                    {
                        Id = "dummy",
                        DisplayName = "로딩 중...",
                        IsDummyItem = true
                    });
                    Notebooks.Add(nb);
                }
                Log4.Info($"[OneNote] 캐시에서 노트북 {Notebooks.Count}개 로드");

                // 캐시 로드 후 커스텀 사이트 노트북도 즉시 로드 (빠른 표시)
                LoadCustomSitePaths();
                if (_customSitePaths.Count > 0)
                {
                    Log4.Info($"[OneNote] 캐시 로드 후 커스텀 사이트 노트북 로드 시작: {_customSitePaths.Count}개");
                    _ = LoadCustomSiteNotebooksAsync();
                }
            }
            else
            {
                Log4.Info("[OneNote] 캐시 없음 - 서버에서 로드 필요");
            }
        }

        // 2. 백그라운드에서 서버 동기화 (로딩 인디케이터 없이) - 중복 실행 방지
        if (_isBackgroundSyncRunning)
        {
            Log4.Debug("[OneNote] 백그라운드 동기화 이미 진행 중 - 건너뜀");
            return;
        }

        _isBackgroundSyncRunning = true;
        _ = Task.Run(async () =>
        {
            try
            {
                Log4.Info("[OneNote] ★★★ 백그라운드 노트북 동기화 시작 ★★★");

                // 개인 + 그룹 노트북 통합 조회
                var allNotebooks = await _oneNoteService.GetAllNotebooksAsync();
                var allNotebooksList = allNotebooks.ToList();
                Log4.Info($"[OneNote] GetAllNotebooksAsync 완료: {allNotebooksList.Count}개");

                // 1단계: 먼저 노트북 목록만 빠르게 표시 (섹션/페이지 없이)
                var notebookOnlyList = new System.Collections.Generic.List<NotebookItemViewModel>();
                foreach (var nbWithSource in allNotebooksList)
                {
                    var notebook = nbWithSource.Notebook;
                    var nbViewModel = new NotebookItemViewModel
                    {
                        Id = notebook.Id ?? string.Empty,
                        DisplayName = notebook.DisplayName ?? "Untitled",
                        CreatedDateTime = notebook.CreatedDateTime?.DateTime,
                        LastModifiedDateTime = notebook.LastModifiedDateTime?.DateTime,
                        IsExpanded = false,  // 초기에는 접힘 상태
                        Source = nbWithSource.Source.ToString(),
                        SourceName = nbWithSource.SourceName,
                        GroupId = nbWithSource.GroupId,
                        SiteId = nbWithSource.SiteId,
                        HasSectionsLoaded = false  // 아직 섹션 로드 안 됨
                    };

                    // 더미 섹션 추가 (TreeView 확장 화살표 표시용)
                    nbViewModel.Sections.Add(new SectionItemViewModel
                    {
                        Id = "dummy",
                        DisplayName = "로딩 중...",
                        IsDummyItem = true
                    });

                    notebookOnlyList.Add(nbViewModel);
                }

                // UI에 노트북 목록만 먼저 표시
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Notebooks.Clear();
                    foreach (var nb in notebookOnlyList)
                        Notebooks.Add(nb);
                    Log4.Info($"[OneNote] 노트북 목록 UI 표시 완료: {Notebooks.Count}개 (섹션/페이지 로딩 중)");
                });

                // 2단계: 노트북 목록만 저장 (섹션/페이지는 on-demand 로드 - Rate Limit 방지)
                // 섹션/페이지는 노트북 확장 시 LoadSectionsForNotebookAsync에서 로드
                Log4.Info($"[OneNote] 노트북 {notebookOnlyList.Count}개 처리 완료");

                // 즐겨찾기 상태 동기화
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    SyncFavoriteStatus();
                    Log4.Info($"[OneNote] UI 업데이트 완료: {Notebooks.Count}개 노트북");
                });

                // 캐시 저장 (0개인 경우 기존 캐시 보호)
                if (notebookOnlyList.Count > 0)
                {
                    SaveNotebooksToCache(notebookOnlyList);
                }
                else
                {
                    Log4.Debug("[OneNote] API에서 0개 반환 - 기존 캐시 유지");
                }

                // 3단계: 사용자가 추가한 커스텀 사이트 노트북 로드
                LoadCustomSitePaths();
                if (_customSitePaths.Count > 0)
                {
                    await LoadCustomSiteNotebooksAsync();
                }

                var personalCount = notebookOnlyList.Count(n => n.Source == "Personal");
                var groupCount = notebookOnlyList.Count(n => n.Source == "Group");
                var customCount = Notebooks.Count(n => n.IsCustomSite);
                Log4.Info($"[OneNote] ★★★ 서버에서 노트북 동기화 완료 ★★★: 개인 {personalCount}개, 그룹 {groupCount}개, 커스텀 사이트 {customCount}개");
                _logger.Information("서버에서 노트북 동기화 완료: 개인 {PersonalCount}개, 그룹 {GroupCount}개, 커스텀 {CustomCount}개",
                    personalCount, groupCount, customCount);
            }
            catch (Exception ex)
            {
                Log4.Error($"[OneNote] ★★★ 백그라운드 노트북 동기화 실패 ★★★: {ex.Message}");
                _logger.Warning(ex, "백그라운드 노트북 동기화 실패");
            }
            finally
            {
                _isBackgroundSyncRunning = false;
            }
        });
    }

    /// <summary>
    /// SharePoint 사이트 경로를 사용하여 노트북을 추가합니다.
    /// </summary>
    /// <param name="sitePath">SharePoint 사이트 경로 (예: "AI785-1" 또는 "sites/AI785-1")</param>
    /// <returns>추가된 노트북 수</returns>
    public async Task<int> AddSiteNotebooksAsync(string sitePath)
    {
        if (string.IsNullOrWhiteSpace(sitePath))
        {
            Log4.Warn("[OneNote] AddSiteNotebooksAsync: 사이트 경로가 비어있습니다.");
            return 0;
        }

        // 사이트 경로 정규화
        var normalizedPath = sitePath.Trim();
        if (!normalizedPath.StartsWith("sites/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = $"sites/{normalizedPath}";
        }

        Log4.Info($"[OneNote] ★★★ 사이트 노트북 추가 시작 ★★★: {normalizedPath}");

        try
        {
            var siteNotebooks = await _oneNoteService.GetNotebooksFromSitePathAsync(normalizedPath);

            if (siteNotebooks == null || siteNotebooks.Count == 0)
            {
                Log4.Info($"[OneNote] 사이트 '{normalizedPath}'에서 노트북을 찾지 못했습니다.");
                return 0;
            }

            int addedCount = 0;
            var existingIds = new HashSet<string>(Notebooks.Select(n => n.Id));

            foreach (var nbWithSource in siteNotebooks)
            {
                var notebook = nbWithSource.Notebook;

                // 중복 체크
                if (existingIds.Contains(notebook.Id ?? string.Empty))
                {
                    Log4.Debug($"[OneNote] 중복 노트북 건너뜀: {notebook.DisplayName}");
                    continue;
                }

                var nbViewModel = new NotebookItemViewModel
                {
                    Id = notebook.Id ?? string.Empty,
                    DisplayName = notebook.DisplayName ?? "Untitled",
                    CreatedDateTime = notebook.CreatedDateTime?.DateTime,
                    LastModifiedDateTime = notebook.LastModifiedDateTime?.DateTime,
                    IsExpanded = false,
                    Source = nbWithSource.Source.ToString(),
                    SourceName = nbWithSource.SourceName,
                    GroupId = nbWithSource.GroupId,
                    SiteId = nbWithSource.SiteId,
                    HasSectionsLoaded = false,
                    IsCustomSite = true  // 수동 추가된 사이트 표시
                };

                // 더미 섹션 추가 (TreeView 확장 화살표 표시용)
                nbViewModel.Sections.Add(new SectionItemViewModel
                {
                    Id = "dummy",
                    DisplayName = "로딩 중...",
                    IsDummyItem = true
                });

                Notebooks.Add(nbViewModel);
                existingIds.Add(nbViewModel.Id);
                addedCount++;

                Log4.Info($"[OneNote] 사이트 노트북 추가됨: {notebook.DisplayName} (Site: {nbWithSource.SourceName})");
            }

            // 사이트 경로 저장 (중복 제외)
            Log4.Info($"[OneNote] 저장 조건 체크: addedCount={addedCount}, normalizedPath='{normalizedPath}', Contains={_customSitePaths.Contains(normalizedPath)}, _customSitePaths=[{string.Join(", ", _customSitePaths)}]");
            if (addedCount > 0 && !_customSitePaths.Contains(normalizedPath))
            {
                Log4.Info($"[OneNote] 사이트 경로 추가: {normalizedPath}");
                _customSitePaths.Add(normalizedPath);
                SaveCustomSitePaths();
            }
            else
            {
                Log4.Warn($"[OneNote] 사이트 경로 저장 건너뜀: addedCount={addedCount}, 이미 존재={_customSitePaths.Contains(normalizedPath)}");
            }

            // 즐겨찾기 상태 동기화
            SyncFavoriteStatus();

            // 캐시 업데이트
            if (Notebooks.Count > 0)
            {
                SaveNotebooksToCache(Notebooks.ToList());
            }

            Log4.Info($"[OneNote] ★★★ 사이트 노트북 추가 완료 ★★★: {addedCount}개 추가됨");
            _logger.Information("[OneNote] 사이트 '{SitePath}'에서 {Count}개 노트북 추가됨", normalizedPath, addedCount);

            return addedCount;
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneNote] 사이트 노트북 추가 실패: {ex.Message}");
            _logger.Error(ex, "[OneNote] 사이트 '{SitePath}' 노트북 추가 실패", normalizedPath);
            throw;
        }
    }

    /// <summary>
    /// 노트북의 섹션과 페이지를 on-demand로 로드 (Rate Limit 방지)
    /// </summary>
    public async Task LoadSectionsForNotebookAsync(NotebookItemViewModel notebook)
    {
        if (notebook == null || notebook.HasSectionsLoaded || notebook.IsLoadingSections)
        {
            // 이미 로드됨 또는 로딩 중
            return;
        }

        try
        {
            // 로딩 시작 표시
            notebook.IsLoadingSections = true;
            Log4.Info($"[OneNote] 노트북 '{notebook.DisplayName}' 섹션 로드 시작");

            // 더미 아이템 제거
            var dummyItems = notebook.Sections.Where(s => s.IsDummyItem).ToList();
            foreach (var dummy in dummyItems)
            {
                notebook.Sections.Remove(dummy);
            }

            // 노트북 소스에 따라 다른 API 사용
            // 그룹 노트북도 SiteId가 있으면 Site API를 우선 사용 (SharePoint 저장 노트북 지원)
            Log4.Debug($"[OneNote] 노트북 '{notebook.DisplayName}' API 호출 - Source={notebook.Source}, GroupId={notebook.GroupId}, SiteId={notebook.SiteId}");
            System.Collections.Generic.IEnumerable<Microsoft.Graph.Models.OnenoteSection> sections;

            // SiteId가 있으면 Site API 우선 사용 (그룹/사이트 모두)
            if (!string.IsNullOrEmpty(notebook.SiteId))
            {
                Log4.Debug($"[OneNote] GetSiteSectionsAsync 호출 (SiteId 우선) - SiteId={notebook.SiteId}, NotebookId={notebook.Id}");
                sections = await _oneNoteService.GetSiteSectionsAsync(notebook.SiteId, notebook.Id);
            }
            else if (notebook.Source == "Group" && !string.IsNullOrEmpty(notebook.GroupId))
            {
                Log4.Debug($"[OneNote] GetGroupSectionsAsync 호출 - GroupId={notebook.GroupId}, NotebookId={notebook.Id}");
                sections = await _oneNoteService.GetGroupSectionsAsync(notebook.GroupId, notebook.Id);
            }
            else
            {
                Log4.Debug($"[OneNote] GetSectionsAsync 호출 (개인) - NotebookId={notebook.Id}");
                sections = await _oneNoteService.GetSectionsAsync(notebook.Id);
            }

            // 1단계: 섹션 목록 먼저 생성하고 UI에 추가 (빠른 응답)
            var sectionList = sections.ToList();
            Log4.Debug($"[OneNote] 노트북 '{notebook.DisplayName}' 섹션 {sectionList.Count}개 조회됨");
            foreach (var sec in sectionList)
            {
                Log4.Debug($"[OneNote] - 섹션: {sec.DisplayName} (ID={sec.Id})");
            }
            var sectionItems = new System.Collections.Generic.List<SectionItemViewModel>();

            foreach (var section in sectionList)
            {
                var sectionItem = new SectionItemViewModel
                {
                    Id = section.Id ?? string.Empty,
                    DisplayName = section.DisplayName ?? "Untitled",
                    NotebookId = notebook.Id,
                    NotebookName = notebook.DisplayName,
                    IsDefault = section.IsDefault ?? false,
                    GroupId = notebook.GroupId,
                    SiteId = notebook.SiteId
                };
                sectionItems.Add(sectionItem);
                notebook.Sections.Add(sectionItem);  // UI에 즉시 추가
            }

            // 2단계: 페이지를 병렬로 로드 (백그라운드)
            var loadPagesTasks = sectionItems.Select(async sectionItem =>
            {
                try
                {
                    System.Collections.Generic.IEnumerable<Microsoft.Graph.Models.OnenotePage> pages;

                    // SiteId가 있으면 Site API 우선 사용 (그룹/사이트 모두)
                    if (!string.IsNullOrEmpty(notebook.SiteId))
                    {
                        Log4.Debug($"[OneNote] GetSitePagesAsync 호출 (SiteId 우선) - 섹션 '{sectionItem.DisplayName}', SiteId={notebook.SiteId}, SectionId={sectionItem.Id}");
                        pages = await _oneNoteService.GetSitePagesAsync(notebook.SiteId, sectionItem.Id);
                    }
                    else if (notebook.Source == "Group" && !string.IsNullOrEmpty(notebook.GroupId))
                    {
                        Log4.Debug($"[OneNote] GetGroupPagesAsync 호출 - 섹션 '{sectionItem.DisplayName}', GroupId={notebook.GroupId}, SectionId={sectionItem.Id}");
                        pages = await _oneNoteService.GetGroupPagesAsync(notebook.GroupId, sectionItem.Id);
                    }
                    else
                    {
                        Log4.Debug($"[OneNote] GetPagesAsync 호출 (개인) - 섹션 '{sectionItem.DisplayName}', SectionId={sectionItem.Id}");
                        pages = await _oneNoteService.GetPagesAsync(sectionItem.Id);
                    }

                    var pageList = pages.ToList();
                    Log4.Debug($"[OneNote] 섹션 '{sectionItem.DisplayName}' 페이지 {pageList.Count}개 조회됨");

                    // UI 스레드에서 페이지 추가 (빈 제목 페이지 필터링)
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        foreach (var page in pageList)
                        {
                            // 빈 제목 또는 "Untitled" 페이지는 건너뛰기
                            var title = page.Title?.Trim();
                            if (string.IsNullOrEmpty(title) || title.Equals("Untitled", StringComparison.OrdinalIgnoreCase))
                                continue;

                            sectionItem.Pages.Add(new PageItemViewModel
                            {
                                Id = page.Id ?? string.Empty,
                                Title = title,
                                SectionId = sectionItem.Id,
                                SectionName = sectionItem.DisplayName,
                                NotebookName = notebook.DisplayName,
                                CreatedDateTime = page.CreatedDateTime?.DateTime,
                                LastModifiedDateTime = page.LastModifiedDateTime?.DateTime,
                                GroupId = notebook.GroupId,
                                SiteId = notebook.SiteId
                            });
                        }
                    });
                }
                catch (Exception pageEx)
                {
                    Log4.Debug($"[OneNote] 페이지 로드 실패 (섹션: {sectionItem.DisplayName}): {pageEx.Message}");
                }
            }).ToList();

            // 모든 페이지 로드 완료 대기
            await Task.WhenAll(loadPagesTasks);

            // 로드 완료 표시
            notebook.HasSectionsLoaded = true;
            Log4.Info($"[OneNote] 노트북 '{notebook.DisplayName}' 섹션 {notebook.Sections.Count}개 로드 완료");

            // 즐겨찾기 상태 동기화 (섹션/페이지 로드 후)
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                SyncFavoriteStatusForNotebook(notebook);
            });
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneNote] 노트북 '{notebook.DisplayName}' 섹션 로드 실패: {ex.Message}");
        }
        finally
        {
            // 로딩 종료 표시
            notebook.IsLoadingSections = false;
        }
    }

    /// <summary>
    /// 캐시에서 노트북 로드
    /// </summary>
    private System.Collections.Generic.List<NotebookItemViewModel>? LoadNotebooksFromCache()
    {
        try
        {
            if (!File.Exists(NotebooksCacheFile))
                return null;

            var json = File.ReadAllText(NotebooksCacheFile);
            return JsonConvert.DeserializeObject<System.Collections.Generic.List<NotebookItemViewModel>>(json);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "노트북 캐시 로드 실패");
            return null;
        }
    }

    /// <summary>
    /// 캐시에 노트북 저장
    /// </summary>
    private void SaveNotebooksToCache(System.Collections.Generic.List<NotebookItemViewModel> notebooks)
    {
        try
        {
            if (!Directory.Exists(CacheDir))
                Directory.CreateDirectory(CacheDir);

            var json = JsonConvert.SerializeObject(notebooks, Formatting.Indented);
            File.WriteAllText(NotebooksCacheFile, json);
            _logger.Debug("노트북 캐시 저장 완료: {Count}개", notebooks.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "노트북 캐시 저장 실패");
        }
    }

    /// <summary>
    /// 사용자가 추가한 사이트 경로를 파일에 저장합니다.
    /// </summary>
    private void SaveCustomSitePaths()
    {
        Log4.Info($"[OneNote] SaveCustomSitePaths 호출됨: {_customSitePaths.Count}개 경로");
        try
        {
            if (!Directory.Exists(CacheDir))
            {
                Directory.CreateDirectory(CacheDir);
                Log4.Info($"[OneNote] 캐시 디렉토리 생성: {CacheDir}");
            }

            var json = JsonConvert.SerializeObject(_customSitePaths, Formatting.Indented);
            File.WriteAllText(CustomSitesFile, json);
            Log4.Info($"[OneNote] 커스텀 사이트 경로 저장 완료: {_customSitePaths.Count}개 → {CustomSitesFile}");
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneNote] 커스텀 사이트 경로 저장 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 저장된 사이트 경로를 로드합니다.
    /// </summary>
    private void LoadCustomSitePaths()
    {
        Log4.Info($"[OneNote] LoadCustomSitePaths 호출됨: 파일={CustomSitesFile}");
        try
        {
            if (!File.Exists(CustomSitesFile))
            {
                Log4.Info($"[OneNote] 커스텀 사이트 파일 없음: {CustomSitesFile}");
                _customSitePaths = new List<string>();
                return;
            }

            var json = File.ReadAllText(CustomSitesFile);
            _customSitePaths = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
            Log4.Info($"[OneNote] 커스텀 사이트 경로 로드 완료: {_customSitePaths.Count}개 → [{string.Join(", ", _customSitePaths)}]");
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneNote] 커스텀 사이트 경로 로드 실패: {ex.Message}");
            _customSitePaths = new List<string>();
        }
    }

    /// <summary>
    /// 저장된 커스텀 사이트에서 노트북을 백그라운드로 로드합니다.
    /// </summary>
    private async Task LoadCustomSiteNotebooksAsync()
    {
        if (_customSitePaths.Count == 0)
            return;

        Log4.Info($"[OneNote] 커스텀 사이트 노트북 로드 시작: {_customSitePaths.Count}개 사이트");

        foreach (var sitePath in _customSitePaths.ToList())
        {
            try
            {
                var siteNotebooks = await _oneNoteService.GetNotebooksFromSitePathAsync(sitePath);
                if (siteNotebooks == null || siteNotebooks.Count == 0)
                    continue;

                var existingIds = new HashSet<string>(Notebooks.Select(n => n.Id));

                foreach (var nbWithSource in siteNotebooks)
                {
                    var notebook = nbWithSource.Notebook;

                    // 중복 체크
                    if (existingIds.Contains(notebook.Id ?? string.Empty))
                        continue;

                    var nbViewModel = new NotebookItemViewModel
                    {
                        Id = notebook.Id ?? string.Empty,
                        DisplayName = notebook.DisplayName ?? "Untitled",
                        CreatedDateTime = notebook.CreatedDateTime?.DateTime,
                        LastModifiedDateTime = notebook.LastModifiedDateTime?.DateTime,
                        IsExpanded = false,
                        Source = nbWithSource.Source.ToString(),
                        SourceName = nbWithSource.SourceName,
                        GroupId = nbWithSource.GroupId,
                        SiteId = nbWithSource.SiteId,
                        HasSectionsLoaded = false,
                        IsCustomSite = true
                    };

                    // 더미 섹션 추가
                    nbViewModel.Sections.Add(new SectionItemViewModel
                    {
                        Id = "dummy",
                        DisplayName = "로딩 중...",
                        IsDummyItem = true
                    });

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        Notebooks.Add(nbViewModel);
                    });

                    Log4.Debug($"[OneNote] 커스텀 사이트 노트북 로드됨: {notebook.DisplayName} (Site: {nbWithSource.SourceName})");
                }
            }
            catch (Exception ex)
            {
                Log4.Warn($"[OneNote] 커스텀 사이트 '{sitePath}' 노트북 로드 실패: {ex.Message}");
            }
        }

        // 즐겨찾기 상태 동기화
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            SyncFavoriteStatus();
        });

        Log4.Info("[OneNote] 커스텀 사이트 노트북 로드 완료");
    }

    /// <summary>
    /// 녹음 파일 목록 로드 (모든 녹음)
    /// </summary>
    [RelayCommand]
    public void LoadRecordings()
    {
        try
        {
            Recordings.Clear();

            if (!Directory.Exists(RecordingsDir))
            {
                Directory.CreateDirectory(RecordingsDir);
            }

            // WAV, MP3, M4A, OGG 파일 검색
            var extensions = new[] { "*.wav", "*.mp3", "*.m4a", "*.ogg", "*.wma" };
            var audioFiles = extensions
                .SelectMany(ext => Directory.GetFiles(RecordingsDir, ext))
                .OrderByDescending(f => File.GetCreationTime(f));

            foreach (var file in audioFiles)
            {
                var fileInfo = new FileInfo(file);
                var recording = new Models.RecordingInfo
                {
                    FilePath = file,
                    FileName = fileInfo.Name,
                    CreatedTime = fileInfo.CreationTime,
                    Duration = GetAudioDuration(file),
                    // mailX에서 녹음한 파일은 "recording_" 접두사로 구분
                    Source = fileInfo.Name.StartsWith("recording_", StringComparison.OrdinalIgnoreCase)
                        ? Models.RecordingSource.MailX
                        : Models.RecordingSource.External
                };

                // 파일명에서 페이지 ID 추출 (형식: recording_{pageId}_{yyyyMMdd}_{HHmmss}.wav)
                if (recording.Source == Models.RecordingSource.MailX)
                {
                    var nameParts = Path.GetFileNameWithoutExtension(fileInfo.Name).Split('_');
                    // 최소 4개 부분: recording, pageId, yyyyMMdd, HHmmss
                    if (nameParts.Length >= 4)
                    {
                        // timestamp는 마지막 2개 부분 (yyyyMMdd_HHmmss)
                        var datePart = nameParts[^2]; // yyyyMMdd (8자리)
                        var timePart = nameParts[^1]; // HHmmss (6자리)
                        if (datePart.Length == 8 && datePart.All(char.IsDigit) &&
                            timePart.Length == 6 && timePart.All(char.IsDigit))
                        {
                            // pageId는 recording_ 이후부터 timestamp 이전까지
                            recording.LinkedPageId = string.Join("_", nameParts[1..^2]);
                        }
                    }
                    // 페이지 ID 없이 녹음된 파일 (형식: recording_{yyyyMMdd}_{HHmmss}.wav)
                    else if (nameParts.Length == 3)
                    {
                        var datePart = nameParts[1];
                        var timePart = nameParts[2];
                        if (datePart.Length == 8 && datePart.All(char.IsDigit) &&
                            timePart.Length == 6 && timePart.All(char.IsDigit))
                        {
                            // 페이지 ID 없음
                            recording.LinkedPageId = null;
                        }
                    }
                }

                Recordings.Add(recording);
            }

            _logger.Information("녹음 파일 {Count}개 로드됨 (mailX: {MailXCount}, 외부: {ExternalCount})",
                Recordings.Count,
                Recordings.Count(r => r.Source == Models.RecordingSource.MailX),
                Recordings.Count(r => r.Source == Models.RecordingSource.External));

            // 현재 페이지 필터링 적용
            FilterRecordingsForCurrentPage();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "녹음 파일 로드 실패");
        }
    }

    /// <summary>
    /// 현재 선택된 페이지의 녹음 목록 로드 (로컬 + OneNote)
    /// </summary>
    [RelayCommand]
    public async Task LoadRecordingsForCurrentPageAsync()
    {
        Log4.Info("★★★ LoadRecordingsForCurrentPageAsync 호출됨 ★★★");
        CurrentPageRecordings.Clear();

        // 먼저 전체 녹음 로드
        if (Recordings.Count == 0)
        {
            LoadRecordings();
        }

        // 페이지가 선택되지 않았으면 모든 녹음 표시
        if (SelectedPage == null)
        {
            Log4.Debug($"페이지 미선택 - 모든 녹음 표시: {Recordings.Count}개");
            foreach (var r in Recordings)
            {
                CurrentPageRecordings.Add(r);
            }
            return;
        }

        var pageId = SelectedPage.Id;
        var sanitizedPageId = SanitizePageId(pageId);
        Log4.Info($"★★★ 페이지 {pageId} ({SelectedPage.Title}) 녹음 로드 시작 (Sanitized: {sanitizedPageId}) ★★★");

        // 1. 해당 페이지에 연결된 mailX 녹음 추가 (sanitized ID로 비교)
        foreach (var recording in Recordings)
        {
            if (recording.LinkedPageId == sanitizedPageId)
            {
                CurrentPageRecordings.Add(recording);
            }
        }

        // 2. OneNote 페이지에서 오디오 리소스 가져오기
        try
        {
            var oneNoteResources = await _oneNoteService.GetPageAudioResourcesAsync(pageId);
            foreach (var resource in oneNoteResources)
            {
                // 이미 다운로드된 파일인지 확인 (리소스 ID 기반)
                var existingDownloaded = CurrentPageRecordings.FirstOrDefault(r =>
                    r.OneNoteResourceId == resource.ResourceId);

                if (existingDownloaded == null)
                {
                    // OneNote 녹음 추가
                    var oneNoteRecording = new Models.RecordingInfo
                    {
                        FileName = resource.FileName,
                        Source = Models.RecordingSource.OneNote,
                        LinkedPageId = pageId,
                        OneNoteResourceId = resource.ResourceId,
                        OneNoteResourceUrl = resource.ResourceUrl,
                        CreatedTime = DateTime.Now
                    };

                    // 미리 다운로드하여 Duration 계산
                    try
                    {
                        var downloadedPath = await _oneNoteService.DownloadAudioResourceAsync(
                            resource.ResourceUrl,
                            resource.FileName,
                            RecordingsDir);

                        if (!string.IsNullOrEmpty(downloadedPath))
                        {
                            oneNoteRecording.FilePath = downloadedPath;
                            oneNoteRecording.Duration = GetAudioDuration(downloadedPath);
                            Log4.Info($"[OneNote] 녹음 다운로드 완료: {resource.FileName}, Duration={oneNoteRecording.Duration}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log4.Warn($"[OneNote] 녹음 다운로드 실패: {resource.FileName} - {ex.Message}");
                    }

                    CurrentPageRecordings.Add(oneNoteRecording);
                }
            }

            var mailxCount = CurrentPageRecordings.Count(r => r.Source == Models.RecordingSource.MailX);
            var oneNoteCount = CurrentPageRecordings.Count(r => r.Source == Models.RecordingSource.OneNote);
            Log4.Info($"★★★ 페이지 {pageId} 녹음 로드 완료: mailX {mailxCount}개, OneNote {oneNoteCount}개 ★★★");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "OneNote 오디오 리소스 로드 실패");
        }
    }

    /// <summary>
    /// 오디오 파일 길이 가져오기
    /// </summary>
    private TimeSpan GetAudioDuration(string filePath)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            // WMA, AAC 등은 MediaFoundationReader 사용
            if (extension is ".wma" or ".wmv" or ".asf" or ".aac" or ".m4a" or ".mp4")
            {
                using var reader = new NAudio.Wave.MediaFoundationReader(filePath);
                return reader.TotalTime;
            }

            // WAV, MP3 등은 AudioFileReader 사용
            using var audioReader = new NAudio.Wave.AudioFileReader(filePath);
            return audioReader.TotalTime;
        }
        catch (Exception ex)
        {
            Log4.Warn($"오디오 길이 확인 실패: {filePath} - {ex.Message}");
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// 녹음 파일 재생 (내장 플레이어 사용)
    /// </summary>
    [RelayCommand]
    public async Task PlayRecordingAsync(Models.RecordingInfo? recording)
    {
        if (recording == null) return;

        Log4.Info($"[Audio] PlayRecordingAsync 호출됨: {recording.FileName}, Source={recording.Source}, FilePath={recording.FilePath ?? "null"}");

        try
        {
            // OneNote 녹음인 경우 다운로드 필요
            if (recording.Source == Models.RecordingSource.OneNote &&
                string.IsNullOrEmpty(recording.FilePath) &&
                !string.IsNullOrEmpty(recording.OneNoteResourceUrl))
            {
                Log4.Info($"[Audio] OneNote 녹음 다운로드 시작: {recording.FileName}");
                Log4.Info($"[Audio] URL: {recording.OneNoteResourceUrl}");

                var downloadedPath = await _oneNoteService.DownloadAudioResourceAsync(
                    recording.OneNoteResourceUrl,
                    recording.FileName,
                    RecordingsDir);

                if (string.IsNullOrEmpty(downloadedPath))
                {
                    Log4.Warn($"[Audio] OneNote 녹음 다운로드 실패: {recording.FileName}");
                    return;
                }

                recording.FilePath = downloadedPath;
                recording.Duration = GetAudioDuration(downloadedPath);
                Log4.Info($"[Audio] OneNote 녹음 다운로드 완료: {downloadedPath}, Duration={recording.Duration}");
            }

            if (string.IsNullOrEmpty(recording.FilePath))
            {
                Log4.Warn($"[Audio] 재생 실패 - FilePath가 비어있음");
                return;
            }

            // 이미 같은 파일이 재생 중이면 일시정지/재개
            if (CurrentPlayingRecording?.FilePath == recording.FilePath && _audioPlayerService != null)
            {
                Log4.Info($"[Audio] 같은 파일 감지, State={_audioPlayerService.State}, TotalDuration={_audioPlayerService.TotalDuration}");

                // 파일이 로드되지 않은 경우 (Duration이 0) 새로 로드
                if (_audioPlayerService.TotalDuration == TimeSpan.Zero)
                {
                    Log4.Info($"[Audio] 파일이 로드되지 않음, 새로 로드 시작");
                    // 토글하지 않고 아래로 진행하여 새로 로드
                }
                else
                {
                    _audioPlayerService.TogglePlayPause();
                    recording.IsPlaying = _audioPlayerService.IsPlaying;
                    OnPropertyChanged(nameof(CurrentPlayingRecording));
                    Log4.Info($"[Audio] 재생 토글: IsPlaying={recording.IsPlaying}");
                    return;
                }
            }

            // 다른 파일 재생 중이면 중지
            StopPlayback();

            // 오디오 플레이어 초기화 (이벤트는 한 번만 등록)
            if (_audioPlayerService == null)
            {
                _audioPlayerService = new Services.Audio.AudioPlayerService();

                // 이벤트 연결 (최초 한 번만)
                _audioPlayerService.PositionChanged += OnAudioPositionChanged;
                _audioPlayerService.PlaybackStopped += OnAudioPlaybackStopped;
                _audioPlayerService.StateChanged += OnAudioStateChanged;
            }

            // 파일 로드 및 재생
            Log4.Info($"[Audio] 파일 로드 시작: {recording.FilePath}");
            _audioPlayerService.Load(recording.FilePath);
            Log4.Info($"[Audio] 파일 로드 완료, TotalDuration={_audioPlayerService.TotalDuration}");

            _audioPlayerService.Play();

            recording.IsPlaying = true;
            CurrentPlayingRecording = recording;
            OnPropertyChanged(nameof(CurrentPlayingRecording));

            Log4.Info($"[Audio] 재생 시작: {recording.FileName}");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "녹음 파일 재생 실패: {File}", recording.FileName);
        }
    }

    /// <summary>
    /// 오디오 위치 변경 이벤트 핸들러
    /// </summary>
    private void OnAudioPositionChanged(TimeSpan position)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (CurrentPlayingRecording != null)
            {
                CurrentPlayingRecording.CurrentPosition = position;
                OnPropertyChanged(nameof(CurrentPlayingRecording));
            }
        });
    }

    /// <summary>
    /// 오디오 재생 중지 이벤트 핸들러
    /// </summary>
    private void OnAudioPlaybackStopped()
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (CurrentPlayingRecording != null)
            {
                CurrentPlayingRecording.IsPlaying = false;
                CurrentPlayingRecording.CurrentPosition = TimeSpan.Zero;
                OnPropertyChanged(nameof(CurrentPlayingRecording));
            }
            CurrentPlayingRecording = null;
        });
    }

    /// <summary>
    /// 오디오 상태 변경 이벤트 핸들러
    /// </summary>
    private void OnAudioStateChanged(NAudio.Wave.PlaybackState state)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (CurrentPlayingRecording != null)
            {
                CurrentPlayingRecording.IsPlaying = state == NAudio.Wave.PlaybackState.Playing;
                OnPropertyChanged(nameof(CurrentPlayingRecording));
            }
        });
    }

    /// <summary>
    /// 재생 정지
    /// </summary>
    [RelayCommand]
    public void StopPlayback()
    {
        if (_audioPlayerService != null)
        {
            _audioPlayerService.Stop();
            if (CurrentPlayingRecording != null)
            {
                CurrentPlayingRecording.IsPlaying = false;
                CurrentPlayingRecording.CurrentPosition = TimeSpan.Zero;
            }
            CurrentPlayingRecording = null;
        }
    }

    /// <summary>
    /// 재생 일시정지/재개 (UI 버튼 및 키보드 단축키용)
    /// </summary>
    [RelayCommand]
    public void TogglePlayPause()
    {
        if (_audioPlayerService == null || CurrentPlayingRecording == null) return;

        _audioPlayerService.TogglePlayPause();
        CurrentPlayingRecording.IsPlaying = _audioPlayerService.IsPlaying;
        OnPropertyChanged(nameof(CurrentPlayingRecording));
    }

    /// <summary>
    /// 5초 뒤로
    /// </summary>
    [RelayCommand]
    public void SeekBackward()
    {
        _audioPlayerService?.SeekBackward();
    }

    /// <summary>
    /// 5초 앞으로
    /// </summary>
    [RelayCommand]
    public void SeekForward()
    {
        _audioPlayerService?.SeekForward();
    }

    /// <summary>
    /// 특정 위치로 이동
    /// </summary>
    public void SeekToPosition(double seconds)
    {
        _audioPlayerService?.Seek(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// 특정 시간으로 이동 (STT 세그먼트 클릭 시 호출)
    /// </summary>
    public async void SeekToTime(TimeSpan time)
    {
        // 현재 선택된 녹음이 재생 중이 아니면 재생 시작
        if (SelectedRecording != null && CurrentPlayingRecording?.FilePath != SelectedRecording.FilePath)
        {
            await PlayRecordingAsync(SelectedRecording);
        }

        // 해당 위치로 이동
        _audioPlayerService?.Seek(time);
    }

    /// <summary>
    /// 상대 위치로 이동 (초 단위)
    /// </summary>
    /// <param name="seconds">이동할 초 (양수: 앞으로, 음수: 뒤로)</param>
    public void SeekRelative(double seconds)
    {
        if (_audioPlayerService == null) return;

        var newPosition = _audioPlayerService.CurrentPosition + TimeSpan.FromSeconds(seconds);
        var clampedSeconds = Math.Clamp(newPosition.TotalSeconds, 0, _audioPlayerService.TotalDuration.TotalSeconds);
        _audioPlayerService.Seek(TimeSpan.FromSeconds(clampedSeconds));

        // 현재 재생 중인 녹음의 위치 업데이트
        if (CurrentPlayingRecording != null)
        {
            CurrentPlayingRecording.CurrentPosition = TimeSpan.FromSeconds(clampedSeconds);
        }
    }

    /// <summary>
    /// 현재 페이지의 녹음 파일만 필터링 (동기 버전 - LoadRecordings 내부 호출용)
    /// </summary>
    public void FilterRecordingsForCurrentPage()
    {
        CurrentPageRecordings.Clear();

        if (SelectedPage == null)
        {
            // 페이지 선택 안 됨: 모든 mailX/외부 녹음 표시
            foreach (var recording in Recordings)
            {
                CurrentPageRecordings.Add(recording);
            }
            _logger.Debug("페이지별 녹음 필터링: 전체 {Total}개 (페이지 미선택)",
                Recordings.Count);
            return;
        }

        // 현재 페이지 ID를 SanitizePageId와 동일한 방식으로 변환
        var pageId = SelectedPage.Id;
        var sanitizedPageId = SanitizePageId(pageId);

        foreach (var recording in Recordings)
        {
            // 해당 페이지에 연결된 녹음만 추가 (sanitized ID로 비교)
            if (recording.LinkedPageId == sanitizedPageId)
            {
                CurrentPageRecordings.Add(recording);
            }
        }

        _logger.Debug("페이지별 녹음 필터링: 전체 {Total}개 중 {Filtered}개 (페이지 ID: {PageId}, Sanitized: {SanitizedId})",
            Recordings.Count, CurrentPageRecordings.Count, pageId, sanitizedPageId);
    }

    /// <summary>
    /// 페이지 ID를 파일명에 사용 가능하도록 정리 (AudioRecordingService와 동일한 로직)
    /// </summary>
    private static string SanitizePageId(string pageId)
    {
        var sanitized = string.Join("", pageId.Split(Path.GetInvalidFileNameChars()));
        if (sanitized.Length > 20)
        {
            sanitized = sanitized.Substring(0, 20);
        }
        return sanitized;
    }

    /// <summary>
    /// 녹음 파일 삭제
    /// </summary>
    [RelayCommand]
    public void DeleteRecording(Models.RecordingInfo? recording)
    {
        if (recording == null || string.IsNullOrEmpty(recording.FilePath)) return;

        try
        {
            if (File.Exists(recording.FilePath))
            {
                File.Delete(recording.FilePath);
                Recordings.Remove(recording);
                _logger.Information("녹음 파일 삭제됨: {File}", recording.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "녹음 파일 삭제 실패: {File}", recording.FileName);
        }
    }

    /// <summary>
    /// 선택된 녹음 변경 시 STT/요약 로드
    /// </summary>
    partial void OnSelectedRecordingChanged(Models.RecordingInfo? value)
    {
        if (value != null)
        {
            _logger.Information("[OneNote] OnSelectedRecordingChanged 호출됨: {FileName}", value.FileName);
            LoadSTTResultAsync(value);
            LoadSummaryResultAsync(value);
        }
        else
        {
            STTSegments.Clear();
            CurrentSummary = null;
        }
    }

    /// <summary>
    /// 선택된 녹음의 STT/요약 결과를 수동으로 로드 (UI에서 직접 호출용)
    /// </summary>
    public void LoadSelectedRecordingResults()
    {
        if (SelectedRecording != null)
        {
            _logger.Information("[OneNote] LoadSelectedRecordingResults 호출: {FileName}", SelectedRecording.FileName);
            LoadSTTResultAsync(SelectedRecording);
            LoadSummaryResultAsync(SelectedRecording);
        }
    }

    /// <summary>
    /// 선택된 녹음의 STT 결과 로드
    /// </summary>
    public async Task LoadSTTResultAsync(Models.RecordingInfo recording)
    {
        STTSegments.Clear();
        _logger.Debug("[OneNote] STT 로드 시작: {FileName}, FilePath: {FilePath}", recording.FileName, recording.FilePath);

        // STT 결과 파일 경로 (녹음 파일과 같은 위치에 .stt.json)
        var sttPath = recording.STTResultPath;
        if (string.IsNullOrEmpty(sttPath))
        {
            sttPath = Path.ChangeExtension(recording.FilePath, ".stt.json");
        }
        _logger.Debug("[OneNote] STT 기본 경로: {Path}, 존재: {Exists}", sttPath, File.Exists(sttPath ?? ""));

        // STT 결과 파일이 없으면 같은 기본 파일명의 STT 파일 검색 (OneNote 녹음 재다운로드 대응)
        if (string.IsNullOrEmpty(sttPath) || !File.Exists(sttPath))
        {
            var dir = Path.GetDirectoryName(recording.FilePath);
            _logger.Debug("[OneNote] STT 기본명 검색 시작: 디렉토리={Dir}", dir);

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                // 파일명에서 기본 이름 추출 (예: "2025.03.04_92.wma" -> "2025.03.04")
                var baseName = Path.GetFileNameWithoutExtension(recording.FileName);
                var originalBaseName = baseName;
                var underscoreIdx = baseName.LastIndexOf('_');
                if (underscoreIdx > 0 && int.TryParse(baseName.Substring(underscoreIdx + 1), out _))
                {
                    baseName = baseName.Substring(0, underscoreIdx);
                }
                _logger.Debug("[OneNote] 기본명 추출: {Original} -> {BaseName}", originalBaseName, baseName);

                // 가장 최근의 STT 결과 파일 찾기
                var searchPattern = $"{baseName}*.stt.json";
                _logger.Debug("[OneNote] 검색 패턴: {Pattern}", searchPattern);

                var sttFiles = Directory.GetFiles(dir, searchPattern)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToArray();
                _logger.Debug("[OneNote] 검색 결과: {Count}개 파일 발견", sttFiles.Length);

                if (sttFiles.Length > 0)
                {
                    sttPath = sttFiles[0];
                    _logger.Information("[OneNote] STT 결과 파일 발견 (기본명 검색): {Path}", sttPath);
                }
            }
        }

        if (string.IsNullOrEmpty(sttPath) || !File.Exists(sttPath))
        {
            _logger.Debug("[OneNote] STT 결과 없음: {FileName}", recording.FileName);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(sttPath);
            var result = STJ.JsonSerializer.Deserialize<Models.TranscriptResult>(json);
            if (result?.Segments != null)
            {
                foreach (var segment in result.Segments)
                {
                    STTSegments.Add(segment);
                }
                recording.STTResultPath = sttPath;
                _logger.Information("[OneNote] STT 결과 로드: {FileName}, {Count}개 세그먼트",
                    recording.FileName, STTSegments.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "[OneNote] STT 결과 로드 실패: {FileName}", recording.FileName);
        }
    }

    /// <summary>
    /// 선택된 녹음의 요약 결과 로드
    /// </summary>
    private async void LoadSummaryResultAsync(Models.RecordingInfo recording)
    {
        CurrentSummary = null;
        _logger.Debug("[OneNote] 요약 로드 시작: {FileName}, FilePath: {FilePath}", recording.FileName, recording.FilePath);

        // 요약 결과 파일 경로 (녹음 파일과 같은 위치에 .summary.json)
        var summaryPath = recording.SummaryResultPath;
        if (string.IsNullOrEmpty(summaryPath))
        {
            summaryPath = Path.ChangeExtension(recording.FilePath, ".summary.json");
        }
        _logger.Debug("[OneNote] 요약 기본 경로: {Path}, 존재: {Exists}", summaryPath, File.Exists(summaryPath ?? ""));

        // 요약 결과 파일이 없으면 같은 기본 파일명의 요약 파일 검색 (OneNote 녹음 재다운로드 대응)
        if (string.IsNullOrEmpty(summaryPath) || !File.Exists(summaryPath))
        {
            var dir = Path.GetDirectoryName(recording.FilePath);
            _logger.Debug("[OneNote] 요약 기본명 검색 시작: 디렉토리={Dir}", dir);

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                // 파일명에서 기본 이름 추출 (예: "2025.03.04_92.wma" -> "2025.03.04")
                var baseName = Path.GetFileNameWithoutExtension(recording.FileName);
                var originalBaseName = baseName;
                var underscoreIdx = baseName.LastIndexOf('_');
                if (underscoreIdx > 0 && int.TryParse(baseName.Substring(underscoreIdx + 1), out _))
                {
                    baseName = baseName.Substring(0, underscoreIdx);
                }
                _logger.Debug("[OneNote] 기본명 추출: {Original} -> {BaseName}", originalBaseName, baseName);

                // 가장 최근의 요약 결과 파일 찾기
                var searchPattern = $"{baseName}*.summary.json";
                _logger.Debug("[OneNote] 검색 패턴: {Pattern}", searchPattern);

                var summaryFiles = Directory.GetFiles(dir, searchPattern)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToArray();
                _logger.Debug("[OneNote] 검색 결과: {Count}개 파일 발견", summaryFiles.Length);

                if (summaryFiles.Length > 0)
                {
                    summaryPath = summaryFiles[0];
                    _logger.Information("[OneNote] 요약 결과 파일 발견 (기본명 검색): {Path}", summaryPath);
                }
            }
        }

        if (string.IsNullOrEmpty(summaryPath) || !File.Exists(summaryPath))
        {
            _logger.Debug("[OneNote] 요약 결과 없음: {FileName}", recording.FileName);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(summaryPath);
            CurrentSummary = STJ.JsonSerializer.Deserialize<Models.RecordingSummary>(json);
            if (CurrentSummary != null)
            {
                recording.SummaryResultPath = summaryPath;
                _logger.Information("[OneNote] 요약 결과 로드: {FileName}", recording.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "[OneNote] 요약 결과 로드 실패: {FileName}", recording.FileName);
        }
    }

    /// <summary>
    /// STT 분석 실행 (sherpa-onnx SenseVoice 또는 Whisper 사용)
    /// </summary>
    /// <param name="recording">녹음 정보</param>
    /// <param name="modelType">STT 모델 유형 (기본: SenseVoice)</param>
    public async Task RunSTTAsync(Models.RecordingInfo recording, Services.Speech.STTModelType modelType = Services.Speech.STTModelType.SenseVoice)
    {
        if (recording == null || string.IsNullOrEmpty(recording.FilePath))
        {
            Utils.Log4.Warn("[OneNote] STT 실행 불가: 녹음 파일 없음");
            return;
        }

        if (!File.Exists(recording.FilePath))
        {
            Utils.Log4.Warn($"[OneNote] STT 실행 불가: 파일이 존재하지 않음 - {recording.FilePath}");
            return;
        }

        IsSTTInProgress = true;
        SttProgress = 0;
        SttProgressText = "준비 중...";
        SttTimeRemaining = "예상 남은 시간: 계산 중...";
        _sttStartTime = DateTime.Now;
        STTSegments.Clear();

        try
        {
            var modelName = modelType switch
            {
                Services.Speech.STTModelType.Whisper => "Whisper (CPU)",
                Services.Speech.STTModelType.WhisperGpu => "Whisper (GPU)",
                _ => "SenseVoice"
            };
            Utils.Log4.Info($"[OneNote] STT 분석 시작: {recording.FileName}, 모델: {modelName}");

            // 모델 다운로드 진행률 이벤트 구독
            void OnDownloadProgress(double progress, string message)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    SttProgress = progress * 0.3; // 다운로드는 전체의 30%
                    SttProgressText = message;
                    Utils.Log4.Debug($"[OneNote] 모델 다운로드: {progress:P0} - {message}");
                });
            }

            _speechService.DownloadProgressChanged += OnDownloadProgress;

            try
            {
                // STT 서비스 초기화 (선택된 모델에 따라)
                bool needsInit = modelType switch
                {
                    Services.Speech.STTModelType.Whisper => !_speechService.IsWhisperInitialized,
                    Services.Speech.STTModelType.WhisperGpu => !_speechService.IsWhisperGpuInitialized,
                    _ => !_speechService.IsSenseVoiceInitialized
                };

                if (needsInit)
                {
                    Utils.Log4.Info($"[OneNote] {modelName} 서비스 초기화 중...");
                    SttProgressText = $"{modelName} 모델 다운로드 중...";

                    bool initialized;
                    if (modelType == Services.Speech.STTModelType.Whisper)
                    {
                        initialized = await _speechService.InitializeWhisperAsync(useGpu: false);
                    }
                    else if (modelType == Services.Speech.STTModelType.WhisperGpu)
                    {
                        initialized = await _speechService.InitializeWhisperAsync(useGpu: true);
                    }
                    else
                    {
                        initialized = await _speechService.InitializeSenseVoiceAsync();
                    }

                    if (!initialized)
                    {
                        Utils.Log4.Error($"[OneNote] {modelName} 서비스 초기화 실패");
                        return;
                    }
                }
            }
            finally
            {
                _speechService.DownloadProgressChanged -= OnDownloadProgress;
            }

            SttProgress = 0.3;
            SttProgressText = "음성 분석 중...";

            // 실시간 세그먼트 수신 이벤트
            void OnSegmentRecognized(Models.TranscriptSegment segment)
            {
                // UI 스레드에서 컬렉션 업데이트
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    STTSegments.Add(segment);
                    LiveSTTSegments.Add(segment);
                });
            }

            // 진행률 이벤트 구독
            void OnProgressChanged(double progress)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // 다운로드(30%) + 분석(70%)
                    SttProgress = 0.3 + (progress * 0.7);

                    // 예상 남은 시간 계산
                    if (_sttStartTime.HasValue && progress > 0.05)
                    {
                        var elapsed = DateTime.Now - _sttStartTime.Value;
                        var estimatedTotal = TimeSpan.FromSeconds(elapsed.TotalSeconds / progress);
                        var remaining = estimatedTotal - elapsed;

                        if (remaining.TotalSeconds > 0)
                        {
                            if (remaining.TotalMinutes >= 1)
                                SttTimeRemaining = $"예상 남은 시간: {remaining.Minutes}분 {remaining.Seconds}초";
                            else
                                SttTimeRemaining = $"예상 남은 시간: {remaining.Seconds}초";
                        }
                        else
                        {
                            SttTimeRemaining = "거의 완료됨...";
                        }
                    }

                    SttProgressText = $"음성 분석 중... ({progress:P0})";
                });
            }

            _speechService.SegmentRecognized += OnSegmentRecognized;
            _speechService.ProgressChanged += OnProgressChanged;

            try
            {
                // 실제 STT 수행 (선택된 모델로)
                var result = await _speechService.TranscribeFileAsync(recording.FilePath, modelType);

                SttProgress = 1.0;
                SttProgressText = "완료!";
                SttTimeRemaining = string.Empty;

                // 결과 저장
                var sttPath = Path.ChangeExtension(recording.FilePath, ".stt.json");
                await _speechService.SaveResultAsync(result, sttPath);
                recording.STTResultPath = sttPath;

                // UI 갱신 (이미 실시간으로 추가되었지만, 최종 결과로 다시 설정)
                STTSegments.Clear();
                foreach (var segment in result.Segments)
                {
                    STTSegments.Add(segment);
                }

                Utils.Log4.Info($"[OneNote] STT 완료: {recording.FileName}, {result.Segments.Count}개 세그먼트, 화자 {result.Speakers.Count}명");
            }
            finally
            {
                _speechService.SegmentRecognized -= OnSegmentRecognized;
                _speechService.ProgressChanged -= OnProgressChanged;
            }
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[OneNote] STT 실행 실패: {recording.FileName} - {ex.Message}");
            SttProgressText = $"오류: {ex.Message}";
        }
        finally
        {
            IsSTTInProgress = false;
            _sttStartTime = null;
        }
    }

    /// <summary>
    /// AI 요약 실행 - STT 결과를 기반으로 요약 생성
    /// </summary>
    public async Task RunSummaryAsync(Models.RecordingInfo recording)
    {
        if (recording == null)
        {
            Utils.Log4.Warn("[OneNote] 요약 실행 불가: 녹음 없음");
            return;
        }

        if (STTSegments.Count == 0)
        {
            Utils.Log4.Warn("[OneNote] 요약 실행 불가: STT 결과 없음");
            return;
        }

        IsSummaryInProgress = true;
        SummaryProgressText = "준비 중...";
        try
        {
            Utils.Log4.Info($"[OneNote] 요약 생성 시작: {recording.FileName}, STT 세그먼트 {STTSegments.Count}개");

            SummaryProgressText = "STT 결과 분석 중...";
            // STT 세그먼트에서 전체 텍스트 추출
            var fullText = string.Join(" ", STTSegments.Select(s => s.Text));
            var speakers = STTSegments.Select(s => s.Speaker).Distinct().ToList();
            var totalDuration = STTSegments.LastOrDefault()?.EndTime ?? TimeSpan.Zero;

            // AIService를 통해 실제 요약 생성 시도
            string titleText = string.Empty;
            string summaryText;
            var keyPoints = new List<string>();
            var actionItems = new List<Models.ActionItem>();
            string modelName = "local-summary";

            try
            {
                // App에서 AIService 가져오기
                var aiService = (System.Windows.Application.Current as App)?.GetService<Services.AI.AIService>();
                if (aiService != null && aiService.CurrentProvider != null)
                {
                    Utils.Log4.Info($"[OneNote] AI Provider 사용: {aiService.CurrentProviderName}");
                    modelName = aiService.CurrentProviderName;

                    SummaryProgressText = $"AI 요약 생성 중... ({modelName})";

                    // 요약 프롬프트 생성
                    var prompt = BuildSummaryPrompt(fullText, speakers, totalDuration);

                    // AI 요약 요청
                    var response = await aiService.CompleteAsync(prompt);
                    Utils.Log4.Info($"[OneNote] AI 응답 길이: {response?.Length ?? 0}");

                    // AI 응답 내용 로깅 (디버깅용, 처음 500자만)
                    if (!string.IsNullOrEmpty(response))
                    {
                        var logResponse = response.Length > 500 ? response.Substring(0, 500) + "..." : response;
                        Utils.Log4.Info($"[OneNote] AI 응답 내용: {logResponse}");
                    }

                    SummaryProgressText = "응답 분석 중...";
                    // 응답 파싱 (title 포함)
                    ParseAISummaryResponse(response, out titleText, out summaryText, out keyPoints, out actionItems);
                    Utils.Log4.Info($"[OneNote] 파싱된 제목: '{titleText}'");
                }
                else
                {
                    Utils.Log4.Info("[OneNote] AI Provider 없음, 로컬 요약 사용");
                    SummaryProgressText = "로컬 요약 생성 중...";
                    // AI Provider 없으면 로컬 요약 생성
                    summaryText = GenerateLocalSummary(fullText, speakers, totalDuration);
                    keyPoints = ExtractKeyPointsLocal(fullText);
                }
            }
            catch (Exception aiEx)
            {
                Utils.Log4.Warn($"[OneNote] AI 요약 실패, 로컬 요약 사용: {aiEx.Message}");
                SummaryProgressText = "로컬 요약으로 대체 중...";
                summaryText = GenerateLocalSummary(fullText, speakers, totalDuration);
                keyPoints = ExtractKeyPointsLocal(fullText);
            }

            SummaryProgressText = "결과 저장 중...";
            // RecordingSummary 객체 생성
            var summary = new Models.RecordingSummary
            {
                AudioFilePath = recording.FilePath,
                CreatedAt = DateTime.Now,
                Title = titleText,
                Summary = summaryText,
                KeyPoints = keyPoints,
                ActionItems = actionItems,
                Participants = speakers,
                RecordingType = DetectRecordingType(fullText, speakers),
                ModelName = modelName,
                SourceSTTPath = recording.STTResultPath
            };

            // 결과 저장
            var summaryPath = Path.ChangeExtension(recording.FilePath, ".summary.json");
            var options = new STJ.JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(summaryPath, STJ.JsonSerializer.Serialize(summary, options));
            recording.SummaryResultPath = summaryPath;

            // UI 갱신
            CurrentSummary = summary;
            SummaryProgressText = "완료!";

            Utils.Log4.Info($"[OneNote] 요약 완료: {recording.FileName}, 제목: {titleText}, 모델: {modelName}");
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[OneNote] 요약 실행 실패: {recording.FileName} - {ex.Message}");
        }
        finally
        {
            IsSummaryInProgress = false;
        }
    }

    /// <summary>
    /// 현재 요약 결과를 파일에 저장 (액션아이템 상태 변경 시 호출)
    /// </summary>
    /// <param name="recording">녹음 정보</param>
    public async Task SaveSummaryAsync(Models.RecordingInfo recording)
    {
        if (recording == null || CurrentSummary == null)
        {
            Utils.Log4.Warn("[OneNote] 요약 저장 불가: 녹음 또는 요약 없음");
            return;
        }

        try
        {
            var summaryPath = Path.ChangeExtension(recording.FilePath, ".summary.json");
            var options = new STJ.JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(summaryPath, STJ.JsonSerializer.Serialize(CurrentSummary, options));
            recording.SummaryResultPath = summaryPath;

            Utils.Log4.Debug($"[OneNote] 요약 저장 완료: {summaryPath}");
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[OneNote] 요약 저장 실패: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// AI 요약용 프롬프트 생성
    /// </summary>
    private string BuildSummaryPrompt(string fullText, List<string> speakers, TimeSpan duration)
    {
        // 전사 내용이 너무 길면 앞부분과 뒷부분 위주로 요약용 텍스트 생성
        var summaryText = fullText;
        if (fullText.Length > 15000)
        {
            // 앞 7000자 + 중간 표시 + 뒤 7000자
            summaryText = fullText.Substring(0, 7000) + 
                "\n\n... [중간 내용 생략] ...\n\n" + 
                fullText.Substring(fullText.Length - 7000);
        }

        return $@"당신은 한국어 회의록 요약 전문가입니다. 아래 녹음 전사 내용을 꼼꼼히 읽고 핵심을 정확하게 추출하세요.

## 녹음 정보
- 총 길이: {duration.TotalMinutes:F1}분
- 참여자 수: {speakers.Count}명

## 전사 내용
{summaryText}

## 중요 지침
- 전사 내용에 실제로 언급된 내용만 추출하세요.
- '>> 사이렌', '감사합니다' 같은 노이즈/인사말은 무시하세요.
- 구체적인 날짜, 회사명, 프로젝트명, 업무 내용을 정확히 추출하세요.

## 응답 형식 (JSON)
{{
  ""title"": ""회의 주제를 나타내는 10~20자 제목 (예: 'Q2 마케팅 전략 회의', '신규 프로젝트 킥오프')"",
  ""summary"": ""이 회의/대화의 핵심 내용을 3~5문장으로 요약. 누가 무엇을 논의했고, 어떤 결론이 났는지 구체적으로 작성"",
  ""keyPoints"": [
    ""구체적 사실/결정사항 1 (예: '삼성생명 프로젝트 4월 말 완료 예정')"",
    ""구체적 사실/결정사항 2"",
    ""구체적 사실/결정사항 3"",
    ""구체적 사실/결정사항 4 (필요시)"",
    ""구체적 사실/결정사항 5 (필요시)""
  ],
  ""actionItems"": [
    {{""description"": ""구체적인 할 일"", ""assignee"": ""담당자명 또는 null"", ""dueDate"": ""기한 또는 null"", ""priority"": ""높음/중간/낮음""}}
  ],
  ""recordingType"": ""회의/강의/인터뷰/브레인스토밍/일상대화/전화통화""
}}

반드시 위 JSON 형식으로만 응답하세요. 마크다운이나 설명 없이 순수 JSON만 출력하세요.";
    }

    /// <summary>
    /// AI 응답 파싱
    /// </summary>
    private void ParseAISummaryResponse(string? response, out string title, out string summary, out List<string> keyPoints, out List<Models.ActionItem> actionItems)
    {
        title = string.Empty;
        summary = string.Empty;
        keyPoints = new List<string>();
        actionItems = new List<Models.ActionItem>();

        if (string.IsNullOrEmpty(response))
            return;

        try
        {
            // JSON 블록 추출
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                using var doc = STJ.JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                // 제목 추출
                if (root.TryGetProperty("title", out var titleProp))
                    title = titleProp.GetString() ?? string.Empty;

                if (root.TryGetProperty("summary", out var summaryProp))
                    summary = summaryProp.GetString() ?? string.Empty;

                if (root.TryGetProperty("keyPoints", out var kpProp) && kpProp.ValueKind == STJ.JsonValueKind.Array)
                {
                    foreach (var item in kpProp.EnumerateArray())
                    {
                        var text = item.GetString();
                        if (!string.IsNullOrEmpty(text))
                            keyPoints.Add(text);
                    }
                }

                if (root.TryGetProperty("actionItems", out var aiProp) && aiProp.ValueKind == STJ.JsonValueKind.Array)
                {
                    foreach (var item in aiProp.EnumerateArray())
                    {
                        var actionItem = new Models.ActionItem
                        {
                            Description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                            Assignee = item.TryGetProperty("assignee", out var assignee) ? assignee.GetString() : null,
                            Priority = item.TryGetProperty("priority", out var priority) ? priority.GetString() ?? "중간" : "중간"
                        };
                        if (!string.IsNullOrEmpty(actionItem.Description))
                            actionItems.Add(actionItem);
                    }
                }
            }
            else
            {
                // JSON이 아닌 경우 전체를 요약으로 사용
                summary = response;
            }
        }
        catch (Exception ex)
        {
            Utils.Log4.Warn($"[OneNote] AI 응답 파싱 실패: {ex.Message}");
            summary = response;
        }
    }

    /// <summary>
    /// 로컬 요약 생성 (AI 없이)
    /// </summary>
    private string GenerateLocalSummary(string fullText, List<string> speakers, TimeSpan duration)
    {
        var speakerInfo = speakers.Count > 1 ? $"{speakers.Count}명의 참여자" : "1명의 화자";
        var durationInfo = duration.TotalMinutes >= 1 ? $"{duration.TotalMinutes:F0}분" : $"{duration.TotalSeconds:F0}초";

        // 텍스트 길이에 따른 요약
        var wordCount = fullText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount < 50)
        {
            return $"짧은 녹음입니다. {speakerInfo}가 {durationInfo} 동안 대화했습니다. 내용: {fullText.Substring(0, Math.Min(200, fullText.Length))}";
        }

        // 첫 문장과 마지막 문장 추출
        var sentences = fullText.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 5)
            .ToList();

        var firstPart = sentences.FirstOrDefault() ?? "";
        var lastPart = sentences.Count > 1 ? sentences.LastOrDefault() ?? "" : "";

        return $"이 녹음은 {speakerInfo}가 {durationInfo} 동안 진행한 대화입니다. " +
               $"대화는 \"{firstPart}\"로 시작하여 " +
               $"\"{lastPart}\"로 마무리됩니다. " +
               $"총 {wordCount}개 단어가 포함되어 있습니다.";
    }

    /// <summary>
    /// 로컬 핵심 포인트 추출
    /// </summary>
    private List<string> ExtractKeyPointsLocal(string fullText)
    {
        var keyPoints = new List<string>();

        // 문장 단위로 분리하여 가장 긴 3개 문장을 핵심 포인트로 선택
        var sentences = fullText.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 10)
            .OrderByDescending(s => s.Length)
            .Take(3)
            .ToList();

        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            if (sentence.Length > 100)
                sentence = sentence.Substring(0, 100) + "...";
            keyPoints.Add($"포인트 {i + 1}: {sentence}");
        }

        if (keyPoints.Count == 0)
        {
            keyPoints.Add("녹음 내용이 너무 짧아 핵심 포인트를 추출할 수 없습니다.");
        }

        return keyPoints;
    }

    /// <summary>
    /// 녹음 유형 감지
    /// </summary>
    private string DetectRecordingType(string fullText, List<string> speakers)
    {
        var text = fullText.ToLower();

        if (text.Contains("회의") || text.Contains("안건") || text.Contains("결정"))
            return "회의";
        if (text.Contains("질문") && text.Contains("답변"))
            return "인터뷰";
        if (speakers.Count == 1)
            return "독백/강의";
        if (speakers.Count == 2)
            return "1:1 대화";

        return "일반 대화";
    }

    /// <summary>
    /// 녹음 시작
    /// </summary>
    // 녹음 서비스 이벤트 핸들러 (중복 등록 방지용)
    private Action<float>? _volumeChangedHandler;
    private Action<TimeSpan>? _durationChangedHandler;
    private Action<string>? _recordingErrorHandler;

    [RelayCommand]
    public void StartRecording()
    {
        if (IsRecording) return;

        try
        {
            // 이전 이벤트 핸들러 해제
            if (_recordingService != null)
            {
                if (_volumeChangedHandler != null)
                    _recordingService.VolumeChanged -= _volumeChangedHandler;
                if (_durationChangedHandler != null)
                    _recordingService.DurationChanged -= _durationChangedHandler;
                _recordingService.RecordingCompleted -= OnRecordingCompleted;
                if (_recordingErrorHandler != null)
                    _recordingService.RecordingError -= _recordingErrorHandler;
            }

            _recordingService ??= new Services.Audio.AudioRecordingService();

            // 이벤트 핸들러 생성
            _volumeChangedHandler = volume =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => RecordingVolume = volume);
            };
            _durationChangedHandler = duration =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => RecordingDuration = duration);
            };
            _recordingErrorHandler = error =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsRecording = false;
                    IsRecordingPaused = false;
                    _logger.Error("녹음 오류: {Error}", error);
                    StopRealtimeSTT();
                });
            };

            // 이벤트 연결
            _recordingService.VolumeChanged += _volumeChangedHandler;
            _recordingService.DurationChanged += _durationChangedHandler;
            _recordingService.RecordingCompleted += OnRecordingCompleted;
            _recordingService.RecordingError += _recordingErrorHandler;

            // 실시간 STT 초기화 (이전 녹음의 결과도 클리어)
            LiveSTTSegments.Clear();
            STTSegments.Clear();
            CurrentSummary = null;
            LiveSummaryText = string.Empty;
            _lastSummarySegmentCount = 0;

            // AI 분석 활성화 시 실시간 STT 시작
            if (IsAIAnalysisEnabled)
            {
                StartRealtimeSTT();
            }

            // 현재 선택된 페이지 ID와 연결 (있으면)
            var pageId = SelectedPage?.Id;
            _recordingService.StartRecording(pageId);

            IsRecording = true;
            IsRecordingPaused = false;
            _logger.Information("녹음 시작됨 (페이지 연결: {PageId}, 실시간 STT: {RealtimeSTT})",
                pageId ?? "없음", IsAIAnalysisEnabled);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "녹음 시작 실패");
            IsRecording = false;
            StopRealtimeSTT();
        }
    }

    /// <summary>
    /// 녹음 완료 이벤트 핸들러
    /// </summary>
    private void OnRecordingCompleted(string filePath)
    {
        _logger.Information("[녹음] 녹음 완료 이벤트 수신: {FilePath}", filePath);

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            _logger.Information("[녹음] 녹음 완료 처리 시작");

            IsRecording = false;
            IsRecordingPaused = false;
            RecordingDuration = TimeSpan.Zero;
            RecordingVolume = 0;

            // 실시간 STT 정리
            StopRealtimeSTT();

            // 녹음 목록 새로고침 (동기)
            _logger.Information("[녹음] 녹음 목록 새로고침 호출");
            LoadRecordings();
            _logger.Information("[녹음] 녹음 목록 새로고침 완료 - CurrentPageRecordings: {Count}개", CurrentPageRecordings.Count);
        });

        // 비동기 작업은 별도로 처리 (fire-and-forget이지만 명시적으로)
        _ = Task.Run(async () =>
        {
            try
            {
                // 실시간 STT 결과가 있으면 저장
                if (LiveSTTSegments.Count > 0)
                {
                    await SaveRealtimeSTTResultAsync(filePath);
                }

                // 실시간 요약이 있으면 저장
                if (!string.IsNullOrWhiteSpace(LiveSummaryText))
                {
                    await SaveRealtimeSummaryAsync(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[녹음] 실시간 STT/요약 저장 실패");
            }
        });
    }

    /// <summary>
    /// 실시간 STT 시작
    /// </summary>
    private void StartRealtimeSTT()
    {
        if (_recordingService == null) return;

        _realtimeSTTCts?.Cancel();
        _realtimeSTTCts = new CancellationTokenSource();

        // 실시간 모드 활성화 (15초 청크)
        _recordingService.RealtimeEnabled = true;
        _recordingService.RealtimeChunkSeconds = 15;

        // 실시간 청크 이벤트 연결
        _recordingService.RealtimeAudioChunkReady += OnRealtimeAudioChunk;

        // 실시간 요약 업데이트 타이머 (30초마다)
        _realtimeSummaryTimer = new System.Timers.Timer(30000);
        _realtimeSummaryTimer.Elapsed += async (s, e) => await UpdateRealtimeSummaryAsync();
        _realtimeSummaryTimer.Start();

        _logger.Information("[녹음] 실시간 STT 모드 활성화 (15초 청크)");
    }

    /// <summary>
    /// 실시간 STT 중지
    /// </summary>
    private void StopRealtimeSTT()
    {
        _realtimeSTTCts?.Cancel();
        _realtimeSTTCts = null;

        _realtimeSummaryTimer?.Stop();
        _realtimeSummaryTimer?.Dispose();
        _realtimeSummaryTimer = null;

        if (_recordingService != null)
        {
            _recordingService.RealtimeEnabled = false;
            _recordingService.RealtimeAudioChunkReady -= OnRealtimeAudioChunk;
        }

        _logger.Information("[녹음] 실시간 STT 모드 비활성화");
    }

    /// <summary>
    /// 실시간 오디오 청크 처리
    /// </summary>
    private async void OnRealtimeAudioChunk(byte[] audioData, TimeSpan chunkStartTime)
    {
        if (_realtimeSTTCts == null || _realtimeSTTCts.IsCancellationRequested)
            return;

        try
        {
            _logger.Debug($"[녹음] 실시간 청크 수신: {chunkStartTime:mm\\:ss}, {audioData.Length} bytes");

            // STT 서비스로 청크 처리
            var segments = await _speechService.ProcessRealtimeChunkAsync(
                audioData, chunkStartTime, 44100, _realtimeSTTCts.Token);

            if (segments.Count > 0)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    foreach (var segment in segments)
                    {
                        LiveSTTSegments.Add(segment);
                    }

                    _logger.Information("[녹음] 실시간 STT: {Count}개 세그먼트 추가 (총 {Total}개)", segments.Count, LiveSTTSegments.Count);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // 취소됨 - 무시
        }
        catch (Exception ex)
        {
            _logger.Error($"[녹음] 실시간 청크 처리 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 실시간 요약 업데이트
    /// </summary>
    private async Task UpdateRealtimeSummaryAsync()
    {
        if (_realtimeSTTCts == null || _realtimeSTTCts.IsCancellationRequested)
            return;

        // 새 세그먼트가 없으면 스킵
        if (LiveSTTSegments.Count <= _lastSummarySegmentCount)
            return;

        try
        {
            var segmentsCopy = System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                LiveSTTSegments.ToList());

            if (segmentsCopy == null || segmentsCopy.Count == 0)
                return;

            // 전체 텍스트 추출
            var fullText = string.Join(" ", segmentsCopy.Select(s => s.Text));
            if (string.IsNullOrWhiteSpace(fullText) || fullText.Length < 50)
                return;

            var speakers = segmentsCopy.Select(s => s.Speaker).Distinct().ToList();
            var duration = segmentsCopy.LastOrDefault()?.EndTime ?? TimeSpan.Zero;

            _logger.Information("[녹음] 실시간 요약 업데이트 시작: {Count}개 세그먼트, {Length}자", segmentsCopy.Count, fullText.Length);

            // AI 요약 시도
            var aiService = (System.Windows.Application.Current as App)?.GetService<Services.AI.AIService>();
            string summaryText;

            if (aiService?.CurrentProvider != null)
            {
                var prompt = BuildRealtimeSummaryPrompt(fullText, speakers, duration);
                var response = await aiService.CompleteAsync(prompt);
                summaryText = response ?? GenerateLocalSummary(fullText, speakers, duration);
            }
            else
            {
                summaryText = GenerateLocalSummary(fullText, speakers, duration);
            }

            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                LiveSummaryText = summaryText;
                _lastSummarySegmentCount = segmentsCopy.Count;
            });

            _logger.Information("[녹음] 실시간 요약 업데이트 완료");
        }
        catch (Exception ex)
        {
            _logger.Error($"[녹음] 실시간 요약 업데이트 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 실시간 요약 프롬프트 생성
    /// </summary>
    private string BuildRealtimeSummaryPrompt(string fullText, List<string> speakers, TimeSpan duration)
    {
        return $@"다음은 진행 중인 녹음의 실시간 음성 인식 결과입니다. 현재까지의 내용을 간략히 요약해주세요.

**참여자**: {string.Join(", ", speakers)}
**경과 시간**: {duration:mm\:ss}

**대화 내용**:
{fullText}

---
위 내용을 3-5문장으로 간략히 요약해주세요.
- 주요 논의 사항
- 참여자들의 주요 발언
형식: 간단한 텍스트 (마크다운 불필요)";
    }

    /// <summary>
    /// 실시간 STT 결과 저장
    /// </summary>
    private async Task SaveRealtimeSTTResultAsync(string audioFilePath)
    {
        if (LiveSTTSegments.Count == 0) return;

        try
        {
            var result = new Models.TranscriptResult
            {
                AudioFilePath = audioFilePath,
                CreatedAt = DateTime.Now,
                ModelName = "SenseVoice-Realtime",
                Language = "ko",
                TotalDuration = LiveSTTSegments.LastOrDefault()?.EndTime ?? TimeSpan.Zero,
                Speakers = LiveSTTSegments.Select(s => s.Speaker).Distinct().ToList()
            };

            result.Segments.AddRange(LiveSTTSegments);

            var sttPath = Path.ChangeExtension(audioFilePath, ".stt.json");
            await _speechService.SaveResultAsync(result, sttPath);

            _logger.Information("[녹음] 실시간 STT 결과 저장: {Path}", sttPath);
        }
        catch (Exception ex)
        {
            _logger.Error($"[녹음] 실시간 STT 결과 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 실시간 요약 결과 저장
    /// </summary>
    private async Task SaveRealtimeSummaryAsync(string audioFilePath)
    {
        if (string.IsNullOrWhiteSpace(LiveSummaryText)) return;

        try
        {
            var speakers = LiveSTTSegments.Select(s => s.Speaker).Distinct().ToList();
            var fullText = string.Join(" ", LiveSTTSegments.Select(s => s.Text));

            var summary = new Models.RecordingSummary
            {
                AudioFilePath = audioFilePath,
                CreatedAt = DateTime.Now,
                Title = "실시간 녹음",
                Summary = LiveSummaryText,
                KeyPoints = new List<string>(),
                ActionItems = new List<Models.ActionItem>(),
                Participants = speakers,
                RecordingType = DetectRecordingType(fullText, speakers),
                ModelName = "Realtime-Summary",
                SourceSTTPath = Path.ChangeExtension(audioFilePath, ".stt.json")
            };

            var summaryPath = Path.ChangeExtension(audioFilePath, ".summary.json");
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(summaryPath, System.Text.Json.JsonSerializer.Serialize(summary, options));

            _logger.Information("[녹음] 실시간 요약 결과 저장: {Path}", summaryPath);
        }
        catch (Exception ex)
        {
            _logger.Error($"[녹음] 실시간 요약 결과 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 녹음 중지
    /// </summary>
    [RelayCommand]
    public void StopRecording()
    {
        if (!IsRecording || _recordingService == null) return;

        try
        {
            var filePath = _recordingService.StopRecording();
            _logger.Information("녹음 중지됨: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "녹음 중지 실패");
        }
    }

    /// <summary>
    /// 녹음 일시정지/재개
    /// </summary>
    [RelayCommand]
    public void TogglePauseRecording()
    {
        if (!IsRecording || _recordingService == null) return;

        try
        {
            if (IsRecordingPaused)
            {
                _recordingService.ResumeRecording();
                IsRecordingPaused = false;
                _logger.Debug("녹음 재개됨");
            }
            else
            {
                _recordingService.PauseRecording();
                IsRecordingPaused = true;
                _logger.Debug("녹음 일시정지됨");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "녹음 일시정지/재개 실패");
        }
    }

    /// <summary>
    /// 녹음 취소
    /// </summary>
    [RelayCommand]
    public void CancelRecording()
    {
        if (!IsRecording || _recordingService == null) return;

        try
        {
            _recordingService.CancelRecording();
            IsRecording = false;
            IsRecordingPaused = false;
            RecordingDuration = TimeSpan.Zero;
            RecordingVolume = 0;
            _logger.Information("녹음 취소됨");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "녹음 취소 실패");
        }
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
    partial void OnSelectedPageChanged(PageItemViewModel? oldValue, PageItemViewModel? newValue)
    {
        Log4.Info($"★★★ OnSelectedPageChanged 호출됨 ★★★ - Old: {oldValue?.Title ?? "null"} -> New: {newValue?.Title ?? "null"}");

        // 이전 페이지에 저장되지 않은 변경사항이 있으면 즉시 저장
        if (HasUnsavedChanges && !string.IsNullOrEmpty(_previousPageId) && !string.IsNullOrEmpty(_editingContent))
        {
            _logger.Information("페이지 전환 - 이전 페이지 자동저장: {PageId}", _previousPageId);
            _ = SavePreviousPageAsync(_previousPageId, _editingContent);
        }

        // 새 페이지 로드
        if (newValue != null)
        {
            _previousPageId = newValue.Id;
            _ = LoadPageContentAsync(newValue.Id);

            // 녹음 관련 데이터 초기화 (페이지 변경 시)
            SelectedRecording = null;
            STTSegments.Clear();
            LiveSTTSegments.Clear();
            CurrentSummary = null;
            LiveSummaryText = string.Empty;

            // 녹음 목록 새로고침 (페이지에 연결된 녹음 + OneNote 녹음)
            _ = LoadRecordingsForCurrentPageAsync();
        }
        else
        {
            _previousPageId = null;
            CurrentPageContent = null;

            // 페이지 미선택 시 모든 녹음 표시
            FilterRecordingsForCurrentPage();
        }
    }

    /// <summary>
    /// 이전 페이지 저장 (페이지 전환 시 호출)
    /// </summary>
    private async Task SavePreviousPageAsync(string pageId, string content)
    {
        try
        {
            _logger.Debug("이전 페이지 저장 시작: {PageId}", pageId);
            var success = await _oneNoteService.UpdatePageContentAsync(pageId, content);
            if (success)
            {
                _logger.Information("이전 페이지 저장 완료: {PageId}", pageId);
            }
            else
            {
                _logger.Warning("이전 페이지 저장 실패: {PageId}", pageId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "이전 페이지 저장 예외: {PageId}", pageId);
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

            // 저장 상태 초기화
            HasUnsavedChanges = false;
            SaveStatus = "저장됨";
            _editingContent = null;

            // 선택된 페이지에서 GroupId/SiteId 가져오기
            var groupId = SelectedPage?.GroupId;
            var siteId = SelectedPage?.SiteId;
            Log4.Debug($"[OneNote] LoadPageContentAsync: PageId={pageId}, GroupId={groupId ?? "N/A"}, SiteId={siteId ?? "N/A"}");

            var content = await _oneNoteService.GetPageContentAsync(pageId, groupId, siteId);

            // editorRoot 콘텐츠 추출 (중복 추가 방지)
            if (!string.IsNullOrEmpty(content))
            {
                content = _oneNoteService.ExtractEditorRootContent(content);
            }

            // Graph API 이미지 URL을 Base64로 변환 (인증 필요한 이미지 처리)
            if (!string.IsNullOrEmpty(content))
            {
                _logger.Debug("이미지 Base64 변환 시작...");
                content = await _oneNoteService.ConvertImagesToBase64Async(content);
            }

            CurrentPageContent = content;
            
            // 로드된 콘텐츠를 _editingContent에도 설정 (자동저장 시 필요)
            _editingContent = content;

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

    /// <summary>
    /// 에디터 콘텐츠 변경 시 호출 (자동저장 트리거)
    /// </summary>
    /// <param name="newContent">새 HTML 콘텐츠</param>
    public void OnContentChanged(string newContent)
    {
        Log4.Debug($"[OneNote] OnContentChanged: {newContent?.Length ?? 0}자, SelectedPage={SelectedPage?.Id ?? "null"}");
        _editingContent = newContent;
        HasUnsavedChanges = true;
        SaveStatus = "수정됨";

        // 자동저장 타이머 리셋
        if (_autoSaveTimer == null)
        {
            _autoSaveTimer = new System.Timers.Timer(AutoSaveDelayMs);
            _autoSaveTimer.Elapsed += async (s, e) =>
            {
                _autoSaveTimer?.Stop();
                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    await SaveAsync();
                });
            };
            _autoSaveTimer.AutoReset = false;
        }

        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    /// <summary>
    /// 페이지 제목 업데이트 (Graph API PATCH)
    /// </summary>
    public async Task UpdatePageTitleAsync(string newTitle)
    {
        if (SelectedPage == null || string.IsNullOrEmpty(SelectedPage.Id))
        {
            Log4.Warn("[OneNote] 제목 업데이트 실패: 선택된 페이지 없음");
            return;
        }

        if (string.IsNullOrWhiteSpace(newTitle))
        {
            Log4.Warn("[OneNote] 제목 업데이트 실패: 새 제목이 비어있음");
            return;
        }

        try
        {
            Log4.Info($"[OneNote] 페이지 제목 업데이트 시작: {SelectedPage.Title} -> {newTitle}");

            var success = await _oneNoteService.UpdatePageTitleAsync(SelectedPage.Id, newTitle);

            if (success)
            {
                // 로컬 상태 업데이트
                SelectedPage.Title = newTitle;
                OnPropertyChanged(nameof(SelectedPage));
                Log4.Info($"[OneNote] 페이지 제목 업데이트 완료: {newTitle}");
            }
            else
            {
                Log4.Warn("[OneNote] 페이지 제목 업데이트 실패");
                throw new Exception("Graph API 호출 실패");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[OneNote] 페이지 제목 업데이트 오류: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 페이지 저장 (Graph API PATCH)
    /// </summary>
    [RelayCommand]
    public async Task SaveAsync()
    {
        Log4.Debug($"[OneNote] SaveAsync 진입: HasUnsavedChanges={HasUnsavedChanges}, PageId={SelectedPage?.Id ?? "null"}, EditingContent={_editingContent?.Length ?? 0}자, IsSaving={IsSaving}");

        // 이미 저장 중이면 스킵
        if (IsSaving)
        {
            Log4.Debug("[OneNote] 이미 저장 중 - 스킵");
            return;
        }

        // 조건 체크
        if (!HasUnsavedChanges)
        {
            Log4.Debug("[OneNote] 저장 스킵: HasUnsavedChanges=false");
            return;
        }
        
        if (string.IsNullOrEmpty(SelectedPage?.Id))
        {
            Log4.Debug("[OneNote] 저장 스킵: SelectedPage가 null 또는 Id가 비어있음");
            return;
        }
        
        if (string.IsNullOrEmpty(_editingContent))
        {
            Log4.Debug("[OneNote] 저장 스킵: _editingContent가 비어있음");
            return;
        }

        try
        {
            IsSaving = true;
            SaveStatus = "저장 중...";
            Log4.Info($"[OneNote] ★★★ 페이지 저장 시작 ★★★: PageId={SelectedPage.Id}, 콘텐츠={_editingContent.Length}자");

            var success = await _oneNoteService.UpdatePageContentAsync(SelectedPage.Id, _editingContent);
            Log4.Debug($"[OneNote] UpdatePageContentAsync 결과: {success}");

            if (success)
            {
                HasUnsavedChanges = false;
                SaveStatus = "저장됨";
                Log4.Info($"[OneNote] ★★★ 페이지 저장 완료 ★★★: {SelectedPage.Id}");
            }
            else
            {
                SaveStatus = "저장 실패";
                Log4.Warn($"[OneNote] 페이지 저장 실패 (API 응답 false): {SelectedPage.Id}");
            }
        }
        catch (Exception ex)
        {
            SaveStatus = "저장 실패";
            Log4.Error($"[OneNote] 페이지 저장 예외: {SelectedPage?.Id} - {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            IsSaving = false;
            Log4.Debug("[OneNote] SaveAsync 완료, IsSaving=false");
        }
    }

    /// <summary>
    /// 자동저장 타이머 및 리소스 정리
    /// </summary>
    public void Dispose()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;

        _audioPlayerService?.Dispose();
        _audioPlayerService = null;

        _recordingService?.Dispose();
        _recordingService = null;
    }

    #region 즐겨찾기 기능

    /// <summary>
    /// 즐겨찾기 목록 로드
    /// </summary>
    public void LoadFavorites()
    {
        try
        {
            FavoritePages.Clear();

            if (!File.Exists(FavoritesFile))
            {
                _logger.Debug("즐겨찾기 파일 없음");
                return;
            }

            var json = File.ReadAllText(FavoritesFile);
            var favorites = JsonConvert.DeserializeObject<FavoritesData>(json);

            if (favorites?.Favorites != null)
            {
                Log4.Info($"[OneNote] 즐겨찾기 JSON 로드: {favorites.Favorites.Count}개");
                foreach (var fav in favorites.Favorites.OrderByDescending(f => f.AddedAt))
                {
                    Log4.Info($"[OneNote] 즐겨찾기 항목: {fav.Title}, ItemType={fav.ItemType} ({(int)fav.ItemType})");
                    var favPage = new PageItemViewModel
                    {
                        Id = fav.PageId,
                        Title = fav.Title,
                        NotebookName = fav.NotebookName,
                        SectionName = fav.SectionName,
                        IsFavorite = true,
                        IsDirectFavorite = true,  // 직접 즐겨찾기된 항목 (최상위 노드)
                        FavoritedAt = fav.AddedAt,
                        GroupId = fav.GroupId ?? string.Empty,
                        SiteId = fav.SiteId ?? string.Empty,
                        ItemType = fav.ItemType,
                        SectionId = fav.NotebookId ?? string.Empty,
                        Source = fav.Source ?? string.Empty
                    };

                    // 노트북/섹션인 경우 확장 아이콘 표시를 위한 더미 자식 추가
                    if (fav.ItemType == FavoriteItemType.Notebook || fav.ItemType == FavoriteItemType.Section)
                    {
                        favPage.Children.Add(new PageItemViewModel { Title = "로딩 중...", ItemType = FavoriteItemType.Page });
                        Log4.Info($"[OneNote] 즐겨찾기 더미 자식 추가: {fav.Title}, Type={fav.ItemType}, Children={favPage.Children.Count}");
                    }

                    FavoritePages.Add(favPage);
                }
            }

            _logger.Information("즐겨찾기 {Count}개 로드", FavoritePages.Count);

            // 첫 번째 섹션/노트북의 Children 확인
            var firstExpandable = FavoritePages.FirstOrDefault(f => f.ItemType == FavoriteItemType.Section || f.ItemType == FavoriteItemType.Notebook);
            if (firstExpandable != null)
            {
                Log4.Info($"[OneNote] 첫 번째 확장 가능 항목: {firstExpandable.Title}, Type={firstExpandable.ItemType}, Children.Count={firstExpandable.Children.Count}");
            }
            else
            {
                Log4.Info("[OneNote] 확장 가능한 즐겨찾기 항목 없음 (모두 Page 타입)");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "즐겨찾기 로드 실패");
        }
    }

    /// <summary>
    /// 즐겨찾기에 페이지 추가
    /// </summary>
    /// <param name="page">추가할 페이지</param>
    public void AddToFavorites(PageItemViewModel page)
    {
        if (page == null || string.IsNullOrEmpty(page.Id))
            return;

        // 이미 즐겨찾기에 있는지 확인
        if (FavoritePages.Any(f => f.Id == page.Id))
        {
            _logger.Debug("이미 즐겨찾기에 있음: {PageId}", page.Id);
            return;
        }

        // 페이지 정보 복사 및 즐겨찾기 설정
        var favoritePage = new PageItemViewModel
        {
            Id = page.Id,
            Title = page.Title,
            SectionId = page.SectionId,
            SectionName = page.SectionName,
            NotebookName = page.NotebookName,
            CreatedDateTime = page.CreatedDateTime,
            LastModifiedDateTime = page.LastModifiedDateTime,
            IsFavorite = true,
            IsDirectFavorite = true,  // 직접 즐겨찾기된 항목
            FavoritedAt = DateTime.Now,
            GroupId = page.GroupId,
            SiteId = page.SiteId
        };

        FavoritePages.Insert(0, favoritePage); // 최신 항목을 맨 위에
        page.IsFavorite = true;
        page.FavoritedAt = favoritePage.FavoritedAt;

        SaveFavorites();
        _logger.Information("즐겨찾기 추가: {Title}", page.Title);
    }

    /// <summary>
    /// 즐겨찾기에서 페이지 제거
    /// </summary>
    /// <param name="page">제거할 페이지</param>
    public void RemoveFromFavorites(PageItemViewModel page)
    {
        if (page == null || string.IsNullOrEmpty(page.Id))
            return;

        var toRemove = FavoritePages.FirstOrDefault(f => f.Id == page.Id);
        if (toRemove != null)
        {
            FavoritePages.Remove(toRemove);
        }

        page.IsFavorite = false;
        page.FavoritedAt = null;

        // 트리뷰의 페이지도 업데이트
        UpdatePageFavoriteStatus(page.Id, false);

        SaveFavorites();
        _logger.Information("즐겨찾기 제거: {Title}", page.Title);
    }

    /// <summary>
    /// 페이지 즐겨찾기 상태 토글
    /// </summary>
    /// <param name="page">토글할 페이지</param>
    public void ToggleFavorite(PageItemViewModel page)
    {
        if (page == null)
            return;

        if (page.IsFavorite)
            RemoveFromFavorites(page);
        else
            AddToFavorites(page);
    }

    /// <summary>
    /// 즐겨찾기에 노트북 추가
    /// </summary>
    public void AddToFavorites(NotebookItemViewModel notebook)
    {
        if (notebook == null || string.IsNullOrEmpty(notebook.Id))
            return;

        // 이미 즐겨찾기에 있는지 확인
        if (FavoritePages.Any(f => f.Id == notebook.Id))
        {
            _logger.Debug("이미 즐겨찾기에 있음: {NotebookId}", notebook.Id);
            return;
        }

        var favoritePage = new PageItemViewModel
        {
            Id = notebook.Id,
            Title = notebook.DisplayName,
            NotebookName = notebook.DisplayName,
            SectionName = string.Empty,
            IsFavorite = true,
            IsDirectFavorite = true,  // 직접 즐겨찾기된 항목
            FavoritedAt = DateTime.Now,
            GroupId = notebook.GroupId,
            SiteId = notebook.SiteId,
            ItemType = FavoriteItemType.Notebook,
            Source = notebook.Source
        };

        // 확장 아이콘 표시를 위한 더미 자식 추가
        favoritePage.Children.Add(new PageItemViewModel { Title = "로딩 중...", ItemType = FavoriteItemType.Page });

        FavoritePages.Insert(0, favoritePage);
        notebook.IsFavorite = true;

        SaveFavorites();
        _logger.Information("즐겨찾기 추가 (노트북): {Title}", notebook.DisplayName);
    }

    /// <summary>
    /// 즐겨찾기에서 노트북 제거
    /// </summary>
    public void RemoveFromFavorites(NotebookItemViewModel notebook)
    {
        if (notebook == null || string.IsNullOrEmpty(notebook.Id))
            return;

        var toRemove = FavoritePages.FirstOrDefault(f => f.Id == notebook.Id);
        if (toRemove != null)
        {
            FavoritePages.Remove(toRemove);
        }

        notebook.IsFavorite = false;

        SaveFavorites();
        _logger.Information("즐겨찾기 제거 (노트북): {Title}", notebook.DisplayName);
    }

    /// <summary>
    /// 즐겨찾기에 섹션 추가
    /// </summary>
    public void AddToFavorites(SectionItemViewModel section)
    {
        if (section == null || string.IsNullOrEmpty(section.Id))
            return;

        // 이미 즐겨찾기에 있는지 확인
        if (FavoritePages.Any(f => f.Id == section.Id))
        {
            _logger.Debug("이미 즐겨찾기에 있음: {SectionId}", section.Id);
            return;
        }

        var favoritePage = new PageItemViewModel
        {
            Id = section.Id,
            Title = section.DisplayName,
            NotebookName = section.NotebookName,
            SectionName = section.DisplayName,
            SectionId = section.NotebookId,
            IsFavorite = true,
            IsDirectFavorite = true,  // 직접 즐겨찾기된 항목
            FavoritedAt = DateTime.Now,
            GroupId = section.GroupId,
            SiteId = section.SiteId,
            ItemType = FavoriteItemType.Section
        };

        // 확장 아이콘 표시를 위한 더미 자식 추가
        favoritePage.Children.Add(new PageItemViewModel { Title = "로딩 중...", ItemType = FavoriteItemType.Page });

        FavoritePages.Insert(0, favoritePage);
        section.IsFavorite = true;

        SaveFavorites();
        _logger.Information("즐겨찾기 추가 (섹션): {Title}", section.DisplayName);
    }

    /// <summary>
    /// 즐겨찾기에서 섹션 제거
    /// </summary>
    public void RemoveFromFavorites(SectionItemViewModel section)
    {
        if (section == null || string.IsNullOrEmpty(section.Id))
            return;

        var toRemove = FavoritePages.FirstOrDefault(f => f.Id == section.Id);
        if (toRemove != null)
        {
            FavoritePages.Remove(toRemove);
        }

        section.IsFavorite = false;

        SaveFavorites();
        _logger.Information("즐겨찾기 제거 (섹션): {Title}", section.DisplayName);
    }

    /// <summary>
    /// 즐겨찾기에서 항목 제거 (ID 기반 - UI 리스트박스용)
    /// </summary>
    public void RemoveFromFavoritesById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return;

        var toRemove = FavoritePages.FirstOrDefault(f => f.Id == id);
        if (toRemove != null)
        {
            FavoritePages.Remove(toRemove);

            // 트리뷰에서도 즐겨찾기 상태 업데이트
            switch (toRemove.ItemType)
            {
                case FavoriteItemType.Notebook:
                    var notebook = Notebooks.FirstOrDefault(n => n.Id == id);
                    if (notebook != null) notebook.IsFavorite = false;
                    break;
                case FavoriteItemType.Section:
                    foreach (var nb in Notebooks)
                    {
                        var section = nb.Sections.FirstOrDefault(s => s.Id == id);
                        if (section != null)
                        {
                            section.IsFavorite = false;
                            break;
                        }
                    }
                    break;
                case FavoriteItemType.Page:
                    UpdatePageFavoriteStatus(id, false);
                    break;
            }

            SaveFavorites();
            _logger.Information("즐겨찾기 제거 (ID): {Id}", id);
        }
    }

    /// <summary>
    /// 트리뷰의 페이지 즐겨찾기 상태 업데이트
    /// </summary>
    private void UpdatePageFavoriteStatus(string pageId, bool isFavorite)
    {
        foreach (var notebook in Notebooks)
        {
            foreach (var section in notebook.Sections)
            {
                var page = section.Pages.FirstOrDefault(p => p.Id == pageId);
                if (page != null)
                {
                    page.IsFavorite = isFavorite;
                    if (!isFavorite)
                        page.FavoritedAt = null;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 즐겨찾기 파일로 저장
    /// </summary>
    public void SaveFavorites()
    {
        try
        {
            var dir = Path.GetDirectoryName(FavoritesFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var data = new FavoritesData
            {
                Favorites = FavoritePages.Select(p => new FavoriteItem
                {
                    PageId = p.Id,
                    Title = p.Title,
                    NotebookName = p.NotebookName,
                    SectionName = p.SectionName,
                    AddedAt = p.FavoritedAt ?? DateTime.Now,
                    GroupId = p.GroupId,
                    SiteId = p.SiteId,
                    ItemType = p.ItemType,
                    NotebookId = p.SectionId,
                    Source = p.Source
                }).ToList()
            };

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(FavoritesFile, json);
            _logger.Debug("즐겨찾기 저장 완료: {Count}개", FavoritePages.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "즐겨찾기 저장 실패");
        }
    }

    /// <summary>
    /// 노트북 로드 후 즐겨찾기 상태 동기화
    /// </summary>
    public void SyncFavoriteStatus()
    {
        var favoriteIds = FavoritePages.Select(f => f.Id).ToHashSet();
        var favoriteTypes = FavoritePages.ToDictionary(f => f.Id, f => f.ItemType);
        var hasUpdates = false;

        foreach (var notebook in Notebooks)
        {
            // 노트북 즐겨찾기 상태 동기화
            if (favoriteIds.Contains(notebook.Id) && favoriteTypes.TryGetValue(notebook.Id, out var nbType) && nbType == FavoriteItemType.Notebook)
            {
                notebook.IsFavorite = true;
            }

            foreach (var section in notebook.Sections)
            {
                // 섹션 즐겨찾기 상태 동기화
                if (favoriteIds.Contains(section.Id) && favoriteTypes.TryGetValue(section.Id, out var secType) && secType == FavoriteItemType.Section)
                {
                    section.IsFavorite = true;
                }

                foreach (var page in section.Pages)
                {
                    if (favoriteIds.Contains(page.Id))
                    {
                        page.IsFavorite = true;
                        var favPage = FavoritePages.FirstOrDefault(f => f.Id == page.Id);
                        if (favPage != null)
                        {
                            page.FavoritedAt = favPage.FavoritedAt;
                            // 즐겨찾기 페이지의 GroupId/SiteId도 동기화
                            if (favPage.GroupId != page.GroupId || favPage.SiteId != page.SiteId)
                            {
                                favPage.GroupId = page.GroupId;
                                favPage.SiteId = page.SiteId;
                                hasUpdates = true;
                                Log4.Debug($"[OneNote] 즐겨찾기 GroupId/SiteId 업데이트: {favPage.Title}, GroupId={page.GroupId}, SiteId={page.SiteId}");
                            }
                        }
                    }
                }
            }
        }

        // GroupId/SiteId가 업데이트되었으면 즐겨찾기 파일 저장
        if (hasUpdates)
        {
            SaveFavorites();
            Log4.Info("[OneNote] 즐겨찾기 GroupId/SiteId 업데이트로 인한 저장 완료");
        }
    }

    /// <summary>
    /// 특정 노트북의 섹션/페이지에 대해 즐겨찾기 상태를 동기화합니다.
    /// (섹션 on-demand 로드 후 호출용)
    /// </summary>
    public void SyncFavoriteStatusForNotebook(NotebookItemViewModel notebook)
    {
        if (notebook == null) return;

        var favoriteIds = FavoritePages.Select(f => f.Id).ToHashSet();
        var favoriteTypes = FavoritePages.ToDictionary(f => f.Id, f => f.ItemType);
        var hasUpdates = false;

        // 노트북 즐겨찾기 상태 동기화
        if (favoriteIds.Contains(notebook.Id) && favoriteTypes.TryGetValue(notebook.Id, out var nbType) && nbType == FavoriteItemType.Notebook)
        {
            notebook.IsFavorite = true;
        }

        foreach (var section in notebook.Sections)
        {
            // 더미 아이템 건너뛰기
            if (section.IsDummyItem) continue;

            // 섹션 즐겨찾기 상태 동기화
            if (favoriteIds.Contains(section.Id) && favoriteTypes.TryGetValue(section.Id, out var secType) && secType == FavoriteItemType.Section)
            {
                section.IsFavorite = true;
                Log4.Debug($"[OneNote] 섹션 즐겨찾기 상태 동기화: {section.DisplayName}");
            }

            foreach (var page in section.Pages)
            {
                if (favoriteIds.Contains(page.Id))
                {
                    page.IsFavorite = true;
                    var favPage = FavoritePages.FirstOrDefault(f => f.Id == page.Id);
                    if (favPage != null)
                    {
                        page.FavoritedAt = favPage.FavoritedAt;
                        // 즐겨찾기 페이지의 GroupId/SiteId도 동기화
                        if (favPage.GroupId != page.GroupId || favPage.SiteId != page.SiteId)
                        {
                            favPage.GroupId = page.GroupId;
                            favPage.SiteId = page.SiteId;
                            hasUpdates = true;
                        }
                    }
                    Log4.Debug($"[OneNote] 페이지 즐겨찾기 상태 동기화: {page.Title}");
                }
            }
        }

        // GroupId/SiteId가 업데이트되었으면 즐겨찾기 파일 저장
        if (hasUpdates)
        {
            SaveFavorites();
        }
    }

    #endregion
}

/// <summary>
/// 즐겨찾기 데이터 (JSON 저장용)
/// </summary>
public class FavoritesData
{
    public List<FavoriteItem> Favorites { get; set; } = new();
}

/// <summary>
/// 즐겨찾기 항목 타입
/// </summary>
public enum FavoriteItemType
{
    Page,
    Section,
    Notebook
}

/// <summary>
/// 즐겨찾기 항목
/// </summary>
public class FavoriteItem
{
    public string PageId { get; set; } = string.Empty;  // ID (Page/Section/Notebook 공용)
    public string Title { get; set; } = string.Empty;
    public string NotebookName { get; set; } = string.Empty;
    public string SectionName { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
    public string? GroupId { get; set; }
    public string? SiteId { get; set; }
    public FavoriteItemType ItemType { get; set; } = FavoriteItemType.Page;  // 항목 타입
    public string? NotebookId { get; set; }  // 노트북 ID (섹션인 경우)
    public string? Source { get; set; }  // 노트북 출처 (Personal/Group/Site)
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
    /// 노트북 출처 (Personal, Group, Site)
    /// </summary>
    [ObservableProperty]
    private string _source = "Personal";

    /// <summary>
    /// 출처 이름 (그룹명/사이트명)
    /// </summary>
    [ObservableProperty]
    private string _sourceName = "개인";

    /// <summary>
    /// 그룹 ID (그룹 노트북인 경우)
    /// </summary>
    [ObservableProperty]
    private string _groupId = string.Empty;

    /// <summary>
    /// 사이트 ID (사이트 노트북인 경우)
    /// </summary>
    [ObservableProperty]
    private string _siteId = string.Empty;

    /// <summary>
    /// 섹션 로드 완료 여부 (on-demand 로딩용)
    /// </summary>
    [ObservableProperty]
    private bool _hasSectionsLoaded;

    /// <summary>
    /// 섹션 로딩 중 여부 (로딩 스피너 표시용)
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingSections;

    /// <summary>
    /// 즐겨찾기 여부
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// 사용자가 수동으로 추가한 사이트 노트북 여부
    /// </summary>
    [ObservableProperty]
    private bool _isCustomSite;

    /// <summary>
    /// 공유 노트북 여부
    /// </summary>
    public bool IsShared => Source != "Personal";

    /// <summary>
    /// 표시용 이름 (출처 포함)
    /// </summary>
    public string DisplayNameWithSource => IsShared ? $"{DisplayName} [{SourceName}]" : DisplayName;

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

    /// <summary>
    /// 그룹 ID (그룹 노트북인 경우)
    /// </summary>
    [ObservableProperty]
    private string _groupId = string.Empty;

    /// <summary>
    /// 사이트 ID (SharePoint 사이트 노트북인 경우)
    /// </summary>
    [ObservableProperty]
    private string _siteId = string.Empty;

    /// <summary>
    /// 더미 아이템 여부 (on-demand 로딩용)
    /// </summary>
    [ObservableProperty]
    private bool _isDummyItem;

    /// <summary>
    /// 즐겨찾기 여부
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// 그룹 노트북 여부
    /// </summary>
    public bool IsGroupNotebook => !string.IsNullOrEmpty(GroupId);

    /// <summary>
    /// 사이트 노트북 여부
    /// </summary>
    public bool IsSiteNotebook => !string.IsNullOrEmpty(SiteId);
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
    /// 즐겨찾기 여부
    /// </summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>
    /// 직접 즐겨찾기된 항목인지 여부 (하위 자식이 아닌 루트 즐겨찾기 항목)
    /// </summary>
    [ObservableProperty]
    private bool _isDirectFavorite;

    /// <summary>
    /// 현재 선택된 항목인지 여부 (양쪽 트리에서 동일 페이지 하이라이트용)
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 즐겨찾기 추가 시간
    /// </summary>
    [ObservableProperty]
    private DateTime? _favoritedAt;

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
    /// 그룹 ID (그룹 노트북인 경우)
    /// </summary>
    [ObservableProperty]
    private string _groupId = string.Empty;

    /// <summary>
    /// 사이트 ID (SharePoint 사이트 노트북인 경우)
    /// </summary>
    [ObservableProperty]
    private string _siteId = string.Empty;

    /// <summary>
    /// 그룹 노트북 여부
    /// </summary>
    public bool IsGroupNotebook => !string.IsNullOrEmpty(GroupId);

    /// <summary>
    /// 사이트 노트북 여부
    /// </summary>
    public bool IsSiteNotebook => !string.IsNullOrEmpty(SiteId);

    /// <summary>
    /// 즐겨찾기 항목 타입 (Page/Section/Notebook)
    /// </summary>
    [ObservableProperty]
    private FavoriteItemType _itemType = FavoriteItemType.Page;

    /// <summary>
    /// 노트북 출처 (Personal/Group/Site) - 노트북 즐겨찾기인 경우
    /// </summary>
    [ObservableProperty]
    private string _source = string.Empty;

    /// <summary>
    /// 즐겨찾기 자식 항목 (노트북→섹션, 섹션→페이지)
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PageItemViewModel> _children = new();

    /// <summary>
    /// 자식 항목 로드 여부
    /// </summary>
    [ObservableProperty]
    private bool _isChildrenLoaded;

    /// <summary>
    /// 자식 로딩 중 여부
    /// </summary>
    [ObservableProperty]
    private bool _isLoadingChildren;

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
