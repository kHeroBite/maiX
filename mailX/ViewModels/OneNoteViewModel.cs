using System;
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

namespace mailX.ViewModels;

/// <summary>
/// OneNote ViewModel - 노트북/섹션/페이지 관리
/// </summary>
public partial class OneNoteViewModel : ViewModelBase
{
    private readonly GraphOneNoteService _oneNoteService;
    private readonly ILogger _logger;

    // 캐시 관련
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "mailX", "cache");
    private static readonly string NotebooksCacheFile = Path.Combine(CacheDir, "onenote_notebooks.json");
    private bool _isInitialLoadFromCache = false;

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
    /// 저장되지 않은 변경사항 있음 여부
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveStatusDisplay))]
    private bool _hasUnsavedChanges;

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
    }

    /// <summary>
    /// 노트북 목록 로드 (캐시 우선, 백그라운드 동기화)
    /// </summary>
    [RelayCommand]
    public async Task LoadNotebooksAsync()
    {
        // 1. 캐시에서 먼저 로드 (빠른 UI 표시) - 로딩 인디케이터 없이 즉시 표시
        if (Notebooks.Count == 0 && !_isInitialLoadFromCache)
        {
            _isInitialLoadFromCache = true;
            var cached = LoadNotebooksFromCache();
            if (cached != null && cached.Count > 0)
            {
                foreach (var nb in cached)
                    Notebooks.Add(nb);
                _logger.Information("캐시에서 노트북 {Count}개 로드", Notebooks.Count);
            }
        }

        // 2. 백그라운드에서 서버 동기화 (로딩 인디케이터 없이)
        _ = Task.Run(async () =>
        {
            try
            {
                var notebooks = await _oneNoteService.GetNotebooksAsync();
                var newList = new System.Collections.Generic.List<NotebookItemViewModel>();

                foreach (var notebook in notebooks)
                {
                    var notebookItem = new NotebookItemViewModel
                    {
                        Id = notebook.Id ?? string.Empty,
                        DisplayName = notebook.DisplayName ?? "Untitled",
                        CreatedDateTime = notebook.CreatedDateTime?.DateTime,
                        LastModifiedDateTime = notebook.LastModifiedDateTime?.DateTime,
                        IsExpanded = true  // 기본적으로 확장
                    };

                    // 섹션 로드
                    var sections = await _oneNoteService.GetSectionsAsync(notebook.Id ?? string.Empty);
                    foreach (var section in sections)
                    {
                        var sectionItem = new SectionItemViewModel
                        {
                            Id = section.Id ?? string.Empty,
                            DisplayName = section.DisplayName ?? "Untitled",
                            NotebookId = notebook.Id ?? string.Empty,
                            NotebookName = notebook.DisplayName ?? string.Empty,
                            IsDefault = section.IsDefault ?? false
                        };

                        // 섹션의 페이지도 미리 로드 (캐시용)
                        var pages = await _oneNoteService.GetPagesAsync(section.Id ?? string.Empty);
                        foreach (var page in pages)
                        {
                            sectionItem.Pages.Add(new PageItemViewModel
                            {
                                Id = page.Id ?? string.Empty,
                                Title = page.Title ?? "Untitled",
                                SectionId = section.Id ?? string.Empty,
                                SectionName = section.DisplayName ?? string.Empty,
                                CreatedDateTime = page.CreatedDateTime?.DateTime,
                                LastModifiedDateTime = page.LastModifiedDateTime?.DateTime
                            });
                        }

                        notebookItem.Sections.Add(sectionItem);
                    }

                    newList.Add(notebookItem);
                }

                // UI 스레드에서 업데이트
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    Notebooks.Clear();
                    foreach (var nb in newList)
                        Notebooks.Add(nb);
                });

                // 캐시 저장
                SaveNotebooksToCache(newList);

                _logger.Information("서버에서 노트북 {Count}개 동기화 완료", Notebooks.Count);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "백그라운드 노트북 동기화 실패");
            }
        });
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
    /// 녹음 파일 목록 로드
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
                return;
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
                Recordings.Add(recording);
            }

            _logger.Information("녹음 파일 {Count}개 로드됨 (mailX: {MailXCount}, 외부: {ExternalCount})",
                Recordings.Count,
                Recordings.Count(r => r.Source == Models.RecordingSource.MailX),
                Recordings.Count(r => r.Source == Models.RecordingSource.External));
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "녹음 파일 로드 실패");
        }
    }

    /// <summary>
    /// 오디오 파일 길이 가져오기
    /// </summary>
    private TimeSpan GetAudioDuration(string filePath)
    {
        try
        {
            using var reader = new NAudio.Wave.AudioFileReader(filePath);
            return reader.TotalTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// 녹음 파일 재생
    /// </summary>
    [RelayCommand]
    public void PlayRecording(Models.RecordingInfo? recording)
    {
        if (recording == null || string.IsNullOrEmpty(recording.FilePath)) return;

        try
        {
            // Windows 기본 프로그램으로 재생
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = recording.FilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "녹음 파일 재생 실패: {File}", recording.FileName);
        }
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
    /// 녹음 시작
    /// </summary>
    [RelayCommand]
    public void StartRecording()
    {
        if (IsRecording) return;

        try
        {
            _recordingService ??= new Services.Audio.AudioRecordingService();

            // 이벤트 연결
            _recordingService.VolumeChanged += volume =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => RecordingVolume = volume);
            };
            _recordingService.DurationChanged += duration =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() => RecordingDuration = duration);
            };
            _recordingService.RecordingCompleted += filePath =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsRecording = false;
                    IsRecordingPaused = false;
                    RecordingDuration = TimeSpan.Zero;
                    RecordingVolume = 0;
                    LoadRecordings(); // 목록 새로고침
                });
            };
            _recordingService.RecordingError += error =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsRecording = false;
                    IsRecordingPaused = false;
                    _logger.Error("녹음 오류: {Error}", error);
                });
            };

            // 현재 선택된 페이지 ID와 연결 (있으면)
            var pageId = SelectedPage?.Id;
            _recordingService.StartRecording(pageId);

            IsRecording = true;
            IsRecordingPaused = false;
            _logger.Information("녹음 시작됨 (페이지 연결: {PageId})", pageId ?? "없음");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "녹음 시작 실패");
            IsRecording = false;
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

            // 저장 상태 초기화
            HasUnsavedChanges = false;
            SaveStatus = "저장됨";
            _editingContent = null;

            var content = await _oneNoteService.GetPageContentAsync(pageId);

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
    /// 자동저장 타이머 정리
    /// </summary>
    public void Dispose()
    {
        _autoSaveTimer?.Stop();
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
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
