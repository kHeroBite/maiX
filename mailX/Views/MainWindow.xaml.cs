using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Wpf.Ui;
using Wpf.Ui.Controls;
using mailX.Models;
using mailX.Models.Settings;
using mailX.Services.Search;
using mailX.Utils;
using mailX.ViewModels;
using mailX.Views.Dialogs;
using mailX.Services.Graph;

namespace mailX.Views;

/// <summary>
/// 메인 윈도우 - 3단 레이아웃 (폴더트리 | 메일리스트 | 본문+AI)
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly Services.Sync.BackgroundSyncService _syncService;
    private Folder? _rightClickedFolder;
    private Email? _rightClickedEmail;
    private bool _webView2Initialized;
    private bool _draftEditorInitialized;
    private bool _draftEditorReady;

    // 실행취소용 변수 (삭제/이동 공통)
    private Email? _lastDeletedEmail;
    private string? _lastDeletedFromFolderId;
    private List<Email>? _lastMovedEmails;
    private Dictionary<int, string>? _lastMovedFromFolderIds; // email.Id -> originalFolderId
    private System.Windows.Threading.DispatcherTimer? _undoTimer;
    private bool _isUndoForMove; // true: 이동 실행취소, false: 삭제 실행취소

    // 드래그&드롭용 변수
    private Point _dragStartPoint;
    private Folder? _draggedFolder;

    // 최근 검색어 (최대 10개)
    private readonly ObservableCollection<string> _recentSearches = new();
    private const int MaxRecentSearches = 10;

    public MainWindow(MainViewModel viewModel, Services.Sync.BackgroundSyncService syncService)
    {
        _syncService = syncService;
        Log4.Debug("MainWindow 생성자 시작");
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        // 검색 폴더 옵션 초기화
        _viewModel.InitializeSearchFolderOptions();

        // 최근 검색어 로드 및 바인딩
        LoadRecentSearches();
        RecentSearchItems.ItemsSource = _recentSearches;

        // 타이틀바 설정
        TitleBar.CloseClicked += (_, _) =>
        {
            Log4.Debug("MainWindow 닫기 버튼 클릭됨");
            Close();
        };

        // 테마 변경 시 메일 목록 새로고침 (글자색 업데이트) + WebView2 테마 갱신 + Mica 백드롭 재적용
        Services.Theme.ThemeService.Instance.ThemeChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                // Mica 백드롭 재적용 (테마 전환 시 유지되도록)
                WindowBackdrop.RemoveBackground(this);
                WindowBackdrop.ApplyBackdrop(this, WindowBackdropType.Mica);

                // CollectionView 새로고침으로 컨버터 재평가
                if (EmailListBox.ItemsSource != null)
                {
                    var view = System.Windows.Data.CollectionViewSource.GetDefaultView(EmailListBox.ItemsSource);
                    view?.Refresh();
                }

                // WebView2 테마 업데이트
                if (_viewModel.SelectedEmail != null)
                {
                    LoadMailBodyAsync(_viewModel.SelectedEmail);
                }
            });
        };

        // SelectedEmail 및 IsEditingDraft 변경 감지
        _viewModel.PropertyChanged += async (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedEmail))
            {
                // 편집 중에 다른 메일 선택 시 자동 저장
                if (_viewModel.IsEditingDraft)
                {
                    await AutoSaveDraftAsync();
                }

                // 임시보관함 메일이면 편집 모드로 열기
                if (_viewModel.SelectedEmail != null && IsDraftsFolder(_viewModel.SelectedFolder))
                {
                    OpenDraftForEditing(_viewModel.SelectedEmail);
                    return;
                }

                LoadMailBodyAsync(_viewModel.SelectedEmail);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsEditingDraft))
            {
                if (_viewModel.IsEditingDraft)
                {
                    // 편집 모드 진입 - TinyMCE 에디터 초기화 및 컨텐츠 로드
                    await InitializeDraftEditorAsync();
                }
                else
                {
                    // 편집 모드 종료
                    _draftEditorReady = false;
                }
            }
        };

        // 캘린더 데이터 업데이트 시 뷰 새로고침
        _viewModel.CalendarDataUpdated += () =>
        {
            Dispatcher.Invoke(async () =>
            {
                // 캘린더 모드일 때만 새로고침
                if (CalendarViewBorder?.Visibility == Visibility.Visible)
                {
                    Log4.Info("캘린더 동기화 완료 - 뷰 새로고침");
                    await LoadMonthEventsAsync(_currentCalendarDate);
                    UpdateCalendarDisplay();
                }
            });
        };

        // WebView2 초기화
        InitializeWebView2Async();

        // MailSyncCompleted 이벤트 직접 구독 (MainViewModel 우회)
        // MainViewModel의 이벤트 핸들러가 호출되지 않는 문제 해결
        _syncService.MailSyncCompleted += OnMailSyncCompletedFromWindow;
        Log4.Info("[MainWindow] MailSyncCompleted 이벤트 구독 완료");

        // CalendarEventsSynced 이벤트 구독 (캘린더 동기화 완료 시 UI 갱신)
        _syncService.CalendarEventsSynced += OnCalendarEventsSyncedFromWindow;
        Log4.Info("[MainWindow] CalendarEventsSynced 이벤트 구독 완료");

        // ChatSynced 이벤트 구독 (채팅 동기화 완료 시 자동 로드)
        _syncService.ChatSynced += OnChatSyncedFromWindow;
        Log4.Info("[MainWindow] ChatSynced 이벤트 구독 완료");

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;
        Log4.Debug("MainWindow 생성자 완료");
    }

    /// <summary>
    /// 메일 동기화 완료 시 UI 갱신 (MainWindow에서 직접 처리)
    /// </summary>
    private void OnMailSyncCompletedFromWindow()
    {
        Log4.Info("[MainWindow] OnMailSyncCompletedFromWindow 이벤트 수신");
        Dispatcher.InvokeAsync(async () =>
        {
            Log4.Info("[MainWindow] Dispatcher에서 읽음 상태 갱신 호출");
            await _viewModel.RefreshEmailReadStatusAsync();
        });
    }

    /// <summary>
    /// 캘린더 동기화 완료 시 UI 갱신 (MainWindow에서 직접 처리)
    /// </summary>
    private void OnCalendarEventsSyncedFromWindow(int added, int updated, int deleted)
    {
        Log4.Info($"[MainWindow] OnCalendarEventsSyncedFromWindow 이벤트 수신: 추가 {added}, 수정 {updated}, 삭제 {deleted}");

        // 변경이 없으면 UI 새로고침 생략
        if (added == 0 && updated == 0 && deleted == 0)
        {
            Log4.Debug("[MainWindow] 캘린더 변경 없음 - UI 새로고침 생략");
            return;
        }

        Dispatcher.InvokeAsync(async () =>
        {
            // 캘린더 뷰가 표시 중일 때만 새로고침
            if (CalendarViewBorder?.Visibility == Visibility.Visible)
            {
                Log4.Info("[MainWindow] 캘린더 동기화 완료 - DB에서 뷰 새로고침");
                await LoadMonthEventsFromDbAsync(_currentCalendarDate);
                UpdateCalendarDisplay();
            }

            // CalendarViewModel이 있으면 새로고침 (추후 사용을 위해 유지)
            _viewModel.CalendarViewModel?.OnCalendarEventsSynced(added, updated, deleted);
        });
    }

    /// <summary>
    /// 채팅 동기화 완료 이벤트 핸들러
    /// 프로그램 시작 시 첫 동기화 후 채팅 데이터를 자동으로 로드
    /// </summary>
    private void OnChatSyncedFromWindow(int chatCount)
    {
        Log4.Info($"[MainWindow] OnChatSyncedFromWindow 이벤트 수신: {chatCount}개 채팅방");

        Dispatcher.InvokeAsync(async () =>
        {
            // TeamsViewModel 초기화 (필요 시)
            if (_teamsViewModel == null)
            {
                try
                {
                    _teamsViewModel = ((App)Application.Current).GetService<TeamsViewModel>()!;
                }
                catch (Exception ex)
                {
                    Log4.Error($"[OnChatSyncedFromWindow] TeamsViewModel 초기화 실패: {ex.Message}");
                    return;
                }
            }

            // 채팅 데이터가 아직 로드되지 않은 경우에만 로드
            if (_teamsViewModel != null && _teamsViewModel.Chats.Count == 0)
            {
                Log4.Info("[OnChatSyncedFromWindow] 채팅 데이터 자동 로드 시작");
                try
                {
                    await _teamsViewModel.LoadChatsAsync();
                    Log4.Info($"[OnChatSyncedFromWindow] 채팅 데이터 로드 완료: {_teamsViewModel.Chats.Count}개");
                }
                catch (Exception ex)
                {
                    Log4.Error($"[OnChatSyncedFromWindow] 채팅 데이터 로드 실패: {ex.Message}");
                }
            }
            else
            {
                Log4.Debug($"[OnChatSyncedFromWindow] 이미 로드됨: {_teamsViewModel?.Chats.Count ?? 0}개");
            }
        });
    }

    /// <summary>
    /// 창 크기 변경 시 검색창 너비 조절
    /// </summary>
    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSearchBoxWidth();
    }

    /// <summary>
    /// 검색창 너비를 창 크기에 맞게 조절
    /// </summary>
    private void UpdateSearchBoxWidth()
    {
        // 타이틀바 고정 요소들의 너비 합계
        // 아이콘(18) + 로고(80) + 메뉴(250) + 폴더콤보(144) + 고급검색버튼(34) + 우측버튼들(5*34=170) + 창버튼(138) + 여백(86)
        const double fixedWidth = 920;
        const double minSearchWidth = 100;
        const double maxSearchWidth = 300;

        double availableWidth = ActualWidth - fixedWidth;
        double searchWidth = Math.Max(minSearchWidth, Math.Min(maxSearchWidth, availableWidth));

        if (TitleBarSearchBox != null)
        {
            TitleBarSearchBox.Width = searchWidth;
        }
    }

    /// <summary>
    /// WebView2 초기화
    /// </summary>
    private async void InitializeWebView2Async()
    {
        try
        {
            await MailBodyWebView.EnsureCoreWebView2Async();
            _webView2Initialized = true;

            // WebView2 설정
            MailBodyWebView.CoreWebView2.Settings.IsScriptEnabled = true;
            MailBodyWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true; // 이벤트 발생을 위해 true 유지
            MailBodyWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            MailBodyWebView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;

            // 테마에 맞게 컨텍스트 메뉴 색상 설정
            var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            MailBodyWebView.CoreWebView2.Profile.PreferredColorScheme =
                theme == Wpf.Ui.Appearance.ApplicationTheme.Dark
                    ? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
                    : Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;

            // 링크 클릭 이벤트 핸들러 등록
            MailBodyWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            MailBodyWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            MailBodyWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;

            Log4.Debug("WebView2 초기화 완료");

            // 이미 선택된 메일이 있으면 로드
            if (_viewModel.SelectedEmail != null)
            {
                LoadMailBodyAsync(_viewModel.SelectedEmail);
            }
        }
        catch (System.Exception ex)
        {
            Log4.Error($"WebView2 초기화 실패: {ex.Message}");
        }
    }

    #region 임시보관함 편집 TinyMCE 에디터

    /// <summary>
    /// 임시보관함 편집용 TinyMCE 에디터 초기화
    /// </summary>
    private async Task InitializeDraftEditorAsync()
    {
        try
        {
            if (!_draftEditorInitialized)
            {
                await DraftBodyWebView.EnsureCoreWebView2Async();
                _draftEditorInitialized = true;

                // 보안 설정
                DraftBodyWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                DraftBodyWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                DraftBodyWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // 메시지 수신 핸들러
                DraftBodyWebView.CoreWebView2.WebMessageReceived += DraftEditor_WebMessageReceived;

                Log4.Debug("DraftBodyWebView 초기화 완료");
            }

            // TinyMCE 에디터 로드
            await LoadDraftTinyMCEEditorAsync();
        }
        catch (System.Exception ex)
        {
            Log4.Error($"DraftBodyWebView 초기화 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 임시보관함 편집용 TinyMCE HTML 로드
    /// </summary>
    private async Task LoadDraftTinyMCEEditorAsync()
    {
        // ThemeService에서 정확한 테마 상태 가져오기
        var isDark = Services.Theme.ThemeService.Instance.IsDarkMode;

        var backgroundColor = isDark ? "#1e1e1e" : "#ffffff";
        var textColor = isDark ? "#ffffff" : "#000000";
        var skin = isDark ? "oxide-dark" : "oxide";
        var contentCss = isDark ? "dark" : "default";

        // 로컬 TinyMCE 폴더 경로 설정 (Self-hosted)
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var tinymcePath = System.IO.Path.Combine(appDir, "Assets", "tinymce");

        // WebView2에서 로컬 파일에 접근할 수 있도록 가상 호스트 매핑
        DraftBodyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "tinymce-draft.local", tinymcePath,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

        var editorHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <script src='https://tinymce-draft.local/tinymce.min.js'></script>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{
            height: 100%;
            background-color: {backgroundColor};
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
        }}
        .tox-tinymce {{ border: none !important; }}
    </style>
</head>
<body>
    <textarea id='editor'></textarea>
    <script>
        let editor;

        tinymce.init({{
            selector: '#editor',
            height: '100%',
            width: '100%',
            menubar: false,
            statusbar: false,
            base_url: 'https://tinymce-draft.local',
            suffix: '.min',
            plugins: 'table lists link image code',
            toolbar: 'bold italic underline strikethrough | forecolor backcolor | fontfamily fontsize | alignleft aligncenter alignright alignjustify | bullist numlist outdent indent | table | link image | code removeformat',
            toolbar_mode: 'wrap',
            font_family_formats: 'Aptos=Aptos,sans-serif; 맑은 고딕=Malgun Gothic; 굴림=Gulim; 돋움=Dotum; 바탕=Batang; 궁서=Gungsuh; Segoe UI=Segoe UI,sans-serif; Arial=arial,helvetica,sans-serif; Arial Black=arial black,avant garde; Comic Sans MS=comic sans ms,sans-serif; Courier New=courier new,courier; Georgia=georgia,palatino; Helvetica=helvetica; Impact=impact,chicago; Tahoma=tahoma,arial,helvetica,sans-serif; Terminal=terminal,monaco; Times New Roman=times new roman,times; Verdana=verdana,geneva',
            skin: '{skin}',
            skin_url: 'https://tinymce-draft.local/skins/ui/{skin}',
            content_css: 'https://tinymce-draft.local/skins/content/{contentCss}/content.min.css',
            content_style: 'body {{ font-family: Aptos, sans-serif; font-size: 14px; color: {textColor}; background-color: {backgroundColor}; padding: 16px; }}',
            table_toolbar: 'tableprops tabledelete | tableinsertrowbefore tableinsertrowafter tabledeleterow | tableinsertcolbefore tableinsertcolafter tabledeletecol',
            table_appearance_options: true,
            table_default_attributes: {{ border: '1' }},
            table_default_styles: {{ 'border-collapse': 'collapse', 'width': '100%' }},
            browser_spellcheck: true,
            contextmenu: false,
            setup: function(ed) {{
                editor = ed;
                ed.on('init', function() {{
                    window.chrome.webview.postMessage({{ type: 'ready' }});
                }});
            }}
        }});

        // C#에서 호출하는 함수들
        window.getContent = function() {{
            return editor ? editor.getContent() : '';
        }};

        window.setContent = function(html) {{
            if (editor) {{
                editor.setContent(html || '');
            }}
        }};

        window.focus = function() {{
            if (editor) {{
                editor.focus();
            }}
        }};
    </script>
</body>
</html>";

        // WebView2로 HTML 로드
        DraftBodyWebView.CoreWebView2.NavigateToString(editorHtml);
    }

    /// <summary>
    /// 임시보관함 에디터 WebView2 메시지 수신 핸들러
    /// </summary>
    private async void DraftEditor_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(e.WebMessageAsJson);
            if (message != null && message.TryGetValue("type", out var type))
            {
                if (type == "ready")
                {
                    _draftEditorReady = true;

                    // 초기 컨텐츠 설정
                    if (!string.IsNullOrEmpty(_viewModel.DraftBody))
                    {
                        await SetDraftEditorContentAsync(_viewModel.DraftBody);
                    }

                    Log4.Debug("임시보관함 TinyMCE 에디터 준비 완료");
                }
            }
        }
        catch (System.Exception ex)
        {
            Log4.Error($"DraftEditor 메시지 처리 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 임시보관함 에디터에 내용 설정
    /// </summary>
    public async Task SetDraftEditorContentAsync(string html)
    {
        if (!_draftEditorReady || DraftBodyWebView.CoreWebView2 == null) return;

        var escapedHtml = System.Text.Json.JsonSerializer.Serialize(html ?? "");
        await DraftBodyWebView.ExecuteScriptAsync($"window.setContent({escapedHtml})");
    }

    /// <summary>
    /// 임시보관함 에디터에서 내용 가져오기
    /// </summary>
    public async Task<string> GetDraftEditorContentAsync()
    {
        if (!_draftEditorReady || DraftBodyWebView.CoreWebView2 == null) return "";

        var result = await DraftBodyWebView.ExecuteScriptAsync("window.getContent()");
        // JSON 문자열로 반환되므로 역직렬화
        if (!string.IsNullOrEmpty(result) && result != "null")
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? "";
        }
        return "";
    }

    /// <summary>
    /// 임시보관함 에디터 내용을 ViewModel에 동기화
    /// </summary>
    public async Task SyncDraftEditorToViewModelAsync()
    {
        if (_draftEditorReady)
        {
            _viewModel.DraftBody = await GetDraftEditorContentAsync();
            Log4.Debug($"DraftBody 동기화 완료: {_viewModel.DraftBody.Length} chars");
        }
    }

    /// <summary>
    /// 임시보관함 보내기 버튼 클릭
    /// </summary>
    private async void DraftSendButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 에디터 내용을 ViewModel에 동기화
            await SyncDraftEditorToViewModelAsync();

            // ViewModel의 보내기 명령 실행
            if (_viewModel.SendDraftCommand.CanExecute(null))
            {
                await _viewModel.SendDraftAsync();
            }
        }
        catch (System.Exception ex)
        {
            Log4.Error($"임시보관함 발송 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 임시보관함 저장 버튼 클릭
    /// </summary>
    private async void DraftSaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 에디터 내용을 ViewModel에 동기화
            await SyncDraftEditorToViewModelAsync();

            // ViewModel의 저장 명령 실행
            if (_viewModel.SaveDraftCommand.CanExecute(null))
            {
                await _viewModel.SaveDraftAsync();
            }
        }
        catch (System.Exception ex)
        {
            Log4.Error($"임시보관함 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 임시보관함 자동 저장 (다른 메일 선택 시 호출)
    /// </summary>
    private async Task AutoSaveDraftAsync()
    {
        try
        {
            Log4.Info("[AutoSaveDraftAsync] 편집 중 다른 메일 선택 - 자동 저장 시작");

            // 에디터 내용을 ViewModel에 동기화
            await SyncDraftEditorToViewModelAsync();

            // 내용이 있으면 저장
            if (!string.IsNullOrWhiteSpace(_viewModel.DraftTo) ||
                !string.IsNullOrWhiteSpace(_viewModel.DraftSubject) ||
                !string.IsNullOrWhiteSpace(_viewModel.DraftBody))
            {
                await _viewModel.AutoSaveDraftAsync();
                Log4.Info("[AutoSaveDraftAsync] 자동 저장 완료");
            }
            else
            {
                // 내용이 없으면 편집 모드만 종료 (SelectedEmail 유지)
                _viewModel.CloseDraftEditor(resetSelectedEmail: false);
                Log4.Debug("[AutoSaveDraftAsync] 내용 없음 - 편집 모드 종료");
            }
        }
        catch (System.Exception ex)
        {
            Log4.Error($"[AutoSaveDraftAsync] 자동 저장 실패: {ex.Message}");
            // 자동 저장 실패 시에도 편집 모드 종료 (SelectedEmail 유지)
            _viewModel.CloseDraftEditor(resetSelectedEmail: false);
        }
    }

    #endregion

    /// <summary>
    /// WebView2 링크 클릭 처리 - 새 브라우저 창에서 열기
    /// </summary>
    private void CoreWebView2_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        // 초기 로드(about:blank 또는 data:)는 허용
        if (e.Uri.StartsWith("about:") || e.Uri.StartsWith("data:"))
            return;

        e.Cancel = true;

        // mailto: 링크인 경우 새 메일 작성 창 열기
        if (e.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            OpenComposeWindowWithMailto(e.Uri);
            return;
        }

        // 외부 링크 클릭 시 기본 브라우저로 열기
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri,
                UseShellExecute = true
            });
            Log4.Debug($"링크 열기: {e.Uri}");
        }
        catch (System.Exception ex)
        {
            Log4.Error($"링크 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// WebView2 새 창 요청 처리 - mailto: 링크 등
    /// </summary>
    private void CoreWebView2_NewWindowRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
    {
        Log4.Debug($"NewWindowRequested: {e.Uri}");

        // mailto: 링크인 경우 새 메일 작성 창 열기
        if (e.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            var mailtoUri = e.Uri;
            // UI 스레드에서 비동기로 실행
            Dispatcher.BeginInvoke(new Action(() => OpenComposeWindowWithMailto(mailtoUri)));
            return;
        }

        // 기타 링크는 기본 브라우저로 열기
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri,
                UseShellExecute = true
            });
        }
        catch (System.Exception ex)
        {
            Log4.Error($"새 창 링크 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// mailto: 링크로 새 메일 작성 창 열기
    /// </summary>
    private void OpenComposeWindowWithMailto(string mailtoUri)
    {
        try
        {
            Log4.Debug($"OpenComposeWindowWithMailto 시작: {mailtoUri}");

            // mailto:email@example.com?subject=제목&body=본문 형식 파싱
            // mailto: 제거 후 파싱
            var mailtoContent = mailtoUri.Substring(7); // "mailto:" 제거

            // URL 디코딩
            mailtoContent = System.Web.HttpUtility.UrlDecode(mailtoContent);

            var parts = mailtoContent.Split('?');
            var toField = parts[0];

            // 이름 <이메일> 형식 파싱 또는 이메일만 있는 경우 DB에서 이름 조회
            var emailWithName = GetEmailWithNameFromDb(toField);

            var subject = "";
            var body = "";
            var cc = "";
            var bcc = "";

            if (parts.Length > 1)
            {
                var query = System.Web.HttpUtility.ParseQueryString(parts[1]);
                subject = query["subject"] ?? "";
                body = query["body"] ?? "";
                cc = query["cc"] ?? "";
                bcc = query["bcc"] ?? "";
            }

            Log4.Debug($"파싱된 이메일: {emailWithName}");

            var graphMailService = (App.Current as App)?.GraphMailService;
            if (graphMailService == null)
            {
                Log4.Error("GraphMailService를 찾을 수 없습니다.");
                return;
            }

            var syncService = (App.Current as App)?.BackgroundSyncService;
            var viewModel = new ViewModels.ComposeViewModel(graphMailService, syncService, ViewModels.ComposeMode.New, null);

            // ViewModel 생성 후 프로퍼티 설정
            viewModel.To = emailWithName;
            if (!string.IsNullOrEmpty(subject)) viewModel.Subject = subject;
            if (!string.IsNullOrEmpty(cc)) viewModel.Cc = cc;
            if (!string.IsNullOrEmpty(bcc)) viewModel.Bcc = bcc;
            if (!string.IsNullOrEmpty(body)) viewModel.Body = body;

            var composeWindow = new ComposeWindow(viewModel);
            composeWindow.Owner = this;
            composeWindow.Show(); // ShowDialog 대신 Show 사용

            Log4.Debug($"mailto 링크로 새 메일 작성 창 열림: {emailWithName}");
        }
        catch (System.Exception ex)
        {
            Log4.Error($"mailto 링크 처리 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 이메일 주소로 DB에서 이름을 찾아 "이름 &lt;이메일&gt;" 형식으로 반환
    /// </summary>
    private static string GetEmailWithNameFromDb(string emailString)
    {
        if (string.IsNullOrWhiteSpace(emailString))
            return "";

        emailString = emailString.Trim();

        // 이미 "이름 <이메일>" 형식이면 그대로 반환
        if (emailString.Contains("<") && emailString.Contains(">"))
            return emailString;

        // 이메일만 있는 경우 DB에서 이름 조회
        try
        {
            using var context = new Data.MailXDbContext(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Data.MailXDbContext>()
                    .UseSqlite($"Data Source={App.DatabasePath}")
                    .Options);

            // From 필드에서 해당 이메일을 가진 레코드 검색
            var fromWithName = context.Emails
                .Where(e => e.From != null && e.From.Contains(emailString))
                .Select(e => e.From)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(fromWithName) && fromWithName.Contains("<") && fromWithName.Contains(">"))
            {
                // "이름 <이메일>" 형식 발견
                Log4.Debug($"DB에서 이름 찾음: {fromWithName}");
                return fromWithName;
            }
        }
        catch (System.Exception ex)
        {
            Log4.Warn($"DB에서 이름 조회 실패 (무시): {ex.Message}");
        }

        // 찾지 못하면 이메일만 반환
        return emailString;
    }

    /// <summary>
    /// WebView2 우클릭 컨텍스트 메뉴 처리
    /// </summary>
    private void CoreWebView2_ContextMenuRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
    {
        Log4.Debug($"ContextMenuRequested 이벤트 발생 - HasLinkUri: {e.ContextMenuTarget.HasLinkUri}");

        var menuItems = e.MenuItems;

        // 링크 위에서 우클릭한 경우
        if (e.ContextMenuTarget.HasLinkUri)
        {
            var linkUri = e.ContextMenuTarget.LinkUri;
            Log4.Debug($"링크 감지됨: {linkUri}");

            // 기존 메뉴 지우고 커스텀 메뉴 추가
            menuItems.Clear();

            // mailto: 링크인 경우
            if (linkUri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                // 이메일 주소 추출
                var emailAddress = linkUri.Substring(7).Split('?')[0];

                // 새 메일 작성 메뉴
                var composeItem = MailBodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
                    "새 메일 작성", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
                var capturedLinkUri = linkUri; // 클로저를 위한 캡처
                composeItem.CustomItemSelected += (s, args) =>
                {
                    Dispatcher.BeginInvoke(new Action(() => OpenComposeWindowWithMailto(capturedLinkUri)));
                };
                menuItems.Add(composeItem);

                // 메일 주소 복사 메뉴
                var copyEmailItem = MailBodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
                    "메일 주소 복사", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
                copyEmailItem.CustomItemSelected += (s, args) =>
                {
                    Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(emailAddress));
                    Log4.Debug($"메일 주소 복사됨: {emailAddress}");
                };
                menuItems.Add(copyEmailItem);
            }
            else
            {
                // 일반 링크 - 링크 열기 메뉴
                var openLinkItem = MailBodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
                    "링크 열기", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
                openLinkItem.CustomItemSelected += (s, args) =>
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = linkUri,
                            UseShellExecute = true
                        });
                        Log4.Debug($"링크 열기: {linkUri}");
                    }
                    catch (System.Exception ex)
                    {
                        Log4.Error($"링크 열기 실패: {ex.Message}");
                    }
                };
                menuItems.Add(openLinkItem);

                // 링크 복사 메뉴
                var copyLinkItem = MailBodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
                    "링크 복사", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
                copyLinkItem.CustomItemSelected += (s, args) =>
                {
                    Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(linkUri));
                    Log4.Debug($"링크 복사됨: {linkUri}");
                };
                menuItems.Add(copyLinkItem);
            }
        }
        // 일반 영역은 기본 메뉴 사용 (복사, 전체 선택 등)
    }

    /// <summary>
    /// 메일 본문을 WebView2에 로드
    /// </summary>
    private async void LoadMailBodyAsync(Email? email)
    {
        if (!_webView2Initialized || MailBodyWebView.CoreWebView2 == null)
            return;

        if (email == null || string.IsNullOrEmpty(email.Body))
        {
            await MailBodyWebView.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML = ''");
            return;
        }

        try
        {
            // 테마에 따른 스타일 결정
            var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            var isDark = theme == Wpf.Ui.Appearance.ApplicationTheme.Dark;
            var bgColor = isDark ? "#1e1e1e" : "#ffffff";
            var textColor = isDark ? "#e0e0e0" : "#1e1e1e";
            var scrollbarThumbColor = isDark ? "#555555" : "#c0c0c0";
            var scrollbarThumbHoverColor = isDark ? "#777777" : "#a0a0a0";
            var scrollbarTrackColor = isDark ? "#2d2d2d" : "#f0f0f0";

            string htmlContent;
            if (email.IsHtml)
            {
                // HTML 메일: 스타일 래핑
                // 다크모드일 때 인라인 스타일을 덮어쓰기 위해 !important 사용
                var darkModeOverride = isDark ? @"
        /* 다크모드: 인라인 스타일 강제 덮어쓰기 */
        body, p, div, span, td, th, li, h1, h2, h3, h4, h5, h6, font, blockquote, pre, code {
            color: #e0e0e0 !important;
            background-color: transparent !important;
        }
        /* 이미지 배경 제외 (투명하게) */
        img { background-color: transparent !important; }
        a { color: #6db3f2 !important; }
        table { border-color: #444444 !important; background-color: transparent !important; }
        td, th { border-color: #444444 !important; background-color: transparent !important; }
        /* 강제 배경색 리셋 */
        [style*='background'] { background-color: transparent !important; background: transparent !important; }
        [bgcolor] { background-color: transparent !important; }
" : "";

                htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{
            font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 20px;
            margin: 0;
            background-color: {bgColor} !important;
            color: {textColor};
        }}
        img {{ max-width: 100%; height: auto; }}
        a {{ color: #0078d4; }}
        table {{ border-collapse: collapse; }}
        td, th {{ padding: 8px; }}
        /* 스크롤바 스타일 (Webkit 브라우저용) */
        ::-webkit-scrollbar {{ width: 6px; height: 6px; }}
        ::-webkit-scrollbar-track {{ background: {scrollbarTrackColor}; border-radius: 3px; }}
        ::-webkit-scrollbar-thumb {{ background: {scrollbarThumbColor}; border-radius: 3px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: {scrollbarThumbHoverColor}; }}
        {darkModeOverride}
    </style>
    <script>
        // mailto 링크에 이름 포함시키기
        document.addEventListener('DOMContentLoaded', function() {{
            document.querySelectorAll('a[href^=""mailto:""]').forEach(function(link) {{
                var href = link.getAttribute('href');
                var email = href.substring(7).split('?')[0]; // mailto: 제거
                var text = link.textContent.trim();

                // 링크 텍스트가 @이름 형식이면 이름 추출
                if (text.startsWith('@') && !text.includes('@', 1)) {{
                    var name = text.substring(1); // @ 제거
                    // href에 이름 <이메일> 형식으로 변경
                    var newHref = 'mailto:' + encodeURIComponent(name + ' <' + email + '>');
                    // subject, body 등 쿼리 파라미터 유지
                    if (href.includes('?')) {{
                        newHref += '?' + href.split('?')[1];
                    }}
                    link.setAttribute('href', newHref);
                }}
            }});
        }});
    </script>
</head>
<body>
{email.Body}
</body>
</html>";
            }
            else
            {
                // 텍스트 메일: pre 태그로 래핑
                var escapedBody = WebUtility.HtmlEncode(email.Body);
                htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <style>
        body {{
            font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 20px;
            margin: 0;
            background-color: {bgColor};
            color: {textColor};
        }}
        pre {{
            white-space: pre-wrap;
            word-wrap: break-word;
            font-family: inherit;
            margin: 0;
        }}
        /* 스크롤바 스타일 (Webkit 브라우저용) */
        ::-webkit-scrollbar {{ width: 6px; height: 6px; }}
        ::-webkit-scrollbar-track {{ background: {scrollbarTrackColor}; border-radius: 3px; }}
        ::-webkit-scrollbar-thumb {{ background: {scrollbarThumbColor}; border-radius: 3px; }}
        ::-webkit-scrollbar-thumb:hover {{ background: {scrollbarThumbHoverColor}; }}
    </style>
</head>
<body>
<pre>{escapedBody}</pre>
</body>
</html>";
            }

            MailBodyWebView.CoreWebView2.NavigateToString(htmlContent);
        }
        catch (System.Exception ex)
        {
            Log4.Error($"메일 본문 로드 실패: {ex.Message}");
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Log4.Debug("MainWindow_Loaded 시작");

        // GPU 모드 체크마크 초기화
        UpdateGpuModeCheckmark();

        // 저장된 동기화 설정 로드
        LoadSavedSyncSettings();

        // 동기화/분석 기간 및 주기 현재 설정 표시 초기화
        UpdateSyncPeriodCurrentDisplay();
        UpdateFavoriteSyncPeriodCurrentDisplay();
        UpdateFavoriteSyncIntervalCurrentDisplay();
        UpdateFullSyncIntervalCurrentDisplay();
        UpdateAIAnalysisPeriodCurrentDisplay();
        UpdateFavoriteAiPeriodCurrentDisplay();
        UpdateFavoriteAnalysisIntervalCurrentDisplay();
        UpdateFullAnalysisIntervalCurrentDisplay();

        // 자동 로그인 메뉴 상태 초기화
        InitializeAutoLoginMenu();

        // 테마 아이콘 초기화
        UpdateThemeIcon();

        // 검색창 초기 크기 설정
        UpdateSearchBoxWidth();

        // 검색 자동완성 팝업 닫기를 위한 전역 클릭 이벤트
        PreviewMouseDown += MainWindow_PreviewMouseDown;

        // 윈도우 비활성화 시 팝업 닫기
        Deactivated += (s, args) => SearchAutocompletePopup.IsOpen = false;

        // 폴더 목록 초기 로드
        await _viewModel.LoadFoldersCommand.ExecuteAsync(null);

        // 채팅 데이터 자동 로드 (BackgroundSyncService 초기 동기화보다 MainWindow 생성이 늦기 때문에 직접 로드)
        await LoadChatsOnStartupAsync();

        Log4.Debug("MainWindow_Loaded 완료");
    }

    /// <summary>
    /// 프로그램 시작 시 채팅 데이터 자동 로드
    /// MainWindow는 BackgroundSyncService 초기 동기화 이후에 생성되므로
    /// ChatSynced 이벤트를 놓치게 됨 → 직접 로드
    /// </summary>
    private async Task LoadChatsOnStartupAsync()
    {
        try
        {
            // TeamsViewModel 초기화 (필요 시)
            if (_teamsViewModel == null)
            {
                _teamsViewModel = ((App)Application.Current).GetService<TeamsViewModel>()!;
            }

            // 채팅 데이터가 아직 로드되지 않은 경우에만 로드
            if (_teamsViewModel != null && _teamsViewModel.Chats.Count == 0)
            {
                Log4.Info("[MainWindow_Loaded] 채팅 데이터 자동 로드 시작");
                await _teamsViewModel.LoadChatsAsync();
                Log4.Info($"[MainWindow_Loaded] 채팅 데이터 로드 완료: {_teamsViewModel.Chats.Count}개");
            }
            else
            {
                Log4.Debug($"[MainWindow_Loaded] 채팅 데이터 이미 로드됨: {_teamsViewModel?.Chats.Count ?? 0}개");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[MainWindow_Loaded] 채팅 데이터 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 전역 마우스 클릭 시 검색 자동완성 팝업 닫기
    /// </summary>
    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 검색창 또는 팝업 내부 클릭이 아니면 팝업 닫기
        if (SearchAutocompletePopup.IsOpen)
        {
            var clickedElement = e.OriginalSource as DependencyObject;

            // 검색창 내부 클릭인지 확인
            if (IsDescendantOf(clickedElement, TitleBarSearchBox))
                return;

            // 팝업 내부 클릭인지 확인
            if (IsDescendantOf(clickedElement, SearchAutocompletePopup.Child))
                return;

            SearchAutocompletePopup.IsOpen = false;
        }
    }

    /// <summary>
    /// 요소가 부모의 자손인지 확인
    /// </summary>
    private static bool IsDescendantOf(DependencyObject? element, DependencyObject? parent)
    {
        if (element == null || parent == null)
            return false;

        var current = element;
        while (current != null)
        {
            if (current == parent)
                return true;
            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void MainWindow_Closed(object? sender, System.EventArgs e)
    {
        Log4.Debug("MainWindow_Closed - 애플리케이션 종료");

        // 이벤트 구독 해제
        _syncService.MailSyncCompleted -= OnMailSyncCompletedFromWindow;
        _syncService.CalendarEventsSynced -= OnCalendarEventsSyncedFromWindow;
        _syncService.ChatSynced -= OnChatSyncedFromWindow;

        // OnExplicitShutdown 모드에서는 명시적으로 종료 호출 필요
        Application.Current.Shutdown();
    }

    /// <summary>
    /// TreeView 폴더 선택 이벤트 핸들러
    /// </summary>
    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is Folder selectedFolder)
        {
            _viewModel.SelectedFolder = selectedFolder;
            // 즐겨찾기 ListBox 선택 해제
            ClearFavoriteListBoxSelection();
        }
    }

    /// <summary>
    /// 즐겨찾기 ListBox 선택 해제
    /// </summary>
    private void ClearFavoriteListBoxSelection()
    {
        var favoriteListBox = FindName("FavoriteListBox") as System.Windows.Controls.ListBox;
        if (favoriteListBox != null)
        {
            favoriteListBox.SelectedItem = null;
        }
    }

    /// <summary>
    /// TreeView 선택 해제
    /// </summary>
    private void ClearTreeViewSelection()
    {
        if (FolderTreeView.SelectedItem != null)
        {
            // 재귀적으로 선택된 TreeViewItem 찾아서 해제
            ClearTreeViewItemSelection(FolderTreeView);
        }
    }

    /// <summary>
    /// TreeView의 모든 항목에서 선택 해제 (재귀)
    /// </summary>
    private void ClearTreeViewItemSelection(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as System.Windows.Controls.TreeViewItem;
            if (container != null)
            {
                if (container.IsSelected)
                {
                    container.IsSelected = false;
                }
                // 자식 항목도 재귀적으로 확인
                if (container.HasItems)
                {
                    ClearTreeViewItemSelection(container);
                }
            }
        }
    }

    /// <summary>
    /// 즐겨찾기 ListBox 선택 변경 이벤트
    /// </summary>
    private void FavoriteListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBox listBox && listBox.SelectedItem is Folder folder)
        {
            _viewModel.SelectedFolder = folder;
            // TreeView 선택 해제
            ClearTreeViewSelection();
        }
    }

    /// <summary>
    /// TreeViewItem 우클릭 시 폴더 컨텍스트 메뉴 표시
    /// </summary>
    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.TreeViewItem treeViewItem &&
            treeViewItem.DataContext is Folder folder)
        {
            _rightClickedFolder = folder;
            treeViewItem.IsSelected = true; // 우클릭한 항목 선택

            // 동적으로 컨텍스트 메뉴 생성
            var contextMenu = new System.Windows.Controls.ContextMenu
            {
                Background = (Brush)FindResource("ApplicationBackgroundBrush"),
                BorderBrush = (Brush)FindResource("ControlElevationBorderBrush"),
                Padding = new Thickness(4)
            };

            // 하위 폴더 만들기
            var createItem = new System.Windows.Controls.MenuItem
            {
                Header = "📁 하위 폴더 만들기",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            createItem.Click += FolderCreate_Click;
            contextMenu.Items.Add(createItem);

            // 이름 바꾸기
            var renameItem = new System.Windows.Controls.MenuItem
            {
                Header = "✏️ 이름 바꾸기",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            renameItem.Click += FolderRename_Click;
            contextMenu.Items.Add(renameItem);

            // 즐겨찾기 추가/제거
            var favoriteItem = new System.Windows.Controls.MenuItem
            {
                Header = folder.IsFavorite ? "⭐ 즐겨찾기 해제" : "⭐ 즐겨찾기 추가",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            favoriteItem.Click += (s, args) => _viewModel.ToggleFavoriteCommand.Execute(folder);
            contextMenu.Items.Add(favoriteItem);

            contextMenu.Items.Add(new System.Windows.Controls.Separator
            {
                Background = (Brush)FindResource("ControlElevationBorderBrush")
            });

            // 삭제
            var deleteItem = new System.Windows.Controls.MenuItem
            {
                Header = "🗑️ 삭제",
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
            };
            deleteItem.Click += FolderDelete_Click;
            contextMenu.Items.Add(deleteItem);

            // 컨텍스트 메뉴 표시
            treeViewItem.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;

            e.Handled = true;
        }
    }

    /// <summary>
    /// 즐겨찾기 추가 (폴더 트리에서)
    /// </summary>
    private void AddFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null && !_rightClickedFolder.IsFavorite)
        {
            _viewModel.ToggleFavoriteCommand.Execute(_rightClickedFolder);
        }
    }

    #region 폴더 CRUD 이벤트 핸들러

    /// <summary>
    /// 하위 폴더 만들기
    /// </summary>
    private async void FolderCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder == null)
        {
            Log4.Warn("폴더 생성 실패: 선택된 폴더 없음");
            return;
        }

        // 폴더 이름 입력 다이얼로그 (간단한 InputBox 대용)
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "새 폴더 만들기",
            Content = new System.Windows.Controls.TextBox
            {
                Name = "FolderNameInput",
                Width = 300,
                Text = "새 폴더",
                SelectionStart = 0,
                SelectionLength = 4
            },
            PrimaryButtonText = "만들기",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            var textBox = dialog.Content as System.Windows.Controls.TextBox;
            var folderName = textBox?.Text?.Trim();

            if (!string.IsNullOrEmpty(folderName))
            {
                Log4.Info($"폴더 생성 요청: '{folderName}' (상위: {_rightClickedFolder.DisplayName})");
                // 선택된 폴더를 설정한 후 Command 호출
                _viewModel.SelectedFolder = _rightClickedFolder;
                if (_viewModel.CreateFolderCommand.CanExecute(folderName))
                {
                    await _viewModel.CreateFolderCommand.ExecuteAsync(folderName);
                }
            }
        }
    }

    /// <summary>
    /// 폴더 이름 바꾸기
    /// </summary>
    private async void FolderRename_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder == null)
        {
            Log4.Warn("폴더 이름 변경 실패: 선택된 폴더 없음");
            return;
        }

        // 시스템 폴더는 이름 변경 불가
        if (IsSystemFolder(_rightClickedFolder.DisplayName))
        {
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "알림",
                Content = "시스템 폴더는 이름을 변경할 수 없습니다.",
                CloseButtonText = "확인"
            }.ShowDialogAsync();
            return;
        }

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "폴더 이름 바꾸기",
            Content = new System.Windows.Controls.TextBox
            {
                Name = "FolderNameInput",
                Width = 300,
                Text = _rightClickedFolder.DisplayName
            },
            PrimaryButtonText = "변경",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            var textBox = dialog.Content as System.Windows.Controls.TextBox;
            var newName = textBox?.Text?.Trim();

            if (!string.IsNullOrEmpty(newName) && newName != _rightClickedFolder.DisplayName)
            {
                Log4.Info($"폴더 이름 변경 요청: '{_rightClickedFolder.DisplayName}' → '{newName}'");
                var args = (_rightClickedFolder, newName);
                if (_viewModel.RenameFolderCommand.CanExecute(args))
                {
                    await _viewModel.RenameFolderCommand.ExecuteAsync(args);
                }
            }
        }
    }

    /// <summary>
    /// 폴더 삭제
    /// </summary>
    private async void FolderDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder == null)
        {
            Log4.Warn("폴더 삭제 실패: 선택된 폴더 없음");
            return;
        }

        // 시스템 폴더는 삭제 불가
        if (IsSystemFolder(_rightClickedFolder.DisplayName))
        {
            await new Wpf.Ui.Controls.MessageBox
            {
                Title = "알림",
                Content = "시스템 폴더는 삭제할 수 없습니다.",
                CloseButtonText = "확인"
            }.ShowDialogAsync();
            return;
        }

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "폴더 삭제",
            Content = $"'{_rightClickedFolder.DisplayName}' 폴더를 삭제하시겠습니까?\n폴더 내 모든 메일이 함께 삭제됩니다.",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            Log4.Info($"폴더 삭제 요청: '{_rightClickedFolder.DisplayName}'");
            if (_viewModel.DeleteFolderCommand.CanExecute(_rightClickedFolder))
            {
                await _viewModel.DeleteFolderCommand.ExecuteAsync(_rightClickedFolder);
            }
        }
    }

    /// <summary>
    /// 시스템 폴더 여부 확인
    /// </summary>
    private bool IsSystemFolder(string folderName)
    {
        var systemFolders = new[] { "받은 편지함", "보낸 편지함", "임시 보관함", "지운 편지함", "정크 메일",
                                     "Inbox", "Sent Items", "Drafts", "Deleted Items", "Junk Email" };
        return systemFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase);
    }

    #endregion

    /// <summary>
    /// 즐겨찾기 ListBox 우클릭 시 해당 폴더 저장
    /// </summary>
    private void FavoriteListBox_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 우클릭한 항목의 DataContext에서 Folder 가져오기
        if (e.OriginalSource is FrameworkElement element)
        {
            var folder = FindParentDataContext<Folder>(element);
            if (folder != null)
            {
                _rightClickedFolder = folder;
                _viewModel.SelectedFolder = folder;
            }
        }
    }

    /// <summary>
    /// 부모 요소에서 DataContext 찾기
    /// </summary>
    private T? FindParentDataContext<T>(FrameworkElement element) where T : class
    {
        var current = element;
        while (current != null)
        {
            if (current.DataContext is T data)
                return data;
            current = current.Parent as FrameworkElement;
        }
        return null;
    }

    /// <summary>
    /// 즐겨찾기 제거 (즐겨찾기 영역에서)
    /// </summary>
    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null && _rightClickedFolder.IsFavorite)
        {
            _viewModel.ToggleFavoriteCommand.Execute(_rightClickedFolder);
        }
    }

    #region 즐겨찾기 드래그&드롭

    /// <summary>
    /// 즐겨찾기 드래그 시작점 기록
    /// </summary>
    private void FavoriteListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    /// <summary>
    /// 즐겨찾기 드래그 시작 감지
    /// </summary>
    private void FavoriteListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPosition = e.GetPosition(null);
        var diff = _dragStartPoint - currentPosition;

        // 최소 드래그 거리 확인
        if (System.Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 드래그 대상 폴더 찾기
        if (e.OriginalSource is FrameworkElement element)
        {
            var folder = FindParentDataContext<Folder>(element);
            if (folder != null)
            {
                _draggedFolder = folder;
                var data = new DataObject(typeof(Folder), folder);
                DragDrop.DoDragDrop(FavoriteListBox, data, DragDropEffects.Move);
            }
        }
    }

    /// <summary>
    /// 즐겨찾기 드래그 오버 (폴더 순서 변경 + 메일 이동)
    /// </summary>
    private void FavoriteListBox_DragOver(object sender, DragEventArgs e)
    {
        // 메일 드래그 처리
        if (e.Data.GetDataPresent("EmailDragData"))
        {
            var element = e.OriginalSource as DependencyObject;
            var listBoxItem = FindAncestor<ListBoxItem>(element);

            if (listBoxItem?.DataContext is Folder)
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
            return;
        }

        // 폴더 순서 변경 처리
        if (!e.Data.GetDataPresent(typeof(Folder)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>
    /// 즐겨찾기 드롭 처리 (폴더 순서 변경 + 메일 이동)
    /// </summary>
    private async void FavoriteListBox_Drop(object sender, DragEventArgs e)
    {
        // 메일 드롭 처리 - 메일을 폴더로 이동
        if (e.Data.GetDataPresent("EmailDragData"))
        {
            var element = e.OriginalSource as DependencyObject;
            var listBoxItem = FindAncestor<ListBoxItem>(element);

            if (listBoxItem?.DataContext is Folder targetFolder)
            {
                var emails = e.Data.GetData("EmailDragData") as List<Email>;
                if (emails != null && emails.Count > 0)
                {
                    // 실행취소를 위해 원래 폴더 정보 저장
                    _lastMovedEmails = new List<Email>(emails);
                    _lastMovedFromFolderIds = emails.ToDictionary(em => em.Id, em => em.ParentFolderId ?? string.Empty);

                    Log4.Info($"메일 드롭 (즐겨찾기): {emails.Count}건 → {targetFolder.DisplayName}");
                    await _viewModel.MoveEmailsToFolderAsync(emails, targetFolder);

                    // 실행취소 팝업 표시
                    ShowUndoMovePopup(emails.Count);
                }
            }
            e.Handled = true;
            return;
        }

        // 폴더 순서 변경 처리
        if (!e.Data.GetDataPresent(typeof(Folder)))
            return;

        var droppedFolder = e.Data.GetData(typeof(Folder)) as Folder;
        if (droppedFolder == null || _draggedFolder == null)
            return;

        // 드롭 위치의 폴더 찾기
        Folder? targetFolderForOrder = null;
        if (e.OriginalSource is FrameworkElement element2)
        {
            targetFolderForOrder = FindParentDataContext<Folder>(element2);
        }

        // 같은 폴더면 무시
        if (targetFolderForOrder == null || targetFolderForOrder.Id == droppedFolder.Id)
        {
            _draggedFolder = null;
            return;
        }

        // ViewModel에 순서 변경 요청
        _viewModel.MoveFavoriteOrder(droppedFolder, targetFolderForOrder);

        _draggedFolder = null;
    }

    #endregion

    #region 메뉴바 이벤트

    /// <summary>
    /// 메일 새로고침 메뉴 클릭
    /// </summary>
    private async void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 메일 새로고침 클릭");
        await _viewModel.RefreshMailsCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// 동기화 일시정지 메뉴 클릭 (기존 - 사용 안함)
    /// </summary>
    private void MenuSyncPause_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 동기화 일시정지 클릭");
        _viewModel.PauseSyncCommand.Execute(null);
    }

    /// <summary>
    /// 동기화 시작 메뉴 클릭 (기존 - 사용 안함)
    /// </summary>
    private void MenuSyncResume_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 동기화 시작 클릭");
        _viewModel.ResumeSyncCommand.Execute(null);
    }

    /// <summary>
    /// 메일 동기화 중지 메뉴 클릭
    /// </summary>
    private void MenuMailSyncPause_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 메일 동기화 중지 클릭");
        _viewModel.PauseSyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsMailSyncPaused = true;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// 메일 동기화 시작 메뉴 클릭
    /// </summary>
    private void MenuMailSyncResume_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 메일 동기화 시작 클릭");
        _viewModel.ResumeSyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsMailSyncPaused = false;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// AI 분석 일시정지 메뉴 클릭
    /// </summary>
    private void MenuAISyncPause_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: AI 분석 일시정지 클릭");
        _viewModel.PauseAISyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsAiAnalysisPaused = true;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// AI 분석 시작 메뉴 클릭
    /// </summary>
    private void MenuAISyncResume_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: AI 분석 시작 클릭");
        _viewModel.ResumeAISyncCommand.Execute(null);

        // 설정 저장
        App.Settings.UserPreferences.IsAiAnalysisPaused = false;
        App.Settings.SaveUserPreferences();
    }

    #endregion

    #region 접속 메뉴 이벤트

    private readonly Services.Storage.LoginSettingsService _loginSettingsService = new();

    /// <summary>
    /// 자동 로그인 메뉴 클릭
    /// </summary>
    private async void MenuAutoLogin_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 자동 로그인 클릭");

        var graphAuthService = ((App)Application.Current).GetService<Services.Graph.GraphAuthService>();
        if (graphAuthService == null)
        {
            Log4.Error("GraphAuthService를 찾을 수 없습니다.");
            return;
        }

        // 현재 자동 로그인 상태 확인
        var loginSettings = _loginSettingsService.Load();
        var isAutoLoginEnabled = loginSettings?.AutoLogin ?? false;

        if (isAutoLoginEnabled)
        {
            // 자동 로그인 해제
            var result = System.Windows.MessageBox.Show(
                "자동 로그인을 해제하시겠습니까?\n\n다음 실행 시 로그인 창이 표시됩니다.",
                "자동 로그인 해제",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                loginSettings!.AutoLogin = false;
                _loginSettingsService.Save(loginSettings);

                // 토큰 캐시 삭제
                Services.Graph.TokenCacheHelper.ClearCache();

                Log4.Info("자동 로그인 해제됨");
                _viewModel.StatusMessage = "자동 로그인이 해제되었습니다.";
                UpdateAutoLoginMenuState(false);
            }
        }
        else
        {
            // 자동 로그인 설정 - 로그인 창 표시
            var result = System.Windows.MessageBox.Show(
                "자동 로그인을 설정하시겠습니까?\n\n로그인 창이 표시되며, 로그인 성공 시 자동 로그인이 활성화됩니다.",
                "자동 로그인 설정",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    _viewModel.StatusMessage = "로그인 중...";

                    // 기존 토큰 캐시 삭제 후 새로 로그인
                    Services.Graph.TokenCacheHelper.ClearCache();

                    var loginSuccess = await graphAuthService.LoginInteractiveAsync();
                    if (loginSuccess)
                    {
                        // 로그인 성공 - 자동 로그인 설정 저장
                        var newSettings = loginSettings ?? new Models.LoginSettings();
                        newSettings.Email = graphAuthService.CurrentUserEmail;
                        newSettings.DisplayName = graphAuthService.CurrentUserDisplayName;
                        newSettings.AutoLogin = true;
                        newSettings.LastLoginAt = DateTime.Now;

                        // Azure AD 설정도 저장
                        if (!string.IsNullOrEmpty(graphAuthService.ClientId))
                        {
                            newSettings.AzureAd = new Models.Settings.AzureAdSettings
                            {
                                ClientId = graphAuthService.ClientId,
                                TenantId = "common"
                            };
                        }

                        _loginSettingsService.Save(newSettings);

                        Log4.Info($"자동 로그인 설정 완료: {newSettings.Email}");
                        _viewModel.StatusMessage = $"자동 로그인이 설정되었습니다. ({newSettings.Email})";
                        UpdateAutoLoginMenuState(true);
                    }
                    else
                    {
                        Log4.Warn("로그인 취소됨");
                        _viewModel.StatusMessage = "로그인이 취소되었습니다.";
                    }
                }
                catch (Exception ex)
                {
                    Log4.Error($"자동 로그인 설정 실패: {ex.Message}");
                    _viewModel.StatusMessage = "로그인 중 오류가 발생했습니다.";
                }
            }
        }
    }

    /// <summary>
    /// 로그아웃 메뉴 클릭
    /// </summary>
    private async void MenuLogout_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 로그아웃 클릭");

        var result = System.Windows.MessageBox.Show(
            "로그아웃 하시겠습니까?\n\n프로그램이 종료되고 다시 로그인 창이 표시됩니다.",
            "로그아웃",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            try
            {
                var graphAuthService = ((App)Application.Current).GetService<Services.Graph.GraphAuthService>();
                if (graphAuthService != null)
                {
                    await graphAuthService.LogoutAsync();
                }

                // 자동 로그인 해제
                var loginSettings = _loginSettingsService.Load();
                if (loginSettings != null)
                {
                    loginSettings.AutoLogin = false;
                    _loginSettingsService.Save(loginSettings);
                }

                Log4.Info("로그아웃 완료 - 앱 재시작");

                // 앱 재시작 (로그인 창 표시)
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    System.Diagnostics.Process.Start(exePath);
                }

                // 현재 앱 종료
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                Log4.Error($"로그아웃 실패: {ex.Message}");
                _viewModel.StatusMessage = "로그아웃 중 오류가 발생했습니다.";
            }
        }
    }

    /// <summary>
    /// 자동 로그인 메뉴 체크 상태 업데이트
    /// </summary>
    private void UpdateAutoLoginMenuState(bool isEnabled)
    {
        if (AutoLoginCheckIcon != null)
        {
            AutoLoginCheckIcon.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 자동 로그인 메뉴 초기화 (Loaded 이벤트에서 호출)
    /// </summary>
    private void InitializeAutoLoginMenu()
    {
        var loginSettings = _loginSettingsService.Load();
        var isAutoLoginEnabled = loginSettings?.AutoLogin ?? false;
        UpdateAutoLoginMenuState(isAutoLoginEnabled);
    }

    /// <summary>
    /// 저장된 동기화 설정 로드 및 적용
    /// </summary>
    private void LoadSavedSyncSettings()
    {
        var prefs = App.Settings.UserPreferences;

        // 메일 동기화 기간 설정 로드
        if (Enum.TryParse<SyncPeriodType>(prefs.MailSyncPeriodType, out var mailPeriodType))
        {
            var mailSettings = new SyncPeriodSettings { PeriodType = mailPeriodType, Value = prefs.MailSyncPeriodValue };
            _viewModel.MailSyncPeriodSettings = mailSettings;
            Log4.Debug($"메일 동기화 기간 로드: {mailSettings.ToDisplayString()}");
        }

        // AI 분석 기간 설정 로드
        if (Enum.TryParse<SyncPeriodType>(prefs.AiAnalysisPeriodType, out var aiPeriodType))
        {
            var aiSettings = new SyncPeriodSettings { PeriodType = aiPeriodType, Value = prefs.AiAnalysisPeriodValue };
            _viewModel.AiAnalysisPeriodSettings = aiSettings;
            Log4.Debug($"AI 분석 기간 로드: {aiSettings.ToDisplayString()}");
        }

        // 동기화 주기 로드 (초 단위 우선, 없으면 분 단위 사용) - 하위 호환용
        if (prefs.MailSyncIntervalSeconds > 0)
        {
            _viewModel.SetSyncInterval(prefs.MailSyncIntervalSeconds);
            Log4.Debug($"동기화 주기 로드: {prefs.MailSyncIntervalSeconds}초");
        }
        else if (prefs.MailSyncIntervalMinutes > 0)
        {
            _viewModel.SetSyncInterval(prefs.MailSyncIntervalMinutes * 60);
            Log4.Debug($"동기화 주기 로드: {prefs.MailSyncIntervalMinutes}분 (하위 호환)");
        }

        // 즐겨찾기 동기화 주기 로드
        if (prefs.FavoriteSyncIntervalSeconds > 0)
        {
            _viewModel.SetFavoriteSyncInterval(prefs.FavoriteSyncIntervalSeconds);
            UpdateFavoriteSyncIntervalCurrentDisplay(prefs.FavoriteSyncIntervalSeconds);
            Log4.Debug($"즐겨찾기 동기화 주기 로드: {prefs.FavoriteSyncIntervalSeconds}초");
        }

        // 전체 동기화 주기 로드
        if (prefs.FullSyncIntervalSeconds > 0)
        {
            _viewModel.SetFullSyncInterval(prefs.FullSyncIntervalSeconds);
            UpdateFullSyncIntervalCurrentDisplay(prefs.FullSyncIntervalSeconds);
            Log4.Debug($"전체 동기화 주기 로드: {prefs.FullSyncIntervalSeconds}초");
        }

        // 캘린더 동기화 주기 로드 (전체 동기화 주기로 통합됨)
        if (prefs.CalendarSyncIntervalSeconds > 0)
        {
            _viewModel.SetCalendarSyncInterval(prefs.CalendarSyncIntervalSeconds);
            Log4.Debug($"캘린더 동기화 주기 로드: {prefs.CalendarSyncIntervalSeconds}초");
        }

        // 채팅 동기화 주기 로드 (전체 동기화 주기로 통합됨)
        if (prefs.ChatSyncIntervalSeconds > 0)
        {
            _viewModel.SetChatSyncInterval(prefs.ChatSyncIntervalSeconds);
            Log4.Debug($"채팅 동기화 주기 로드: {prefs.ChatSyncIntervalSeconds}초");
        }

        // 메일 동기화 일시정지 상태 로드
        if (prefs.IsMailSyncPaused)
        {
            _viewModel.PauseSyncCommand.Execute(null);
            Log4.Debug("메일 동기화 일시정지 상태 로드");
        }

        // AI 분석 주기 로드 (초 단위) - 하위 호환용
        if (prefs.AiAnalysisIntervalSeconds > 0)
        {
            _viewModel.SetAIAnalysisInterval(prefs.AiAnalysisIntervalSeconds);
            Log4.Debug($"AI 분석 주기 로드: {prefs.AiAnalysisIntervalSeconds}초");
        }

        // 즐겨찾기 AI 분석 주기 로드
        if (prefs.FavoriteAnalysisIntervalSeconds > 0)
        {
            _viewModel.SetFavoriteAnalysisInterval(prefs.FavoriteAnalysisIntervalSeconds);
            UpdateFavoriteAnalysisIntervalCurrentDisplay(prefs.FavoriteAnalysisIntervalSeconds);
            Log4.Debug($"즐겨찾기 AI 분석 주기 로드: {prefs.FavoriteAnalysisIntervalSeconds}초");
        }

        // 전체 AI 분석 주기 로드
        if (prefs.FullAnalysisIntervalSeconds > 0)
        {
            _viewModel.SetFullAnalysisInterval(prefs.FullAnalysisIntervalSeconds);
            UpdateFullAnalysisIntervalCurrentDisplay(prefs.FullAnalysisIntervalSeconds);
            Log4.Debug($"전체 AI 분석 주기 로드: {prefs.FullAnalysisIntervalSeconds}초");
        }

        // AI 분석 일시정지 상태 로드
        if (prefs.IsAiAnalysisPaused)
        {
            _viewModel.PauseAISyncCommand.Execute(null);
            Log4.Debug("AI 분석 일시정지 상태 로드");
        }
    }

    #endregion

    #region 테마 메뉴 이벤트

    /// <summary>
    /// 다크 모드 메뉴 클릭
    /// </summary>
    private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 다크 모드 클릭");
        Services.Theme.ThemeService.Instance.SetDarkMode();
        SyncSettingsUIFromMenu(); // 설정 UI 동기화
    }

    /// <summary>
    /// 라이트 모드 메뉴 클릭
    /// </summary>
    private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 라이트 모드 클릭");
        Services.Theme.ThemeService.Instance.SetLightMode();
        SyncSettingsUIFromMenu(); // 설정 UI 동기화
    }

    /// <summary>
    /// GPU 모드 메뉴 클릭 (토글)
    /// </summary>
    private void MenuGpuMode_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: GPU 모드 토글");
        Services.Theme.RenderModeService.Instance.ToggleGpuMode();
        UpdateGpuModeCheckmark();
        SyncSettingsUIFromMenu(); // 설정 UI 동기화

        // 사용자에게 재시작 안내
        var currentMode = Services.Theme.RenderModeService.Instance.GetCurrentModeString();
        _viewModel.StatusMessage = $"렌더링 모드가 {currentMode}로 변경되었습니다. 완전 적용을 위해 앱을 재시작하세요.";
    }

    /// <summary>
    /// GPU 모드 체크마크 업데이트
    /// </summary>
    private void UpdateGpuModeCheckmark()
    {
        var isGpuMode = Services.Theme.RenderModeService.Instance.IsGpuMode;
        // 체크마크 표시/숨김
        if (GpuModeCheckMark != null)
        {
            GpuModeCheckMark.Visibility = isGpuMode ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// API 관리 메뉴 클릭
    /// </summary>
    private void MenuApiSettings_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: API 관리 클릭");
        var apiSettingsWindow = new ApiSettingsWindow(App.Settings);
        apiSettingsWindow.Owner = this;
        apiSettingsWindow.ShowDialog();
    }

    /// <summary>
    /// 서명 관리 메뉴 클릭
    /// </summary>
    private void MenuSignatureSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log4.Info("메뉴: 서명 관리 클릭");
            Log4.Debug("SignatureSettingsDialog 생성 시작");
            var dialog = new SignatureSettingsDialog();
            Log4.Debug("SignatureSettingsDialog Owner 설정");
            dialog.Owner = this;
            Log4.Debug("SignatureSettingsDialog LoadSettings 호출");
            dialog.LoadSettings(_viewModel.SignatureSettings);
            Log4.Debug("SignatureSettingsDialog ShowDialog 호출");

            if (dialog.ShowDialog() == true && dialog.IsSaved && dialog.ResultSettings != null)
            {
                _viewModel.SignatureSettings = dialog.ResultSettings;
                Log4.Info($"서명 설정 저장 완료: {dialog.ResultSettings.Signatures.Count}개");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"서명 관리 다이얼로그 오류: {ex.Message}\n{ex.StackTrace}");
            System.Windows.MessageBox.Show($"서명 관리 다이얼로그를 열 수 없습니다.\n{ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    #endregion

    #region 동기화 메뉴 이벤트

    // 메일 동기화 기간 설정
    private void MenuMailSync5_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Count, 5);
    private void MenuMailSyncDay_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Days, 1);
    private void MenuMailSyncWeek_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Weeks, 1);
    private void MenuMailSyncMonth_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Months, 1);
    private void MenuMailSyncYear_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.Years, 1);
    private void MenuMailSyncAll_Click(object sender, RoutedEventArgs e) => SetMailSyncPeriod(SyncPeriodType.All, 0);

    private void SetMailSyncPeriod(SyncPeriodType periodType, int value)
    {
        var settings = new SyncPeriodSettings { PeriodType = periodType, Value = value };
        _viewModel.MailSyncPeriodSettings = settings;
        Log4.Info($"메일 동기화 기간 설정: {settings.ToDisplayString()}");
        _viewModel.StatusMessage = $"메일 동기화 기간: {settings.ToDisplayString()}";
        UpdateSyncPeriodCurrentDisplay(settings);

        // 설정 저장
        App.Settings.UserPreferences.MailSyncPeriodType = periodType.ToString();
        App.Settings.UserPreferences.MailSyncPeriodValue = value;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// 동기화 기간 현재 설정 표시 업데이트
    /// </summary>
    private void UpdateSyncPeriodCurrentDisplay(SyncPeriodSettings? settings = null)
    {
        settings ??= _viewModel.MailSyncPeriodSettings ?? SyncPeriodSettings.Default;
        if (MenuSyncPeriodCurrent != null)
        {
            MenuSyncPeriodCurrent.Header = $"현재: {settings.ToDisplayString()}";
        }

        // 동기화 기간 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new[] { MenuMailSync5, MenuMailSyncDay, MenuMailSyncWeek, MenuMailSyncMonth, MenuMailSyncYear, MenuMailSyncAll };
        var periodTypes = new[] { (SyncPeriodType.Count, 5), (SyncPeriodType.Days, 1), (SyncPeriodType.Weeks, 1), (SyncPeriodType.Months, 1), (SyncPeriodType.Years, 1), (SyncPeriodType.All, 0) };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = settings.PeriodType == periodTypes[i].Item1 && settings.Value == periodTypes[i].Item2;
                // 선택된 항목은 하이라이트, 그 외는 시스템 기본값 사용 (null로 리셋)
                menuItems[i].ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i].Foreground = highlightColor;
            }
        }
    }

    // AI 분석 기간 설정
    private void MenuAIAnalysis5_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Count, 5);
    private void MenuAIAnalysisDay_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Days, 1);
    private void MenuAIAnalysisWeek_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Weeks, 1);
    private void MenuAIAnalysisMonth_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Months, 1);
    private void MenuAIAnalysisYear_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.Years, 1);
    private void MenuAIAnalysisAll_Click(object sender, RoutedEventArgs e) => SetAiAnalysisPeriod(SyncPeriodType.All, 0);

    private void SetAiAnalysisPeriod(SyncPeriodType periodType, int value)
    {
        var settings = new SyncPeriodSettings { PeriodType = periodType, Value = value };
        _viewModel.AiAnalysisPeriodSettings = settings;
        Log4.Info($"AI 분석 기간 설정: {settings.ToDisplayString()}");
        _viewModel.StatusMessage = $"AI 분석 기간: {settings.ToDisplayString()}";
        UpdateAIAnalysisPeriodCurrentDisplay(settings);

        // 설정 저장
        App.Settings.UserPreferences.AiAnalysisPeriodType = periodType.ToString();
        App.Settings.UserPreferences.AiAnalysisPeriodValue = value;
        App.Settings.SaveUserPreferences();
    }

    /// <summary>
    /// AI 분석 기간 현재 설정 표시 업데이트
    /// </summary>
    private void UpdateAIAnalysisPeriodCurrentDisplay(SyncPeriodSettings? settings = null)
    {
        settings ??= _viewModel.AiAnalysisPeriodSettings ?? SyncPeriodSettings.Default;
        if (MenuAIAnalysisPeriodCurrent != null)
        {
            MenuAIAnalysisPeriodCurrent.Header = $"현재: {settings.ToDisplayString()}";
        }

        // AI 분석 기간 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new[] { MenuAIAnalysis5, MenuAIAnalysisDay, MenuAIAnalysisWeek, MenuAIAnalysisMonth, MenuAIAnalysisYear, MenuAIAnalysisAll };
        var periodTypes = new[] { (SyncPeriodType.Count, 5), (SyncPeriodType.Days, 1), (SyncPeriodType.Weeks, 1), (SyncPeriodType.Months, 1), (SyncPeriodType.Years, 1), (SyncPeriodType.All, 0) };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = settings.PeriodType == periodTypes[i].Item1 && settings.Value == periodTypes[i].Item2;
                // 선택된 항목은 하이라이트, 그 외는 시스템 기본값 사용 (null로 리셋)
                menuItems[i].ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i].Foreground = highlightColor;
            }
        }
    }

    // 즐겨찾기 메일 동기화 주기 설정
    private void MenuFavoriteSyncInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
        {
            SetFavoriteSyncInterval(seconds);
        }
    }

    private void SetFavoriteSyncInterval(int seconds)
    {
        _viewModel.SetFavoriteSyncInterval(seconds);
        var displayText = GetIntervalDisplayText(seconds);
        Log4.Info($"즐겨찾기 동기화 주기 설정: {displayText}");
        _viewModel.StatusMessage = $"즐겨찾기 동기화 주기: {displayText}";
        UpdateFavoriteSyncIntervalCurrentDisplay(seconds);

        // 설정 저장
        App.Settings.UserPreferences.FavoriteSyncIntervalSeconds = seconds;
        App.Settings.SaveUserPreferences();
    }

    private void UpdateFavoriteSyncIntervalCurrentDisplay(int? seconds = null)
    {
        seconds ??= _viewModel.FavoriteSyncIntervalSeconds;
        if (MenuFavoriteSyncIntervalCurrent != null)
        {
            MenuFavoriteSyncIntervalCurrent.Header = $"현재: {GetIntervalDisplayText(seconds.Value)}";
        }

        // 즐겨찾기 동기화 주기 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new System.Windows.Controls.MenuItem?[] { MenuFavoriteSyncInterval1s, MenuFavoriteSyncInterval5s, MenuFavoriteSyncInterval10s, MenuFavoriteSyncInterval30s, MenuFavoriteSyncInterval1m, MenuFavoriteSyncInterval5m };
        var intervalSeconds = new[] { 1, 5, 10, 30, 60, 300 };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = seconds == intervalSeconds[i];
                menuItems[i]!.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i]!.Foreground = highlightColor;
            }
        }
    }

    // 전체메일 동기화 주기 설정
    private void MenuFullSyncInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
        {
            SetFullSyncInterval(seconds);
        }
    }

    private void SetFullSyncInterval(int seconds)
    {
        _viewModel.SetFullSyncInterval(seconds);
        var displayText = GetIntervalDisplayText(seconds);
        Log4.Info($"전체메일 동기화 주기 설정: {displayText}");
        _viewModel.StatusMessage = $"전체메일 동기화 주기: {displayText}";
        UpdateFullSyncIntervalCurrentDisplay(seconds);

        // 설정 저장
        App.Settings.UserPreferences.FullSyncIntervalSeconds = seconds;
        App.Settings.SaveUserPreferences();
    }

    private void UpdateFullSyncIntervalCurrentDisplay(int? seconds = null)
    {
        seconds ??= _viewModel.FullSyncIntervalSeconds;
        if (MenuFullSyncIntervalCurrent != null)
        {
            MenuFullSyncIntervalCurrent.Header = $"현재: {GetIntervalDisplayText(seconds.Value)}";
        }

        // 전체메일 동기화 주기 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new System.Windows.Controls.MenuItem?[] { MenuFullSyncInterval1s, MenuFullSyncInterval5s, MenuFullSyncInterval10s, MenuFullSyncInterval30s, MenuFullSyncInterval1m, MenuFullSyncInterval5m, MenuFullSyncInterval10m, MenuFullSyncInterval30m, MenuFullSyncInterval1h };
        var intervalSeconds = new[] { 1, 5, 10, 30, 60, 300, 600, 1800, 3600 };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = seconds == intervalSeconds[i];
                menuItems[i]!.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i]!.Foreground = highlightColor;
            }
        }
    }

    // 즐겨찾기 AI 분석 주기 설정
    private void MenuFavoriteAnalysisInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
        {
            SetFavoriteAnalysisInterval(seconds);
        }
    }

    private void SetFavoriteAnalysisInterval(int seconds)
    {
        _viewModel.SetFavoriteAnalysisInterval(seconds);
        var displayText = GetIntervalDisplayText(seconds);
        Log4.Info($"즐겨찾기 AI 분석 주기 설정: {displayText}");
        _viewModel.StatusMessage = $"즐겨찾기 AI 분석 주기: {displayText}";
        UpdateFavoriteAnalysisIntervalCurrentDisplay(seconds);

        // 설정 저장
        App.Settings.UserPreferences.FavoriteAnalysisIntervalSeconds = seconds;
        App.Settings.SaveUserPreferences();
    }

    private void UpdateFavoriteAnalysisIntervalCurrentDisplay(int? seconds = null)
    {
        seconds ??= _viewModel.FavoriteAnalysisIntervalSeconds;
        if (MenuFavoriteAnalysisIntervalCurrent != null)
        {
            MenuFavoriteAnalysisIntervalCurrent.Header = $"현재: {GetIntervalDisplayText(seconds.Value)}";
        }

        // 즐겨찾기 AI 분석 주기 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new System.Windows.Controls.MenuItem?[] { MenuFavoriteAnalysisInterval1s, MenuFavoriteAnalysisInterval5s, MenuFavoriteAnalysisInterval10s, MenuFavoriteAnalysisInterval30s, MenuFavoriteAnalysisInterval1m, MenuFavoriteAnalysisInterval5m };
        var intervalSeconds = new[] { 1, 5, 10, 30, 60, 300 };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = seconds == intervalSeconds[i];
                menuItems[i]!.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i]!.Foreground = highlightColor;
            }
        }
    }

    // 전체메일 AI 분석 주기 설정
    private void MenuFullAnalysisInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string tagStr && int.TryParse(tagStr, out int seconds))
        {
            SetFullAnalysisInterval(seconds);
        }
    }

    private void SetFullAnalysisInterval(int seconds)
    {
        _viewModel.SetFullAnalysisInterval(seconds);
        var displayText = GetIntervalDisplayText(seconds);
        Log4.Info($"전체메일 AI 분석 주기 설정: {displayText}");
        _viewModel.StatusMessage = $"전체메일 AI 분석 주기: {displayText}";
        UpdateFullAnalysisIntervalCurrentDisplay(seconds);

        // 설정 저장
        App.Settings.UserPreferences.FullAnalysisIntervalSeconds = seconds;
        App.Settings.SaveUserPreferences();
    }

    private void UpdateFullAnalysisIntervalCurrentDisplay(int? seconds = null)
    {
        seconds ??= _viewModel.FullAnalysisIntervalSeconds;
        if (MenuFullAnalysisIntervalCurrent != null)
        {
            MenuFullAnalysisIntervalCurrent.Header = $"현재: {GetIntervalDisplayText(seconds.Value)}";
        }

        // 전체메일 AI 분석 주기 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));

        var menuItems = new System.Windows.Controls.MenuItem?[] { MenuFullAnalysisInterval1s, MenuFullAnalysisInterval5s, MenuFullAnalysisInterval10s, MenuFullAnalysisInterval30s, MenuFullAnalysisInterval1m, MenuFullAnalysisInterval5m, MenuFullAnalysisInterval10m, MenuFullAnalysisInterval30m, MenuFullAnalysisInterval1h };
        var intervalSeconds = new[] { 1, 5, 10, 30, 60, 300, 600, 1800, 3600 };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = seconds == intervalSeconds[i];
                menuItems[i]!.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i]!.Foreground = highlightColor;
            }
        }
    }

    private static string GetIntervalDisplayText(int seconds)
    {
        return seconds switch
        {
            < 60 => $"{seconds}초",
            60 => "1분",
            < 3600 => $"{seconds / 60}분",
            3600 => "1시간",
            _ => $"{seconds / 3600}시간"
        };
    }

    // 즐겨찾기 동기화 기간 설정 (신규)
    private void MenuFavoriteSyncPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string tagStr)
        {
            var parts = tagStr.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int value))
            {
                SetFavoriteSyncPeriod(parts[0], value);
            }
        }
    }

    private void SetFavoriteSyncPeriod(string periodType, int value)
    {
        App.Settings.UserPreferences.FavoriteSyncPeriodType = periodType;
        App.Settings.UserPreferences.FavoriteSyncPeriodValue = value;
        App.Settings.SaveUserPreferences();

        var displayText = GetPeriodDisplayText(periodType, value);
        Log4.Info($"즐겨찾기 동기화 기간 설정: {displayText}");
        _viewModel.StatusMessage = $"즐겨찾기 동기화 기간: {displayText}";
        UpdateFavoriteSyncPeriodCurrentDisplay(periodType, value);
    }

    private void UpdateFavoriteSyncPeriodCurrentDisplay(string? periodType = null, int? value = null)
    {
        periodType ??= App.Settings.UserPreferences.FavoriteSyncPeriodType;
        value ??= App.Settings.UserPreferences.FavoriteSyncPeriodValue;

        if (MenuFavoriteSyncPeriodCurrent != null)
        {
            MenuFavoriteSyncPeriodCurrent.Header = $"현재: {GetPeriodDisplayText(periodType, value.Value)}";
        }

        // 즐겨찾기 동기화 기간 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));

        var menuItems = new System.Windows.Controls.MenuItem?[] { MenuFavoriteSyncPeriod5, MenuFavoriteSyncPeriodDay, MenuFavoriteSyncPeriodWeek, MenuFavoriteSyncPeriodMonth, MenuFavoriteSyncPeriodYear, MenuFavoriteSyncPeriodAll };
        var periodTypes = new[] { ("Count", 5), ("Days", 1), ("Weeks", 1), ("Months", 1), ("Years", 1), ("All", 0) };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = periodType == periodTypes[i].Item1 && value == periodTypes[i].Item2;
                menuItems[i]!.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i]!.Foreground = highlightColor;
            }
        }
    }

    // 즐겨찾기 AI 분석 기간 설정 (신규)
    private void MenuFavoriteAiPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Tag is string tagStr)
        {
            var parts = tagStr.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out int value))
            {
                SetFavoriteAiPeriod(parts[0], value);
            }
        }
    }

    private void SetFavoriteAiPeriod(string periodType, int value)
    {
        App.Settings.UserPreferences.FavoriteAiPeriodType = periodType;
        App.Settings.UserPreferences.FavoriteAiPeriodValue = value;
        App.Settings.SaveUserPreferences();

        var displayText = GetPeriodDisplayText(periodType, value);
        Log4.Info($"즐겨찾기 AI 분석 기간 설정: {displayText}");
        _viewModel.StatusMessage = $"즐겨찾기 AI 분석 기간: {displayText}";
        UpdateFavoriteAiPeriodCurrentDisplay(periodType, value);
    }

    private void UpdateFavoriteAiPeriodCurrentDisplay(string? periodType = null, int? value = null)
    {
        periodType ??= App.Settings.UserPreferences.FavoriteAiPeriodType;
        value ??= App.Settings.UserPreferences.FavoriteAiPeriodValue;

        if (MenuFavoriteAiPeriodCurrent != null)
        {
            MenuFavoriteAiPeriodCurrent.Header = $"현재: {GetPeriodDisplayText(periodType, value.Value)}";
        }

        // 즐겨찾기 AI 분석 기간 메뉴 하이라이팅
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFD700"));

        var menuItems = new System.Windows.Controls.MenuItem?[] { MenuFavoriteAiPeriod5, MenuFavoriteAiPeriodDay, MenuFavoriteAiPeriodWeek, MenuFavoriteAiPeriodMonth, MenuFavoriteAiPeriodYear, MenuFavoriteAiPeriodAll };
        var periodTypes = new[] { ("Count", 5), ("Days", 1), ("Weeks", 1), ("Months", 1), ("Years", 1), ("All", 0) };

        for (int i = 0; i < menuItems.Length; i++)
        {
            if (menuItems[i] != null)
            {
                bool isSelected = periodType == periodTypes[i].Item1 && value == periodTypes[i].Item2;
                menuItems[i]!.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
                if (isSelected)
                    menuItems[i]!.Foreground = highlightColor;
            }
        }
    }

    private static string GetPeriodDisplayText(string periodType, int value)
    {
        return periodType switch
        {
            "Count" => $"최근 {value}건",
            "Days" => value == 1 ? "하루" : $"{value}일",
            "Weeks" => value == 1 ? "1주일" : $"{value}주",
            "Months" => value == 1 ? "1달" : $"{value}개월",
            "Years" => value == 1 ? "1년" : $"{value}년",
            "All" => "전체",
            _ => "알 수 없음"
        };
    }

    /// <summary>
    /// 동기화 상세 설정 다이얼로그 열기
    /// </summary>
    private void MenuSyncSettings_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("동기화 설정 다이얼로그 열기");

        var dialog = new SyncSettingsDialog
        {
            Owner = this
        };

        // 현재 설정 로드
        dialog.LoadSettings(
            _viewModel.MailSyncPeriodSettings ?? SyncPeriodSettings.Default,
            _viewModel.AiAnalysisPeriodSettings ?? SyncPeriodSettings.Default
        );

        // 다이얼로그 표시
        if (dialog.ShowDialog() == true && dialog.IsSaved)
        {
            // 메일 동기화 기간 적용 및 저장
            if (dialog.MailSyncSettings != null)
            {
                _viewModel.MailSyncPeriodSettings = dialog.MailSyncSettings;
                App.Settings.UserPreferences.MailSyncPeriodType = dialog.MailSyncSettings.PeriodType.ToString();
                App.Settings.UserPreferences.MailSyncPeriodValue = dialog.MailSyncSettings.Value;
            }

            // AI 분석 기간 적용 및 저장
            if (dialog.AiAnalysisSettings != null)
            {
                _viewModel.AiAnalysisPeriodSettings = dialog.AiAnalysisSettings;
                App.Settings.UserPreferences.AiAnalysisPeriodType = dialog.AiAnalysisSettings.PeriodType.ToString();
                App.Settings.UserPreferences.AiAnalysisPeriodValue = dialog.AiAnalysisSettings.Value;
            }

            // 설정 파일에 저장
            App.Settings.SaveUserPreferences();

            _viewModel.StatusMessage = "동기화 설정이 저장되었습니다.";
        }
    }

    /// <summary>
    /// 전체 재동기화 메뉴 클릭
    /// </summary>
    private async void MenuForceResync_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 전체 재동기화 클릭 (모든 서비스)");

        try
        {
            // 1. 메일 동기화
            _viewModel.StatusMessage = "메일 동기화 중...";
            await _viewModel.ForceResyncAllAsync();

            // 2. 캘린더 동기화
            _viewModel.StatusMessage = "캘린더 동기화 중...";
            await _syncService.SyncCalendarAsync();

            // 3. 채팅 동기화
            _viewModel.StatusMessage = "채팅 동기화 중...";
            await _syncService.SyncChatsAsync();
            if (_teamsViewModel != null)
            {
                await _teamsViewModel.LoadChatsAsync();
            }

            // 4. 원노트 동기화
            _viewModel.StatusMessage = "원노트 동기화 중...";
            if (_oneNoteViewModel != null)
            {
                await _oneNoteViewModel.LoadNotebooksAsync();
                await _oneNoteViewModel.LoadRecentPagesAsync();
            }

            // 5. 플래너 동기화
            _viewModel.StatusMessage = "플래너 동기화 중...";
            if (_plannerViewModel != null)
            {
                await _plannerViewModel.LoadPlansAsync();
                await _plannerViewModel.LoadMyTasksAsync();
            }

            _viewModel.StatusMessage = "전체 재동기화 완료";
            Log4.Info("전체 재동기화 완료 (모든 서비스)");
        }
        catch (Exception ex)
        {
            Log4.Error($"전체 재동기화 실패: {ex.Message}");
            _viewModel.StatusMessage = $"재동기화 실패: {ex.Message}";
        }
    }

    #endregion

    #region 폴더 컨텍스트 메뉴 이벤트

    /// <summary>
    /// 폴더 재동기화
    /// </summary>
    private void FolderResync_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null)
        {
            Log4.Info($"폴더 재동기화: {_rightClickedFolder.DisplayName}");
            // TODO: 해당 폴더의 메일 강제 재동기화
            _viewModel.StatusMessage = $"'{_rightClickedFolder.DisplayName}' 폴더 재동기화 중...";
        }
    }

    /// <summary>
    /// 폴더 AI 재분석
    /// </summary>
    private void FolderAIReanalyze_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolder != null)
        {
            Log4.Info($"폴더 AI 재분석: {_rightClickedFolder.DisplayName}");
            // TODO: 해당 폴더의 메일 AI 강제 재분석
            _viewModel.StatusMessage = $"'{_rightClickedFolder.DisplayName}' 폴더 AI 재분석 중...";
        }
    }

    #endregion

    #region 메일 컨텍스트 메뉴 이벤트

    /// <summary>
    /// 메일 컨텍스트 메뉴 열릴 때 - 선택된 메일 저장
    /// </summary>
    private void EmailContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        // 선택된 메일을 우클릭 메일로 저장
        _rightClickedEmail = EmailListBox.SelectedItem as Email ?? _viewModel.SelectedEmail;
        Log4.Info($"컨텍스트 메뉴 열림 - 선택된 메일: {_rightClickedEmail?.Subject ?? "null"} (EntryId: {_rightClickedEmail?.EntryId ?? "null"})");
    }

    /// <summary>
    /// 메일 리스트 우클릭 시 선택 처리 및 우클릭 메일 저장
    /// </summary>
    private void EmailListBox_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Log4.Debug($"EmailListBox 우클릭 - OriginalSource: {e.OriginalSource?.GetType().Name}");

        // 우클릭한 항목이 선택되어 있지 않으면 해당 항목만 선택
        if (e.OriginalSource is FrameworkElement element)
        {
            var email = FindParentDataContext<Email>(element);
            _rightClickedEmail = email;  // 우클릭한 메일 저장

            Log4.Debug($"우클릭 메일 저장: {email?.Subject ?? "null"} (EntryId: {email?.EntryId ?? "null"})");

            if (email != null && !EmailListBox.SelectedItems.Contains(email))
            {
                EmailListBox.SelectedItems.Clear();
                EmailListBox.SelectedItems.Add(email);
            }
        }
        else
        {
            Log4.Debug("우클릭 - OriginalSource가 FrameworkElement가 아님");
        }
    }

    /// <summary>
    /// 선택된 메일 AI 재분석
    /// </summary>
    private void EmailAIReanalyze_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"메일 AI 재분석: {selectedEmails.Count}건");
            // TODO: 선택된 메일들 AI 강제 재분석
            _viewModel.StatusMessage = $"{selectedEmails.Count}건 메일 AI 재분석 중...";
        }
    }

    /// <summary>
    /// 플래그 설정
    /// </summary>
    private async void EmailSetFlag_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"플래그 설정: {selectedEmails.Count}건");
            await _viewModel.UpdateFlagStatusAsync(selectedEmails, "flagged");
        }
    }

    /// <summary>
    /// 플래그 완료
    /// </summary>
    private async void EmailCompleteFlag_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"플래그 완료: {selectedEmails.Count}건");
            await _viewModel.UpdateFlagStatusAsync(selectedEmails, "complete");
        }
    }

    /// <summary>
    /// 플래그 해제
    /// </summary>
    private async void EmailClearFlag_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"플래그 해제: {selectedEmails.Count}건");
            await _viewModel.UpdateFlagStatusAsync(selectedEmails, "notFlagged");
        }
    }

    /// <summary>
    /// 핀 고정/해제 토글 (컨텍스트 메뉴)
    /// </summary>
    private void EmailTogglePin_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            foreach (var email in selectedEmails)
            {
                email.IsPinned = !email.IsPinned;
            }
            _viewModel.TogglePinnedCommand.Execute(null);
            Log4.Info($"핀 고정 토글: {selectedEmails.Count}건");
        }
    }

    /// <summary>
    /// 읽음으로 표시
    /// </summary>
    private async void EmailMarkAsRead_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"읽음 표시: {selectedEmails.Count}건");
            await _viewModel.UpdateReadStatusAsync(selectedEmails, true);
        }
    }

    /// <summary>
    /// 읽지 않음으로 표시
    /// </summary>
    private async void EmailMarkAsUnread_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count > 0)
        {
            Log4.Info($"읽지 않음 표시: {selectedEmails.Count}건");
            await _viewModel.UpdateReadStatusAsync(selectedEmails, false);
        }
    }

    /// <summary>
    /// 선택된 메일 삭제 (공통 메서드)
    /// </summary>
    private async Task DeleteSelectedEmailAsync()
    {
        Log4.Info("DeleteSelectedEmailAsync 호출됨");

        // 우클릭한 메일이 있으면 우선 사용, 없으면 선택된 메일 사용
        var targetEmail = _rightClickedEmail ?? _viewModel.SelectedEmail ?? EmailListBox.SelectedItem as Email;

        Log4.Debug($"삭제 시도 - 우클릭: {_rightClickedEmail?.Subject ?? "null"}, 선택: {_viewModel.SelectedEmail?.Subject ?? "null"}");

        if (targetEmail == null)
        {
            Log4.Warn("삭제할 메일이 없습니다.");
            _rightClickedEmail = null;
            return;
        }

        if (string.IsNullOrEmpty(targetEmail.EntryId))
        {
            Log4.Warn($"EntryId가 없는 메일은 삭제할 수 없습니다: {targetEmail.Subject}");
            _rightClickedEmail = null;
            return;
        }

        // 삭제 전 정보 저장 (실행취소용)
        _lastDeletedEmail = targetEmail;
        _lastDeletedFromFolderId = targetEmail.ParentFolderId;

        Log4.Info($"메일 삭제 요청: {targetEmail.Subject} (EntryId: {targetEmail.EntryId})");

        try
        {
            await _viewModel.DeleteEmailCommand.ExecuteAsync(targetEmail);
        }
        catch (Exception ex)
        {
            Log4.Error($"DeleteEmailCommand 실행 실패: {ex}");
            return;
        }

        // 실행취소 팝업 표시
        ShowUndoDeletePopup();

        _rightClickedEmail = null;
    }

    /// <summary>
    /// 선택된 메일 삭제 (이벤트 핸들러)
    /// </summary>
    private async void EmailDelete_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("=== EmailDelete_Click 호출됨 ===");
        await DeleteSelectedEmailAsync();
    }

    /// <summary>
    /// 삭제 버튼 클릭 이벤트 핸들러 (Button 사용)
    /// </summary>
    private async void EmailDelete_Button_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("=== EmailDelete_Button_Click 호출됨 ===");
        Log4.Debug($"_rightClickedEmail: {_rightClickedEmail?.Subject ?? "null"}");
        Log4.Debug($"_viewModel.SelectedEmail: {_viewModel.SelectedEmail?.Subject ?? "null"}");
        Log4.Debug($"EmailListBox.SelectedItem: {(EmailListBox.SelectedItem as Email)?.Subject ?? "null"}");

        try
        {
            // ContextMenu 닫기
            if (EmailListBox.ContextMenu != null)
            {
                EmailListBox.ContextMenu.IsOpen = false;
            }

            await DeleteSelectedEmailAsync();
            Log4.Info("삭제 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"EmailDelete_Button_Click 예외: {ex.Message}");
            Log4.Error($"스택: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 실행취소 팝업 표시 (삭제/이동 공통)
    /// </summary>
    private void ShowUndoPopup(string message, bool isMove)
    {
        // 기존 타이머 중지
        _undoTimer?.Stop();

        // 팝업 텍스트 설정
        UndoPopupText.Text = message;
        _isUndoForMove = isMove;

        // 팝업 표시
        UndoDeletePopup.Visibility = Visibility.Visible;

        // 5초 후 자동 숨김
        _undoTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _undoTimer.Tick += (s, e) =>
        {
            _undoTimer.Stop();
            HideUndoPopup();
        };
        _undoTimer.Start();
    }

    /// <summary>
    /// 삭제 실행취소 팝업 표시 (기존 호환용)
    /// </summary>
    private void ShowUndoDeletePopup()
    {
        if (_lastDeletedEmail == null) return;
        ShowUndoPopup("삭제됨", false);
    }

    /// <summary>
    /// 이동 실행취소 팝업 표시
    /// </summary>
    private void ShowUndoMovePopup(int count)
    {
        if (_lastMovedEmails == null || _lastMovedEmails.Count == 0) return;
        var message = count == 1 ? "이동됨" : $"{count}개 이동됨";
        ShowUndoPopup(message, true);
    }

    /// <summary>
    /// 실행취소 팝업 숨김
    /// </summary>
    private void HideUndoPopup()
    {
        UndoDeletePopup.Visibility = Visibility.Collapsed;
        _lastDeletedEmail = null;
        _lastDeletedFromFolderId = null;
        _lastMovedEmails = null;
        _lastMovedFromFolderIds = null;
    }

    /// <summary>
    /// 실행취소 버튼 클릭 (삭제/이동 공통)
    /// </summary>
    private async void UndoAction_Click(object sender, RoutedEventArgs e)
    {
        _undoTimer?.Stop();

        // 팝업 숨기기
        UndoDeletePopup.Visibility = Visibility.Collapsed;

        if (_isUndoForMove)
        {
            // 이동 실행취소
            await UndoMoveAsync();
        }
        else
        {
            // 삭제 실행취소
            await UndoDeleteAsync();
        }
    }

    /// <summary>
    /// 삭제 실행취소
    /// </summary>
    private async Task UndoDeleteAsync()
    {
        var emailToRestore = _lastDeletedEmail;
        var originalFolderId = _lastDeletedFromFolderId;

        // 정리
        _lastDeletedEmail = null;
        _lastDeletedFromFolderId = null;

        if (emailToRestore == null || string.IsNullOrEmpty(originalFolderId))
        {
            Log4.Warn("실행취소할 메일 정보가 없습니다.");
            return;
        }

        try
        {
            await _viewModel.RestoreDeletedEmailAsync(emailToRestore, originalFolderId);
            Log4.Info($"메일 복원 완료: {emailToRestore.Subject}");
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 복원 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 이동 실행취소
    /// </summary>
    private async Task UndoMoveAsync()
    {
        var emailsToRestore = _lastMovedEmails;
        var originalFolderIds = _lastMovedFromFolderIds;

        // 정리
        _lastMovedEmails = null;
        _lastMovedFromFolderIds = null;

        if (emailsToRestore == null || emailsToRestore.Count == 0 || originalFolderIds == null)
        {
            Log4.Warn("실행취소할 이동 정보가 없습니다.");
            return;
        }

        try
        {
            await _viewModel.RestoreMovedEmailsAsync(emailsToRestore, originalFolderIds);
            Log4.Info($"메일 이동 취소 완료: {emailsToRestore.Count}건");
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 이동 취소 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 메일 목록에서 Flag 버튼 클릭
    /// </summary>
    private async void EmailFlag_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // 이벤트 버블링 방지

        if (sender is FrameworkElement element && element.Tag is Email email)
        {
            // 플래그 상태 토글: notFlagged → flagged → complete → notFlagged
            var newStatus = email.FlagStatus?.ToLower() switch
            {
                "flagged" => "complete",
                "complete" => "notFlagged",
                _ => "flagged"
            };

            await _viewModel.UpdateFlagStatusAsync(new List<Email> { email }, newStatus);
            Log4.Debug($"플래그 변경: {email.Subject} → {newStatus}");
        }
    }

    /// <summary>
    /// 플래그 버튼 PreviewMouseLeftButtonDown (ListBox 클릭 문제 해결용)
    /// </summary>
    private async void EmailFlag_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // ListBox 선택 방지
        Serilog.Log.Information("[EmailFlag_PreviewMouseDown] 플래그 버튼 마우스다운 이벤트 발생");

        if (sender is FrameworkElement element && element.Tag is Email email)
        {
            // 플래그 상태 토글: notFlagged → flagged → complete → notFlagged
            var newStatus = email.FlagStatus?.ToLower() switch
            {
                "flagged" => "complete",
                "complete" => "notFlagged",
                _ => "flagged"
            };

            Serilog.Log.Information("[EmailFlag_PreviewMouseDown] 플래그 변경 시도: {Subject} → {NewStatus}",
                email.Subject, newStatus);

            await _viewModel.UpdateFlagStatusAsync(new List<Email> { email }, newStatus);
        }
    }

    /// <summary>
    /// 메일 목록에서 Pin 버튼 클릭
    /// </summary>
    private void EmailPin_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // 이벤트 버블링 방지
        Serilog.Log.Information("[EmailPin_Click] 핀 버튼 클릭 이벤트 발생");

        if (sender is FrameworkElement element && element.Tag is Email email)
        {
            Serilog.Log.Information("[EmailPin_Click] 핀 토글 시도: {Subject}, CanExecute: {CanExecute}",
                email.Subject, _viewModel.TogglePinnedCommand.CanExecute(email));

            // TogglePinnedCommand로 핀 상태 토글
            if (_viewModel.TogglePinnedCommand.CanExecute(email))
            {
                _viewModel.TogglePinnedCommand.Execute(email);
                Serilog.Log.Information("[EmailPin_Click] 핀 토글 명령 실행됨");
            }
            Log4.Debug($"핀 토글: {email.Subject}");
        }
        else
        {
            Serilog.Log.Warning("[EmailPin_Click] sender 또는 Tag가 유효하지 않음");
        }
    }

    /// <summary>
    /// 핀 버튼 PreviewMouseLeftButtonDown (ListBox 클릭 문제 해결용)
    /// </summary>
    private void EmailPin_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // ListBox 선택 방지
        Serilog.Log.Information("[EmailPin_PreviewMouseDown] 핀 버튼 마우스다운 이벤트 발생");

        if (sender is FrameworkElement element && element.Tag is Email email)
        {
            Serilog.Log.Information("[EmailPin_PreviewMouseDown] 핀 토글 시도: {Subject}", email.Subject);

            // TogglePinnedCommand로 핀 상태 토글
            if (_viewModel.TogglePinnedCommand.CanExecute(email))
            {
                _viewModel.TogglePinnedCommand.Execute(email);
                Serilog.Log.Information("[EmailPin_PreviewMouseDown] 핀 토글 명령 실행됨: {Subject}, IsPinned: {IsPinned}",
                    email.Subject, email.IsPinned);
            }
        }
    }

    /// <summary>
    /// 삭제 버튼 PreviewMouseLeftButtonDown (아웃룩 스타일)
    /// </summary>
    private async void EmailDelete_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // ListBox 선택 방지
        Serilog.Log.Information("[EmailDelete_PreviewMouseDown] 삭제 버튼 마우스다운 이벤트 발생");

        if (sender is FrameworkElement element && element.Tag is Email email)
        {
            if (string.IsNullOrEmpty(email.EntryId))
            {
                Log4.Warn($"EntryId가 없는 메일은 삭제할 수 없습니다: {email.Subject}");
                return;
            }

            Serilog.Log.Information("[EmailDelete_PreviewMouseDown] 삭제 시도: {Subject}", email.Subject);

            // 삭제 전 정보 저장 (실행취소용)
            _lastDeletedEmail = email;
            _lastDeletedFromFolderId = email.ParentFolderId;

            // DeleteEmailCommand로 삭제
            if (_viewModel.DeleteEmailCommand.CanExecute(email))
            {
                await _viewModel.DeleteEmailCommand.ExecuteAsync(email);
                Serilog.Log.Information("[EmailDelete_PreviewMouseDown] 삭제 명령 실행됨: {Subject}", email.Subject);

                // 실행취소 팝업 표시
                ShowUndoDeletePopup();
            }
        }
    }

    /// <summary>
    /// 새 메일 버튼 클릭
    /// </summary>
    private void NewMailButton_Click(object sender, RoutedEventArgs e)
    {
        OpenComposeWindow(ViewModels.ComposeMode.New);
    }

    /// <summary>
    /// 답장 클릭
    /// </summary>
    private void EmailReply_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = EmailListBox.SelectedItem as Email;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Reply, selectedEmail);
        }
    }

    /// <summary>
    /// 전체 답장 클릭
    /// </summary>
    private void EmailReplyAll_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = EmailListBox.SelectedItem as Email;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.ReplyAll, selectedEmail);
        }
    }

    /// <summary>
    /// 전달 클릭
    /// </summary>
    private void EmailForward_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = EmailListBox.SelectedItem as Email;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Forward, selectedEmail);
        }
    }

    #endregion

    #region 메일 본문 상단 액션 버튼 핸들러

    /// <summary>
    /// 회신 버튼 클릭
    /// </summary>
    private void ReplyButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Reply, selectedEmail);
        }
    }

    /// <summary>
    /// 전체 회신 버튼 클릭
    /// </summary>
    private void ReplyAllButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.ReplyAll, selectedEmail);
        }
    }

    /// <summary>
    /// 전달 버튼 클릭
    /// </summary>
    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            OpenComposeWindow(ViewModels.ComposeMode.Forward, selectedEmail);
        }
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 삭제 확인
            var result = System.Windows.MessageBox.Show(
                $"'{selectedEmail.Subject}' 메일을 삭제하시겠습니까?",
                "삭제 확인",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                // 기존 컨텍스트 메뉴의 삭제 기능 호출
                EmailDelete_Click(sender, e);
            }
        }
    }

    /// <summary>
    /// 플래그 버튼 클릭 (토글)
    /// </summary>
    private void FlagButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 현재 플래그 상태에 따라 설정/해제 호출
            if (selectedEmail.FlagStatus == "flagged")
            {
                EmailClearFlag_Click(sender, e);
            }
            else
            {
                EmailSetFlag_Click(sender, e);
            }
        }
    }

    /// <summary>
    /// 읽음/안읽음 토글 버튼 클릭
    /// </summary>
    private void ReadUnreadButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 현재 읽음 상태에 따라 토글
            if (selectedEmail.IsRead)
            {
                EmailMarkAsUnread_Click(sender, e);
            }
            else
            {
                EmailMarkAsRead_Click(sender, e);
            }
        }
    }

    /// <summary>
    /// 더보기 버튼 클릭 (추가 작업 메뉴)
    /// </summary>
    private void MoreActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            // 더보기 컨텍스트 메뉴 생성
            var contextMenu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)FindResource("ApplicationBackgroundBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"),
                Padding = new Thickness(4)
            };

            // AI 재분석 메뉴
            var aiItem = new System.Windows.Controls.MenuItem { Header = "🤖 AI 재분석" };
            aiItem.Click += EmailAIReanalyze_Click;
            contextMenu.Items.Add(aiItem);

            contextMenu.Items.Add(new Separator());

            // 카테고리 설정
            var categoryItem = new System.Windows.Controls.MenuItem { Header = "🏷️ 카테고리 설정..." };
            categoryItem.Click += EmailSetCategories_Click;
            contextMenu.Items.Add(categoryItem);

            // 즐겨찾기 추가
            var starItem = new System.Windows.Controls.MenuItem { Header = "⭐ 즐겨찾기 토글" };
            starItem.Click += EmailToggleStar_Click;
            contextMenu.Items.Add(starItem);

            button.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// 즐겨찾기 토글
    /// </summary>
    private void EmailToggleStar_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            selectedEmail.IsStarred = !selectedEmail.IsStarred;
            Log4.Info($"즐겨찾기 토글: {selectedEmail.Subject} -> {(selectedEmail.IsStarred ? "추가" : "해제")}");
        }
    }

    /// <summary>
    /// 카테고리 설정 (더보기 메뉴에서 호출)
    /// </summary>
    private void EmailSetCategories_Click(object sender, RoutedEventArgs e)
    {
        var selectedEmail = _viewModel.SelectedEmail;
        if (selectedEmail != null)
        {
            // 카테고리 선택 다이얼로그를 열거나 간단한 처리
            Log4.Info($"카테고리 설정 요청: {selectedEmail.Subject}");
            System.Windows.MessageBox.Show(
                "카테고리 설정 기능은 추후 구현 예정입니다.",
                "알림",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
    }

    #endregion

    /// <summary>
    /// 메일 작성 창 열기
    /// </summary>
    private void OpenComposeWindow(ViewModels.ComposeMode mode, Email? originalEmail = null)
    {
        try
        {
            var graphMailService = (App.Current as App)?.GraphMailService;
            if (graphMailService == null)
            {
                Log4.Error("GraphMailService를 찾을 수 없습니다.");
                return;
            }

            // 보낸메일 즉시 동기화를 위해 BackgroundSyncService도 전달
            var syncService = (App.Current as App)?.BackgroundSyncService;
            var viewModel = new ViewModels.ComposeViewModel(graphMailService, syncService, mode, originalEmail);
            var composeWindow = new ComposeWindow(viewModel);
            composeWindow.Owner = this;
            composeWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 작성 창 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 임시보관함 폴더인지 확인
    /// </summary>
    private bool IsDraftsFolder(Folder? folder)
    {
        if (folder == null) return false;

        // 폴더 이름으로 임시보관함 확인
        return folder.DisplayName.Equals("Drafts", StringComparison.OrdinalIgnoreCase) ||
               folder.DisplayName.Equals("임시 보관함", StringComparison.OrdinalIgnoreCase) ||
               folder.DisplayName.Equals("초안", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 이메일 문자열을 파싱 - "이름 &lt;이메일&gt;" 형식이면 그대로 반환, 아니면 이메일만 반환
    /// </summary>
    private static string ParseEmailWithName(string emailString)
    {
        if (string.IsNullOrWhiteSpace(emailString))
            return "";

        emailString = emailString.Trim();

        // "이름" <email> 또는 이름 <email> 형식 체크
        if (emailString.Contains("<") && emailString.Contains(">"))
        {
            // 이미 이름 <이메일> 형식이면 그대로 반환
            return emailString;
        }

        // 이메일만 있는 경우 그대로 반환
        return emailString;
    }

    /// <summary>
    /// 임시보관함 메일을 인플레이스 편집 모드로 열기
    /// </summary>
    private void OpenDraftForEditing(Email draftEmail)
    {
        try
        {
            Log4.Info($"임시보관함 메일 인플레이스 편집: {draftEmail.Subject}");

            // ViewModel의 드래프트 편집 모드 활성화
            _viewModel.LoadDraftForEditing(draftEmail);
        }
        catch (Exception ex)
        {
            Log4.Error($"임시보관함 메일 편집 실패: {ex.Message}");
        }
    }

    #region 정렬 및 전체 선택 핸들러

    /// <summary>
    /// 정렬 드롭다운 버튼 클릭 - 정렬 옵션 메뉴 표시
    /// </summary>
    private void SortDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            var contextMenu = new ContextMenu
            {
                Background = (System.Windows.Media.Brush)FindResource("ApplicationBackgroundBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("ControlElevationBorderBrush"),
                Padding = new Thickness(4)
            };

            // 날짜 정렬
            var dateItem = new System.Windows.Controls.MenuItem { Header = "📅 날짜" };
            dateItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("ReceivedDateTime");
            contextMenu.Items.Add(dateItem);

            // 제목 정렬
            var subjectItem = new System.Windows.Controls.MenuItem { Header = "📝 제목" };
            subjectItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("Subject");
            contextMenu.Items.Add(subjectItem);

            // 발신자 정렬
            var fromItem = new System.Windows.Controls.MenuItem { Header = "👤 발신자" };
            fromItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("From");
            contextMenu.Items.Add(fromItem);

            // 중요도 정렬
            var priorityItem = new System.Windows.Controls.MenuItem { Header = "⭐ 중요도" };
            priorityItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("PriorityScore");
            contextMenu.Items.Add(priorityItem);

            contextMenu.Items.Add(new Separator());

            // 읽지 않은 메일 정렬
            var unreadItem = new System.Windows.Controls.MenuItem { Header = "📧 읽지 않음" };
            unreadItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("IsRead");
            contextMenu.Items.Add(unreadItem);

            // 플래그 정렬
            var flagItem = new System.Windows.Controls.MenuItem { Header = "🚩 플래그" };
            flagItem.Click += (s, args) => _viewModel.SortEmailsCommand.Execute("FlagStatus");
            contextMenu.Items.Add(flagItem);

            button.ContextMenu = contextMenu;
            contextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// 전체 선택 체크박스 체크됨
    /// </summary>
    private void SelectAllCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Emails != null)
        {
            EmailListBox.SelectAll();
            Log4.Debug($"전체 선택: {_viewModel.Emails.Count}개");
        }
    }

    /// <summary>
    /// 전체 선택 체크박스 해제됨
    /// </summary>
    private void SelectAllCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        EmailListBox.UnselectAll();
        Log4.Debug("전체 선택 해제");
    }

    #endregion

    #region Phase 1: 검색 및 키보드 단축키

    /// <summary>
    /// 검색 텍스트 박스 키 이벤트 핸들러
    /// Enter 키 누르면 검색 실행
    /// </summary>
    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.SearchCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.ClearSearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    #endregion

    #region Phase 2: 메일 드래그&드롭

    private Point _emailDragStartPoint;
    private bool _isDraggingEmail;
    private List<Email>? _draggedEmails;

    /// <summary>
    /// 메일 리스트 마우스 다운 - 드래그 시작점 기록
    /// </summary>
    private void EmailListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _emailDragStartPoint = e.GetPosition(null);
        _isDraggingEmail = false;
    }

    /// <summary>
    /// 메일 리스트 마우스 이동 - 드래그 시작 감지
    /// </summary>
    private void EmailListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var currentPos = e.GetPosition(null);
        var diff = _emailDragStartPoint - currentPos;

        // 최소 이동 거리 체크
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // 선택된 메일들 가져오기
        var selectedEmails = EmailListBox.SelectedItems.Cast<Email>().ToList();
        if (selectedEmails.Count == 0) return;

        _isDraggingEmail = true;
        _draggedEmails = selectedEmails;

        // 드래그 데이터 설정
        var dragData = new DataObject("EmailDragData", selectedEmails);

        // 드래그 시작
        DragDrop.DoDragDrop(EmailListBox, dragData, DragDropEffects.Move);

        _isDraggingEmail = false;
        _draggedEmails = null;
    }

    /// <summary>
    /// 메일 리스트 더블클릭 - 새 창에서 메일 열기
    /// 임시보관함: 메일 작성 창, 그 외: 메일 보기 창
    /// </summary>
    private void EmailListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 클릭한 위치에서 ListBoxItem 찾기
        var element = e.OriginalSource as DependencyObject;
        var listBoxItem = FindAncestor<System.Windows.Controls.ListBoxItem>(element);
        if (listBoxItem == null) return;

        var email = listBoxItem.DataContext as Email;
        if (email == null) return;

        try
        {
            // 임시보관함이면 메일 작성 창으로 열기
            if (IsDraftsFolder(_viewModel.SelectedFolder))
            {
                Log4.Info($"임시보관함 메일 더블클릭 - 작성 창으로 열기: {email.Subject}");
                var graphMailService = (App.Current as App)?.GraphMailService;
                if (graphMailService == null)
                {
                    Log4.Warn("GraphMailService를 가져올 수 없습니다.");
                    return;
                }
                var syncService = (App.Current as App)?.BackgroundSyncService;
                var viewModel = new ViewModels.ComposeViewModel(graphMailService, syncService, ComposeMode.EditDraft, email);
                var composeWindow = new ComposeWindow(viewModel);
                composeWindow.Show();
            }
            else
            {
                // 그 외 폴더는 메일 보기 창으로 열기
                Log4.Info($"메일 더블클릭 - 보기 창으로 열기: {email.Subject}");
                var viewWindow = new EmailViewWindow(email);
                viewWindow.Show();
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 새 창 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 폴더 트리 드래그 진입 - 드롭 가능 여부 표시
    /// </summary>
    private void FolderTreeView_DragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("EmailDragData"))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    /// <summary>
    /// 폴더 트리 드래그 오버
    /// </summary>
    private void FolderTreeView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (e.Data.GetDataPresent("EmailDragData"))
        {
            // 드롭 대상 폴더 찾기
            var element = e.OriginalSource as DependencyObject;
            var treeViewItem = FindAncestor<System.Windows.Controls.TreeViewItem>(element);

            if (treeViewItem?.DataContext is Folder targetFolder)
            {
                e.Effects = DragDropEffects.Move;
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// 폴더 트리에 드롭 - 메일 이동
    /// </summary>
    private async void FolderTreeView_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("EmailDragData")) return;

        // 드롭 대상 폴더 찾기
        var element = e.OriginalSource as DependencyObject;
        var treeViewItem = FindAncestor<System.Windows.Controls.TreeViewItem>(element);

        if (treeViewItem?.DataContext is Folder targetFolder)
        {
            var emails = e.Data.GetData("EmailDragData") as List<Email>;
            if (emails != null && emails.Count > 0)
            {
                // 실행취소를 위해 원래 폴더 정보 저장
                _lastMovedEmails = new List<Email>(emails);
                _lastMovedFromFolderIds = emails.ToDictionary(em => em.Id, em => em.ParentFolderId ?? string.Empty);

                Log4.Info($"메일 드롭: {emails.Count}건 → {targetFolder.DisplayName}");
                await _viewModel.MoveEmailsToFolderAsync(emails, targetFolder);

                // 실행취소 팝업 표시
                ShowUndoMovePopup(emails.Count);
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// 시각적 트리에서 특정 타입의 부모 요소 찾기
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T target)
                return target;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }


    /// <summary>
    /// 키보드 단축키 처리
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // 검색 텍스트 박스에 포커스가 있으면 단축키 처리 안함
        if (TitleBarSearchBox.IsFocused)
            return;

        // Ctrl 키 조합
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.R:
                    // Ctrl+R: 답장
                    if (_viewModel.SelectedEmail != null)
                    {
                        OpenComposeWindow(ComposeMode.Reply, _viewModel.SelectedEmail);
                        e.Handled = true;
                    }
                    break;

                case Key.F:
                    // Ctrl+F: 검색 텍스트 박스로 포커스
                    TitleBarSearchBox.Focus();
                    e.Handled = true;
                    break;

                case Key.N:
                    // Ctrl+N: 새 메일
                    OpenComposeWindow(ComposeMode.New, null);
                    e.Handled = true;
                    break;
            }
        }
        // Ctrl+Shift 키 조합
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            switch (e.Key)
            {
                case Key.R:
                    // Ctrl+Shift+R: 전체 답장
                    if (_viewModel.SelectedEmail != null)
                    {
                        OpenComposeWindow(ComposeMode.ReplyAll, _viewModel.SelectedEmail);
                        e.Handled = true;
                    }
                    break;

                case Key.F:
                    // Ctrl+Shift+F: 전달
                    if (_viewModel.SelectedEmail != null)
                    {
                        OpenComposeWindow(ComposeMode.Forward, _viewModel.SelectedEmail);
                        e.Handled = true;
                    }
                    break;
            }
        }
        // 단일 키
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.Delete:
                    // Delete: 선택된 메일 삭제 (실행취소 팝업 포함)
                    _ = DeleteSelectedEmailAsync();
                    e.Handled = true;
                    break;

                case Key.F5:
                    // F5: 새로고침
                    _viewModel.LoadEmailsCommand.Execute(null);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    // Escape: 실행취소 팝업이 표시되어 있으면 실행취소
                    if (UndoDeletePopup.Visibility == Visibility.Visible)
                    {
                        UndoAction_Click(null!, null!);
                        e.Handled = true;
                    }
                    // 검색 모드면 검색 초기화
                    else if (_viewModel.IsSearchMode)
                    {
                        _viewModel.ClearSearchCommand.Execute(null);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    #endregion

    #region 좌측 네비게이션 아이콘바

    /// <summary>
    /// 메일 모드로 전환
    /// </summary>
    private void NavMailButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 메일 모드");
        ShowMailView();
    }

    /// <summary>
    /// 캘린더 모드로 전환
    /// </summary>
    private void NavCalendarButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 캘린더 모드");
        ShowCalendarView();
    }

    /// <summary>
    /// 채팅 모드로 전환
    /// </summary>
    private void NavChatButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 채팅 모드");
        ShowChatView();
    }

    /// <summary>
    /// 팀 모드로 전환
    /// </summary>
    private void NavTeamsButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 팀 모드");
        ShowTeamsView();
    }

    /// <summary>
    /// 활동 모드로 전환
    /// </summary>
    private void NavActivityButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 활동 모드");
        ShowActivityView();
    }

    /// <summary>
    /// 플래너 모드로 전환
    /// </summary>
    private void NavPlannerButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 플래너 모드");
        ShowPlannerView();
    }

    /// <summary>
    /// OneDrive 모드로 전환
    /// </summary>
    private void NavOneDriveButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: OneDrive 모드");
        ShowOneDriveView();
    }

    /// <summary>
    /// OneNote 모드로 전환
    /// </summary>
    private void NavOneNoteButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: OneNote 모드");
        ShowOneNoteView();
    }

    /// <summary>
    /// 통화 모드로 전환
    /// </summary>
    private void NavCallsButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 통화 모드");
        ShowCallsView();
    }

    /// <summary>
    /// 설정 버튼 체크 (라디오버튼)
    /// </summary>
    private void NavSettingsButton_Checked(object sender, RoutedEventArgs e)
    {
        Log4.Info("네비게이션: 설정 모드");
        ShowSettingsView();
    }

    /// <summary>
    /// 타이틀바 설정 버튼 클릭 (동기화 설정으로 연결)
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 동기화 설정");
        MenuSyncSettings_Click(sender, e);
    }

    /// <summary>
    /// 커스텀 최소화 버튼 클릭
    /// </summary>
    private void CustomMinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 커스텀 최대화/복원 버튼 클릭
    /// </summary>
    private void CustomMaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Maximize24;
            CustomMaximizeButton.ToolTip = "최대화";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.SquareMultiple24;
            CustomMaximizeButton.ToolTip = "복원";
        }
    }

    /// <summary>
    /// 커스텀 종료 버튼 클릭
    /// </summary>
    private void CustomCloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 알림 버튼 클릭 - 알림 패널 팝업 열기
    /// </summary>
    private void NotificationButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 알림 패널 열기");
        NotificationPopup.IsOpen = !NotificationPopup.IsOpen;
    }

    /// <summary>
    /// 알림 패널 닫기 버튼 클릭
    /// </summary>
    private void CloseNotificationPopup_Click(object sender, RoutedEventArgs e)
    {
        NotificationPopup.IsOpen = false;
    }

    /// <summary>
    /// 타이틀바 검색창 키 이벤트
    /// </summary>
    private void TitleBarSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var searchText = TitleBarSearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(searchText))
            {
                AddRecentSearch(searchText);
            }
            _viewModel.SearchCommand.Execute(null);
            SearchAutocompletePopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _viewModel.ClearSearchCommand.Execute(null);
            TitleBarSearchBox.Text = "";
            SearchAutocompletePopup.IsOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 타이틀바 검색창 텍스트 변경 - 실시간 연락처 검색
    /// </summary>
    private async void TitleBarSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = TitleBarSearchBox.Text?.Trim() ?? "";

        // "모두" 또는 "사람" 탭일 때만 연락처 검색
        if (_currentSearchTab == "모두" || _currentSearchTab == "사람")
        {
            await SearchContactsAsync(searchText);
        }
    }

    /// <summary>
    /// 검색창 포커스 시 자동완성 팝업 열기
    /// </summary>
    private void TitleBarSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchAutocompletePopup.IsOpen = true;
    }

    /// <summary>
    /// 검색창 포커스 해제 시 자동완성 팝업 닫기
    /// </summary>
    private void TitleBarSearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // 포커스가 팝업 내부로 이동한 경우는 닫지 않음
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var focusedElement = Keyboard.FocusedElement as DependencyObject;
            if (focusedElement != null)
            {
                // 포커스가 팝업 내부에 있는지 확인
                var parent = focusedElement;
                while (parent != null)
                {
                    if (parent == SearchAutocompletePopup.Child)
                        return;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            // 검색창 자체에 포커스가 있으면 닫지 않음
            if (TitleBarSearchBox.IsFocused || TitleBarSearchBox.IsKeyboardFocusWithin)
                return;

            SearchAutocompletePopup.IsOpen = false;
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 검색 자동완성 뒤로가기 버튼
    /// </summary>
    private void SearchAutocompleteBack_Click(object sender, RoutedEventArgs e)
    {
        SearchAutocompletePopup.IsOpen = false;
    }

    // 연락처 검색용 필드
    private ContactSearchService? _contactSearchService;
    private CancellationTokenSource? _contactSearchCts;
    private string _currentSearchTab = "모두";

    /// <summary>
    /// 검색 탭 클릭 (모두/메일/사람)
    /// </summary>
    private async void SearchTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button clickedTab) return;

        // 모든 탭 버튼을 Secondary로 변경
        SearchTabAll.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        SearchTabMail.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        SearchTabPerson.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

        // 클릭한 탭을 Primary로 변경
        clickedTab.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;

        _currentSearchTab = clickedTab.Content?.ToString() ?? "모두";
        Log4.Info($"검색 탭 변경: {_currentSearchTab}");

        // 탭에 따라 검색 결과 필터링
        var searchText = TitleBarSearchBox.Text?.Trim() ?? "";

        if (_currentSearchTab == "사람")
        {
            // 연락처만 표시
            RecentSearchItems.Visibility = Visibility.Collapsed;
            await SearchContactsAsync(searchText);
        }
        else
        {
            // 최근 검색 표시
            RecentSearchItems.Visibility = Visibility.Visible;

            if (_currentSearchTab == "모두" && !string.IsNullOrEmpty(searchText))
            {
                // 모두 탭: 연락처도 함께 표시
                await SearchContactsAsync(searchText);
            }
            else
            {
                // 메일 탭: 연락처 숨김
                ContactSuggestionsHeader.Visibility = Visibility.Collapsed;
                ContactSuggestionItems.ItemsSource = null;
            }
        }
    }

    /// <summary>
    /// 연락처 검색
    /// </summary>
    private async Task SearchContactsAsync(string query)
    {
        // 2자 미만이면 연락처 숨김
        if (string.IsNullOrEmpty(query) || query.Length < 2)
        {
            ContactSuggestionsHeader.Visibility = Visibility.Collapsed;
            ContactSuggestionItems.ItemsSource = null;
            return;
        }

        try
        {
            // ContactSearchService 가져오기
            if (_contactSearchService == null)
            {
                _contactSearchService = ((App)Application.Current).GetService<ContactSearchService>();
            }

            if (_contactSearchService == null)
            {
                Log4.Warn("ContactSearchService를 사용할 수 없습니다.");
                return;
            }

            // 이전 검색 취소
            _contactSearchCts?.Cancel();
            _contactSearchCts = new CancellationTokenSource();

            // 디바운싱 (300ms)
            await Task.Delay(300, _contactSearchCts.Token);

            // 검색 실행
            var contacts = await _contactSearchService.SearchContactsAsync(query, _contactSearchCts.Token);

            if (_contactSearchCts.Token.IsCancellationRequested)
                return;

            // 결과 표시
            if (contacts.Count > 0)
            {
                ContactSuggestionsHeader.Visibility = Visibility.Visible;
                ContactSuggestionItems.ItemsSource = contacts;

                // 비동기 프로필 사진 로딩 (UI 차단 없이)
                _ = _contactSearchService.EnrichWithPhotosAsync(contacts);
            }
            else
            {
                ContactSuggestionsHeader.Visibility = Visibility.Collapsed;
                ContactSuggestionItems.ItemsSource = null;
            }
        }
        catch (TaskCanceledException)
        {
            // 취소됨 - 정상
        }
        catch (Exception ex)
        {
            Log4.Warn($"연락처 검색 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 연락처 제안 클릭 - 해당 연락처로 메일 작성
    /// </summary>
    private void ContactSuggestionItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border) return;
        if (border.Tag is not ContactSuggestion contact) return;

        // 검색 팝업 닫기
        SearchAutocompletePopup.IsOpen = false;
        TitleBarSearchBox.Text = "";

        // 해당 연락처로 메일 작성
        OpenComposeWindowWithRecipient(contact);
    }

    /// <summary>
    /// 연락처를 받는 사람으로 메일 작성 창 열기
    /// </summary>
    private void OpenComposeWindowWithRecipient(ContactSuggestion contact)
    {
        try
        {
            var graphMailService = ((App)Application.Current).GetService<Services.Graph.GraphMailService>();
            if (graphMailService == null)
            {
                Log4.Error("GraphMailService를 사용할 수 없습니다.");
                return;
            }

            var composeVm = new ComposeViewModel(graphMailService, _syncService);
            composeVm.To = contact.FormattedAddress;

            var composeWindow = new ComposeWindow(composeVm)
            {
                Owner = this
            };

            Log4.Info($"연락처로 메일 작성: {contact.Email}");
            composeWindow.Show();
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 작성 창 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 메일 뷰 표시
    /// </summary>
    private void ShowMailView()
    {
        // 모든 뷰 숨기기
        HideAllViews();

        // 메일 관련 UI 요소 표시
        if (FolderTreeBorder != null) FolderTreeBorder.Visibility = Visibility.Visible;
        if (Splitter1 != null) Splitter1.Visibility = Visibility.Visible;
        if (MailListBorder != null) MailListBorder.Visibility = Visibility.Visible;
        if (Splitter2 != null) Splitter2.Visibility = Visibility.Visible;
        if (BodyAreaGrid != null) BodyAreaGrid.Visibility = Visibility.Visible;

        // 우측 패널: AI 패널 표시
        if (AIPanelBorder != null) AIPanelBorder.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "메일";
        _viewModel.IsCalendarViewActive = false;
        _viewModel.IsCalendarMode = false;

        // 기능별 테마 적용
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("mail");
    }

    /// <summary>
    /// 캘린더 뷰 표시
    /// </summary>
    private void ShowCalendarView()
    {
        // 모든 뷰 숨기기
        HideAllViews();

        // 캘린더 뷰 표시
        if (CalendarViewBorder != null) CalendarViewBorder.Visibility = Visibility.Visible;

        // 우측 패널: 캘린더 세부 패널 표시
        if (CalendarDetailPanel != null) CalendarDetailPanel.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "일정";
        _viewModel.IsCalendarViewActive = true;
        _viewModel.IsCalendarMode = true;

        // 캘린더 데이터 로드
        LoadCalendarDataAsync();

        // 기능별 테마 적용
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("calendar");
    }

    /// <summary>
    /// 모든 뷰 숨기기 (공통 초기화)
    /// </summary>
    private void HideAllViews()
    {
        // 메일 관련 UI 요소 숨김
        if (FolderTreeBorder != null) FolderTreeBorder.Visibility = Visibility.Collapsed;
        if (Splitter1 != null) Splitter1.Visibility = Visibility.Collapsed;
        if (MailListBorder != null) MailListBorder.Visibility = Visibility.Collapsed;
        if (Splitter2 != null) Splitter2.Visibility = Visibility.Collapsed;
        if (BodyAreaGrid != null) BodyAreaGrid.Visibility = Visibility.Collapsed;

        // 캘린더 뷰 숨김
        if (CalendarViewBorder != null) CalendarViewBorder.Visibility = Visibility.Collapsed;

        // 새 뷰들 숨김
        if (ChatViewBorder != null) ChatViewBorder.Visibility = Visibility.Collapsed;
        if (TeamsViewBorder != null) TeamsViewBorder.Visibility = Visibility.Collapsed;
        if (ActivityViewBorder != null) ActivityViewBorder.Visibility = Visibility.Collapsed;
        if (PlannerViewBorder != null) PlannerViewBorder.Visibility = Visibility.Collapsed;
        if (OneDriveViewBorder != null) OneDriveViewBorder.Visibility = Visibility.Collapsed;
        if (OneNoteViewBorder != null) OneNoteViewBorder.Visibility = Visibility.Collapsed;
        if (CallsViewBorder != null) CallsViewBorder.Visibility = Visibility.Collapsed;
        if (SettingsViewBorder != null) SettingsViewBorder.Visibility = Visibility.Collapsed;

        // 우측 패널 숨김
        if (AIPanelBorder != null) AIPanelBorder.Visibility = Visibility.Collapsed;
        if (CalendarDetailPanel != null) CalendarDetailPanel.Visibility = Visibility.Collapsed;

        _viewModel.IsCalendarViewActive = false;
        _viewModel.IsCalendarMode = false;
    }

    /// <summary>
    /// 채팅 뷰 표시
    /// </summary>
    private async void ShowChatView()
    {
        HideAllViews();

        if (ChatViewBorder != null) ChatViewBorder.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "채팅";
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("chat");

        // TeamsViewModel 초기화 (필요 시)
        if (_teamsViewModel == null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] TeamsViewModel 초기화 시작");
                _teamsViewModel = ((App)Application.Current).GetService<TeamsViewModel>()!;
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TeamsViewModel 초기화 완료: {(_teamsViewModel != null ? "성공" : "null")}");
                Log4.Info($"TeamsViewModel 초기화 완료: {(_teamsViewModel != null ? "성공" : "null")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TeamsViewModel 초기화 실패: {ex.Message}");
                Log4.Error($"TeamsViewModel 초기화 실패: {ex.Message}");
            }
        }

        // 채팅 데이터 로드 (최초 1회)
        Log4.Info($"[ShowChatView] 분기 조건 체크: _teamsViewModel={(_teamsViewModel != null ? "not null" : "null")}, Chats.Count={_teamsViewModel?.Chats.Count ?? -1}");
        System.Diagnostics.Debug.WriteLine($"[DEBUG] _teamsViewModel: {(_teamsViewModel != null ? "not null" : "null")}, Chats.Count: {_teamsViewModel?.Chats.Count ?? -1}");
        if (_teamsViewModel != null && _teamsViewModel.Chats.Count == 0)
        {
            Log4.Info("[ShowChatView] Chats.Count == 0 → LoadChatDataAsync 호출");
            System.Diagnostics.Debug.WriteLine("[DEBUG] LoadChatDataAsync 호출");
            await LoadChatDataAsync();
        }
        else if (_teamsViewModel == null)
        {
            System.Diagnostics.Debug.WriteLine("[DEBUG] TeamsViewModel이 null입니다");
            Log4.Error("TeamsViewModel이 null입니다. 채팅 로드 불가");
        }
        else
        {
            // 이미 로드된 경우에도 ItemsSource 강제 새로고침 (UI 업데이트 보장)
            Log4.Info($"[ShowChatView] Chats.Count > 0 → 채팅 이미 로드됨 - UI 새로고침: Chats={_teamsViewModel.Chats.Count}개, FavoriteChats={_teamsViewModel.FavoriteChats.Count}개");
            UpdateChatListUI();
        }
    }

    /// <summary>
    /// 채팅 데이터 로드
    /// </summary>
    private async Task LoadChatDataAsync()
    {
        Log4.Info("[LoadChatDataAsync] 시작");
        try
        {
            System.Diagnostics.Debug.WriteLine("[DEBUG] LoadChatDataAsync 시작");
            Log4.Info("[LoadChatDataAsync] LoadChatsAsync 호출 전");
            await _teamsViewModel!.LoadChatsAsync();
            System.Diagnostics.Debug.WriteLine($"[DEBUG] LoadChatsAsync 완료: {_teamsViewModel.Chats.Count}개");
            Log4.Info($"[LoadChatDataAsync] LoadChatsAsync 완료: {_teamsViewModel.Chats.Count}개, FavoriteChats: {_teamsViewModel.FavoriteChats.Count}개");

            // 채팅 목록 UI 업데이트
            Log4.Info("[LoadChatDataAsync] UpdateChatListUI 호출 전");
            UpdateChatListUI();
            Log4.Info("[LoadChatDataAsync] UpdateChatListUI 호출 후");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] LoadChatDataAsync 실패: {ex.Message}\n{ex.StackTrace}");
            Log4.Error($"[LoadChatDataAsync] 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 채팅 목록 UI 업데이트
    /// </summary>
    private void UpdateChatListUI()
    {
        // UI 스레드에서 실행 보장
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(UpdateChatListUI);
            return;
        }

        Log4.Info("[UpdateChatListUI] 시작");

        if (_teamsViewModel == null)
        {
            Log4.Warn("[UpdateChatListUI] _teamsViewModel이 null입니다");
            return;
        }

        try
        {
            // 디버그: 현대자동차 채팅방 데이터 확인
            var hyundaiChat = _teamsViewModel.Chats.FirstOrDefault(c => c.DisplayName?.Contains("현대자동차") == true);
            if (hyundaiChat == null)
            {
                // FavoriteChats에서도 확인
                hyundaiChat = _teamsViewModel.FavoriteChats.FirstOrDefault(c => c.DisplayName?.Contains("현대자동차") == true);
            }
            if (hyundaiChat != null)
            {
                // Serilog로도 로깅 (Log4가 파일에 기록되지 않을 수 있으므로)
                Serilog.Log.Information("[UI바인딩] 현대자동차 채팅방 - HashCode: {Hash}, LastUpdatedDateTime: {Time}, Display: {Display}",
                    hyundaiChat.GetHashCode(), hyundaiChat.LastUpdatedDateTime, hyundaiChat.LastUpdatedDisplay);
                Log4.Info($"[UI바인딩] 현대자동차 채팅방 - HashCode: {hyundaiChat.GetHashCode()}, LastUpdatedDateTime: {hyundaiChat.LastUpdatedDateTime}, Display: {hyundaiChat.LastUpdatedDisplay}");
            }

            // 모든 ChatItemViewModel의 LastUpdatedDisplay 갱신
            foreach (var chat in _teamsViewModel.Chats)
            {
                chat.RefreshLastUpdatedDisplay();
            }
            foreach (var chat in _teamsViewModel.FavoriteChats)
            {
                chat.RefreshLastUpdatedDisplay();
            }
            Serilog.Log.Information("[UpdateChatListUI] RefreshLastUpdatedDisplay 호출 완료");

            // 채팅 목록 ItemsSource 설정
            if (ChatListBox != null)
            {
                ChatListBox.ItemsSource = null;
                ChatListBox.Items.Refresh();
                ChatListBox.ItemsSource = _teamsViewModel.Chats;
                ChatListBox.Items.Refresh();
                Serilog.Log.Information("[UpdateChatListUI] ChatListBox.ItemsSource 설정: {Count}개", _teamsViewModel.Chats.Count);
                Log4.Info($"[UpdateChatListUI] ChatListBox.ItemsSource 설정: {_teamsViewModel.Chats.Count}개");

                // 바인딩 후 실제 데이터 확인
                foreach (var item in ChatListBox.Items.Cast<ChatItemViewModel>().Take(10))
                {
                    if (item.DisplayName?.Contains("현대자동차") == true)
                    {
                        Serilog.Log.Information("[바인딩검증] ChatListBox 아이템 - HashCode: {Hash}, Name: {Name}, LastUpdatedDisplay: {Display}",
                            item.GetHashCode(), item.DisplayName, item.LastUpdatedDisplay);
                    }
                }
            }
            else
            {
                Log4.Warn("[UpdateChatListUI] ChatListBox가 null입니다");
            }

            // 즐겨찾기 목록 ItemsSource 설정
            if (ChatFavoritesListBox != null)
            {
                ChatFavoritesListBox.ItemsSource = null;
                ChatFavoritesListBox.Items.Refresh();
                ChatFavoritesListBox.ItemsSource = _teamsViewModel.FavoriteChats;
                ChatFavoritesListBox.Items.Refresh();
                Serilog.Log.Information("[UpdateChatListUI] ChatFavoritesListBox.ItemsSource 설정: {Count}개", _teamsViewModel.FavoriteChats.Count);
                Log4.Info($"[UpdateChatListUI] ChatFavoritesListBox.ItemsSource 설정: {_teamsViewModel.FavoriteChats.Count}개");
            }
            else
            {
                Log4.Warn("[UpdateChatListUI] ChatFavoritesListBox가 null입니다");
            }

            // 빈 상태 표시 (채팅 로딩 오버레이 사용)
            if (ChatListLoadingOverlay != null)
            {
                ChatListLoadingOverlay.Visibility = Visibility.Collapsed;
            }

            Serilog.Log.Information("[UpdateChatListUI] 완료");
            Log4.Info("[UpdateChatListUI] 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"[UpdateChatListUI] 예외 발생: {ex.Message}");
        }
    }

    /// <summary>
    /// 팀 뷰 표시
    /// </summary>
    private async void ShowTeamsView()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[Teams] ShowTeamsView 호출됨");
            Serilog.Log.Information("[Teams] ShowTeamsView 호출됨");
            Log4.Info("[Teams] ShowTeamsView 호출됨");
            HideAllViews();

            if (TeamsViewBorder != null) TeamsViewBorder.Visibility = Visibility.Visible;

            _viewModel.StatusMessage = "팀";
            Services.Theme.ThemeService.Instance.ApplyFeatureTheme("teams");

            // 팀 데이터 로드 (최초 1회 또는 비어있을 때)
            var teamsCount = _teamsViewModel?.Teams.Count ?? -1;
            System.Diagnostics.Debug.WriteLine($"[Teams] 로드 조건 확인: _teamsViewModel={(_teamsViewModel != null ? "not null" : "null")}, Teams.Count={teamsCount}");
            Serilog.Log.Information("[Teams] 로드 조건 확인: _teamsViewModel={IsNull}, Teams.Count={Count}",
                _teamsViewModel != null ? "not null" : "null", teamsCount);

            if (_teamsViewModel == null || _teamsViewModel.Teams.Count == 0)
            {
                Serilog.Log.Information("[Teams] LoadTeamsDataAsync 호출 시작");
                await LoadTeamsDataAsync();
                Serilog.Log.Information("[Teams] LoadTeamsDataAsync 호출 완료");
            }
            else
            {
                // 이미 로드된 경우 DataContext만 설정
                Serilog.Log.Information("[Teams] 이미 로드됨, DataContext만 설정 (Teams.Count={Count})", teamsCount);
                TeamsViewBorder.DataContext = _teamsViewModel;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Teams] ShowTeamsView 오류: {ex.Message}");
            Serilog.Log.Error(ex, "[Teams] ShowTeamsView 오류");
            Log4.Error($"[Teams] ShowTeamsView 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// 팀 데이터 로드
    /// </summary>
    private async Task LoadTeamsDataAsync()
    {
        try
        {
            Serilog.Log.Information("[Teams] ========== 팀 데이터 로드 시작 ==========");
            Log4.Info("[Teams] ========== 팀 데이터 로드 시작 ==========");

            if (_teamsViewModel == null)
            {
                _teamsViewModel = ((App)Application.Current).GetService<TeamsViewModel>()!;
                Serilog.Log.Information("[Teams] TeamsViewModel DI로 초기화 완료");
                Log4.Info("[Teams] TeamsViewModel DI로 초기화 완료");
            }

            Serilog.Log.Information("[Teams] LoadTeamsAsync 호출 전...");
            Log4.Info("[Teams] LoadTeamsAsync 호출 전...");
            await _teamsViewModel.LoadTeamsAsync();
            Serilog.Log.Information("[Teams] LoadTeamsAsync 완료 - 팀 {Count}개", _teamsViewModel.Teams.Count);
            Log4.Info($"[Teams] LoadTeamsAsync 완료 - 팀 {_teamsViewModel.Teams.Count}개");

            // DataContext 설정
            TeamsViewBorder.DataContext = _teamsViewModel;
            Serilog.Log.Information("[Teams] TeamsViewBorder.DataContext 설정 완료");
            Log4.Info("[Teams] TeamsViewBorder.DataContext 설정 완료");

            // 팀 목록이 비어있으면 API 문제 가능성
            if (_teamsViewModel.Teams.Count == 0)
            {
                Serilog.Log.Warning("[Teams] ⚠️ 팀 목록이 비어있습니다! Graph API 권한 또는 연결 문제 확인 필요");
                Log4.Info("[Teams] ⚠️ 팀 목록이 비어있습니다! Graph API 권한 또는 연결 문제 확인 필요");
            }
            else
            {
                foreach (var team in _teamsViewModel.Teams)
                {
                    Serilog.Log.Information("[Teams] 로드된 팀: {TeamName} (채널 {ChannelCount}개)", team.DisplayName, team.Channels.Count);
                    Log4.Info($"[Teams] 로드된 팀: {team.DisplayName} (채널 {team.Channels.Count}개)");
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[Teams] 팀 데이터 로드 실패");
            Log4.Error($"[Teams] 팀 데이터 로드 실패: {ex.Message}");
            Log4.Error($"[Teams] StackTrace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// 활동 뷰 표시
    /// </summary>
    private async void ShowActivityView()
    {
        HideAllViews();

        if (ActivityViewBorder != null) ActivityViewBorder.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "활동";
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("activity");

        // 활동 데이터 로드 (최초 1회)
        if (_activityViewModel == null)
        {
            await LoadActivityDataAsync();
        }
    }

    /// <summary>
    /// 플래너 뷰 표시
    /// </summary>
    private async void ShowPlannerView()
    {
        HideAllViews();

        if (PlannerViewBorder != null) PlannerViewBorder.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "플래너";
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("planner");

        // 플랜 목록 로드 (최초 1회)
        if (_plannerViewModel == null)
        {
            await LoadPlannerDataAsync();
        }
    }

    /// <summary>
    /// OneDrive 뷰 표시
    /// </summary>
    private void ShowOneDriveView()
    {
        HideAllViews();

        if (OneDriveViewBorder != null) OneDriveViewBorder.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "OneDrive";
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("onedrive");
    }

    /// <summary>
    /// OneNote 뷰 표시
    /// </summary>
    private void ShowOneNoteView()
    {
        HideAllViews();

        if (OneNoteViewBorder != null) OneNoteViewBorder.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "OneNote";
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("onenote");
    }

    /// <summary>
    /// 통화 뷰 표시
    /// </summary>
    private async void ShowCallsView()
    {
        HideAllViews();

        if (CallsViewBorder != null) CallsViewBorder.Visibility = Visibility.Visible;

        _viewModel.StatusMessage = "통화";
        Services.Theme.ThemeService.Instance.ApplyFeatureTheme("calls");

        // 통화 데이터 로드 (최초 1회)
        if (_callsViewModel == null)
        {
            await LoadCallsDataAsync();
        }
    }

    /// <summary>
    /// 통화 데이터 로드
    /// </summary>
    private async Task LoadCallsDataAsync()
    {
        try
        {
            if (_callsViewModel == null)
            {
                _callsViewModel = ((App)Application.Current).GetService<CallsViewModel>()!;
            }

            await _callsViewModel.InitializeAsync();
            CallsContactsListView.ItemsSource = _callsViewModel.FrequentContacts;
            CallsSearchResultsListView.ItemsSource = _callsViewModel.SearchResults;
            CallsViewBorder.DataContext = _callsViewModel;

            // 빈 상태 표시 업데이트
            UpdateCallsContactsEmptyState();
            UpdateCallsMyStatus();
        }
        catch (Exception ex)
        {
            Log4.Error($"통화 데이터 로드 실패: {ex.Message}");
        }
    }

    private void UpdateCallsContactsEmptyState()
    {
        if (_callsViewModel == null) return;

        if (_callsViewModel.FrequentContacts.Count == 0)
        {
            CallsContactsEmptyState.Visibility = Visibility.Visible;
            CallsContactsListView.Visibility = Visibility.Collapsed;
        }
        else
        {
            CallsContactsEmptyState.Visibility = Visibility.Collapsed;
            CallsContactsListView.Visibility = Visibility.Visible;
        }
    }

    private void UpdateCallsMyStatus()
    {
        if (_callsViewModel == null) return;

        CallsMyStatusText.Text = _callsViewModel.MyAvailability switch
        {
            "Available" => "대화 가능",
            "Busy" => "다른 용무 중",
            "DoNotDisturb" => "방해 금지",
            "Away" => "자리 비움",
            "Offline" => "오프라인",
            _ => "알 수 없음"
        };

        var color = _callsViewModel.MyAvailability switch
        {
            "Available" => "#107C10",
            "Busy" or "DoNotDisturb" => "#D13438",
            "Away" => "#FFAA44",
            "Offline" => "#8A8886",
            _ => "#8A8886"
        };

        CallsMyStatusBrush.Color = (Color)ColorConverter.ConvertFromString(color);
    }

    /// <summary>
    /// REST API로 탭 전환 처리
    /// </summary>
    public void NavigateToTab(string tabName)
    {
        Log4.Info($"[NavigateToTab] 탭 전환 요청: {tabName}");
        var tabLower = tabName.ToLowerInvariant();
        switch (tabLower)
        {
            case "mail":
                NavMailButton.IsChecked = true;
                ShowMailView();
                break;
            case "calendar":
                NavCalendarButton.IsChecked = true;
                ShowCalendarView();
                break;
            case "chat":
                NavChatButton.IsChecked = true;
                ShowChatView();
                break;
            case "teams":
                NavTeamsButton.IsChecked = true;
                ShowTeamsView();
                break;
            case "activity":
                NavActivityButton.IsChecked = true;
                ShowActivityView();
                break;
            case "planner":
                NavPlannerButton.IsChecked = true;
                ShowPlannerView();
                break;
            case "onedrive":
                NavOneDriveButton.IsChecked = true;
                ShowOneDriveView();
                break;
            case "onenote":
                NavOneNoteButton.IsChecked = true;
                ShowOneNoteView();
                break;
            case "calls":
                NavCallsButton.IsChecked = true;
                ShowCallsView();
                break;
            default:
                Log4.Warn($"[NavigateToTab] 알 수 없는 탭: {tabName}");
                break;
        }
    }

    /// <summary>
    /// 캘린더 데이터 비동기 로드
    /// </summary>
    private async void LoadCalendarDataAsync()
    {
        try
        {
            // 현재 월의 일정 로드
            await LoadMonthEventsAsync(_currentCalendarDate);
            UpdateCalendarDisplay();
        }
        catch (Exception ex)
        {
            Log4.Error($"캘린더 데이터 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region 캘린더 뷰 로직

    private DateTime _currentCalendarDate = DateTime.Today;
    private DateTime _selectedCalendarDate = DateTime.Today;
    private List<Microsoft.Graph.Models.Event>? _currentMonthEvents;

    /// <summary>
    /// 이전 월로 이동
    /// </summary>
    private void CalPrevMonthBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarDate = _currentCalendarDate.AddMonths(-1);
        LoadCalendarDataAsync();
    }

    /// <summary>
    /// 다음 월로 이동
    /// </summary>
    private void CalNextMonthBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarDate = _currentCalendarDate.AddMonths(1);
        LoadCalendarDataAsync();
    }

    /// <summary>
    /// 오늘로 이동
    /// </summary>
    private void CalTodayBtn_Click(object sender, RoutedEventArgs e)
    {
        _currentCalendarDate = DateTime.Today;
        LoadCalendarDataAsync();
    }

    /// <summary>
    /// 일간 뷰로 전환
    /// </summary>
    private void CalDayViewBtn_Click(object sender, RoutedEventArgs e)
    {
        CalDayViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        CalWeekViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalMonthViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        // TODO: 일간 뷰 구현
        _viewModel.StatusMessage = "일간 뷰 (구현 예정)";
    }

    /// <summary>
    /// 주간 뷰로 전환
    /// </summary>
    private void CalWeekViewBtn_Click(object sender, RoutedEventArgs e)
    {
        CalDayViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalWeekViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        CalMonthViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        // TODO: 주간 뷰 구현
        _viewModel.StatusMessage = "주간 뷰 (구현 예정)";
    }

    /// <summary>
    /// 월간 뷰로 전환
    /// </summary>
    private void CalMonthViewBtn_Click(object sender, RoutedEventArgs e)
    {
        CalDayViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalWeekViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CalMonthViewBtn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        UpdateCalendarDisplay();
    }

    /// <summary>
    /// 새 일정 버튼 클릭
    /// </summary>
    private async void NewEventButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("새 일정 생성 클릭");
        await OpenEventEditDialogAsync(null, _currentCalendarDate);
    }

    /// <summary>
    /// 일정 편집 다이얼로그 열기
    /// </summary>
    private async Task OpenEventEditDialogAsync(Microsoft.Graph.Models.Event? existingEvent, DateTime? targetDate = null)
    {
        try
        {
            EventEditDialog dialog;
            if (existingEvent != null)
            {
                dialog = new EventEditDialog(existingEvent);
                dialog.Owner = this;
            }
            else if (targetDate.HasValue)
            {
                dialog = new EventEditDialog(targetDate.Value);
                dialog.Owner = this;
            }
            else
            {
                dialog = new EventEditDialog();
                dialog.Owner = this;
            }

            var result = dialog.ShowDialog();
            if (result == true)
            {
                if (dialog.IsDeleted)
                {
                    _viewModel.StatusMessage = "일정이 삭제되었습니다.";
                    Log4.Info("일정 삭제 완료");
                }
                else if (dialog.ResultEvent != null)
                {
                    _viewModel.StatusMessage = existingEvent != null ?
                        "일정이 수정되었습니다." : "새 일정이 생성되었습니다.";
                    Log4.Info($"일정 저장 완료: {dialog.ResultEvent.Subject}");
                }

                // 캘린더 새로고침
                await LoadMonthEventsAsync(_currentCalendarDate);
                UpdateCalendarDisplay();
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"일정 편집 다이얼로그 오류: {ex.Message}");
            _viewModel.StatusMessage = "일정 편집 중 오류가 발생했습니다.";
        }
    }

    /// <summary>
    /// 월별 일정 로드
    /// </summary>
    private async Task LoadMonthEventsAsync(DateTime month)
    {
        try
        {
            var firstDay = new DateTime(month.Year, month.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var calendarService = ((App)Application.Current).GetService<Services.Graph.GraphCalendarService>();
            if (calendarService != null)
            {
                Log4.Info($"캘린더 일정 조회 시작: {firstDay:yyyy-MM-dd} ~ {lastDay.AddDays(1):yyyy-MM-dd}");
                var events = await calendarService.GetEventsAsync(firstDay, lastDay.AddDays(1));
                _currentMonthEvents = events?.ToList();
                _viewModel.CurrentMonthEventCount = _currentMonthEvents?.Count ?? 0;
                Log4.Info($"캘린더 일정 로드 완료: {_currentMonthEvents?.Count ?? 0}건 ({month:yyyy-MM})");
            }
            else
            {
                Log4.Warn("캘린더 서비스가 null입니다.");
                _currentMonthEvents = new List<Microsoft.Graph.Models.Event>();
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"월별 일정 로드 실패: {ex.Message}\n{ex.StackTrace}");
            _currentMonthEvents = new List<Microsoft.Graph.Models.Event>();
        }
    }

    /// <summary>
    /// 월별 일정 로드 (DB에서)
    /// BackgroundSyncService에서 동기화된 캘린더 이벤트를 DB에서 조회
    /// </summary>
    private async Task LoadMonthEventsFromDbAsync(DateTime month)
    {
        try
        {
            var firstDay = new DateTime(month.Year, month.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            Log4.Info($"[DB] 캘린더 일정 조회 시작: {firstDay:yyyy-MM-dd} ~ {lastDay:yyyy-MM-dd}");

            var app = (App)Application.Current;
            using var scope = app.ServiceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<Data.MailXDbContext>();

            // DB에서 해당 월의 캘린더 이벤트 조회 (삭제되지 않은 것만)
            var dbEvents = await dbContext.CalendarEvents
                .Where(e => !e.IsDeleted && !e.IsCancelled)
                .Where(e => e.StartDateTime.Date <= lastDay.Date && e.EndDateTime.Date >= firstDay.Date)
                .OrderBy(e => e.StartDateTime)
                .ThenBy(e => e.Subject)
                .ThenBy(e => e.Id)
                .ToListAsync();

            // CalendarEvent → Microsoft.Graph.Models.Event 변환
            _currentMonthEvents = dbEvents.Select(ConvertToGraphEvent).ToList();
            _viewModel.CurrentMonthEventCount = _currentMonthEvents.Count;

            Log4.Info($"[DB] 캘린더 일정 로드 완료: {_currentMonthEvents.Count}건 ({month:yyyy-MM})");
        }
        catch (Exception ex)
        {
            Log4.Error($"[DB] 월별 일정 로드 실패: {ex.Message}\n{ex.StackTrace}");
            _currentMonthEvents = new List<Microsoft.Graph.Models.Event>();
        }
    }

    /// <summary>
    /// CalendarEvent (DB 모델) → Microsoft.Graph.Models.Event 변환
    /// 기존 UI 코드와의 호환성을 위해 Graph 모델로 변환
    /// </summary>
    private static Microsoft.Graph.Models.Event ConvertToGraphEvent(CalendarEvent dbEvent)
    {
        var graphEvent = new Microsoft.Graph.Models.Event
        {
            Id = dbEvent.GraphId,
            ICalUId = dbEvent.ICalUId,
            Subject = dbEvent.Subject,
            Start = new Microsoft.Graph.Models.DateTimeTimeZone
            {
                DateTime = dbEvent.StartDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = dbEvent.StartTimeZone ?? "Asia/Seoul"
            },
            End = new Microsoft.Graph.Models.DateTimeTimeZone
            {
                DateTime = dbEvent.EndDateTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                TimeZone = dbEvent.EndTimeZone ?? "Asia/Seoul"
            },
            IsAllDay = dbEvent.IsAllDay,
            Location = new Microsoft.Graph.Models.Location
            {
                DisplayName = dbEvent.Location
            },
            IsOnlineMeeting = dbEvent.IsOnlineMeeting,
            OnlineMeetingUrl = dbEvent.OnlineMeetingUrl,
            IsReminderOn = dbEvent.IsReminderOn,
            ReminderMinutesBeforeStart = dbEvent.ReminderMinutesBeforeStart,
            WebLink = dbEvent.WebLink
        };

        // 본문 설정
        if (!string.IsNullOrEmpty(dbEvent.Body))
        {
            graphEvent.Body = new Microsoft.Graph.Models.ItemBody
            {
                Content = dbEvent.Body,
                ContentType = dbEvent.BodyContentType == "html"
                    ? Microsoft.Graph.Models.BodyType.Html
                    : Microsoft.Graph.Models.BodyType.Text
            };
        }

        // 주최자 설정
        if (!string.IsNullOrEmpty(dbEvent.OrganizerEmail))
        {
            graphEvent.Organizer = new Microsoft.Graph.Models.Recipient
            {
                EmailAddress = new Microsoft.Graph.Models.EmailAddress
                {
                    Address = dbEvent.OrganizerEmail,
                    Name = dbEvent.OrganizerName
                }
            };
        }

        // 중요도 설정
        if (!string.IsNullOrEmpty(dbEvent.Importance))
        {
            graphEvent.Importance = dbEvent.Importance.ToLower() switch
            {
                "low" => Microsoft.Graph.Models.Importance.Low,
                "high" => Microsoft.Graph.Models.Importance.High,
                _ => Microsoft.Graph.Models.Importance.Normal
            };
        }

        // 상태 표시 설정
        if (!string.IsNullOrEmpty(dbEvent.ShowAs))
        {
            graphEvent.ShowAs = dbEvent.ShowAs.ToLower() switch
            {
                "free" => Microsoft.Graph.Models.FreeBusyStatus.Free,
                "tentative" => Microsoft.Graph.Models.FreeBusyStatus.Tentative,
                "busy" => Microsoft.Graph.Models.FreeBusyStatus.Busy,
                "oof" => Microsoft.Graph.Models.FreeBusyStatus.Oof,
                "workingelsewhere" => Microsoft.Graph.Models.FreeBusyStatus.WorkingElsewhere,
                _ => Microsoft.Graph.Models.FreeBusyStatus.Unknown
            };
        }

        // 카테고리 설정
        if (!string.IsNullOrEmpty(dbEvent.Categories))
        {
            try
            {
                var categories = System.Text.Json.JsonSerializer.Deserialize<List<string>>(dbEvent.Categories);
                if (categories != null)
                {
                    graphEvent.Categories = categories;
                }
            }
            catch { /* JSON 파싱 실패 무시 */ }
        }

        return graphEvent;
    }

    /// <summary>
    /// 캘린더 표시 업데이트
    /// </summary>
    private void UpdateCalendarDisplay()
    {
        // 월/년 텍스트 업데이트
        var monthYearText = $"{_currentCalendarDate.Year}년 {_currentCalendarDate.Month}월";
        if (CalMonthYearText != null) CalMonthYearText.Text = monthYearText;
        if (CalMainMonthYearText != null) CalMainMonthYearText.Text = monthYearText;

        // 월간 캘린더 그리드 업데이트
        UpdateMonthCalendarGrid();

        // 미니 캘린더 업데이트
        UpdateMiniCalendarGrid();

        // 오늘 날짜 또는 선택된 날짜의 일정으로 세부 패널 초기화
        var targetDate = _selectedCalendarDate.Month == _currentCalendarDate.Month &&
                         _selectedCalendarDate.Year == _currentCalendarDate.Year
            ? _selectedCalendarDate
            : DateTime.Today;

        var dayEvents = _currentMonthEvents?
            .Where(e => e.Start?.DateTime != null &&
                        GetLocalStartTime(e).Date == targetDate.Date)
            .OrderBy(e => GetLocalStartTime(e))
            .ThenBy(e => e.Subject)
            .ThenBy(e => e.Id)
            .ToList() ?? new List<Microsoft.Graph.Models.Event>();

        UpdateSelectedDateEventsPanel(targetDate, dayEvents);
        UpdateCalendarDetailPanel(targetDate, dayEvents);
    }

    /// <summary>
    /// 월간 캘린더 그리드 동적 생성
    /// </summary>
    private void UpdateMonthCalendarGrid()
    {
        if (MonthCalendarGrid == null) return;

        // 기존 날짜 셀 제거 (요일 헤더 제외)
        var toRemove = MonthCalendarGrid.Children.Cast<UIElement>()
            .Where(c => Grid.GetRow(c) > 0)
            .ToList();
        foreach (var child in toRemove)
            MonthCalendarGrid.Children.Remove(child);

        var firstDay = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_currentCalendarDate.Year, _currentCalendarDate.Month);
        var startDayOfWeek = (int)firstDay.DayOfWeek;

        var day = 1;
        for (int week = 0; week < 6 && day <= daysInMonth; week++)
        {
            for (int dayOfWeek = 0; dayOfWeek < 7 && day <= daysInMonth; dayOfWeek++)
            {
                if (week == 0 && dayOfWeek < startDayOfWeek)
                    continue;

                var cellDate = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, day);
                var cell = CreateDayCell(cellDate);
                Grid.SetRow(cell, week + 1);
                Grid.SetColumn(cell, dayOfWeek);
                MonthCalendarGrid.Children.Add(cell);

                day++;
            }
        }
    }

    /// <summary>
    /// 날짜 셀 생성
    /// </summary>
    private Border CreateDayCell(DateTime date)
    {
        var isToday = date.Date == DateTime.Today;
        var dayEvents = _currentMonthEvents?
            .Where(e => e.Start?.DateTime != null &&
                        GetLocalStartTime(e).Date == date.Date)
            .OrderBy(e => GetLocalStartTime(e))
            .ThenBy(e => e.Subject)
            .ThenBy(e => e.Id)
            .ToList() ?? new List<Microsoft.Graph.Models.Event>();

        var cell = new Border
        {
            BorderBrush = (Brush)FindResource("ControlElevationBorderBrush"),
            BorderThickness = new Thickness(0.5),
            Margin = new Thickness(1),
            Background = isToday ?
                (Brush)FindResource("SystemAccentColorSecondaryBrush") :
                Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel { Margin = new Thickness(4) };

        // 날짜 숫자
        var dayText = new System.Windows.Controls.TextBlock
        {
            Text = date.Day.ToString(),
            FontSize = 12,
            FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
            Foreground = date.DayOfWeek == DayOfWeek.Sunday ? new SolidColorBrush(Color.FromRgb(255, 107, 107)) :
                         date.DayOfWeek == DayOfWeek.Saturday ? new SolidColorBrush(Color.FromRgb(107, 157, 255)) :
                         (Brush)FindResource("TextFillColorPrimaryBrush"),
            Margin = new Thickness(2, 0, 0, 4)
        };
        stack.Children.Add(dayText);

        // 일정 표시 (최대 3개)
        var displayEvents = dayEvents.Take(3);
        foreach (var evt in displayEvents)
        {
            var capturedEvent = evt; // 람다에서 사용할 변수 캡처
            var eventBorder = new Border
            {
                Background = GetEventColor(evt),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Tag = evt // 이벤트 객체 저장
            };
            var eventText = new System.Windows.Controls.TextBlock
            {
                Text = evt.Subject ?? "(제목 없음)",
                FontSize = 10,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            eventBorder.Child = eventText;

            // 일정 클릭 시 편집 다이얼로그 열기
            eventBorder.MouseLeftButtonDown += async (s, args) =>
            {
                args.Handled = true; // 날짜 셀 클릭 이벤트 전파 방지
                Log4.Info($"일정 클릭: {capturedEvent.Subject}");
                await OpenEventEditDialogAsync(capturedEvent, null);
            };

            stack.Children.Add(eventBorder);
        }

        // 더 많은 일정이 있으면 표시
        if (dayEvents.Count > 3)
        {
            var moreText = new System.Windows.Controls.TextBlock
            {
                Text = $"+{dayEvents.Count - 3}개 더",
                FontSize = 9,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                Margin = new Thickness(2, 2, 0, 0)
            };
            stack.Children.Add(moreText);
        }

        cell.Child = stack;

        // 클릭 이벤트: 단일 클릭=날짜 선택, 더블 클릭=새 일정 생성
        cell.MouseLeftButtonDown += async (s, e) =>
        {
            if (e.ClickCount == 2)
            {
                Log4.Info($"날짜 더블클릭: {date:yyyy-MM-dd} - 새 일정 생성");
                await OpenEventEditDialogAsync(null, date);
            }
            else if (e.ClickCount == 1)
            {
                Log4.Info($"날짜 클릭: {date:yyyy-MM-dd}");
                _selectedCalendarDate = date;
                _viewModel.StatusMessage = $"{date:yyyy년 M월 d일} 선택됨 ({dayEvents.Count}건 일정)";
                UpdateSelectedDateEventsPanel(date, dayEvents);
                UpdateCalendarDetailPanel(date, dayEvents);
            }
        };

        return cell;
    }

    /// <summary>
    /// 일정 색상 결정
    /// </summary>
    private Brush GetEventColor(Microsoft.Graph.Models.Event evt)
    {
        // 카테고리 기반 색상
        var categories = evt.Categories?.FirstOrDefault();
        return categories switch
        {
            "이메일 마감" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
            "할일" => new SolidColorBrush(Color.FromRgb(52, 152, 219)),
            _ => new SolidColorBrush(Color.FromRgb(46, 204, 113))
        };
    }

    /// <summary>
    /// 선택된 날짜의 일정 목록 패널 업데이트 (좌측 패널)
    /// </summary>
    private void UpdateSelectedDateEventsPanel(DateTime date, List<Microsoft.Graph.Models.Event> events)
    {
        if (SelectedDateEventsPanel == null) return;

        // 헤더 텍스트 업데이트
        if (SelectedDateText != null)
        {
            var dateText = date.Date == DateTime.Today ? "오늘의 일정" : $"{date:M월 d일} 일정";
            SelectedDateText.Text = $"{dateText} ({events.Count}건)";
        }

        // 기존 일정 아이템 제거 (NoEventsText 제외)
        var itemsToRemove = SelectedDateEventsPanel.Children.Cast<UIElement>()
            .Where(c => c != NoEventsText)
            .ToList();
        foreach (var item in itemsToRemove)
            SelectedDateEventsPanel.Children.Remove(item);

        // 일정 없음 표시
        if (NoEventsText != null)
            NoEventsText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 일정 아이템 추가 (로컬 시간 기준 정렬)
        foreach (var evt in events.OrderBy(e => GetLocalStartTime(e)).ThenBy(e => e.Subject).ThenBy(e => e.Id))
        {
            var capturedEvent = evt;
            var eventCard = CreateEventCard(evt);
            eventCard.MouseLeftButtonDown += async (s, e) =>
            {
                e.Handled = true;
                await OpenEventEditDialogAsync(capturedEvent, null);
            };
            SelectedDateEventsPanel.Children.Add(eventCard);
        }
    }

    /// <summary>
    /// 일정 카드 UI 생성 (좌측 패널용)
    /// </summary>
    private Border CreateEventCard(Microsoft.Graph.Models.Event evt)
    {
        var card = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = GetEventColor(evt),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand
        };

        var stack = new StackPanel();

        // 시간 (Graph API 시간대 → 로컬 시간 변환)
        string timeText = "";
        if (evt.IsAllDay ?? false)
        {
            timeText = "종일";
        }
        else if (evt.Start?.DateTime != null)
        {
            var startTime = ConvertGraphTimeToLocal(DateTime.Parse(evt.Start.DateTime), evt.Start.TimeZone);
            timeText = startTime.ToString("HH:mm");
            if (evt.End?.DateTime != null)
            {
                var endTime = ConvertGraphTimeToLocal(DateTime.Parse(evt.End.DateTime), evt.End.TimeZone);
                timeText += $" - {endTime:HH:mm}";
            }
        }

        var timeBlock = new System.Windows.Controls.TextBlock
        {
            Text = timeText,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
        };
        stack.Children.Add(timeBlock);

        // 제목
        var titleBlock = new System.Windows.Controls.TextBlock
        {
            Text = evt.Subject ?? "(제목 없음)",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
        };
        stack.Children.Add(titleBlock);

        // 장소 (있는 경우)
        if (!string.IsNullOrEmpty(evt.Location?.DisplayName))
        {
            var locationBlock = new System.Windows.Controls.TextBlock
            {
                Text = $"📍 {evt.Location.DisplayName}",
                FontSize = 10,
                Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(locationBlock);
        }

        card.Child = stack;

        // 호버 효과
        card.MouseEnter += (s, e) =>
        {
            card.Background = (Brush)FindResource("SubtleFillColorSecondaryBrush");
        };
        card.MouseLeave += (s, e) =>
        {
            card.Background = Brushes.Transparent;
        };

        return card;
    }

    /// <summary>
    /// 캘린더 세부 패널(우측) 업데이트
    /// </summary>
    private void UpdateCalendarDetailPanel(DateTime date, List<Microsoft.Graph.Models.Event> events)
    {
        if (CalendarDetailPanel == null) return;

        // 날짜 정보 업데이트
        if (CalDetailDateText != null)
            CalDetailDateText.Text = $"{date:yyyy년 M월 d일}";
        if (CalDetailDayText != null)
            CalDetailDayText.Text = date.ToString("dddd", new System.Globalization.CultureInfo("ko-KR"));

        // 일정 개수 업데이트
        if (CalDetailEventCountText != null)
            CalDetailEventCountText.Text = $"일정 ({events.Count}건)";

        // 기존 일정 아이템 제거
        if (CalDetailEventsList != null)
        {
            var itemsToRemove = CalDetailEventsList.Children.Cast<UIElement>()
                .Where(c => c != CalDetailNoEventsText)
                .ToList();
            foreach (var item in itemsToRemove)
                CalDetailEventsList.Children.Remove(item);

            // 일정 없음 텍스트 표시
            if (CalDetailNoEventsText != null)
                CalDetailNoEventsText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // 일정 카드 추가 (로컬 시간 기준 정렬)
            foreach (var evt in events.OrderBy(e => GetLocalStartTime(e)).ThenBy(e => e.Subject).ThenBy(e => e.Id))
            {
                var capturedEvent = evt;
                var eventCard = CreateDetailEventCard(evt);
                eventCard.MouseLeftButtonDown += async (s, e) =>
                {
                    e.Handled = true;
                    await OpenEventEditDialogAsync(capturedEvent, null);
                };
                CalDetailEventsList.Children.Add(eventCard);
            }
        }

        // 월간 요약 업데이트
        UpdateCalendarMonthlySummary();
    }

    /// <summary>
    /// 세부 일정 카드 생성 (우측 패널용)
    /// </summary>
    private Border CreateDetailEventCard(Microsoft.Graph.Models.Event evt)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("SubtleFillColorSecondaryBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = Cursors.Hand
        };

        var mainStack = new StackPanel();

        // 상단: 시간 + 아이콘
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 시간 (Graph API 시간대 → 로컬 시간 변환)
        string timeText = "";
        if (evt.IsAllDay ?? false)
        {
            timeText = "종일";
        }
        else if (evt.Start?.DateTime != null)
        {
            var startTime = ConvertGraphTimeToLocal(DateTime.Parse(evt.Start.DateTime), evt.Start.TimeZone);
            timeText = startTime.ToString("HH:mm");
            if (evt.End?.DateTime != null)
            {
                var endTime = ConvertGraphTimeToLocal(DateTime.Parse(evt.End.DateTime), evt.End.TimeZone);
                timeText += $" - {endTime:HH:mm}";
            }
        }

        var timeBlock = new System.Windows.Controls.TextBlock
        {
            Text = timeText,
            FontSize = 11,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
        };
        Grid.SetColumn(timeBlock, 0);
        headerGrid.Children.Add(timeBlock);

        // Teams 회의 아이콘
        if (evt.IsOnlineMeeting ?? false)
        {
            var teamsIcon = new System.Windows.Controls.TextBlock
            {
                Text = "🔗",
                FontSize = 14,
                ToolTip = "Teams 온라인 회의"
            };
            Grid.SetColumn(teamsIcon, 1);
            headerGrid.Children.Add(teamsIcon);
        }

        mainStack.Children.Add(headerGrid);

        // 제목
        var titleBlock = new System.Windows.Controls.TextBlock
        {
            Text = evt.Subject ?? "(제목 없음)",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush")
        };
        mainStack.Children.Add(titleBlock);

        // 장소
        if (!string.IsNullOrEmpty(evt.Location?.DisplayName))
        {
            var locationStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            locationStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "📍",
                FontSize = 12,
                Margin = new Thickness(0, 0, 4, 0)
            });
            locationStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = evt.Location.DisplayName,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush")
            });
            mainStack.Children.Add(locationStack);
        }

        // 참석자
        if (evt.Attendees != null && evt.Attendees.Any())
        {
            var attendeeStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            attendeeStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "👥",
                FontSize = 12,
                Margin = new Thickness(0, 0, 4, 0)
            });
            var attendeeNames = evt.Attendees
                .Where(a => a.EmailAddress?.Name != null)
                .Take(3)
                .Select(a => a.EmailAddress!.Name);
            var attendeeText = string.Join(", ", attendeeNames);
            if (evt.Attendees.Count() > 3)
                attendeeText += $" 외 {evt.Attendees.Count() - 3}명";

            attendeeStack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = attendeeText,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            mainStack.Children.Add(attendeeStack);
        }

        // 색상 표시 막대
        card.BorderBrush = GetEventColor(evt);
        card.BorderThickness = new Thickness(3, 0, 0, 0);

        card.Child = mainStack;

        // 호버 효과
        card.MouseEnter += (s, e) =>
        {
            card.Background = (Brush)FindResource("SubtleFillColorTertiaryBrush");
        };
        card.MouseLeave += (s, e) =>
        {
            card.Background = (Brush)FindResource("SubtleFillColorSecondaryBrush");
        };

        return card;
    }

    /// <summary>
    /// 월간 요약 업데이트
    /// </summary>
    private void UpdateCalendarMonthlySummary()
    {
        if (_currentMonthEvents == null) return;

        // 총 일정
        if (CalDetailMonthTotalText != null)
            CalDetailMonthTotalText.Text = $"{_currentMonthEvents.Count}건";

        // 이번 주 일정
        var startOfWeek = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);
        var weekEvents = _currentMonthEvents.Count(e =>
        {
            if (e.Start?.DateTime == null) return false;
            var eventDate = DateTime.Parse(e.Start.DateTime).Date;
            return eventDate >= startOfWeek && eventDate < endOfWeek;
        });

        if (CalDetailWeekTotalText != null)
            CalDetailWeekTotalText.Text = $"{weekEvents}건";
    }

    /// <summary>
    /// 캘린더 세부 패널의 일정 추가 버튼 클릭
    /// </summary>
    private async void CalDetailAddEventButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenEventEditDialogAsync(null, _selectedCalendarDate);
    }

    /// <summary>
    /// TODO 추가 버튼 클릭 (캘린더 패널 To Do 탭)
    /// </summary>
    private void AddTodoButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Graph API Tasks 연동하여 할 일 추가 다이얼로그 열기
        Log4.Info("TODO 추가 버튼 클릭");
        _viewModel.StatusMessage = "할 일 추가 기능은 추후 지원 예정입니다.";
    }

    /// <summary>
    /// 미니 캘린더 그리드 업데이트
    /// </summary>
    private void UpdateMiniCalendarGrid()
    {
        if (MiniCalendarGrid == null) return;

        // 기존 날짜 버튼 제거 (요일 헤더 제외)
        var toRemove = MiniCalendarGrid.Children.Cast<UIElement>()
            .Where(c => Grid.GetRow(c) > 0)
            .ToList();
        foreach (var child in toRemove)
            MiniCalendarGrid.Children.Remove(child);

        var firstDay = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(_currentCalendarDate.Year, _currentCalendarDate.Month);
        var startDayOfWeek = (int)firstDay.DayOfWeek;

        var day = 1;
        for (int week = 0; week < 6 && day <= daysInMonth; week++)
        {
            for (int dayOfWeek = 0; dayOfWeek < 7 && day <= daysInMonth; dayOfWeek++)
            {
                if (week == 0 && dayOfWeek < startDayOfWeek)
                    continue;

                var date = new DateTime(_currentCalendarDate.Year, _currentCalendarDate.Month, day);
                var isToday = date.Date == DateTime.Today;

                var btn = new System.Windows.Controls.Button
                {
                    Content = day.ToString(),
                    MinWidth = 28,
                    Height = 28,
                    FontSize = 11,
                    Padding = new Thickness(2, 0, 2, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Background = isToday ?
                        (Brush)FindResource("SystemAccentColorSecondaryBrush") :
                        Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Tag = date
                };
                btn.Click += MiniCalendarDay_Click;

                Grid.SetRow(btn, week + 1);
                Grid.SetColumn(btn, dayOfWeek);
                MiniCalendarGrid.Children.Add(btn);

                day++;
            }
        }
    }

    /// <summary>
    /// 미니 캘린더 날짜 클릭
    /// </summary>
    private void MiniCalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is DateTime date)
        {
            _currentCalendarDate = date;
            UpdateCalendarDisplay();
            Log4.Info($"미니 캘린더 날짜 선택: {date:yyyy-MM-dd}");
        }
    }

    #endregion

    #region 새로운 타이틀바 기능 (테마 토글, 고급 검색, 재로그인)

    /// <summary>
    /// 테마 토글 버튼 클릭
    /// </summary>
    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 테마 토글");
        var themeService = Services.Theme.ThemeService.Instance;
        themeService.ToggleTheme();
        UpdateThemeIcon();
        SyncSettingsUIFromMenu(); // 설정 UI 동기화
    }

    /// <summary>
    /// 테마 아이콘 업데이트
    /// </summary>
    private void UpdateThemeIcon()
    {
        var themeService = Services.Theme.ThemeService.Instance;
        // 다크모드일 때 해(Sun) 아이콘 = "라이트모드로 전환"
        // 라이트모드일 때 달(Moon) 아이콘 = "다크모드로 전환"
        ThemeIcon.Symbol = themeService.IsDarkMode
            ? Wpf.Ui.Controls.SymbolRegular.WeatherSunny24
            : Wpf.Ui.Controls.SymbolRegular.WeatherMoon24;

        // AI 분석 별 색상 업데이트 (라이트모드: 진한 주황, 다크모드: 밝은 골드)
        UpdateAISyncStarColors(themeService.IsDarkMode);

        // 테마 메뉴 하이라이팅 업데이트
        UpdateThemeMenuHighlight(themeService.IsDarkMode);
    }

    /// <summary>
    /// 테마 메뉴 하이라이팅 업데이트
    /// </summary>
    private void UpdateThemeMenuHighlight(bool isDarkMode)
    {
        var highlightColor = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3"));
        var normalBrush = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush");

        if (MenuThemeDarkIcon != null && MenuThemeDarkText != null)
        {
            MenuThemeDarkIcon.Foreground = isDarkMode ? highlightColor : normalBrush;
            MenuThemeDarkText.Foreground = isDarkMode ? highlightColor : normalBrush;
        }

        if (MenuThemeLightIcon != null && MenuThemeLightText != null)
        {
            MenuThemeLightIcon.Foreground = isDarkMode ? normalBrush : highlightColor;
            MenuThemeLightText.Foreground = isDarkMode ? normalBrush : highlightColor;
        }
    }

    /// <summary>
    /// AI 분석 별 색상 및 메뉴 아이콘 색상 업데이트 (테마에 따라)
    /// </summary>
    private void UpdateAISyncStarColors(bool isDarkMode)
    {
        if (isDarkMode)
        {
            // 다크모드: 밝은 골드/노랑 계열
            AISyncStar1.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFD700"));
            AISyncStar2.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFDF00"));
            AISyncStar3.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFC125"));

            // AI 메뉴 아이콘 색상 (다크모드)
            MenuAISyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFD700")); // 분석 중지: 노랑
            MenuAISyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888888")); // 분석 시작: 회색

            // 메일 동기화 메뉴 아이콘 색상 (다크모드)
            MenuMailSyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2196F3")); // 동기화 중지: 파랑
            MenuMailSyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#888888")); // 동기화 시작: 회색
        }
        else
        {
            // 라이트모드: 진한 주황/갈색 계열 (더 잘 보임)
            AISyncStar1.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E69500"));
            AISyncStar2.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D98C00"));
            AISyncStar3.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CC7A00"));

            // AI 메뉴 아이콘 색상 (라이트모드: 진한 색상)
            MenuAISyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E69500")); // 분석 중지: 진한 주황
            MenuAISyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555555")); // 분석 시작: 진한 회색

            // 메일 동기화 메뉴 아이콘 색상 (라이트모드: 진한 색상)
            MenuMailSyncPauseIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1565C0")); // 동기화 중지: 진한 파랑
            MenuMailSyncResumeIcon.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555555")); // 동기화 시작: 진한 회색
        }
    }

    /// <summary>
    /// 고급 검색 버튼 클릭
    /// </summary>
    private void AdvancedSearchButton_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("타이틀바: 고급 검색 토글");
        AdvancedSearchPopup.IsOpen = !AdvancedSearchPopup.IsOpen;
    }

    /// <summary>
    /// 고급 검색 실행
    /// </summary>
    private void AdvancedSearch_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("고급 검색 실행");
        AdvancedSearchPopup.IsOpen = false;
        _viewModel.ExecuteAdvancedSearchCommand.Execute(null);
    }

    /// <summary>
    /// 고급 검색 초기화
    /// </summary>
    private void AdvancedSearchClear_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("고급 검색 초기화");
        _viewModel.ClearAdvancedSearchCommand.Execute(null);
    }

    /// <summary>
    /// DatePicker 로드 시 날짜 형식 설정 (yyyy년 MM월 dd일)
    /// </summary>
    private void DatePicker_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker datePicker)
        {
            // DatePicker 내부의 TextBox를 찾아서 형식 적용
            datePicker.SelectedDateChanged += (s, args) =>
            {
                ApplyDateFormat(datePicker);
            };
            ApplyDateFormat(datePicker);
        }
    }

    /// <summary>
    /// DatePicker에 커스텀 날짜 형식 적용
    /// </summary>
    private void ApplyDateFormat(DatePicker datePicker)
    {
        if (datePicker.SelectedDate.HasValue)
        {
            var textBox = FindChild<System.Windows.Controls.Primitives.DatePickerTextBox>(datePicker);
            if (textBox != null)
            {
                textBox.Text = datePicker.SelectedDate.Value.ToString("yyyy년 MM월 dd일");
            }
        }
    }

    /// <summary>
    /// 시각적 트리에서 자식 요소 찾기
    /// </summary>
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;
            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// 재로그인 메뉴 클릭
    /// </summary>
    private void MenuRelogin_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 재로그인");
        // 로그아웃 후 다시 로그인
        MenuLogout_Click(sender, e);
    }

    /// <summary>
    /// 종료 메뉴 클릭
    /// </summary>
    private void MenuExit_Click(object sender, RoutedEventArgs e)
    {
        Log4.Info("메뉴: 종료");
        Application.Current.Shutdown();
    }

    #endregion

    #region 최근 검색어 관리

    /// <summary>
    /// 최근 검색어 파일 경로
    /// </summary>
    private static string RecentSearchesFilePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mailX", "recent_searches.json");

    /// <summary>
    /// 최근 검색어 로드 (JSON 파일에서)
    /// </summary>
    private void LoadRecentSearches()
    {
        try
        {
            if (System.IO.File.Exists(RecentSearchesFilePath))
            {
                var json = System.IO.File.ReadAllText(RecentSearchesFilePath);
                var searches = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (searches != null)
                {
                    _recentSearches.Clear();
                    foreach (var search in searches.Take(MaxRecentSearches))
                    {
                        _recentSearches.Add(search);
                    }
                }
            }
            Log4.Debug($"최근 검색어 로드: {_recentSearches.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"최근 검색어 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 최근 검색어 저장
    /// </summary>
    private void SaveRecentSearches()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(RecentSearchesFilePath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            var json = System.Text.Json.JsonSerializer.Serialize(_recentSearches.ToList());
            System.IO.File.WriteAllText(RecentSearchesFilePath, json);
            Log4.Debug($"최근 검색어 저장: {_recentSearches.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"최근 검색어 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 검색어 추가 (중복 제거, 최신 항목 맨 위)
    /// </summary>
    private void AddRecentSearch(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return;

        var trimmed = searchText.Trim();

        // 중복 제거
        if (_recentSearches.Contains(trimmed))
        {
            _recentSearches.Remove(trimmed);
        }

        // 맨 앞에 추가
        _recentSearches.Insert(0, trimmed);

        // 최대 개수 초과 시 뒤에서 제거
        while (_recentSearches.Count > MaxRecentSearches)
        {
            _recentSearches.RemoveAt(_recentSearches.Count - 1);
        }

        SaveRecentSearches();
    }

    /// <summary>
    /// 최근 검색어 항목 클릭
    /// </summary>
    private void RecentSearchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string searchText)
        {
            TitleBarSearchBox.Text = searchText;
            _viewModel.SearchKeyword = searchText;
            SearchAutocompletePopup.IsOpen = false;
            _viewModel.SearchCommand.Execute(null);
            AddRecentSearch(searchText);
        }
    }

    /// <summary>
    /// 최근 검색어 삭제 버튼 클릭
    /// </summary>
    private void RemoveRecentSearch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string searchText)
        {
            _recentSearches.Remove(searchText);
            SaveRecentSearches();
            Log4.Debug($"검색어 삭제: {searchText}");
            e.Handled = true; // 부모 Border의 클릭 이벤트 전파 방지
        }
    }

    #endregion

    #region 시간대 변환 헬퍼

    /// <summary>
    /// Graph API 시간을 로컬 시간으로 변환
    /// Graph API는 지정된 TimeZone의 로컬 시간을 반환하므로,
    /// 해당 TimeZone에서 시스템 로컬 시간으로 변환 필요
    /// </summary>
    /// <param name="dateTime">Graph API에서 파싱한 DateTime</param>
    /// <param name="timeZoneId">Graph API의 TimeZone ID (예: "Korea Standard Time", "UTC")</param>
    /// <returns>시스템 로컬 시간</returns>
    private DateTime ConvertGraphTimeToLocal(DateTime dateTime, string? timeZoneId)
    {
        try
        {
            // TimeZone이 없으면 UTC로 가정
            if (string.IsNullOrEmpty(timeZoneId))
            {
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
                // Windows 시간대 ID가 아닌 경우 - UTC로 폴백
                var utcTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                return utcTime.ToLocalTime();
            }

            // 소스 시간대의 시간을 UTC로 변환 후 로컬로 변환
            var sourceTime = DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
            var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(sourceTime, sourceTimeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, TimeZoneInfo.Local);
        }
        catch
        {
            return dateTime; // 변환 실패 시 원본 반환
        }
    }

    /// <summary>
    /// Graph API Event의 로컬 시작 시간 가져오기 (정렬용)
    /// </summary>
    private DateTime GetLocalStartTime(Microsoft.Graph.Models.Event evt)
    {
        if (evt.Start?.DateTime == null)
            return DateTime.MaxValue;

        if (!DateTime.TryParse(evt.Start.DateTime, out var parsedTime))
            return DateTime.MaxValue;

        return ConvertGraphTimeToLocal(parsedTime, evt.Start.TimeZone);
    }

    #endregion

    #region 채팅 이벤트 핸들러

    private TeamsViewModel? _teamsViewModel;

    /// <summary>
    /// 채팅 검색 토글 버튼 클릭
    /// </summary>
    private void ChatSearchToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChatSearchPanel != null)
        {
            ChatSearchPanel.Visibility = ChatSearchPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (ChatSearchPanel.Visibility == Visibility.Visible && ChatSearchBox != null)
            {
                ChatSearchBox.Focus();
            }
        }
    }

    /// <summary>
    /// 채팅 검색 키 입력
    /// </summary>
    private void ChatSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ChatSearchBox != null)
        {
            var query = ChatSearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                Log4.Debug($"채팅 검색: {query}");
                // TODO: 검색 실행
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (ChatSearchPanel != null)
                ChatSearchPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 채팅 목록 선택 변경
    /// </summary>
    private async void ChatListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is ChatItemViewModel selectedChat)
        {
            Log4.Debug($"채팅 선택: {selectedChat.DisplayName} (from: {listBox.Name})");

            // 다른 ListBox의 선택 해제 (무한 루프 방지를 위해 이벤트 핸들러 임시 해제)
            if (listBox == ChatListBox && ChatFavoritesListBox?.SelectedItem != null)
            {
                ChatFavoritesListBox.SelectionChanged -= ChatListBox_SelectionChanged;
                ChatFavoritesListBox.SelectedItem = null;
                ChatFavoritesListBox.SelectionChanged += ChatListBox_SelectionChanged;
            }
            else if (listBox == ChatFavoritesListBox && ChatListBox?.SelectedItem != null)
            {
                ChatListBox.SelectionChanged -= ChatListBox_SelectionChanged;
                ChatListBox.SelectedItem = null;
                ChatListBox.SelectionChanged += ChatListBox_SelectionChanged;
            }

            // 빈 상태 패널 숨기고 콘텐츠 패널 표시
            if (ChatEmptyStatePanel != null)
                ChatEmptyStatePanel.Visibility = Visibility.Collapsed;
            if (ChatContentPanel != null)
                ChatContentPanel.Visibility = Visibility.Visible;

            // 헤더 업데이트
            if (ChatHeaderTitle != null)
                ChatHeaderTitle.Text = selectedChat.DisplayName;
            if (ChatHeaderAvatar != null)
                ChatHeaderAvatar.Text = !string.IsNullOrEmpty(selectedChat.DisplayName)
                    ? selectedChat.DisplayName.Substring(0, 1).ToUpper()
                    : "?";

            // 메시지 로드
            await LoadChatMessagesAsync(selectedChat.Id);
        }
    }

    /// <summary>
    /// 채팅 메시지 로드
    /// </summary>
    private async Task LoadChatMessagesAsync(string chatId)
    {
        if (_teamsViewModel == null || string.IsNullOrEmpty(chatId))
            return;

        try
        {
            if (ChatMessagesLoadingOverlay != null)
                ChatMessagesLoadingOverlay.Visibility = Visibility.Visible;

            await _teamsViewModel.LoadMessagesAsync(chatId);

            if (ChatMessagesItemsControl != null)
                ChatMessagesItemsControl.ItemsSource = _teamsViewModel.Messages;

            // 스크롤 맨 아래로
            if (ChatMessagesScrollViewer != null)
                ChatMessagesScrollViewer.ScrollToEnd();
        }
        catch (Exception ex)
        {
            Log4.Error($"채팅 메시지 로드 실패: {ex.Message}");
        }
        finally
        {
            if (ChatMessagesLoadingOverlay != null)
                ChatMessagesLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 채팅 새로고침 버튼 클릭
    /// </summary>
    private async void ChatRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadChatsAsync();
    }

    /// <summary>
    /// 채팅 즐겨찾기 추가
    /// </summary>
    private async void ChatFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (ChatListBox?.SelectedItem is ChatItemViewModel chat && _teamsViewModel != null)
        {
            await _teamsViewModel.ToggleFavoriteAsync(chat);
        }
    }

    /// <summary>
    /// 채팅 즐겨찾기 해제
    /// </summary>
    private async void ChatUnfavorite_Click(object sender, RoutedEventArgs e)
    {
        if (ChatFavoritesListBox?.SelectedItem is ChatItemViewModel chat && _teamsViewModel != null)
        {
            await _teamsViewModel.ToggleFavoriteAsync(chat);
        }
    }

    #region 채팅 즐겨찾기 드래그 앤 드롭

    private ChatItemViewModel? _draggedChatItem;
    private bool _isChatDragging;

    /// <summary>
    /// 드래그 시작점 기록
    /// </summary>
    private void ChatFavorites_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isChatDragging = false;

        // 드래그 대상 아이템 찾기
        var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (listBoxItem != null)
        {
            _draggedChatItem = listBoxItem.DataContext as ChatItemViewModel;
        }
        else
        {
            _draggedChatItem = null;
        }
    }

    /// <summary>
    /// 마우스 이동 시 드래그 시작
    /// </summary>
    private void ChatFavorites_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedChatItem == null)
            return;

        Point currentPosition = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPosition;

        // 최소 드래그 거리 확인 (실수로 드래그 시작하는 것 방지)
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isChatDragging = true;

            // 드래그 데이터 설정
            var data = new DataObject(typeof(ChatItemViewModel), _draggedChatItem);
            DragDrop.DoDragDrop(ChatFavoritesListBox, data, DragDropEffects.Move);
        }
    }

    /// <summary>
    /// 드래그 오버 시 드롭 허용 표시
    /// </summary>
    private void ChatFavorites_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ChatItemViewModel)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    /// <summary>
    /// 드롭 시 위치 변경
    /// </summary>
    private async void ChatFavorites_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ChatItemViewModel)))
            return;

        var droppedItem = e.Data.GetData(typeof(ChatItemViewModel)) as ChatItemViewModel;
        if (droppedItem == null || _teamsViewModel == null)
            return;

        // 드롭 위치의 아이템 찾기
        var targetListBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        ChatItemViewModel? targetItem = null;

        if (targetListBoxItem != null)
        {
            targetItem = targetListBoxItem.DataContext as ChatItemViewModel;
        }

        // 동일 아이템이면 무시
        if (targetItem == null || targetItem.Id == droppedItem.Id)
            return;

        Log4.Info($"[ChatFavorites_Drop] 드래그: {droppedItem.DisplayName} → 타겟: {targetItem.DisplayName}");

        // 순서 변경 실행
        await _teamsViewModel.ReorderFavoriteAsync(droppedItem.Id, targetItem.Id);
    }

    #endregion

    /// <summary>
    /// 채팅 목록 로드
    /// </summary>
    private async Task LoadChatsAsync()
    {
        if (_teamsViewModel == null)
        {
            // TeamsViewModel 초기화 (DI에서 가져오기)
            try
            {
                _teamsViewModel = ((App)Application.Current).GetService<TeamsViewModel>();
            }
            catch (Exception ex)
            {
                Log4.Error($"TeamsViewModel 초기화 실패: {ex.Message}");
                return;
            }
        }

        if (_teamsViewModel == null) return;

        try
        {
            if (ChatListLoadingOverlay != null)
                ChatListLoadingOverlay.Visibility = Visibility.Visible;

            await _teamsViewModel.LoadChatsAsync();

            // UI 업데이트 (두 ListBox 모두 업데이트)
            UpdateChatListUI();

            Log4.Info($"채팅 목록 로드 완료: {_teamsViewModel.Chats.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"채팅 목록 로드 실패: {ex.Message}");
        }
        finally
        {
            if (ChatListLoadingOverlay != null)
                ChatListLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 채팅 메시지 입력 키 이벤트
    /// </summary>
    private async void ChatMessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        // 플레이스홀더 업데이트
        UpdateChatMessagePlaceholder();

        // Enter 키로 전송 (Shift+Enter는 줄바꿈)
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendChatMessageAsync();
        }
    }

    /// <summary>
    /// 채팅 메시지 플레이스홀더 업데이트
    /// </summary>
    private void UpdateChatMessagePlaceholder()
    {
        if (ChatMessagePlaceholder != null && ChatMessageInput != null)
        {
            ChatMessagePlaceholder.Visibility = string.IsNullOrEmpty(ChatMessageInput.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 채팅 전송 버튼 클릭
    /// </summary>
    private async void ChatSendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendChatMessageAsync();
    }

    /// <summary>
    /// 채팅 메시지 전송
    /// </summary>
    private async Task SendChatMessageAsync()
    {
        if (_teamsViewModel == null || ChatMessageInput == null)
            return;

        var message = ChatMessageInput.Text?.Trim();
        if (string.IsNullOrEmpty(message))
            return;

        try
        {
            _teamsViewModel.NewMessageText = message;
            ChatMessageInput.Text = string.Empty;
            UpdateChatMessagePlaceholder();

            await _teamsViewModel.SendMessageAsync();

            // 스크롤 맨 아래로
            if (ChatMessagesScrollViewer != null)
                ChatMessagesScrollViewer.ScrollToEnd();
        }
        catch (Exception ex)
        {
            Log4.Error($"메시지 전송 실패: {ex.Message}");
        }
    }

    #region 채팅 필터 이벤트 핸들러

    /// <summary>
    /// 읽지 않음 필터 체크됨
    /// </summary>
    private void ChatFilterUnread_Checked(object sender, RoutedEventArgs e)
    {
        if (_teamsViewModel != null)
        {
            _teamsViewModel.FilterUnread = true;
            ApplyChatFilters();
        }
    }

    /// <summary>
    /// 읽지 않음 필터 체크 해제됨
    /// </summary>
    private void ChatFilterUnread_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_teamsViewModel != null)
        {
            _teamsViewModel.FilterUnread = false;
            ApplyChatFilters();
        }
    }

    /// <summary>
    /// 채팅 필터 체크됨
    /// </summary>
    private void ChatFilterChat_Checked(object sender, RoutedEventArgs e)
    {
        if (_teamsViewModel != null)
        {
            _teamsViewModel.FilterChat = true;
            ApplyChatFilters();
        }
    }

    /// <summary>
    /// 채팅 필터 체크 해제됨
    /// </summary>
    private void ChatFilterChat_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_teamsViewModel != null)
        {
            _teamsViewModel.FilterChat = false;
            ApplyChatFilters();
        }
    }

    /// <summary>
    /// 모임 채팅 필터 체크됨
    /// </summary>
    private void ChatFilterMeeting_Checked(object sender, RoutedEventArgs e)
    {
        if (_teamsViewModel != null)
        {
            _teamsViewModel.FilterMeeting = true;
            ApplyChatFilters();
        }
    }

    /// <summary>
    /// 모임 채팅 필터 체크 해제됨
    /// </summary>
    private void ChatFilterMeeting_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_teamsViewModel != null)
        {
            _teamsViewModel.FilterMeeting = false;
            ApplyChatFilters();
        }
    }

    /// <summary>
    /// 채팅 필터 적용
    /// </summary>
    private void ApplyChatFilters()
    {
        if (_teamsViewModel == null || ChatListBox == null) return;

        var filteredChats = _teamsViewModel.AllChats?.Where(chat =>
        {
            // 필터가 모두 해제된 경우 모든 채팅 표시
            if (!_teamsViewModel.FilterUnread && !_teamsViewModel.FilterChat && !_teamsViewModel.FilterMeeting)
                return true;

            bool match = false;

            // 읽지 않음 필터
            if (_teamsViewModel.FilterUnread && chat.UnreadCount > 0)
                match = true;

            // 채팅 필터 (1:1 채팅)
            if (_teamsViewModel.FilterChat && !chat.IsGroupChat)
                match = true;

            // 모임 채팅 필터 (그룹 채팅)
            if (_teamsViewModel.FilterMeeting && chat.IsGroupChat)
                match = true;

            return match;
        }).ToList() ?? new List<ChatItemViewModel>();

        ChatListBox.ItemsSource = filteredChats;
        Log4.Debug($"채팅 필터 적용: {filteredChats.Count}개 표시");
    }

    #endregion

    #endregion

    #region OneNote 이벤트 핸들러

    private OneNoteViewModel? _oneNoteViewModel;

    /// <summary>
    /// OneNote 새로고침 버튼 클릭
    /// </summary>
    private async void OneNoteRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadOneNoteNotebooksAsync();
    }

    /// <summary>
    /// OneNote 새 노트북 버튼 클릭
    /// </summary>
    private async void OneNoteNewNotebookButton_Click(object sender, RoutedEventArgs e)
    {
        // 간단한 입력 다이얼로그 (실제 구현에서는 별도 다이얼로그 필요)
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "새 노트북",
            Content = "새 노트북 이름을 입력하세요 (현재는 기본 이름 사용)",
            PrimaryButtonText = "만들기",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            if (_oneNoteViewModel != null)
            {
                await _oneNoteViewModel.CreateNotebookAsync($"새 노트북 {DateTime.Now:yyyyMMdd_HHmmss}");
                await LoadOneNoteNotebooksAsync();
            }
        }
    }

    /// <summary>
    /// OneNote 검색 키 입력
    /// </summary>
    private async void OneNoteSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && OneNoteSearchBox != null)
        {
            var query = OneNoteSearchBox.Text?.Trim();
            if (!string.IsNullOrEmpty(query) && _oneNoteViewModel != null)
            {
                Log4.Debug($"OneNote 검색: {query}");
                _oneNoteViewModel.SearchQuery = query;
                await _oneNoteViewModel.SearchPagesAsync();

                // 검색 결과를 최근 노트 목록에 표시
                if (OneNoteRecentListBox != null)
                    OneNoteRecentListBox.ItemsSource = _oneNoteViewModel.SearchResults;
            }
        }
        else if (e.Key == Key.Escape && OneNoteSearchBox != null)
        {
            OneNoteSearchBox.Text = string.Empty;
            // 최근 노트 목록 복원
            if (_oneNoteViewModel != null && OneNoteRecentListBox != null)
                OneNoteRecentListBox.ItemsSource = _oneNoteViewModel.RecentPages;
        }
    }

    /// <summary>
    /// OneNote 최근 노트 목록 선택 변경
    /// </summary>
    private async void OneNoteRecentListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is PageItemViewModel selectedPage && _oneNoteViewModel != null)
        {
            Log4.Debug($"OneNote 페이지 선택: {selectedPage.Title}");
            await LoadOneNotePageAsync(selectedPage);
        }
    }

    /// <summary>
    /// OneNote 트리뷰 선택 변경
    /// </summary>
    private async void OneNoteTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is PageItemViewModel selectedPage && _oneNoteViewModel != null)
        {
            Log4.Debug($"OneNote 페이지 선택 (트리뷰): {selectedPage.Title}");
            await LoadOneNotePageAsync(selectedPage);
        }
        else if (e.NewValue is SectionItemViewModel selectedSection && _oneNoteViewModel != null)
        {
            Log4.Debug($"OneNote 섹션 선택: {selectedSection.DisplayName}");
            _oneNoteViewModel.SelectedSection = selectedSection;
        }
        else if (e.NewValue is NotebookItemViewModel selectedNotebook && _oneNoteViewModel != null)
        {
            Log4.Debug($"OneNote 노트북 선택: {selectedNotebook.DisplayName}");
            _oneNoteViewModel.SelectedNotebook = selectedNotebook;
        }
    }

    /// <summary>
    /// OneNote 새 페이지 버튼 클릭
    /// </summary>
    private async void OneNoteNewPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_oneNoteViewModel?.SelectedSection == null)
        {
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = "알림",
                Content = "먼저 섹션을 선택해주세요.",
                PrimaryButtonText = "확인"
            };
            await dialog.ShowDialogAsync();
            return;
        }

        // 간단한 입력 다이얼로그 (실제 구현에서는 별도 다이얼로그 필요)
        var createDialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "새 페이지",
            Content = "새 페이지를 만드시겠습니까?",
            PrimaryButtonText = "만들기",
            CloseButtonText = "취소"
        };

        var result = await createDialog.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            await _oneNoteViewModel.CreatePageAsync($"새 페이지 {DateTime.Now:HH:mm}");
            await LoadOneNoteNotebooksAsync();
        }
    }

    /// <summary>
    /// OneNote 노트북 목록 로드
    /// </summary>
    private async Task LoadOneNoteNotebooksAsync()
    {
        if (_oneNoteViewModel == null)
        {
            // OneNoteViewModel 초기화
            try
            {
                using var scope = ((App)Application.Current).ServiceProvider.CreateScope();
                var oneNoteService = scope.ServiceProvider.GetService<GraphOneNoteService>();
                if (oneNoteService != null)
                {
                    _oneNoteViewModel = new OneNoteViewModel(oneNoteService);
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"OneNoteViewModel 초기화 실패: {ex.Message}");
                return;
            }
        }

        if (_oneNoteViewModel == null) return;

        try
        {
            if (OneNoteLoadingOverlay != null)
                OneNoteLoadingOverlay.Visibility = Visibility.Visible;

            await _oneNoteViewModel.LoadNotebooksAsync();

            if (OneNoteTreeView != null)
                OneNoteTreeView.ItemsSource = _oneNoteViewModel.Notebooks;

            // 최근 페이지도 로드
            await _oneNoteViewModel.LoadRecentPagesAsync();
            if (OneNoteRecentListBox != null)
                OneNoteRecentListBox.ItemsSource = _oneNoteViewModel.RecentPages;

            Log4.Info($"OneNote 노트북 로드 완료: {_oneNoteViewModel.Notebooks.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"OneNote 노트북 로드 실패: {ex.Message}");
        }
        finally
        {
            if (OneNoteLoadingOverlay != null)
                OneNoteLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// OneNote 페이지 로드 및 표시
    /// </summary>
    private async Task LoadOneNotePageAsync(PageItemViewModel page)
    {
        if (_oneNoteViewModel == null || page == null) return;

        try
        {
            // 로딩 표시
            if (OneNoteLoadingOverlay != null)
                OneNoteLoadingOverlay.Visibility = Visibility.Visible;
            if (OneNoteEmptyState != null)
                OneNoteEmptyState.Visibility = Visibility.Collapsed;

            // 페이지 콘텐츠 로드
            await _oneNoteViewModel.LoadPageContentAsync(page.Id);

            // 헤더 업데이트
            if (OneNotePageHeaderBorder != null)
                OneNotePageHeaderBorder.Visibility = Visibility.Visible;
            if (OneNotePageTitleText != null)
                OneNotePageTitleText.Text = page.Title;
            if (OneNotePageLocationText != null)
                OneNotePageLocationText.Text = page.LocationDisplay;

            // 콘텐츠 표시 (HTML을 텍스트로 변환하여 표시)
            if (OneNoteContentBorder != null && _oneNoteViewModel.CurrentPageContent != null)
            {
                OneNoteContentBorder.Visibility = Visibility.Visible;

                // HTML에서 텍스트만 추출하여 RichTextBox에 표시
                if (OneNoteContentRichTextBox != null)
                {
                    var plainText = StripHtmlForDisplay(_oneNoteViewModel.CurrentPageContent);
                    var paragraph = new System.Windows.Documents.Paragraph(
                        new System.Windows.Documents.Run(plainText));
                    OneNoteContentRichTextBox.Document.Blocks.Clear();
                    OneNoteContentRichTextBox.Document.Blocks.Add(paragraph);
                }
            }

            Log4.Debug($"OneNote 페이지 로드 완료: {page.Title}");
        }
        catch (Exception ex)
        {
            Log4.Error($"OneNote 페이지 로드 실패: {ex.Message}");
        }
        finally
        {
            if (OneNoteLoadingOverlay != null)
                OneNoteLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// HTML에서 텍스트 추출 (간단한 버전)
    /// </summary>
    private string StripHtmlForDisplay(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // HTML 태그 제거
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ");
        // HTML 엔티티 디코딩
        text = System.Net.WebUtility.HtmlDecode(text);
        // 연속된 공백 정리
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    #endregion

    #region OneDrive 이벤트 핸들러

    private OneDriveViewModel? _oneDriveViewModel;

    /// <summary>
    /// OneDrive 새 폴더 버튼 클릭
    /// </summary>
    private async void OneDriveNewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_oneDriveViewModel == null)
            {
                _oneDriveViewModel = ((App)Application.Current).GetService<OneDriveViewModel>()!;
            }

            // 폴더 이름 입력 받기
            var dialog = new ContentDialog
            {
                Title = "새 폴더",
                Content = new System.Windows.Controls.TextBox { Text = "", Width = 300 },
                PrimaryButtonText = "생성",
                CloseButtonText = "취소",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var textBox = dialog.Content as System.Windows.Controls.TextBox;
                if (!string.IsNullOrWhiteSpace(textBox?.Text))
                {
                    await _oneDriveViewModel.CreateFolderAsync(textBox.Text);
                    Log4.Info($"OneDrive 폴더 생성 완료: {textBox.Text}");
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"OneDrive 폴더 생성 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// OneDrive 업로드 버튼 클릭
    /// </summary>
    private async void OneDriveUploadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "파일 업로드",
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var filePath = openFileDialog.FileName;
                var fileName = System.IO.Path.GetFileName(filePath);

                if (_oneDriveViewModel == null)
                {
                    _oneDriveViewModel = ((App)Application.Current).GetService<OneDriveViewModel>()!;
                }

                // 파일 업로드 서비스 호출
                var oneDriveService = ((App)Application.Current).GetService<GraphOneDriveService>()!;
                using var stream = System.IO.File.OpenRead(filePath);
                await oneDriveService.UploadSmallFileAsync(_oneDriveViewModel.CurrentFolderId, fileName, stream);

                // 새로고침
                await _oneDriveViewModel.RefreshAsync();
                Log4.Info($"OneDrive 파일 업로드 완료: {fileName}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"OneDrive 파일 업로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// OneDrive 새로고침 버튼 클릭
    /// </summary>
    private async void OneDriveRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_oneDriveViewModel == null)
            {
                _oneDriveViewModel = ((App)Application.Current).GetService<OneDriveViewModel>()!;
            }

            await _oneDriveViewModel.RefreshAsync();
            Log4.Debug("OneDrive 새로고침 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"OneDrive 새로고침 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// OneDrive 목록 뷰 버튼 클릭
    /// </summary>
    private void OneDriveListViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_oneDriveViewModel != null)
        {
            _oneDriveViewModel.ViewMode = "list";
            Log4.Debug("OneDrive 뷰 모드 변경: list");
        }
    }

    /// <summary>
    /// OneDrive 그리드 뷰 버튼 클릭
    /// </summary>
    private void OneDriveGridViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_oneDriveViewModel != null)
        {
            _oneDriveViewModel.ViewMode = "grid";
            Log4.Debug("OneDrive 뷰 모드 변경: grid");
        }
    }

    /// <summary>
    /// OneDrive 검색 박스 KeyDown
    /// </summary>
    private async void OneDriveSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            try
            {
                if (_oneDriveViewModel == null)
                {
                    _oneDriveViewModel = ((App)Application.Current).GetService<OneDriveViewModel>()!;
                }

                var searchBox = sender as System.Windows.Controls.TextBox;
                if (!string.IsNullOrWhiteSpace(searchBox?.Text))
                {
                    _oneDriveViewModel.SearchQuery = searchBox.Text;
                    await _oneDriveViewModel.SearchAsync();
                    Log4.Debug($"OneDrive 검색: {searchBox.Text}");
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"OneDrive 검색 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// OneDrive 상위 폴더 이동 버튼 클릭
    /// </summary>
    private async void OneDriveGoUpButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_oneDriveViewModel == null)
            {
                _oneDriveViewModel = ((App)Application.Current).GetService<OneDriveViewModel>()!;
            }

            await _oneDriveViewModel.GoUpAsync();
            Log4.Debug("OneDrive 상위 폴더로 이동");
        }
        catch (Exception ex)
        {
            Log4.Error($"OneDrive 상위 폴더 이동 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// OneDrive Breadcrumb 아이템 클릭
    /// </summary>
    private async void OneDriveBreadcrumbItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is System.Windows.Controls.Button button && button.DataContext is BreadcrumbItem breadcrumb)
            {
                if (_oneDriveViewModel == null)
                {
                    _oneDriveViewModel = ((App)Application.Current).GetService<OneDriveViewModel>()!;
                }

                await _oneDriveViewModel.NavigateToBreadcrumbAsync(breadcrumb);
                Log4.Debug($"OneDrive Breadcrumb 이동: {breadcrumb.Name}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"OneDrive Breadcrumb 이동 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// OneDrive 파일 로드 버튼 클릭
    /// </summary>
    private async void OneDriveLoadFilesButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadOneDriveFilesAsync();
    }

    /// <summary>
    /// OneDrive 파일 목록 로드
    /// </summary>
    private async Task LoadOneDriveFilesAsync()
    {
        try
        {
            if (OneDriveLoadingOverlay != null)
                OneDriveLoadingOverlay.Visibility = Visibility.Visible;

            if (_oneDriveViewModel == null)
            {
                _oneDriveViewModel = ((App)Application.Current).GetService<OneDriveViewModel>()!;
            }

            // OneDrive 루트 폴더 로드
            await _oneDriveViewModel.LoadRootAsync();

            // 드라이브 정보 로드
            await _oneDriveViewModel.LoadDriveInfoAsync();

            // ListView에 데이터 바인딩
            if (OneDriveFileListView != null)
            {
                OneDriveFileListView.ItemsSource = _oneDriveViewModel.Items;
            }

            // Breadcrumb 바인딩
            if (OneDriveBreadcrumb != null)
            {
                OneDriveBreadcrumb.ItemsSource = _oneDriveViewModel.Breadcrumbs;
            }

            // 드라이브 정보 로그
            if (_oneDriveViewModel.DriveInfo != null)
            {
                Log4.Debug($"OneDrive 사용량: {_oneDriveViewModel.DriveInfo.UsedDisplay} / {_oneDriveViewModel.DriveInfo.TotalDisplay}");
            }

            Log4.Info($"OneDrive 파일 목록 로드 완료: {_oneDriveViewModel.Items.Count}개");
        }
        catch (Exception ex)
        {
            Log4.Error($"OneDrive 파일 목록 로드 실패: {ex.Message}");
        }
        finally
        {
            if (OneDriveLoadingOverlay != null)
                OneDriveLoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// OneDrive 파일 목록 선택 변경
    /// </summary>
    private void OneDriveFileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_oneDriveViewModel != null && sender is System.Windows.Controls.ListView listView)
        {
            _oneDriveViewModel.SelectedItem = listView.SelectedItem as DriveItemViewModel;
        }
    }

    /// <summary>
    /// OneDrive 파일 목록 더블클릭
    /// </summary>
    private async void OneDriveFileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_oneDriveViewModel?.SelectedItem == null)
                return;

            var selectedItem = _oneDriveViewModel.SelectedItem;

            if (selectedItem.IsFolder)
            {
                // 폴더인 경우 해당 폴더로 이동
                await _oneDriveViewModel.OpenItemAsync(selectedItem);

                // ListView 다시 바인딩
                if (OneDriveFileListView != null)
                {
                    OneDriveFileListView.ItemsSource = _oneDriveViewModel.Items;
                }

                Log4.Debug($"OneDrive 폴더 열기: {selectedItem.Name}");
            }
            else
            {
                // 파일인 경우 다운로드 또는 미리보기
                Log4.Info($"OneDrive 파일 열기: {selectedItem.Name}");

                // 파일 다운로드 대화상자
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "파일 저장",
                    FileName = selectedItem.Name
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var oneDriveService = ((App)Application.Current).GetService<GraphOneDriveService>()!;
                    using var stream = await oneDriveService.DownloadFileAsync(selectedItem.Id);
                    if (stream != null)
                    {
                        using var fileStream = System.IO.File.Create(saveFileDialog.FileName);
                        await stream.CopyToAsync(fileStream);
                        Log4.Info($"OneDrive 파일 다운로드 완료: {saveFileDialog.FileName}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"OneDrive 아이템 열기 실패: {ex.Message}");
        }
    }

    #endregion

    #region Teams 이벤트 핸들러

    /// <summary>
    /// Teams 새로고침 버튼 클릭
    /// </summary>
    private async void TeamsRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[Teams] 새로고침 버튼 클릭");
            await LoadTeamsDataAsync();
            System.Diagnostics.Debug.WriteLine($"[Teams] 새로고침 완료: {_teamsViewModel?.Teams.Count ?? 0}개 팀");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Teams] 팀 새로고침 실패: {ex.Message}");
            Log4.Error($"팀 새로고침 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 팀 아이템 클릭 (확장/축소)
    /// </summary>
    private void TeamItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is TeamItemViewModel team)
        {
            team.IsExpanded = !team.IsExpanded;
        }
    }

    /// <summary>
    /// 채널 아이템 클릭
    /// </summary>
    private async void ChannelItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is ChannelItemViewModel channel)
            {
                if (_teamsViewModel != null)
                {
                    await _teamsViewModel.SelectChannelAsync(channel);
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"채널 선택 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 즐겨찾기 채널 아이템 클릭
    /// </summary>
    private async void FavoriteChannelItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is FavoriteChannelViewModel favorite)
            {
                if (_teamsViewModel != null)
                {
                    // 팀에서 해당 채널 찾기
                    var team = _teamsViewModel.Teams.FirstOrDefault(t => t.Id == favorite.TeamId);
                    var channel = team?.Channels.FirstOrDefault(c => c.Id == favorite.ChannelId);
                    if (channel != null)
                    {
                        await _teamsViewModel.SelectChannelAsync(channel);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"즐겨찾기 채널 선택 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// Teams 게시물 탭 클릭
    /// </summary>
    private void TeamsPostsTab_Click(object sender, RoutedEventArgs e)
    {
        _teamsViewModel?.SwitchChannelTabCommand.Execute("posts");
    }

    /// <summary>
    /// Teams 파일 탭 클릭
    /// </summary>
    private void TeamsFilesTab_Click(object sender, RoutedEventArgs e)
    {
        _teamsViewModel?.SwitchChannelTabCommand.Execute("files");
    }

    /// <summary>
    /// 스레드에서 회신 버튼 클릭
    /// </summary>
    private void ReplyToThread_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is ChannelMessageViewModel message)
            {
                // 입력창에 포커스를 주고 회신 준비
                // 추후 스레드 회신 기능 구현 시 확장
                TeamsChannelMessageInput?.Focus();
                Log4.Info($"스레드 회신 준비: 메시지 ID={message.Id}, 작성자={message.FromUser}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"스레드 회신 준비 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 채널 파일 클릭
    /// </summary>
    private void ChannelFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is ChannelFileViewModel file)
            {
                if (!string.IsNullOrEmpty(file.WebUrl))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = file.WebUrl,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"파일 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 채널 메시지 입력 KeyDown
    /// </summary>
    private async void TeamsChannelMessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendTeamsChannelMessageAsync();
        }
    }

    /// <summary>
    /// 채널 메시지 전송 버튼 클릭
    /// </summary>
    private async void TeamsChannelSendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendTeamsChannelMessageAsync();
    }

    /// <summary>
    /// 채널 메시지 전송
    /// </summary>
    private async Task SendTeamsChannelMessageAsync()
    {
        try
        {
            var content = TeamsChannelMessageInput.Text.Trim();
            if (string.IsNullOrEmpty(content) || _teamsViewModel == null)
                return;

            TeamsChannelMessageInput.Text = string.Empty;
            await _teamsViewModel.SendChannelMessageAsync(content);
        }
        catch (Exception ex)
        {
            Log4.Error($"채널 메시지 전송 실패: {ex.Message}");
        }
    }

    #endregion

    #region Activity 이벤트 핸들러

    private ActivityViewModel? _activityViewModel;

    /// <summary>
    /// Activity 새로고침 버튼 클릭
    /// </summary>
    private async void ActivityRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadActivityDataAsync();
    }

    /// <summary>
    /// 모든 활동 필터 버튼 클릭
    /// </summary>
    private void ActivityFilterAllButton_Click(object sender, RoutedEventArgs e)
    {
        SetActivityFilter("all");
    }

    /// <summary>
    /// 메일 필터 버튼 클릭
    /// </summary>
    private void ActivityFilterMailButton_Click(object sender, RoutedEventArgs e)
    {
        SetActivityFilter("mail");
    }

    /// <summary>
    /// 채팅 필터 버튼 클릭
    /// </summary>
    private void ActivityFilterChatButton_Click(object sender, RoutedEventArgs e)
    {
        SetActivityFilter("chat");
    }

    /// <summary>
    /// 파일 필터 버튼 클릭
    /// </summary>
    private void ActivityFilterFileButton_Click(object sender, RoutedEventArgs e)
    {
        SetActivityFilter("file");
    }

    /// <summary>
    /// 필터 설정
    /// </summary>
    private void SetActivityFilter(string filter)
    {
        if (_activityViewModel == null)
        {
            _activityViewModel = ((App)Application.Current).GetService<ActivityViewModel>()!;
        }

        _activityViewModel.SetFilterCommand.Execute(filter);
        ActivityListView.ItemsSource = _activityViewModel.FilteredActivities;

        // 필터 버튼 UI 업데이트
        ActivityFilterAllButton.Appearance = filter == "all" ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        ActivityFilterMailButton.Appearance = filter == "mail" ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        ActivityFilterChatButton.Appearance = filter == "chat" ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Transparent;
        ActivityFilterFileButton.Appearance = filter == "file" ? Wpf.Ui.Controls.ControlAppearance.Secondary : Wpf.Ui.Controls.ControlAppearance.Transparent;
    }

    /// <summary>
    /// 활동 목록 선택 변경
    /// </summary>
    private void ActivityListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActivityListView.SelectedItem is ActivityItemViewModel selectedActivity)
        {
            _activityViewModel?.OpenActivityCommand.Execute(selectedActivity);
        }
    }

    /// <summary>
    /// 활동 로드 버튼 클릭
    /// </summary>
    private async void ActivityLoadButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadActivityDataAsync();
    }

    /// <summary>
    /// Activity 데이터 로드
    /// </summary>
    private async Task LoadActivityDataAsync()
    {
        try
        {
            if (_activityViewModel == null)
            {
                _activityViewModel = ((App)Application.Current).GetService<ActivityViewModel>()!;
            }

            await _activityViewModel.LoadActivitiesAsync();

            // 활동 목록 바인딩
            ActivityListView.ItemsSource = _activityViewModel.FilteredActivities;

            // Empty state 처리
            if (_activityViewModel.FilteredActivities.Count == 0)
            {
                ActivityEmptyState.Visibility = Visibility.Visible;
            }
            else
            {
                ActivityEmptyState.Visibility = Visibility.Collapsed;
            }

            Log4.Info($"Activity 데이터 로드 완료: {_activityViewModel.Activities.Count}개 활동");
        }
        catch (Exception ex)
        {
            Log4.Error($"Activity 데이터 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region Planner 이벤트 핸들러

    private PlannerViewModel? _plannerViewModel;

    // 드래그앤드롭 상태 변수
    private Point _plannerTaskDragStartPoint;
    private TaskItemViewModel? _plannerDraggedTask;
    private BucketViewModel? _plannerDragSourceBucket;
    private bool _plannerIsDragging = false;

    /// <summary>
    /// Planner 새로고침 버튼 클릭
    /// </summary>
    private async void PlannerRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plannerViewModel == null)
        {
            _plannerViewModel = ((App)Application.Current).GetService<PlannerViewModel>()!;
        }

        await _plannerViewModel.RefreshAsync();
        PlannerListBox.ItemsSource = _plannerViewModel.Plans;
        PlannerPinnedPlansItemsControl.ItemsSource = _plannerViewModel.PinnedPlans;
    }

    /// <summary>
    /// 나의 하루 버튼 클릭
    /// </summary>
    private async void PlannerMyDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plannerViewModel == null)
        {
            _plannerViewModel = ((App)Application.Current).GetService<PlannerViewModel>()!;
        }

        await _plannerViewModel.LoadMyDayTasksAsync();

        // 보드 숨기고 내 작업 뷰 표시
        PlannerBoardScrollViewer.Visibility = Visibility.Collapsed;
        PlannerNoPlanSelected.Visibility = Visibility.Collapsed;
        PlannerMyTasksView.Visibility = Visibility.Visible;
        PlannerMyTasksViewTitle.Text = "나의 하루";
        PlannerMyTasksListView.ItemsSource = _plannerViewModel.MyDayTasks;
        PlannerBoardTitle.Text = "나의 하루";
        PlannerAddBucketButton.IsEnabled = false;
        PlannerAddTaskButton.IsEnabled = false;

        Log4.Info($"Planner 나의 하루 {_plannerViewModel.MyDayTasks.Count}개 로드");
    }

    /// <summary>
    /// 내 작업 보기 버튼 클릭
    /// </summary>
    private async void PlannerMyTasksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plannerViewModel == null)
        {
            _plannerViewModel = ((App)Application.Current).GetService<PlannerViewModel>()!;
        }

        await _plannerViewModel.LoadMyTasksAsync();

        // 보드 숨기고 내 작업 뷰 표시
        PlannerBoardScrollViewer.Visibility = Visibility.Collapsed;
        PlannerNoPlanSelected.Visibility = Visibility.Collapsed;
        PlannerMyTasksView.Visibility = Visibility.Visible;
        PlannerMyTasksViewTitle.Text = "내 작업";
        PlannerMyTasksListView.ItemsSource = _plannerViewModel.MyTasks;
        PlannerBoardTitle.Text = "내 작업";
        PlannerAddBucketButton.IsEnabled = false;
        PlannerAddTaskButton.IsEnabled = false;

        Log4.Info($"Planner 내 작업 {_plannerViewModel.MyTasks.Count}개 로드");
    }

    /// <summary>
    /// 플랜 목록 선택 변경
    /// </summary>
    private async void PlannerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_plannerViewModel == null)
        {
            _plannerViewModel = ((App)Application.Current).GetService<PlannerViewModel>()!;
        }

        // 이전 선택 해제
        foreach (var item in e.RemovedItems.OfType<PlanItemViewModel>())
        {
            item.IsSelected = false;
        }

        // 새 선택 설정
        foreach (var item in e.AddedItems.OfType<PlanItemViewModel>())
        {
            item.IsSelected = true;
        }

        if (PlannerListBox.SelectedItem is PlanItemViewModel selectedPlan)
        {
            await _plannerViewModel.SelectPlanAsync(selectedPlan);

            // 칸반 보드 표시
            PlannerMyTasksView.Visibility = Visibility.Collapsed;
            PlannerBoardScrollViewer.Visibility = Visibility.Visible;
            PlannerNoPlanSelected.Visibility = Visibility.Collapsed;

            // UI 업데이트
            PlannerBoardTitle.Text = selectedPlan.Title;
            PlannerAddBucketButton.IsEnabled = true;
            PlannerAddTaskButton.IsEnabled = true;

            // 버킷 목록 바인딩
            PlannerBucketsItemsControl.ItemsSource = _plannerViewModel.Buckets;

            Log4.Debug($"Planner 플랜 선택: {selectedPlan.Title}");
        }
    }

    /// <summary>
    /// REST API를 통한 Planner 플랜 선택 (인덱스 기반)
    /// </summary>
    public void SelectPlannerPlanByIndex(int index)
    {
        if (_plannerViewModel == null || _plannerViewModel.Plans.Count == 0)
        {
            Log4.Warn($"[SelectPlannerPlanByIndex] 플랜 목록이 비어 있습니다.");
            return;
        }

        if (index < 0 || index >= _plannerViewModel.Plans.Count)
        {
            Log4.Warn($"[SelectPlannerPlanByIndex] 유효하지 않은 인덱스: {index} (플랜 수: {_plannerViewModel.Plans.Count})");
            return;
        }

        // ListBox 선택 변경 (SelectionChanged 이벤트가 자동으로 발생)
        PlannerListBox.SelectedIndex = index;
        Log4.Info($"[SelectPlannerPlanByIndex] 플랜 인덱스 {index} 선택됨");
    }

    /// <summary>
    /// 고정된 플랜 아이템 선택 변경
    /// </summary>
    private void PlannerPinnedPlanItem_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 고정 목록에서 선택 시 내 플랜 목록도 동기화
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is PlanItemViewModel plan)
        {
            if (_plannerViewModel == null)
            {
                _plannerViewModel = ((App)Application.Current).GetService<PlannerViewModel>()!;
            }

            // 해당 플랜 선택 (내 플랜 목록에서) - 이렇게 하면 IsSelected가 true로 설정됨
            var planInList = _plannerViewModel.Plans.FirstOrDefault(p => p.Id == plan.Id);
            if (planInList != null)
            {
                PlannerListBox.SelectedItem = planInList;
            }
        }
    }

    /// <summary>
    /// 플랜 핀 고정/해제 버튼 클릭
    /// </summary>
    private void PlannerPinButton_Click(object sender, RoutedEventArgs e)
    {
        // 이벤트 버블링 방지 (ListBox 선택 이벤트 방지)
        e.Handled = true;

        if (sender is FrameworkElement element && element.Tag is PlanItemViewModel plan)
        {
            if (_plannerViewModel == null)
            {
                _plannerViewModel = ((App)Application.Current).GetService<PlannerViewModel>()!;
            }

            // 핀 상태 토글
            plan.IsPinned = !plan.IsPinned;

            // PinnedPlans 컬렉션 업데이트
            if (plan.IsPinned)
            {
                if (!_plannerViewModel.PinnedPlans.Any(p => p.Id == plan.Id))
                {
                    _plannerViewModel.PinnedPlans.Add(plan);
                }
                Log4.Info($"[PlannerPinButton_Click] 플랜 '{plan.Title}' 핀 고정됨");
            }
            else
            {
                var pinnedItem = _plannerViewModel.PinnedPlans.FirstOrDefault(p => p.Id == plan.Id);
                if (pinnedItem != null)
                {
                    _plannerViewModel.PinnedPlans.Remove(pinnedItem);
                }
                Log4.Info($"[PlannerPinButton_Click] 플랜 '{plan.Title}' 핀 해제됨");
            }

            // 고정 섹션 UI 강제 갱신 (null 후 재할당)
            PlannerPinnedPlansItemsControl.ItemsSource = null;
            PlannerPinnedPlansItemsControl.ItemsSource = _plannerViewModel.PinnedPlans;

            // 고정 항목 유무에 따라 Expander 표시/숨김
            PlannerPinnedExpander.Visibility = _plannerViewModel.PinnedPlans.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            // 핀 상태 저장
            _plannerViewModel.SavePinnedPlanIds();
        }
    }

    /// <summary>
    /// 플랜 로드 버튼 클릭
    /// </summary>
    private async void PlannerLoadPlansButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadPlannerDataAsync();
    }

    /// <summary>
    /// 버킷 추가 버튼 클릭
    /// </summary>
    private async void PlannerAddBucketButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plannerViewModel?.SelectedPlan == null)
            return;

        var dialog = new ContentDialog
        {
            Title = "새 버킷",
            Content = new System.Windows.Controls.TextBox
            {
                Text = "",
                Width = 300
            },
            PrimaryButtonText = "생성",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var textBox = dialog.Content as System.Windows.Controls.TextBox;
            if (!string.IsNullOrWhiteSpace(textBox?.Text))
            {
                await _plannerViewModel.CreateBucketAsync(textBox.Text);
                Log4.Info($"Planner 버킷 생성: {textBox.Text}");
            }
        }
    }

    /// <summary>
    /// 작업 추가 버튼 클릭
    /// </summary>
    private async void PlannerAddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plannerViewModel?.SelectedPlan == null || _plannerViewModel.Buckets.Count == 0)
            return;

        var dialog = new ContentDialog
        {
            Title = "새 작업",
            Content = new System.Windows.Controls.TextBox
            {
                Text = "",
                Width = 300
            },
            PrimaryButtonText = "생성",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var textBox = dialog.Content as System.Windows.Controls.TextBox;
            if (!string.IsNullOrWhiteSpace(textBox?.Text))
            {
                await _plannerViewModel.CreateTaskAsync(textBox.Text);
                Log4.Info($"Planner 작업 생성: {textBox.Text}");
            }
        }
    }

    /// <summary>
    /// 버킷 내 작업 추가 버튼 클릭
    /// </summary>
    private async void PlannerBucketAddTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is BucketViewModel bucket)
        {
            if (_plannerViewModel?.SelectedPlan == null)
                return;

            var dialog = new ContentDialog
            {
                Title = $"'{bucket.Name}'에 작업 추가",
                Content = new System.Windows.Controls.TextBox
                {
                    Text = "",
                    Width = 300
                },
                PrimaryButtonText = "생성",
                CloseButtonText = "취소"
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var textBox = dialog.Content as System.Windows.Controls.TextBox;
                if (!string.IsNullOrWhiteSpace(textBox?.Text))
                {
                    var plannerService = ((App)Application.Current).GetService<GraphPlannerService>()!;
                    var task = await plannerService.CreateTaskAsync(_plannerViewModel.SelectedPlan.Id, bucket.Id, textBox.Text);
                    if (task != null)
                    {
                        bucket.Tasks.Insert(0, new TaskItemViewModel
                        {
                            Id = task.Id ?? string.Empty,
                            Title = task.Title ?? textBox.Text,
                            BucketId = bucket.Id,
                            PlanId = _plannerViewModel.SelectedPlan.Id,
                            ETag = task.AdditionalData?.TryGetValue("@odata.etag", out var etag) == true ? etag?.ToString() : null
                        });
                        Log4.Info($"Planner 작업 생성 (버킷 {bucket.Name}): {textBox.Text}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 버킷 메뉴 버튼 클릭
    /// </summary>
    private void PlannerBucketMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is BucketViewModel bucket)
        {
            var contextMenu = new ContextMenu();

            var renameItem = new System.Windows.Controls.MenuItem { Header = "이름 변경" };
            renameItem.Click += async (s, args) => await RenameBucketAsync(bucket);
            contextMenu.Items.Add(renameItem);

            var deleteItem = new System.Windows.Controls.MenuItem { Header = "삭제" };
            deleteItem.Click += async (s, args) => await DeleteBucketAsync(bucket);
            contextMenu.Items.Add(deleteItem);

            contextMenu.IsOpen = true;
        }
    }

    private async Task RenameBucketAsync(BucketViewModel bucket)
    {
        var dialog = new ContentDialog
        {
            Title = "버킷 이름 변경",
            Content = new System.Windows.Controls.TextBox
            {
                Text = bucket.Name,
                Width = 300
            },
            PrimaryButtonText = "변경",
            CloseButtonText = "취소"
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var textBox = dialog.Content as System.Windows.Controls.TextBox;
            if (!string.IsNullOrWhiteSpace(textBox?.Text) && textBox.Text != bucket.Name)
            {
                // API 호출하여 이름 변경 (GraphPlannerService에 메서드 추가 필요)
                bucket.Name = textBox.Text;
                Log4.Info($"Planner 버킷 이름 변경: {bucket.Name}");
            }
        }
    }

    private async Task DeleteBucketAsync(BucketViewModel bucket)
    {
        if (string.IsNullOrEmpty(bucket.ETag))
        {
            Log4.Warn("Planner 버킷 삭제 실패: ETag 없음");
            return;
        }

        var plannerService = ((App)Application.Current).GetService<GraphPlannerService>()!;
        var success = await plannerService.DeleteBucketAsync(bucket.Id, bucket.ETag);
        if (success)
        {
            _plannerViewModel?.Buckets.Remove(bucket);
            Log4.Info($"Planner 버킷 삭제: {bucket.Name}");
        }
    }

    /// <summary>
    /// 작업 카드 드래그 시작 및 더블클릭 감지
    /// </summary>
    private async void PlannerTaskCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _plannerTaskDragStartPoint = e.GetPosition(null);
        if (sender is FrameworkElement element && element.Tag is TaskItemViewModel task)
        {
            // 더블클릭 감지
            if (e.ClickCount == 2)
            {
                await ShowTaskEditDialogAsync(task);
                e.Handled = true;
                return;
            }

            _plannerDraggedTask = task;
            _plannerDragSourceBucket = _plannerViewModel?.Buckets.FirstOrDefault(b => b.Tasks.Contains(task));
        }
    }

    /// <summary>
    /// 작업 카드 드래그 진행
    /// </summary>
    private void PlannerTaskCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _plannerDraggedTask == null || _plannerIsDragging)
            return;

        var diff = _plannerTaskDragStartPoint - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _plannerIsDragging = true;
        var dragData = new DataObject("PlannerTask", _plannerDraggedTask);
        dragData.SetData("SourceBucket", _plannerDragSourceBucket);
        DragDrop.DoDragDrop(sender as DependencyObject, dragData, DragDropEffects.Move);
        _plannerIsDragging = false;
        _plannerDraggedTask = null;
        _plannerDragSourceBucket = null;
    }

    /// <summary>
    /// 작업 카드 클릭 (선택)
    /// </summary>
    private void PlannerTaskCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 드래그 중이 아닌 경우에만 카드 선택
        if (!_plannerIsDragging && sender is FrameworkElement element && element.Tag is TaskItemViewModel task)
        {
            SelectPlannerTask(task);
        }
    }

    /// <summary>
    /// 작업 카드 선택
    /// </summary>
    private void SelectPlannerTask(TaskItemViewModel task)
    {
        if (_plannerViewModel == null)
            return;

        // 모든 버킷의 모든 태스크 선택 해제
        foreach (var bucket in _plannerViewModel.Buckets)
        {
            foreach (var t in bucket.Tasks)
            {
                t.IsSelected = false;
            }
        }

        // 선택한 태스크만 선택 상태로
        task.IsSelected = true;

        // 포커스를 플래너 뷰로 이동 (Delete 키 이벤트 수신용)
        PlannerViewBorder.Focus();
    }

    /// <summary>
    /// 현재 선택된 작업 가져오기
    /// </summary>
    private TaskItemViewModel? GetSelectedPlannerTask()
    {
        if (_plannerViewModel == null)
            return null;

        foreach (var bucket in _plannerViewModel.Buckets)
        {
            var selected = bucket.Tasks.FirstOrDefault(t => t.IsSelected);
            if (selected != null)
                return selected;
        }
        return null;
    }

    /// <summary>
    /// 플래너 뷰 키보드 이벤트 (Delete 키 등)
    /// </summary>
    private async void PlannerView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            var selectedTask = GetSelectedPlannerTask();
            if (selectedTask != null)
            {
                await DeletePlannerTaskWithConfirmAsync(selectedTask);
            }
        }
    }

    /// <summary>
    /// 컨텍스트 메뉴 - 열기
    /// </summary>
    private async void PlannerTaskContextMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
        {
            if (contextMenu.PlacementTarget is Border border && border.Tag is TaskItemViewModel task)
            {
                await ShowTaskEditDialogAsync(task);
            }
        }
    }

    /// <summary>
    /// 컨텍스트 메뉴 - 완료로 표시
    /// </summary>
    private async void PlannerTaskContextMenu_Complete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
        {
            if (contextMenu.PlacementTarget is Border border && border.Tag is TaskItemViewModel task)
            {
                if (_plannerViewModel != null)
                {
                    await _plannerViewModel.CompleteTaskAsync(task);
                }
            }
        }
    }

    /// <summary>
    /// 컨텍스트 메뉴 - 삭제
    /// </summary>
    private async void PlannerTaskContextMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
        {
            if (contextMenu.PlacementTarget is Border border && border.Tag is TaskItemViewModel task)
            {
                await DeletePlannerTaskWithConfirmAsync(task);
            }
        }
    }

    /// <summary>
    /// 작업 삭제 확인 후 삭제
    /// </summary>
    private async Task DeletePlannerTaskWithConfirmAsync(TaskItemViewModel task)
    {
        var dialog = new ContentDialog
        {
            Title = "작업 삭제",
            Content = $"'{task.Title}' 작업을 삭제하시겠습니까?\n\n이 작업은 영구적으로 삭제되며 복구할 수 없습니다.",
            PrimaryButtonText = "삭제",
            CloseButtonText = "취소",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (_plannerViewModel != null)
            {
                await _plannerViewModel.DeleteTaskAsync(task);
                Log4.Info($"[DeletePlannerTaskWithConfirmAsync] 작업 '{task.Title}' 삭제됨");
            }
        }
    }

    /// <summary>
    /// 버킷 드래그 진입
    /// </summary>
    private void PlannerBucket_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
            border.BorderThickness = new Thickness(2);
        }
    }

    /// <summary>
    /// 버킷 드래그 이탈
    /// </summary>
    private void PlannerBucket_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }
    }

    /// <summary>
    /// 버킷 드롭
    /// </summary>
    private async void PlannerBucket_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Thickness(0);
        }

        var task = e.Data.GetData("PlannerTask") as TaskItemViewModel;
        var sourceBucket = e.Data.GetData("SourceBucket") as BucketViewModel;
        var targetBucket = (sender as FrameworkElement)?.Tag as BucketViewModel;

        if (task != null && targetBucket != null && sourceBucket?.Id != targetBucket.Id)
        {
            if (_plannerViewModel != null)
            {
                var success = await _plannerViewModel.MoveTaskToBucketAsync(task, targetBucket.Id);
                if (success)
                {
                    // UI 스레드에서 컬렉션 업데이트
                    await Dispatcher.InvokeAsync(() =>
                    {
                        sourceBucket?.Tasks.Remove(task);
                        task.BucketId = targetBucket.Id;
                        targetBucket.Tasks.Add(task);
                    });
                    Log4.Info($"Planner 작업 이동: {task.Title} -> {targetBucket.Name}");
                }
            }
        }
    }

    /// <summary>
    /// 칸반 보드 마우스 휠 스크롤 (Shift+휠 또는 Ctrl+휠로 좌우 스크롤)
    /// </summary>
    private void PlannerBoardScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            // Shift+휠 또는 Ctrl+휠일 때 좌우 스크롤
            if (Keyboard.Modifiers == ModifierKeys.Shift || Keyboard.Modifiers == ModifierKeys.Control)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
            // 일반 휠은 기본 동작 (자식 스크롤뷰어로 전달하여 상하 스크롤)
        }
    }

    /// <summary>
    /// 내 작업 리스트 선택 변경
    /// </summary>
    private async void PlannerMyTasksListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlannerMyTasksListView.SelectedItem is TaskItemViewModel task)
        {
            await ShowTaskEditDialogAsync(task);
            PlannerMyTasksListView.SelectedItem = null;
        }
    }

    /// <summary>
    /// 내 작업 체크박스 클릭 (완료 토글)
    /// </summary>
    private async void PlannerMyTaskCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is TaskItemViewModel task)
        {
            await _plannerViewModel?.ToggleTaskCompleteCommand.ExecuteAsync(task);
        }
    }

    /// <summary>
    /// 작업 상세 편집 다이얼로그 표시
    /// </summary>
    private async Task ShowTaskEditDialogAsync(TaskItemViewModel task)
    {
        try
        {
            // 현재 플랜의 버킷 목록 가져오기
            var buckets = _plannerViewModel?.Buckets ?? new ObservableCollection<BucketViewModel>();
            var plannerService = ((App)Application.Current).GetService<GraphPlannerService>()!;

            // TaskEditDialog 열기
            var dialog = new TaskEditDialog(task, buckets, plannerService);
            dialog.Owner = this;

            var result = dialog.ShowDialog();

            if (result == true)
            {
                // 저장된 경우 - 다이얼로그 내에서 이미 API 호출하여 저장됨
                // 버킷 변경 시 UI 업데이트
                if (task.BucketId != dialog.SelectedBucketId && !string.IsNullOrEmpty(dialog.SelectedBucketId))
                {
                    var sourceBucket = _plannerViewModel?.Buckets.FirstOrDefault(b => b.Id != dialog.SelectedBucketId && b.Tasks.Contains(task));
                    var targetBucket = _plannerViewModel?.Buckets.FirstOrDefault(b => b.Id == dialog.SelectedBucketId);

                    sourceBucket?.Tasks.Remove(task);
                    if (targetBucket != null && !targetBucket.Tasks.Contains(task))
                    {
                        targetBucket.Tasks.Add(task);
                    }
                }

                Log4.Info($"Planner 작업 편집 완료: {task.Title}");
            }
            else if (dialog.IsDeleted)
            {
                // 삭제된 경우
                await _plannerViewModel?.DeleteTaskCommand.ExecuteAsync(task);
                Log4.Info($"Planner 작업 삭제: {task.Title}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"작업 편집 다이얼로그 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// Planner 데이터 로드
    /// </summary>
    private async Task LoadPlannerDataAsync()
    {
        try
        {
            if (_plannerViewModel == null)
            {
                _plannerViewModel = ((App)Application.Current).GetService<PlannerViewModel>()!;
            }

            await _plannerViewModel.LoadPlansAsync();

            // 플랜 목록 바인딩
            PlannerListBox.ItemsSource = _plannerViewModel.Plans;
            PlannerPinnedPlansItemsControl.ItemsSource = _plannerViewModel.PinnedPlans;

            // Empty state 처리
            if (_plannerViewModel.Plans.Count == 0)
            {
                PlannerEmptyState.Visibility = Visibility.Visible;
            }
            else
            {
                PlannerEmptyState.Visibility = Visibility.Collapsed;
            }

            // 초기 상태: 플랜 미선택 안내 표시, 보드 숨김
            PlannerBoardScrollViewer.Visibility = Visibility.Collapsed;
            PlannerMyTasksView.Visibility = Visibility.Collapsed;
            PlannerNoPlanSelected.Visibility = Visibility.Visible;

            Log4.Info($"Planner 데이터 로드 완료: {_plannerViewModel.Plans.Count}개 플랜");
        }
        catch (Exception ex)
        {
            Log4.Error($"Planner 데이터 로드 실패: {ex.Message}");
        }
    }

    #endregion

    #region Calls 이벤트 핸들러

    private CallsViewModel? _callsViewModel;

    /// <summary>
    /// 통화 새로고침 버튼 클릭
    /// </summary>
    private async void CallsRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_callsViewModel == null) return;

        await _callsViewModel.RefreshAsync();
        UpdateCallsContactsEmptyState();
        UpdateCallsMyStatus();
    }

    /// <summary>
    /// 상태 변경 버튼 클릭
    /// </summary>
    private void CallsStatusMenuButton_Click(object sender, RoutedEventArgs e)
    {
        // 상태 변경 메뉴 표시
        var contextMenu = new ContextMenu();

        var statuses = new[] {
            ("Available", "대화 가능"),
            ("Busy", "다른 용무 중"),
            ("DoNotDisturb", "방해 금지"),
            ("Away", "자리 비움"),
            ("Offline", "오프라인")
        };

        foreach (var (status, text) in statuses)
        {
            var menuItem = new System.Windows.Controls.MenuItem { Header = text, Tag = status };
            menuItem.Click += async (s, args) =>
            {
                if (_callsViewModel != null)
                {
                    await _callsViewModel.SetMyStatusAsync((string)((System.Windows.Controls.MenuItem)s!).Tag!);
                    UpdateCallsMyStatus();
                }
            };
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.IsOpen = true;
    }

    /// <summary>
    /// 연락처 검색 키 입력
    /// </summary>
    private async void CallsSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _callsViewModel != null)
        {
            _callsViewModel.SearchQuery = CallsSearchBox.Text;
            await _callsViewModel.SearchUsersAsync();

            // 검색 결과가 있으면 검색 결과 패널 표시
            if (_callsViewModel.SearchResults.Count > 0)
            {
                CallsSearchResultsPanel.Visibility = Visibility.Visible;
                CallsDefaultPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                CallsSearchResultsPanel.Visibility = Visibility.Collapsed;
                CallsDefaultPanel.Visibility = Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// 다이얼 패드 탭 클릭
    /// </summary>
    private void CallsDialPadTab_Click(object sender, RoutedEventArgs e)
    {
        CallsDialPadTab.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        CallsContactsTab.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        CallsDialPadPanel.Visibility = Visibility.Visible;
        CallsContactsPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 연락처 탭 클릭
    /// </summary>
    private void CallsContactsTab_Click(object sender, RoutedEventArgs e)
    {
        CallsDialPadTab.Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent;
        CallsContactsTab.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        CallsDialPadPanel.Visibility = Visibility.Collapsed;
        CallsContactsPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 다이얼 버튼 클릭
    /// </summary>
    private void DialButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag != null)
        {
            var digit = btn.Tag.ToString();
            CallsDialNumber.Text += digit;
            _callsViewModel?.DialDigit(digit!);
        }
    }

    /// <summary>
    /// 다이얼 지우기 버튼 클릭
    /// </summary>
    private void DialClearButton_Click(object sender, RoutedEventArgs e)
    {
        CallsDialNumber.Text = string.Empty;
        _callsViewModel?.ClearDial();
    }

    /// <summary>
    /// 다이얼 백스페이스 버튼 클릭
    /// </summary>
    private void DialBackspaceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CallsDialNumber.Text))
        {
            CallsDialNumber.Text = CallsDialNumber.Text[..^1];
        }
        _callsViewModel?.BackspaceDial();
    }

    /// <summary>
    /// 다이얼 통화 버튼 클릭
    /// </summary>
    private void DialCallButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CallsDialNumber.Text))
        {
            System.Windows.MessageBox.Show("전화번호를 입력해주세요.", "알림", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        _callsViewModel?.MakeCall();
        System.Windows.MessageBox.Show($"실제 통화 기능은 Azure Communication Services 연동이 필요합니다.\n번호: {CallsDialNumber.Text}",
            "알림", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>
    /// 연락처 리스트 선택 변경
    /// </summary>
    private void CallsContactsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView listView && listView.SelectedItem is ContactItemViewModel contact)
        {
            _callsViewModel?.SelectContact(contact);
        }
    }

    /// <summary>
    /// 검색 결과 리스트 선택 변경
    /// </summary>
    private void CallsSearchResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView listView && listView.SelectedItem is ContactItemViewModel contact)
        {
            _callsViewModel?.SelectContact(contact);
        }
    }

    /// <summary>
    /// 연락처 음성 통화 버튼 클릭
    /// </summary>
    private void ContactCallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is ContactItemViewModel contact)
        {
            _callsViewModel?.SelectContact(contact);
            _callsViewModel?.MakeCall();
            System.Windows.MessageBox.Show($"실제 통화 기능은 Azure Communication Services 연동이 필요합니다.\n대상: {contact.DisplayName}",
                "알림", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// 연락처 영상 통화 버튼 클릭
    /// </summary>
    private void ContactVideoCallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is ContactItemViewModel contact)
        {
            _callsViewModel?.SelectContact(contact);
            _callsViewModel?.MakeVideoCall();
            System.Windows.MessageBox.Show($"실제 영상 통화 기능은 Azure Communication Services 연동이 필요합니다.\n대상: {contact.DisplayName}",
                "알림", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// 검색 결과 음성 통화 버튼 클릭
    /// </summary>
    private void SearchResultCallButton_Click(object sender, RoutedEventArgs e)
    {
        ContactCallButton_Click(sender, e);
    }

    /// <summary>
    /// 검색 결과 영상 통화 버튼 클릭
    /// </summary>
    private void SearchResultVideoCallButton_Click(object sender, RoutedEventArgs e)
    {
        ContactVideoCallButton_Click(sender, e);
    }

    #endregion

    #region 설정 뷰 관련

    private string _selectedSettingsMainMenu = "";
    private string _selectedSettingsSubMenu = "";

    /// <summary>
    /// 설정 뷰 표시
    /// </summary>
    private void ShowSettingsView()
    {
        HideAllViews();
        if (SettingsViewBorder != null) SettingsViewBorder.Visibility = Visibility.Visible;
        _viewModel.StatusMessage = "설정";

        // 대메뉴 초기화
        InitializeSettingsMainMenu();

        // 기본 선택: AI 동기화
        SelectSettingsMainMenu("sync_ai");
    }

    /// <summary>
    /// 설정 대메뉴 초기화
    /// </summary>
    private void InitializeSettingsMainMenu()
    {
        if (SettingsMainMenuPanel == null) return;
        SettingsMainMenuPanel.Children.Clear();

        var mainMenuItems = new[]
        {
            ("sync_ai", "Bot24", "AI"),
            ("sync_ms365", "Cloud24", "MS365"),
            ("mail", "Mail24", "메일"),
            ("api", "Key24", "API 관리"),
            ("general", "Settings24", "기타 설정")
        };

        foreach (var (key, icon, text) in mainMenuItems)
        {
            var btn = CreateSettingsMainMenuButton(key, icon, text);
            SettingsMainMenuPanel.Children.Add(btn);
        }
    }

    /// <summary>
    /// 설정 대메뉴 버튼 생성 (좌측 세로 바 스타일)
    /// </summary>
    private Border CreateSettingsMainMenuButton(string key, string iconSymbol, string text)
    {
        // 선택 표시용 좌측 세로 바
        var indicator = new Border
        {
            Width = 3,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(2)
        };

        // 아이콘
        Wpf.Ui.Controls.SymbolIcon? icon = null;
        if (Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(iconSymbol, out var symbol))
        {
            icon = new Wpf.Ui.Controls.SymbolIcon
            {
                Symbol = symbol,
                FontSize = 18,
                Margin = new Thickness(8, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        // 텍스트
        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };

        // 내용 StackPanel
        var contentPanel = new StackPanel { Orientation = Orientation.Horizontal };
        contentPanel.Children.Add(indicator);
        if (icon != null) contentPanel.Children.Add(icon);
        contentPanel.Children.Add(textBlock);

        // 버튼 역할을 하는 Border
        var btn = new Border
        {
            Tag = key,
            Padding = new Thickness(0, 10, 16, 10),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = contentPanel
        };

        // 마우스 이벤트
        btn.MouseEnter += (s, e) =>
        {
            if (_selectedSettingsMainMenu != key)
                btn.Background = (Brush)FindResource("ControlFillColorDefaultBrush");
        };
        btn.MouseLeave += (s, e) =>
        {
            if (_selectedSettingsMainMenu != key)
                btn.Background = Brushes.Transparent;
        };
        btn.MouseLeftButtonUp += (s, e) => SelectSettingsMainMenu(key);

        return btn;
    }

    /// <summary>
    /// 설정 대메뉴 선택 처리
    /// </summary>
    private void SelectSettingsMainMenu(string menuKey)
    {
        _selectedSettingsMainMenu = menuKey;
        UpdateSettingsSubMenu(menuKey);

        // 대메뉴 선택 상태 업데이트 (좌측 세로 바 표시)
        UpdateSettingsMainMenuSelection(menuKey);

        // 첫 번째 소메뉴 자동 선택
        var subMenuItems = GetSubMenuItems(menuKey);
        if (subMenuItems.Length > 0)
        {
            SelectSettingsSubMenu(subMenuItems[0].key);
        }
    }

    /// <summary>
    /// 대메뉴 선택 상태 업데이트 (좌측 세로 바 표시)
    /// </summary>
    private void UpdateSettingsMainMenuSelection(string selectedKey)
    {
        if (SettingsMainMenuPanel == null) return;

        foreach (var child in SettingsMainMenuPanel.Children)
        {
            if (child is Border btn && btn.Child is StackPanel contentPanel && contentPanel.Children.Count > 0)
            {
                var key = btn.Tag?.ToString();
                var indicator = contentPanel.Children[0] as Border;

                if (indicator != null)
                {
                    if (key == selectedKey)
                    {
                        // 선택됨: 녹색 세로 바 표시
                        indicator.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 녹색
                        btn.Background = Brushes.Transparent;
                    }
                    else
                    {
                        // 선택 안됨: 세로 바 숨김
                        indicator.Background = Brushes.Transparent;
                        btn.Background = Brushes.Transparent;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 소메뉴 항목 정의
    /// </summary>
    private (string key, string text)[] GetSubMenuItems(string mainMenuKey)
    {
        return mainMenuKey switch
        {
            "sync_ai" => new[] { ("sync_ai_favorite", "즐겨찾기"), ("sync_ai_all", "전체") },
            "sync_ms365" => new[] { ("sync_ms365_favorite", "즐겨찾기"), ("sync_ms365_all", "전체") },
            "mail" => new[] { ("mail_signature", "서명 관리") },
            "api" => new[] { ("api_ai_providers", "AI Provider"), ("api_tinymce", "TinyMCE") },
            "general" => new[] { ("general_theme", "일반"), ("general_account", "계정") },
            _ => Array.Empty<(string, string)>()
        };
    }

    /// <summary>
    /// 설정 소메뉴 업데이트
    /// </summary>
    private void UpdateSettingsSubMenu(string mainMenuKey)
    {
        if (SettingsSubMenuPanel == null || SettingsSubMenuTitle == null) return;

        SettingsSubMenuPanel.Children.Clear();
        SettingsSubMenuTitle.Text = mainMenuKey switch
        {
            "sync_ai" => "AI 동기화",
            "sync_ms365" => "MS365 동기화",
            "mail" => "메일",
            "api" => "API 관리",
            "general" => "기타 설정",
            _ => ""
        };

        foreach (var (key, text) in GetSubMenuItems(mainMenuKey))
        {
            var btn = CreateSettingsSubMenuButton(key, text);
            SettingsSubMenuPanel.Children.Add(btn);
        }
    }

    /// <summary>
    /// 설정 소메뉴 버튼 생성 (좌측 세로 바 스타일)
    /// </summary>
    private Border CreateSettingsSubMenuButton(string key, string text)
    {
        // 선택 표시용 좌측 세로 바
        var indicator = new Border
        {
            Width = 3,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(2)
        };

        // 텍스트
        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Margin = new Thickness(12, 0, 0, 0)
        };

        // 내용 StackPanel
        var contentPanel = new StackPanel { Orientation = Orientation.Horizontal };
        contentPanel.Children.Add(indicator);
        contentPanel.Children.Add(textBlock);

        // 버튼 역할을 하는 Border
        var btn = new Border
        {
            Tag = key,
            Padding = new Thickness(0, 8, 16, 8),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            Child = contentPanel
        };

        // 마우스 이벤트
        btn.MouseEnter += (s, e) =>
        {
            if (_selectedSettingsSubMenu != key)
                btn.Background = (Brush)FindResource("ControlFillColorDefaultBrush");
        };
        btn.MouseLeave += (s, e) =>
        {
            if (_selectedSettingsSubMenu != key)
                btn.Background = Brushes.Transparent;
        };
        btn.MouseLeftButtonUp += (s, e) => SelectSettingsSubMenu(key);

        return btn;
    }

    /// <summary>
    /// 설정 소메뉴 선택 처리
    /// </summary>
    private void SelectSettingsSubMenu(string subMenuKey)
    {
        _selectedSettingsSubMenu = subMenuKey;

        // 소메뉴 선택 상태 업데이트 (좌측 세로 바 표시)
        UpdateSettingsSubMenuSelection(subMenuKey);

        UpdateSettingsContent(subMenuKey);
    }

    /// <summary>
    /// 소메뉴 선택 상태 업데이트 (좌측 세로 바 표시)
    /// </summary>
    private void UpdateSettingsSubMenuSelection(string selectedKey)
    {
        if (SettingsSubMenuPanel == null) return;

        foreach (var child in SettingsSubMenuPanel.Children)
        {
            if (child is Border btn && btn.Child is StackPanel contentPanel && contentPanel.Children.Count > 0)
            {
                var key = btn.Tag?.ToString();
                var indicator = contentPanel.Children[0] as Border;

                if (indicator != null)
                {
                    if (key == selectedKey)
                    {
                        // 선택됨: 녹색 세로 바 표시
                        indicator.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 녹색
                        btn.Background = Brushes.Transparent;
                    }
                    else
                    {
                        // 선택 안됨: 세로 바 숨김
                        indicator.Background = Brushes.Transparent;
                        btn.Background = Brushes.Transparent;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 설정 내용 패널 업데이트
    /// </summary>
    private void UpdateSettingsContent(string subMenuKey)
    {
        if (SettingsContentPanel == null) return;
        SettingsContentPanel.Children.Clear();

        switch (subMenuKey)
        {
            case "sync_ai_favorite":
                ShowAiSyncFavoriteSettings();
                break;
            case "sync_ai_all":
                ShowAiSyncAllSettings();
                break;
            case "sync_ms365_favorite":
                ShowMs365SyncFavoriteSettings();
                break;
            case "sync_ms365_all":
                ShowMs365SyncAllSettings();
                break;
            case "mail_signature":
                ShowSignatureSettings();
                break;
            case "api_ai_providers":
                ShowAiProviderSettings();
                break;
            case "api_tinymce":
                ShowTinyMCESettings();
                break;
            case "general_theme":
                ShowGeneralSettings();
                break;
            case "general_account":
                ShowAccountSettings();
                break;
        }
    }

    /// <summary>
    /// 설정 섹션 헤더 생성
    /// </summary>
    private System.Windows.Controls.TextBlock CreateSettingsSectionHeader(string title)
    {
        return new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16)
        };
    }

    /// <summary>
    /// 설정 그룹 Border 생성
    /// </summary>
    private Border CreateSettingsGroupBorder()
    {
        return new Border
        {
            Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16)
        };
    }

    /// <summary>
    /// 설정 라벨 생성
    /// </summary>
    private System.Windows.Controls.TextBlock CreateSettingsLabel(string text)
    {
        return new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 8)
        };
    }

    /// <summary>
    /// 설정 설명 텍스트 생성
    /// </summary>
    private System.Windows.Controls.TextBlock CreateSettingsDescription(string text)
    {
        return new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
    }

    /// <summary>
    /// 저장 버튼 생성
    /// </summary>
    private Wpf.Ui.Controls.Button CreateSaveButton(Action saveAction)
    {
        var btn = new Wpf.Ui.Controls.Button
        {
            Content = "저장",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(24, 8, 24, 8)
        };
        btn.Click += (s, e) => saveAction();
        return btn;
    }

    #region AI 동기화 설정 (즐겨찾기/전체)

    /// <summary>
    /// AI 동기화 즐겨찾기 설정 UI 표시
    /// </summary>
    private void ShowAiSyncFavoriteSettings()
    {
        if (SettingsContentPanel == null) return;
        var prefs = App.Settings.UserPreferences;

        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("AI 동기화 - 즐겨찾기"));

        // AI 분석 주기 (라디오버튼 선택)
        var intervalGroup = CreateSettingsGroupBorder();
        var intervalStack = new StackPanel();
        intervalStack.Children.Add(CreateSettingsLabel("AI 분석 주기"));

        var currentInterval = prefs.AiAnalysisIntervalSeconds > 0 ? prefs.AiAnalysisIntervalSeconds : 300;
        var intervalOptions = new[] { (1, "1초"), (5, "5초"), (10, "10초"), (30, "30초"), (60, "1분"), (300, "5분") };

        var intervalWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (seconds, label) in intervalOptions)
        {
            var radio = new RadioButton
            {
                Content = label,
                Tag = seconds,
                IsChecked = currentInterval == seconds,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "AiFavoriteInterval"
            };
            radio.Checked += (s, e) =>
            {
                prefs.AiAnalysisIntervalSeconds = seconds;
                App.Settings.SaveUserPreferences();
                Log4.Info($"AI 분석 주기 저장: {seconds}초");
            };
            intervalWrap.Children.Add(radio);
        }
        intervalStack.Children.Add(intervalWrap);
        intervalStack.Children.Add(CreateSettingsDescription("즐겨찾기 메일에 대한 AI 분석 주기입니다."));

        intervalGroup.Child = intervalStack;
        SettingsContentPanel.Children.Add(intervalGroup);
    }

    /// <summary>
    /// AI 동기화 전체 설정 UI 표시
    /// </summary>
    private void ShowAiSyncAllSettings()
    {
        if (SettingsContentPanel == null) return;
        var prefs = App.Settings.UserPreferences;

        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("AI 동기화 - 전체"));

        // AI 분석 기간 (라디오버튼 선택)
        var periodGroup = CreateSettingsGroupBorder();
        var periodStack = new StackPanel();
        periodStack.Children.Add(CreateSettingsLabel("분석 대상 기간"));

        var currentPeriod = $"{prefs.AiAnalysisPeriodType}:{prefs.AiAnalysisPeriodValue}";
        var periodOptions = new[] { ("Count:5", "최근 5건"), ("Days:1", "하루"), ("Weeks:1", "1주일"), ("Months:1", "1달"), ("Years:1", "1년"), ("All:0", "전체") };

        var periodWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (value, label) in periodOptions)
        {
            var parts = value.Split(':');
            var radio = new RadioButton
            {
                Content = label,
                Tag = value,
                IsChecked = currentPeriod == value,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "AiAllPeriod"
            };
            radio.Checked += (s, e) =>
            {
                prefs.AiAnalysisPeriodType = parts[0];
                prefs.AiAnalysisPeriodValue = int.Parse(parts[1]);
                App.Settings.SaveUserPreferences();
                Log4.Info($"AI 분석 기간 저장: {parts[0]}:{parts[1]}");
            };
            periodWrap.Children.Add(radio);
        }
        periodStack.Children.Add(periodWrap);
        periodStack.Children.Add(CreateSettingsDescription("AI 분석 대상 메일 범위입니다."));

        periodGroup.Child = periodStack;
        SettingsContentPanel.Children.Add(periodGroup);

        // AI 분석 주기 (라디오버튼 선택)
        var intervalGroup = CreateSettingsGroupBorder();
        var intervalStack = new StackPanel();
        intervalStack.Children.Add(CreateSettingsLabel("분석 주기"));

        var currentInterval = prefs.AiAnalysisIntervalSeconds > 0 ? prefs.AiAnalysisIntervalSeconds : 300;
        var intervalOptions = new[] { (1, "1초"), (5, "5초"), (10, "10초"), (30, "30초"), (60, "1분"), (300, "5분"), (600, "10분"), (1800, "30분"), (3600, "1시간") };

        var intervalWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (seconds, label) in intervalOptions)
        {
            var radio = new RadioButton
            {
                Content = label,
                Tag = seconds,
                IsChecked = currentInterval == seconds,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "AiAllInterval"
            };
            radio.Checked += (s, e) =>
            {
                prefs.AiAnalysisIntervalSeconds = seconds;
                App.Settings.SaveUserPreferences();
                Log4.Info($"AI 분석 주기 저장: {seconds}초");
            };
            intervalWrap.Children.Add(radio);
        }
        intervalStack.Children.Add(intervalWrap);
        intervalStack.Children.Add(CreateSettingsDescription("전체 메일에 대한 AI 분석 주기입니다."));

        intervalGroup.Child = intervalStack;
        SettingsContentPanel.Children.Add(intervalGroup);
    }

    #endregion

    #region MS365 동기화 설정 (즐겨찾기/전체)

    /// <summary>
    /// MS365 동기화 즐겨찾기 설정 UI 표시
    /// </summary>
    private void ShowMs365SyncFavoriteSettings()
    {
        if (SettingsContentPanel == null) return;
        var prefs = App.Settings.UserPreferences;

        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("MS365 동기화 - 즐겨찾기"));

        var intervalOptions = new[] { (1, "1초"), (5, "5초"), (10, "10초"), (30, "30초"), (60, "1분"), (300, "5분") };

        // 메일 동기화 주기 (라디오버튼)
        var mailGroup = CreateSettingsGroupBorder();
        var mailStack = new StackPanel();
        mailStack.Children.Add(CreateSettingsLabel("메일 동기화 주기"));

        var mailIntervalSeconds = prefs.MailSyncIntervalSeconds > 0 ? prefs.MailSyncIntervalSeconds : prefs.MailSyncIntervalMinutes * 60;
        var mailWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (seconds, label) in intervalOptions)
        {
            var radio = new RadioButton
            {
                Content = label,
                Tag = seconds,
                IsChecked = mailIntervalSeconds == seconds,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "Ms365FavoriteMailInterval"
            };
            radio.Checked += (s, e) =>
            {
                prefs.MailSyncIntervalSeconds = seconds;
                App.Settings.SaveUserPreferences();
                Log4.Info($"메일 동기화 주기 저장: {seconds}초");
            };
            mailWrap.Children.Add(radio);
        }
        mailStack.Children.Add(mailWrap);
        mailStack.Children.Add(CreateSettingsDescription("즐겨찾기 메일의 동기화 주기입니다."));

        mailGroup.Child = mailStack;
        SettingsContentPanel.Children.Add(mailGroup);

        // 캘린더 동기화 주기 (라디오버튼)
        var calendarGroup = CreateSettingsGroupBorder();
        var calendarStack = new StackPanel();
        calendarStack.Children.Add(CreateSettingsLabel("캘린더 동기화 주기"));

        var calendarWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (seconds, label) in intervalOptions)
        {
            var radio = new RadioButton
            {
                Content = label,
                Tag = seconds,
                IsChecked = prefs.CalendarSyncIntervalSeconds == seconds,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "Ms365FavoriteCalendarInterval"
            };
            radio.Checked += (s, e) =>
            {
                prefs.CalendarSyncIntervalSeconds = seconds;
                App.Settings.SaveUserPreferences();
                Log4.Info($"캘린더 동기화 주기 저장: {seconds}초");
            };
            calendarWrap.Children.Add(radio);
        }
        calendarStack.Children.Add(calendarWrap);
        calendarStack.Children.Add(CreateSettingsDescription("즐겨찾기 캘린더의 동기화 주기입니다."));

        calendarGroup.Child = calendarStack;
        SettingsContentPanel.Children.Add(calendarGroup);

        // 채팅 동기화 주기 (라디오버튼)
        var chatGroup = CreateSettingsGroupBorder();
        var chatStack = new StackPanel();
        chatStack.Children.Add(CreateSettingsLabel("채팅 동기화 주기"));

        var chatWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (seconds, label) in intervalOptions)
        {
            var radio = new RadioButton
            {
                Content = label,
                Tag = seconds,
                IsChecked = prefs.ChatSyncIntervalSeconds == seconds,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "Ms365FavoriteChatInterval"
            };
            radio.Checked += (s, e) =>
            {
                prefs.ChatSyncIntervalSeconds = seconds;
                App.Settings.SaveUserPreferences();
                Log4.Info($"채팅 동기화 주기 저장: {seconds}초");
            };
            chatWrap.Children.Add(radio);
        }
        chatStack.Children.Add(chatWrap);
        chatStack.Children.Add(CreateSettingsDescription("즐겨찾기 채팅의 동기화 주기입니다."));

        chatGroup.Child = chatStack;
        SettingsContentPanel.Children.Add(chatGroup);
    }

    /// <summary>
    /// MS365 동기화 전체 설정 UI 표시
    /// </summary>
    private void ShowMs365SyncAllSettings()
    {
        if (SettingsContentPanel == null) return;
        var prefs = App.Settings.UserPreferences;

        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("MS365 동기화 - 전체"));

        // 메일 동기화 기간 (라디오버튼 선택)
        var periodGroup = CreateSettingsGroupBorder();
        var periodStack = new StackPanel();
        periodStack.Children.Add(CreateSettingsLabel("메일 동기화 대상 기간"));

        var currentPeriod = $"{prefs.MailSyncPeriodType}:{prefs.MailSyncPeriodValue}";
        var periodOptions = new[] { ("Count:5", "최근 5건"), ("Days:1", "하루"), ("Weeks:1", "1주일"), ("Months:1", "1달"), ("Years:1", "1년"), ("All:0", "전체") };

        var periodWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (value, label) in periodOptions)
        {
            var parts = value.Split(':');
            var radio = new RadioButton
            {
                Content = label,
                Tag = value,
                IsChecked = currentPeriod == value,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "Ms365AllPeriod"
            };
            radio.Checked += (s, e) =>
            {
                prefs.MailSyncPeriodType = parts[0];
                prefs.MailSyncPeriodValue = int.Parse(parts[1]);
                App.Settings.SaveUserPreferences();
                Log4.Info($"메일 동기화 기간 저장: {parts[0]}:{parts[1]}");
            };
            periodWrap.Children.Add(radio);
        }
        periodStack.Children.Add(periodWrap);
        periodStack.Children.Add(CreateSettingsDescription("동기화할 메일 범위입니다."));

        periodGroup.Child = periodStack;
        SettingsContentPanel.Children.Add(periodGroup);

        // 메일 동기화 주기 (라디오버튼 선택)
        var intervalGroup = CreateSettingsGroupBorder();
        var intervalStack = new StackPanel();
        intervalStack.Children.Add(CreateSettingsLabel("메일 동기화 주기"));

        var mailIntervalSeconds = prefs.MailSyncIntervalSeconds > 0 ? prefs.MailSyncIntervalSeconds : prefs.MailSyncIntervalMinutes * 60;
        var intervalOptions = new[] { (1, "1초"), (5, "5초"), (10, "10초"), (30, "30초"), (60, "1분"), (300, "5분"), (600, "10분"), (1800, "30분"), (3600, "1시간") };

        var intervalWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (seconds, label) in intervalOptions)
        {
            var radio = new RadioButton
            {
                Content = label,
                Tag = seconds,
                IsChecked = mailIntervalSeconds == seconds,
                Margin = new Thickness(0, 0, 16, 8),
                GroupName = "Ms365AllInterval"
            };
            radio.Checked += (s, e) =>
            {
                prefs.MailSyncIntervalSeconds = seconds;
                App.Settings.SaveUserPreferences();
                Log4.Info($"메일 동기화 주기 저장: {seconds}초");
            };
            intervalWrap.Children.Add(radio);
        }
        intervalStack.Children.Add(intervalWrap);
        intervalStack.Children.Add(CreateSettingsDescription("전체 메일에 대한 동기화 주기입니다."));

        intervalGroup.Child = intervalStack;
        SettingsContentPanel.Children.Add(intervalGroup);
    }

    #endregion

    #region 서명 설정

    private ListBox? _signatureListBox;
    private Wpf.Ui.Controls.TextBox? _signatureNameBox;
    private System.Windows.Controls.TextBox? _signatureContentBox;
    private string? _currentSignatureId;

    /// <summary>
    /// 서명 설정 UI 표시
    /// </summary>
    private void ShowSignatureSettings()
    {
        if (SettingsContentPanel == null) return;

        var signatureSettings = App.Settings.Signature;

        // 헤더
        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("서명 관리"));

        // 2단 레이아웃 (좌: 서명 목록, 우: 편집)
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 좌측: 서명 목록
        var listGroup = CreateSettingsGroupBorder();
        listGroup.Margin = new Thickness(0, 0, 12, 0);
        var listStack = new StackPanel();

        listStack.Children.Add(CreateSettingsLabel("서명 목록"));

        _signatureListBox = new ListBox
        {
            Height = 200,
            Margin = new Thickness(0, 0, 0, 12)
        };
        RefreshSignatureList(signatureSettings);
        _signatureListBox.SelectionChanged += (s, e) => OnSignatureSelectionChanged(signatureSettings);
        listStack.Children.Add(_signatureListBox);

        // 추가/삭제 버튼
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var addBtn = new Wpf.Ui.Controls.Button { Content = "추가", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 6, 16, 6) };
        addBtn.Click += (s, e) => AddNewSignature(signatureSettings);
        var delBtn = new Wpf.Ui.Controls.Button { Content = "삭제", Appearance = Wpf.Ui.Controls.ControlAppearance.Danger, Padding = new Thickness(16, 6, 16, 6) };
        delBtn.Click += (s, e) => DeleteSelectedSignature(signatureSettings);
        btnPanel.Children.Add(addBtn);
        btnPanel.Children.Add(delBtn);
        listStack.Children.Add(btnPanel);

        listGroup.Child = listStack;
        Grid.SetColumn(listGroup, 0);
        mainGrid.Children.Add(listGroup);

        // 우측: 서명 편집
        var editGroup = CreateSettingsGroupBorder();
        var editStack = new StackPanel();

        editStack.Children.Add(CreateSettingsLabel("서명 이름"));
        _signatureNameBox = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "서명 이름을 입력하세요",
            Margin = new Thickness(0, 0, 0, 12)
        };
        _signatureNameBox.TextChanged += (s, e) => SaveCurrentSignature(signatureSettings);
        editStack.Children.Add(_signatureNameBox);

        editStack.Children.Add(CreateSettingsLabel("서명 내용 (텍스트)"));
        _signatureContentBox = new System.Windows.Controls.TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            Margin = new Thickness(0, 0, 0, 12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _signatureContentBox.TextChanged += (s, e) => SaveCurrentSignature(signatureSettings);
        editStack.Children.Add(_signatureContentBox);

        editStack.Children.Add(CreateSettingsDescription("HTML 서명은 텍스트 내용을 기반으로 자동 생성됩니다."));

        editGroup.Child = editStack;
        Grid.SetColumn(editGroup, 1);
        mainGrid.Children.Add(editGroup);

        SettingsContentPanel.Children.Add(mainGrid);

        // 기본 서명 설정 그룹
        var defaultGroup = CreateSettingsGroupBorder();
        defaultGroup.Margin = new Thickness(0, 16, 0, 0);
        var defaultStack = new StackPanel();

        defaultStack.Children.Add(CreateSettingsLabel("기본 서명 설정"));

        var autoNewMailCheck = new CheckBox
        {
            Content = "새 메일에 자동으로 서명 추가",
            IsChecked = signatureSettings.AutoAddToNewMail,
            Margin = new Thickness(0, 8, 0, 4)
        };
        autoNewMailCheck.Checked += (s, e) => { signatureSettings.AutoAddToNewMail = true; App.Settings.SaveSignature(); };
        autoNewMailCheck.Unchecked += (s, e) => { signatureSettings.AutoAddToNewMail = false; App.Settings.SaveSignature(); };
        defaultStack.Children.Add(autoNewMailCheck);

        var autoReplyCheck = new CheckBox
        {
            Content = "답장/전달에 자동으로 서명 추가",
            IsChecked = signatureSettings.AutoAddToReplyForward,
            Margin = new Thickness(0, 4, 0, 0)
        };
        autoReplyCheck.Checked += (s, e) => { signatureSettings.AutoAddToReplyForward = true; App.Settings.SaveSignature(); };
        autoReplyCheck.Unchecked += (s, e) => { signatureSettings.AutoAddToReplyForward = false; App.Settings.SaveSignature(); };
        defaultStack.Children.Add(autoReplyCheck);

        defaultGroup.Child = defaultStack;
        SettingsContentPanel.Children.Add(defaultGroup);

        // 첫 번째 서명 선택
        if (_signatureListBox.Items.Count > 0)
            _signatureListBox.SelectedIndex = 0;
    }

    private void RefreshSignatureList(Models.Settings.SignatureSettings signatureSettings)
    {
        if (_signatureListBox == null) return;
        _signatureListBox.Items.Clear();
        foreach (var sig in signatureSettings.Signatures)
        {
            _signatureListBox.Items.Add(new ListBoxItem { Content = sig.Name, Tag = sig.Id });
        }
    }

    private void OnSignatureSelectionChanged(Models.Settings.SignatureSettings signatureSettings)
    {
        if (_signatureListBox?.SelectedItem is not ListBoxItem item) return;
        var sigId = item.Tag?.ToString();
        var sig = signatureSettings.Signatures.Find(s => s.Id == sigId);
        if (sig == null) return;

        _currentSignatureId = sig.Id;
        if (_signatureNameBox != null) _signatureNameBox.Text = sig.Name;
        if (_signatureContentBox != null) _signatureContentBox.Text = sig.PlainTextContent;
    }

    private void SaveCurrentSignature(Models.Settings.SignatureSettings signatureSettings)
    {
        if (string.IsNullOrEmpty(_currentSignatureId)) return;
        var sig = signatureSettings.Signatures.Find(s => s.Id == _currentSignatureId);
        if (sig == null) return;

        sig.Name = _signatureNameBox?.Text ?? "";
        sig.PlainTextContent = _signatureContentBox?.Text ?? "";
        sig.HtmlContent = $"<p>{System.Net.WebUtility.HtmlEncode(sig.PlainTextContent).Replace("\n", "<br/>")}</p>";
        sig.ModifiedAt = DateTime.Now;

        App.Settings.SaveSignature();

        // 목록 업데이트
        if (_signatureListBox?.SelectedItem is ListBoxItem item)
        {
            item.Content = sig.Name;
        }
    }

    private void AddNewSignature(Models.Settings.SignatureSettings signatureSettings)
    {
        var newSig = new Models.Settings.EmailSignature
        {
            Name = $"새 서명 {signatureSettings.Signatures.Count + 1}",
            PlainTextContent = "",
            HtmlContent = ""
        };
        signatureSettings.Signatures.Add(newSig);
        App.Settings.SaveSignature();
        RefreshSignatureList(signatureSettings);
        if (_signatureListBox != null)
            _signatureListBox.SelectedIndex = _signatureListBox.Items.Count - 1;
    }

    private void DeleteSelectedSignature(Models.Settings.SignatureSettings signatureSettings)
    {
        if (_signatureListBox?.SelectedItem is not ListBoxItem item) return;
        var sigId = item.Tag?.ToString();
        var sig = signatureSettings.Signatures.Find(s => s.Id == sigId);
        if (sig == null) return;

        signatureSettings.Signatures.Remove(sig);
        App.Settings.SaveSignature();
        _currentSignatureId = null;
        RefreshSignatureList(signatureSettings);
        if (_signatureListBox.Items.Count > 0)
            _signatureListBox.SelectedIndex = 0;
    }

    #endregion

    #region AI Provider 설정

    /// <summary>
    /// AI Provider 설정 UI 표시
    /// </summary>
    // AI Provider 설정용 필드
    private Dictionary<string, RadioButton>? _providerRadioButtons;
    private Dictionary<string, Wpf.Ui.Controls.TextBox>? _providerApiKeyBoxes;
    private Dictionary<string, ComboBox>? _providerModelCombos;
    private Dictionary<string, Wpf.Ui.Controls.TextBox>? _providerBaseUrlBoxes;
    private Dictionary<string, System.Windows.Controls.TextBlock>? _providerStatusTexts;

    private void ShowAiProviderSettings()
    {
        if (SettingsContentPanel == null) return;

        var aiSettings = App.Settings.AIProviders;

        // 필드 초기화
        _providerRadioButtons = new Dictionary<string, RadioButton>();
        _providerApiKeyBoxes = new Dictionary<string, Wpf.Ui.Controls.TextBox>();
        _providerModelCombos = new Dictionary<string, ComboBox>();
        _providerBaseUrlBoxes = new Dictionary<string, Wpf.Ui.Controls.TextBox>();
        _providerStatusTexts = new Dictionary<string, System.Windows.Controls.TextBlock>();

        // 헤더
        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("AI Provider 설정"));

        // 설명
        var descGroup = CreateSettingsGroupBorder();
        var descStack = new StackPanel();
        descStack.Children.Add(CreateSettingsDescription("메일 분석에 사용할 AI Provider를 설정합니다. 라디오버튼으로 대표 Provider를 선택하세요."));
        descGroup.Child = descStack;
        SettingsContentPanel.Children.Add(descGroup);

        // 각 Provider 설정 (Expander 스타일)
        CreateProviderExpanderSection("Claude", aiSettings.Claude, aiSettings.DefaultProvider == "Claude",
            GetClaudeModels(), isLocal: false);

        CreateProviderExpanderSection("OpenAI", aiSettings.OpenAI, aiSettings.DefaultProvider == "OpenAI",
            GetOpenAIModels(), isLocal: false);

        CreateProviderExpanderSection("Gemini", aiSettings.Gemini, aiSettings.DefaultProvider == "Gemini",
            GetGeminiModels(), isLocal: false);

        CreateProviderExpanderSection("Ollama", aiSettings.Ollama, aiSettings.DefaultProvider == "Ollama",
            GetOllamaModels(), isLocal: true);

        CreateProviderExpanderSection("LMStudio", aiSettings.LMStudio, aiSettings.DefaultProvider == "LMStudio",
            GetLMStudioModels(), isLocal: true);

        // 고급 설정 섹션
        CreateAdvancedSettingsSection();
    }

    /// <summary>
    /// Provider별 모델 목록 - Claude
    /// </summary>
    private string[] GetClaudeModels() => new[]
    {
        "claude-sonnet-4-20250514",
        "claude-3-5-sonnet-20241022",
        "claude-3-5-haiku-20241022",
        "claude-3-opus-20240229",
        "claude-3-sonnet-20240229",
        "claude-3-haiku-20240307"
    };

    /// <summary>
    /// Provider별 모델 목록 - OpenAI
    /// </summary>
    private string[] GetOpenAIModels() => new[]
    {
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-4-turbo",
        "gpt-4",
        "gpt-3.5-turbo"
    };

    /// <summary>
    /// Provider별 모델 목록 - Gemini
    /// </summary>
    private string[] GetGeminiModels() => new[]
    {
        "gemini-2.0-flash",
        "gemini-1.5-pro",
        "gemini-1.5-flash",
        "gemini-1.0-pro"
    };

    /// <summary>
    /// Provider별 모델 목록 - Ollama (로컬)
    /// </summary>
    private string[] GetOllamaModels() => new[]
    {
        "llama3.3",
        "llama3.2",
        "llama3.1",
        "mistral",
        "mixtral",
        "codellama",
        "phi3"
    };

    /// <summary>
    /// Provider별 모델 목록 - LMStudio (로컬)
    /// </summary>
    private string[] GetLMStudioModels() => new[]
    {
        "local-model",
        "lmstudio-community/Meta-Llama-3.1-8B-Instruct-GGUF",
        "lmstudio-community/Mistral-7B-Instruct-v0.3-GGUF"
    };

    /// <summary>
    /// Provider Expander 섹션 생성 (라디오버튼 + 테스트 + 모델 콤보)
    /// </summary>
    private void CreateProviderExpanderSection(
        string providerName,
        Models.Settings.AIProviderConfig config,
        bool isDefault,
        string[] availableModels,
        bool isLocal)
    {
        if (SettingsContentPanel == null) return;

        var group = CreateSettingsGroupBorder();
        var mainStack = new StackPanel();

        // 헤더 영역 (라디오버튼 + Provider명 + 상태)
        var headerPanel = new Grid();
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 라디오버튼 (대표 Provider 선택)
        var radio = new RadioButton
        {
            GroupName = "DefaultAIProvider",
            IsChecked = isDefault,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        radio.Checked += (s, e) => OnDefaultProviderChanged(providerName);
        _providerRadioButtons![providerName] = radio;
        Grid.SetColumn(radio, 0);
        headerPanel.Children.Add(radio);

        // Provider 이름
        var nameText = new System.Windows.Controls.TextBlock
        {
            Text = providerName,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 1);
        headerPanel.Children.Add(nameText);

        // 상태 표시 (설정됨/미설정 또는 N개 모델 발견)
        var hasConfig = isLocal || !string.IsNullOrEmpty(config.ApiKey);
        var statusText = new System.Windows.Controls.TextBlock
        {
            Text = hasConfig ? "✓ 설정됨" : "미설정",
            Foreground = hasConfig ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")) : Brushes.Gray,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _providerStatusTexts![providerName] = statusText;
        Grid.SetColumn(statusText, 3);
        headerPanel.Children.Add(statusText);

        mainStack.Children.Add(headerPanel);

        // 설정 내용 패널
        var contentBorder = new Border
        {
            Margin = new Thickness(28, 12, 0, 0),
            Padding = new Thickness(0)
        };
        var contentStack = new StackPanel();

        // API 키 (로컬이 아닌 경우만)
        if (!isLocal)
        {
            contentStack.Children.Add(CreateSettingsLabel("API Key"));

            var apiKeyPanel = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            apiKeyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            apiKeyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // API 키 입력
            var apiKeyBox = new Wpf.Ui.Controls.TextBox
            {
                Text = MaskApiKey(config.ApiKey),
                PlaceholderText = "API 키를 입력하세요",
                Margin = new Thickness(0, 0, 8, 0),
                Tag = config.ApiKey // 원본 값 저장
            };
            apiKeyBox.GotFocus += (s, e) =>
            {
                // 포커스 받으면 원본 값 표시
                if (apiKeyBox.Tag is string originalKey && !string.IsNullOrEmpty(originalKey))
                {
                    apiKeyBox.Text = originalKey;
                }
            };
            apiKeyBox.LostFocus += (s, e) =>
            {
                // 포커스 잃으면 마스킹
                var newKey = apiKeyBox.Text;
                apiKeyBox.Tag = newKey;
                config.ApiKey = newKey;
                apiKeyBox.Text = MaskApiKey(newKey);
                OnProviderSettingChanged(providerName, isLocal);
            };
            Grid.SetColumn(apiKeyBox, 0);
            apiKeyPanel.Children.Add(apiKeyBox);

            // 테스트 버튼
            var testButton = new Wpf.Ui.Controls.Button
            {
                Content = "테스트",
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                Padding = new Thickness(16, 6, 16, 6)
            };
            testButton.Click += async (s, e) =>
            {
                // 테스트 전 현재 입력값 저장
                var currentKey = apiKeyBox.Text;
                if (!currentKey.Contains("*"))
                {
                    config.ApiKey = currentKey;
                    apiKeyBox.Tag = currentKey;
                }
                await TestAndLoadModelsAsync(providerName, statusText);
            };
            Grid.SetColumn(testButton, 1);
            apiKeyPanel.Children.Add(testButton);

            contentStack.Children.Add(apiKeyPanel);
        }
        else
        {
            // 로컬 Provider의 경우 테스트 버튼만
            var testPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var testButton = new Wpf.Ui.Controls.Button
            {
                Content = "연결 테스트",
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                Padding = new Thickness(16, 6, 16, 6)
            };
            testButton.Click += async (s, e) => await TestAndLoadModelsAsync(providerName, statusText);
            testPanel.Children.Add(testButton);
            contentStack.Children.Add(testPanel);
        }

        // 모델 선택 (ComboBox)
        contentStack.Children.Add(CreateSettingsLabel("Model"));
        var modelCombo = new ComboBox
        {
            IsEditable = false,
            Margin = new Thickness(0, 0, 0, 12)
        };
        foreach (var model in availableModels)
        {
            modelCombo.Items.Add(model);
        }
        // 현재 모델이 목록에 없으면 추가
        if (!string.IsNullOrEmpty(config.Model) && !availableModels.Contains(config.Model))
        {
            modelCombo.Items.Insert(0, config.Model);
        }
        modelCombo.Text = config.Model ?? (availableModels.Length > 0 ? availableModels[0] : "");
        modelCombo.SelectionChanged += (s, e) => OnProviderSettingChanged(providerName, isLocal);
        modelCombo.LostFocus += (s, e) => OnProviderSettingChanged(providerName, isLocal);
        _providerModelCombos![providerName] = modelCombo;
        contentStack.Children.Add(modelCombo);

        // 고급 설정 (Expander)
        var advancedExpander = new Expander
        {
            Header = "고급 설정",
            IsExpanded = false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var advancedStack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        // Base URL
        advancedStack.Children.Add(CreateSettingsLabel("API URL"));
        var defaultUrl = GetDefaultProviderUrl(providerName);
        var baseUrlBox = new Wpf.Ui.Controls.TextBox
        {
            Text = config.BaseUrl ?? defaultUrl,
            PlaceholderText = defaultUrl,
            Margin = new Thickness(0, 0, 0, 8)
        };
        baseUrlBox.TextChanged += (s, e) => OnProviderSettingChanged(providerName, isLocal);
        _providerBaseUrlBoxes![providerName] = baseUrlBox;
        advancedStack.Children.Add(baseUrlBox);

        advancedStack.Children.Add(CreateSettingsDescription("API 엔드포인트 URL을 변경할 수 있습니다. 일반적으로 기본값을 사용합니다."));

        advancedExpander.Content = advancedStack;
        contentStack.Children.Add(advancedExpander);

        contentBorder.Child = contentStack;
        mainStack.Children.Add(contentBorder);

        group.Child = mainStack;
        SettingsContentPanel.Children.Add(group);
    }

    /// <summary>
    /// Provider 기본 URL 반환
    /// </summary>
    private string GetDefaultProviderUrl(string providerName)
    {
        return providerName switch
        {
            "Claude" => "https://api.anthropic.com",
            "OpenAI" => "https://api.openai.com/v1",
            "Gemini" => "https://generativelanguage.googleapis.com/v1beta",
            "Ollama" => "http://localhost:11434",
            "LMStudio" => "http://localhost:1234/v1",
            _ => ""
        };
    }

    /// <summary>
    /// API 키 마스킹 (앞 8자리와 뒤 4자리만 표시, 중간은 *** 처리)
    /// </summary>
    private string MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return "";
        if (apiKey.Length <= 12) return new string('*', apiKey.Length);

        // 앞 8자리 + *** + 뒤 4자리
        return apiKey.Substring(0, 8) + "***" + apiKey.Substring(apiKey.Length - 4);
    }

    /// <summary>
    /// API 테스트 후 모델 목록 불러오기
    /// </summary>
    private async Task TestAndLoadModelsAsync(string providerName, System.Windows.Controls.TextBlock statusText)
    {
        try
        {
            Log4.Info($"AI Provider 테스트 및 모델 로드 시작: {providerName}");

            var aiSettings = App.Settings.AIProviders;
            var config = providerName switch
            {
                "Claude" => aiSettings.Claude,
                "OpenAI" => aiSettings.OpenAI,
                "Gemini" => aiSettings.Gemini,
                "Ollama" => aiSettings.Ollama,
                "LMStudio" => aiSettings.LMStudio,
                _ => null
            };

            if (config == null)
            {
                ShowSettingsMessage($"{providerName} 설정을 찾을 수 없습니다.", isError: true);
                return;
            }

            // 로컬이 아닌 경우 API 키 확인
            bool isLocal = providerName == "Ollama" || providerName == "LMStudio";
            if (!isLocal && string.IsNullOrEmpty(config.ApiKey))
            {
                ShowSettingsMessage("API 키를 입력해주세요.", isError: true);
                return;
            }

            statusText.Text = "테스트 중...";
            statusText.Foreground = Brushes.Orange;

            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            var models = new List<string>();
            string baseUrl = config.BaseUrl ?? GetDefaultProviderUrl(providerName);

            switch (providerName)
            {
                case "OpenAI":
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
                    var openAiResponse = await httpClient.GetAsync($"{baseUrl}/models");
                    if (openAiResponse.IsSuccessStatusCode)
                    {
                        var json = await openAiResponse.Content.ReadAsStringAsync();
                        models = ParseOpenAIModels(json);
                    }
                    break;

                case "Claude":
                    // Claude API는 모델 목록 엔드포인트가 없으므로 연결 테스트만 수행
                    httpClient.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
                    httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                    // Claude는 목록을 제공하지 않으므로 기본 모델 목록 유지
                    models = GetClaudeModels().ToList();
                    break;

                case "Gemini":
                    var geminiResponse = await httpClient.GetAsync($"{baseUrl}/models?key={config.ApiKey}");
                    if (geminiResponse.IsSuccessStatusCode)
                    {
                        var json = await geminiResponse.Content.ReadAsStringAsync();
                        models = ParseGeminiModels(json);
                    }
                    break;

                case "Ollama":
                    var ollamaResponse = await httpClient.GetAsync($"{baseUrl}/api/tags");
                    if (ollamaResponse.IsSuccessStatusCode)
                    {
                        var json = await ollamaResponse.Content.ReadAsStringAsync();
                        models = ParseOllamaModels(json);
                    }
                    break;

                case "LMStudio":
                    var lmStudioResponse = await httpClient.GetAsync($"{baseUrl}/models");
                    if (lmStudioResponse.IsSuccessStatusCode)
                    {
                        var json = await lmStudioResponse.Content.ReadAsStringAsync();
                        models = ParseLMStudioModels(json);
                    }
                    break;
            }

            if (models.Count > 0)
            {
                // 모델 콤보박스 업데이트
                if (_providerModelCombos != null && _providerModelCombos.TryGetValue(providerName, out var modelCombo))
                {
                    var currentModel = modelCombo.Text;
                    modelCombo.Items.Clear();
                    foreach (var model in models)
                    {
                        modelCombo.Items.Add(model);
                    }
                    // 기존 선택된 모델이 목록에 있으면 선택 유지
                    if (!string.IsNullOrEmpty(currentModel) && models.Contains(currentModel))
                    {
                        modelCombo.Text = currentModel;
                    }
                    else if (!string.IsNullOrEmpty(currentModel))
                    {
                        modelCombo.Items.Insert(0, currentModel);
                        modelCombo.Text = currentModel;
                    }
                    else if (models.Count > 0)
                    {
                        modelCombo.SelectedIndex = 0;
                    }
                }

                statusText.Text = $"✓ {models.Count}개 모델 발견";
                statusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                Log4.Info($"AI Provider 모델 로드 완료: {providerName}, {models.Count}개");
            }
            else
            {
                statusText.Text = "✓ 연결됨";
                statusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
                Log4.Info($"AI Provider 연결 성공: {providerName} (모델 목록 없음)");
            }
        }
        catch (Exception ex)
        {
            statusText.Text = "연결 실패";
            statusText.Foreground = Brushes.Red;
            ShowSettingsMessage($"{providerName} 연결 오류: {ex.Message}", isError: true);
            Log4.Error($"AI Provider 테스트 오류: {providerName}, {ex.Message}");
        }
    }

    /// <summary>
    /// OpenAI 모델 목록 파싱
    /// </summary>
    private List<string> ParseOpenAIModels(string json)
    {
        var models = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                    {
                        var modelId = id.GetString();
                        // GPT 모델만 필터링 (선택적)
                        if (!string.IsNullOrEmpty(modelId))
                        {
                            models.Add(modelId);
                        }
                    }
                }
            }
            // GPT 모델을 우선 정렬
            models = models.OrderByDescending(m => m.StartsWith("gpt-4"))
                           .ThenByDescending(m => m.StartsWith("gpt-3"))
                           .ThenBy(m => m)
                           .ToList();
        }
        catch (Exception ex)
        {
            Log4.Error($"OpenAI 모델 파싱 오류: {ex.Message}");
        }
        return models;
    }

    /// <summary>
    /// Gemini 모델 목록 파싱
    /// </summary>
    private List<string> ParseGeminiModels(string json)
    {
        var models = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var item in modelsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var name))
                    {
                        var modelName = name.GetString();
                        if (!string.IsNullOrEmpty(modelName))
                        {
                            // "models/" 접두어 제거
                            models.Add(modelName.Replace("models/", ""));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"Gemini 모델 파싱 오류: {ex.Message}");
        }
        return models;
    }

    /// <summary>
    /// Ollama 모델 목록 파싱
    /// </summary>
    private List<string> ParseOllamaModels(string json)
    {
        var models = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var item in modelsArray.EnumerateArray())
                {
                    if (item.TryGetProperty("name", out var name))
                    {
                        var modelName = name.GetString();
                        if (!string.IsNullOrEmpty(modelName))
                        {
                            models.Add(modelName);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"Ollama 모델 파싱 오류: {ex.Message}");
        }
        return models;
    }

    /// <summary>
    /// LMStudio 모델 목록 파싱
    /// </summary>
    private List<string> ParseLMStudioModels(string json)
    {
        var models = new List<string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                    {
                        var modelId = id.GetString();
                        if (!string.IsNullOrEmpty(modelId))
                        {
                            models.Add(modelId);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"LMStudio 모델 파싱 오류: {ex.Message}");
        }
        return models;
    }

    /// <summary>
    /// 고급 설정 섹션 (전역)
    /// </summary>
    private void CreateAdvancedSettingsSection()
    {
        // 더 이상 사용하지 않음 - 각 Provider 내에 고급 설정 Expander로 이동
    }

    /// <summary>
    /// 대표 Provider 변경 시 처리
    /// </summary>
    private void OnDefaultProviderChanged(string providerName)
    {
        var aiSettings = App.Settings.AIProviders;
        aiSettings.DefaultProvider = providerName;
        App.Settings.SaveAIProviders();
        Log4.Info($"대표 AI Provider 변경: {providerName}");
    }

    /// <summary>
    /// Provider 설정 변경 시 자동 저장
    /// </summary>
    private void OnProviderSettingChanged(string providerName, bool isLocal)
    {
        var aiSettings = App.Settings.AIProviders;
        var config = providerName switch
        {
            "Claude" => aiSettings.Claude,
            "OpenAI" => aiSettings.OpenAI,
            "Gemini" => aiSettings.Gemini,
            "Ollama" => aiSettings.Ollama,
            "LMStudio" => aiSettings.LMStudio,
            _ => null
        };

        if (config == null) return;

        // API 키 저장
        if (!isLocal && _providerApiKeyBoxes != null && _providerApiKeyBoxes.TryGetValue(providerName, out var apiKeyBox))
        {
            config.ApiKey = apiKeyBox.Text;
        }

        // 모델 저장
        if (_providerModelCombos != null && _providerModelCombos.TryGetValue(providerName, out var modelCombo))
        {
            config.Model = modelCombo.Text;
        }

        // Base URL 저장
        if (_providerBaseUrlBoxes != null && _providerBaseUrlBoxes.TryGetValue(providerName, out var baseUrlBox))
        {
            config.BaseUrl = baseUrlBox.Text;
        }

        // 상태 텍스트 업데이트
        if (_providerStatusTexts != null && _providerStatusTexts.TryGetValue(providerName, out var statusText))
        {
            var hasConfig = isLocal || !string.IsNullOrEmpty(config.ApiKey);
            statusText.Text = hasConfig ? "✓ 설정됨" : "미설정";
            statusText.Foreground = hasConfig ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")) : Brushes.Gray;
        }

        App.Settings.SaveAIProviders();
        Log4.Debug($"AI Provider 설정 자동 저장: {providerName}");
    }

    /// <summary>
    /// Provider 연결 테스트
    /// </summary>
    private async Task TestProviderConnectionAsync(string providerName)
    {
        try
        {
            Log4.Info($"AI Provider 연결 테스트 시작: {providerName}");

            var aiSettings = App.Settings.AIProviders;
            var config = providerName switch
            {
                "Claude" => aiSettings.Claude,
                "OpenAI" => aiSettings.OpenAI,
                "Gemini" => aiSettings.Gemini,
                "Ollama" => aiSettings.Ollama,
                "LMStudio" => aiSettings.LMStudio,
                _ => null
            };

            if (config == null)
            {
                ShowSettingsMessage($"{providerName} 설정을 찾을 수 없습니다.", isError: true);
                return;
            }

            if (string.IsNullOrEmpty(config.ApiKey) && providerName != "Ollama" && providerName != "LMStudio")
            {
                ShowSettingsMessage("API 키를 입력해주세요.", isError: true);
                return;
            }

            // 간단한 HTTP 연결 테스트
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            string testUrl = providerName switch
            {
                "Claude" => "https://api.anthropic.com/v1/messages",
                "OpenAI" => "https://api.openai.com/v1/models",
                "Gemini" => $"https://generativelanguage.googleapis.com/v1beta/models?key={config.ApiKey}",
                "Ollama" => $"{config.BaseUrl ?? "http://localhost:11434"}/api/tags",
                "LMStudio" => $"{config.BaseUrl ?? "http://localhost:1234/v1"}/models",
                _ => ""
            };

            if (providerName == "Claude")
            {
                httpClient.DefaultRequestHeaders.Add("x-api-key", config.ApiKey);
                httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
            else if (providerName == "OpenAI")
            {
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
            }

            var response = await httpClient.GetAsync(testUrl);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                ShowSettingsMessage($"{providerName} 연결 성공!", isError: false);
                Log4.Info($"AI Provider 연결 테스트 성공: {providerName}");
            }
            else
            {
                ShowSettingsMessage($"{providerName} 연결 실패: {response.StatusCode}", isError: true);
                Log4.Warn($"AI Provider 연결 테스트 실패: {providerName}, Status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            ShowSettingsMessage($"{providerName} 연결 오류: {ex.Message}", isError: true);
            Log4.Error($"AI Provider 연결 테스트 오류: {providerName}, {ex.Message}");
        }
    }

    /// <summary>
    /// 설정 메시지 표시
    /// </summary>
    private void ShowSettingsMessage(string message, bool isError)
    {
        var msgBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = isError ? "오류" : "알림",
            Content = message
        };
        msgBox.ShowDialogAsync();
    }

    #endregion

    #region TinyMCE 설정

    /// <summary>
    /// TinyMCE 설정 UI 표시
    /// </summary>
    private void ShowTinyMCESettings()
    {
        if (SettingsContentPanel == null) return;

        var aiSettings = App.Settings.AIProviders;

        // 헤더
        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("TinyMCE 에디터 설정"));

        // TinyMCE API 키 그룹
        var tinyMCEGroup = CreateSettingsGroupBorder();
        var tinyMCEStack = new StackPanel();

        tinyMCEStack.Children.Add(CreateSettingsLabel("TinyMCE API 키"));
        tinyMCEStack.Children.Add(CreateSettingsDescription("메일 작성 에디터(TinyMCE)의 API 키입니다. tiny.cloud에서 발급받을 수 있습니다."));

        var tinyMCEApiKeyBox = new Wpf.Ui.Controls.TextBox
        {
            Text = aiSettings.TinyMCE?.ApiKey ?? "",
            PlaceholderText = "TinyMCE API 키를 입력하세요",
            Margin = new Thickness(0, 12, 0, 0)
        };
        tinyMCEStack.Children.Add(tinyMCEApiKeyBox);

        tinyMCEGroup.Child = tinyMCEStack;
        SettingsContentPanel.Children.Add(tinyMCEGroup);

        // 저장 버튼
        SettingsContentPanel.Children.Add(CreateSaveButton(() =>
        {
            if (aiSettings.TinyMCE == null)
                aiSettings.TinyMCE = new Models.Settings.TinyMCEConfig();
            aiSettings.TinyMCE.ApiKey = tinyMCEApiKeyBox.Text;
            App.Settings.SaveAIProviders();
            Log4.Info("TinyMCE 설정 저장");
            ShowSettingsSavedMessage();
        }));
    }

    #endregion

    #region 일반 설정

    // 설정 UI 동기화용 필드
    private RadioButton? _settingsDarkRadio;
    private RadioButton? _settingsLightRadio;
    private CheckBox? _settingsGpuCheckBox;
    private bool _isUpdatingSettingsUI; // 이벤트 무한 루프 방지

    /// <summary>
    /// 일반 설정 UI 표시
    /// </summary>
    private void ShowGeneralSettings()
    {
        if (SettingsContentPanel == null) return;

        var prefs = App.Settings.UserPreferences;

        // 헤더
        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("일반 설정"));

        // 테마 설정 그룹
        var themeGroup = CreateSettingsGroupBorder();
        var themeStack = new StackPanel();

        themeStack.Children.Add(CreateSettingsLabel("테마"));
        themeStack.Children.Add(CreateSettingsDescription("테마를 선택하면 즉시 적용됩니다. 상단 메뉴와 동기화됩니다."));

        var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };

        _settingsDarkRadio = new RadioButton
        {
            Content = "다크 모드",
            GroupName = "ThemeGroup",
            IsChecked = prefs.Theme == "Dark",
            Margin = new Thickness(0, 0, 24, 0)
        };
        _settingsLightRadio = new RadioButton
        {
            Content = "라이트 모드",
            GroupName = "ThemeGroup",
            IsChecked = prefs.Theme == "Light"
        };

        // 테마 즉시 적용 이벤트
        _settingsDarkRadio.Checked += (s, e) =>
        {
            if (_isUpdatingSettingsUI) return;
            Services.Theme.ThemeService.Instance.SetDarkMode();
            UpdateThemeIcon(); // 상단 메뉴 아이콘 동기화
            Log4.Info("설정 UI: 테마 변경 → Dark (상단 메뉴 동기화)");
        };
        _settingsLightRadio.Checked += (s, e) =>
        {
            if (_isUpdatingSettingsUI) return;
            Services.Theme.ThemeService.Instance.SetLightMode();
            UpdateThemeIcon(); // 상단 메뉴 아이콘 동기화
            Log4.Info("설정 UI: 테마 변경 → Light (상단 메뉴 동기화)");
        };

        themePanel.Children.Add(_settingsDarkRadio);
        themePanel.Children.Add(_settingsLightRadio);
        themeStack.Children.Add(themePanel);

        themeGroup.Child = themeStack;
        SettingsContentPanel.Children.Add(themeGroup);

        // GPU 모드 설정 그룹
        var gpuGroup = CreateSettingsGroupBorder();
        var gpuStack = new StackPanel();

        gpuStack.Children.Add(CreateSettingsLabel("렌더링 모드"));

        _settingsGpuCheckBox = new CheckBox
        {
            Content = "GPU 가속 사용",
            IsChecked = prefs.UseGpuMode,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // GPU 모드 즉시 적용 이벤트
        _settingsGpuCheckBox.Checked += (s, e) =>
        {
            if (_isUpdatingSettingsUI) return;
            Services.Theme.RenderModeService.Instance.SetGpuMode(true);
            UpdateGpuModeCheckmark(); // 상단 메뉴 체크마크 동기화
            Log4.Info("설정 UI: GPU 모드 활성화 (상단 메뉴 동기화, 재시작 필요)");
            ShowSettingsMessage("GPU 가속이 활성화되었습니다. 변경 사항은 앱 재시작 후 적용됩니다.", isError: false);
        };
        _settingsGpuCheckBox.Unchecked += (s, e) =>
        {
            if (_isUpdatingSettingsUI) return;
            Services.Theme.RenderModeService.Instance.SetGpuMode(false);
            UpdateGpuModeCheckmark(); // 상단 메뉴 체크마크 동기화
            Log4.Info("설정 UI: GPU 모드 비활성화 (상단 메뉴 동기화, 재시작 필요)");
            ShowSettingsMessage("GPU 가속이 비활성화되었습니다. 변경 사항은 앱 재시작 후 적용됩니다.", isError: false);
        };

        gpuStack.Children.Add(_settingsGpuCheckBox);
        gpuStack.Children.Add(CreateSettingsDescription("GPU 가속을 사용하면 그래픽 성능이 향상되지만, 일부 시스템에서 호환성 문제가 발생할 수 있습니다. 변경 시 앱 재시작이 필요합니다."));

        gpuGroup.Child = gpuStack;
        SettingsContentPanel.Children.Add(gpuGroup);
    }

    /// <summary>
    /// 설정 UI의 테마/GPU 상태 동기화 (상단 메뉴에서 호출)
    /// </summary>
    private void SyncSettingsUIFromMenu()
    {
        if (_settingsDarkRadio == null || _settingsLightRadio == null || _settingsGpuCheckBox == null) return;

        _isUpdatingSettingsUI = true;
        try
        {
            var prefs = App.Settings.UserPreferences;
            var isDarkMode = Services.Theme.ThemeService.Instance.IsDarkMode;
            var isGpuMode = Services.Theme.RenderModeService.Instance.IsGpuMode;

            _settingsDarkRadio.IsChecked = isDarkMode;
            _settingsLightRadio.IsChecked = !isDarkMode;
            _settingsGpuCheckBox.IsChecked = isGpuMode;

            Log4.Debug($"설정 UI 동기화: 테마={isDarkMode}, GPU={isGpuMode}");
        }
        finally
        {
            _isUpdatingSettingsUI = false;
        }
    }

    #endregion

    #region 계정 설정

    /// <summary>
    /// 계정 설정 UI 표시
    /// </summary>
    private void ShowAccountSettings()
    {
        if (SettingsContentPanel == null) return;

        var loginSettings = App.Settings.Login;

        // 헤더
        SettingsContentPanel.Children.Add(CreateSettingsSectionHeader("계정 설정"));

        // 현재 계정 정보 그룹
        var accountGroup = CreateSettingsGroupBorder();
        var accountStack = new StackPanel();

        accountStack.Children.Add(CreateSettingsLabel("현재 로그인 계정"));

        var emailText = new System.Windows.Controls.TextBlock
        {
            Text = loginSettings?.Email ?? "(로그인되지 않음)",
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 4)
        };
        accountStack.Children.Add(emailText);

        var displayNameText = new System.Windows.Controls.TextBlock
        {
            Text = loginSettings?.DisplayName ?? "",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
            Margin = new Thickness(0, 0, 0, 12)
        };
        accountStack.Children.Add(displayNameText);

        // 자동 로그인 체크박스 (즉시 반영)
        var autoLoginCheckBox = new CheckBox
        {
            Content = "자동 로그인",
            IsChecked = loginSettings?.AutoLogin ?? false,
            Margin = new Thickness(0, 8, 0, 0)
        };

        // 자동 로그인 즉시 반영 이벤트
        autoLoginCheckBox.Checked += (s, e) =>
        {
            if (loginSettings != null)
            {
                loginSettings.AutoLogin = true;
                App.Settings.SaveLogin();
                Log4.Info("자동 로그인 활성화");
            }
        };
        autoLoginCheckBox.Unchecked += (s, e) =>
        {
            if (loginSettings != null)
            {
                loginSettings.AutoLogin = false;
                App.Settings.SaveLogin();
                Log4.Info("자동 로그인 비활성화");
            }
        };

        accountStack.Children.Add(autoLoginCheckBox);
        accountStack.Children.Add(CreateSettingsDescription("자동 로그인을 활성화하면 앱 시작 시 자동으로 로그인합니다. 변경 사항은 즉시 저장됩니다."));

        accountGroup.Child = accountStack;
        SettingsContentPanel.Children.Add(accountGroup);

        // 로그아웃 그룹
        var logoutGroup = CreateSettingsGroupBorder();
        var logoutStack = new StackPanel();

        logoutStack.Children.Add(CreateSettingsLabel("계정 관리"));

        var logoutBtn = new Wpf.Ui.Controls.Button
        {
            Content = "로그아웃",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(24, 8, 24, 8)
        };
        logoutBtn.Click += (s, e) =>
        {
            MenuLogout_Click(s, e);
        };
        logoutStack.Children.Add(logoutBtn);

        logoutGroup.Child = logoutStack;
        SettingsContentPanel.Children.Add(logoutGroup);
    }

    #endregion

    /// <summary>
    /// 설정 저장 완료 메시지 표시
    /// </summary>
    private void ShowSettingsSavedMessage()
    {
        _viewModel.StatusMessage = "설정이 저장되었습니다.";
    }

    #endregion
}
