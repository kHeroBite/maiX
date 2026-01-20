using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph.Models;
using Wpf.Ui.Controls;
using mailX.ViewModels;
using mailX.Models;
using mailX.Utils;
using mailX.Services.Theme;
using mailX.Services.Graph;
using mailX.Views.Dialogs;
using mailX.Data;

namespace mailX.Views;

/// <summary>
/// 메일 작성 창
/// </summary>
public partial class ComposeWindow : FluentWindow
{
    private readonly ComposeViewModel _viewModel;
    private bool _webView2Initialized = false;
    private bool _editorReady = false;
    private bool _mailSent = false; // 메일 발송 완료 플래그
    private bool _closingConfirmed = false; // 닫기 확인 완료 플래그

    // 자동완성용 필드
    private Wpf.Ui.Controls.TextBox? _currentTextBox;
    private Popup? _currentPopup;
    private System.Windows.Controls.ListBox? _currentListBox;
    private List<ContactSuggestion> _suggestions = new();

    public ComposeWindow(ComposeViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += ComposeWindow_Loaded;
        KeyDown += ComposeWindow_KeyDown;
    }

    /// <summary>
    /// ESC 키로 창 닫기 확인
    /// </summary>
    private async void ComposeWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            await HandleCloseRequestAsync();
        }
    }

    private async void ComposeWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeWebView2Async();
    }

    /// <summary>
    /// WebView2 초기화
    /// </summary>
    private async Task InitializeWebView2Async()
    {
        try
        {
            if (_webView2Initialized) return;

            // WebView2 환경 생성
            var userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "mailX", "WebView2Cache", "Editor");

            System.IO.Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await EditorWebView.EnsureCoreWebView2Async(env);

            _webView2Initialized = true;

            // 보안 설정
            EditorWebView.CoreWebView2.Settings.IsScriptEnabled = true;
            EditorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            EditorWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            // 에디터 HTML 로드
            await LoadTinyMCEEditorAsync();

            Log4.Debug("ComposeWindow WebView2 초기화 완료");
        }
        catch (Exception ex)
        {
            Log4.Error($"WebView2 초기화 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"에디터 초기화 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// TinyMCE 에디터 HTML 로드
    /// </summary>
    private async Task LoadTinyMCEEditorAsync()
    {
        // ThemeService에서 정확한 테마 상태 가져오기
        var isDark = ThemeService.Instance.IsDarkMode;

        var backgroundColor = isDark ? "#1e1e1e" : "#ffffff";
        var textColor = isDark ? "#ffffff" : "#000000";
        var skin = isDark ? "oxide-dark" : "oxide";
        var contentCss = isDark ? "dark" : "default";

        // 로컬 TinyMCE 폴더 경로 설정 (Self-hosted)
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var tinymcePath = System.IO.Path.Combine(appDir, "Assets", "tinymce");

        // WebView2에서 로컬 파일에 접근할 수 있도록 가상 호스트 매핑
        EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "tinymce.local", tinymcePath,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

        var editorHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <script src='https://tinymce.local/tinymce.min.js'></script>
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
            base_url: 'https://tinymce.local',
            suffix: '.min',
            plugins: 'table lists link image code',
            toolbar: 'bold italic underline strikethrough | forecolor backcolor | fontfamily fontsize | alignleft aligncenter alignright alignjustify | bullist numlist outdent indent | table | link image | code removeformat',
            skin: '{skin}',
            skin_url: 'https://tinymce.local/skins/ui/{skin}',
            content_css: 'https://tinymce.local/skins/content/{contentCss}/content.min.css',
            content_style: 'body {{ font-family: Segoe UI, sans-serif; font-size: 14px; color: {textColor}; background-color: {backgroundColor}; padding: 16px; }}',
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

        window.insertContent = function(html) {{
            if (editor) {{
                editor.insertContent(html);
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
        EditorWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        EditorWebView.CoreWebView2.NavigateToString(editorHtml);
    }

    /// <summary>
    /// WebView2에서 메시지 수신
    /// </summary>
    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(e.WebMessageAsJson);
            if (message != null && message.TryGetValue("type", out var type))
            {
                if (type == "ready")
                {
                    _editorReady = true;

                    // 로딩 패널 숨기고 에디터 표시
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    EditorWebView.Visibility = Visibility.Visible;

                    // 초기 컨텐츠 설정 (답장/전달 시)
                    if (!string.IsNullOrEmpty(_viewModel.InitialBody))
                    {
                        await SetEditorContentAsync(_viewModel.InitialBody);
                    }

                    Log4.Debug("TinyMCE 에디터 준비 완료");
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"WebView2 메시지 처리 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 에디터에 내용 설정
    /// </summary>
    public async Task SetEditorContentAsync(string html)
    {
        if (!_editorReady || EditorWebView.CoreWebView2 == null) return;

        // 다크모드일 때 인라인 스타일 색상을 밝은 색으로 변환
        var processedHtml = html;
        if (ThemeService.Instance.IsDarkMode)
        {
            processedHtml = ConvertHtmlForDarkMode(html);
        }

        var escapedHtml = System.Text.Json.JsonSerializer.Serialize(processedHtml);
        await EditorWebView.ExecuteScriptAsync($"window.setContent({escapedHtml})");
    }

    /// <summary>
    /// 다크모드용 HTML 변환 - 인라인 스타일의 어두운 색상을 밝은 색으로 변환
    /// </summary>
    private string ConvertHtmlForDarkMode(string html)
    {
        if (string.IsNullOrEmpty(html)) return html;

        // 검정/어두운 색상을 밝은 색상으로 변환
        var result = html;

        // color: #000000, color:#000, color:black 등을 밝은 색으로 변환
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"color\s*:\s*(#000000|#000|black|#1e1e1e|#333333|#333|#222222|#222)",
            "color: #e0e0e0",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // color: rgb(0,0,0) 형식도 변환
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"color\s*:\s*rgb\s*\(\s*0\s*,\s*0\s*,\s*0\s*\)",
            "color: #e0e0e0",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 어두운 배경색을 투명하게 변환 (에디터 배경 사용)
        result = System.Text.RegularExpressions.Regex.Replace(
            result,
            @"background(-color)?\s*:\s*(#ffffff|#fff|white)",
            "background-color: transparent",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return result;
    }

    /// <summary>
    /// 에디터에서 내용 가져오기
    /// </summary>
    public async Task<string> GetEditorContentAsync()
    {
        if (!_editorReady || EditorWebView.CoreWebView2 == null) return "";

        var result = await EditorWebView.ExecuteScriptAsync("window.getContent()");

        // JSON 문자열 디코딩
        if (result.StartsWith("\"") && result.EndsWith("\""))
        {
            result = System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? "";
        }

        return result;
    }

    /// <summary>
    /// 첨부 버튼 클릭
    /// </summary>
    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "첨부할 파일 선택"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var filePath in dialog.FileNames)
            {
                _viewModel.AddAttachment(filePath);
            }
        }
    }

    /// <summary>
    /// 취소 버튼 클릭
    /// </summary>
    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        await HandleCloseRequestAsync();
    }

    /// <summary>
    /// 보내기 버튼 클릭
    /// </summary>
    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 유효성 검사
            if (string.IsNullOrWhiteSpace(_viewModel.To))
            {
                System.Windows.MessageBox.Show("받는 사람을 입력하세요.", "알림",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                ToTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(_viewModel.Subject))
            {
                var result = System.Windows.MessageBox.Show("제목이 비어있습니다. 계속 보내시겠습니까?", "확인",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
                if (result != System.Windows.MessageBoxResult.Yes) return;
            }

            // 에디터에서 본문 가져오기
            var body = await GetEditorContentAsync();
            _viewModel.Body = body;

            // 버튼 비활성화
            SendButton.IsEnabled = false;
            SendButton.Content = "보내는 중...";

            // 메일 발송
            var success = await _viewModel.SendMailAsync();

            if (success)
            {
                Log4.Info($"메일 발송 완료: {_viewModel.Subject}");
                _mailSent = true;
                _closingConfirmed = true;
                DialogResult = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show("메일 발송에 실패했습니다.", "오류",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                SendButton.IsEnabled = true;
                SendButton.Content = "보내기";
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 발송 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"메일 발송 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            SendButton.IsEnabled = true;
            SendButton.Content = "보내기";
        }
    }

    /// <summary>
    /// 창 닫기 이벤트
    /// </summary>
    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // 이미 확인된 닫기이면 바로 닫음
        if (_closingConfirmed)
        {
            // WebView2 정리
            if (EditorWebView.CoreWebView2 != null)
            {
                EditorWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            }
            return;
        }

        // 닫기 취소하고 확인 대화상자 표시
        e.Cancel = true;
        await HandleCloseRequestAsync();
    }

    /// <summary>
    /// 닫기 요청 처리 - 확인 대화상자 표시
    /// </summary>
    private async Task HandleCloseRequestAsync()
    {
        // 메일이 이미 발송되었으면 바로 닫음
        if (_mailSent)
        {
            _closingConfirmed = true;
            Close();
            return;
        }

        // 확인 대화상자 표시
        var dialog = new ComposeCloseDialog
        {
            Owner = this
        };

        var dialogResult = dialog.ShowDialog();

        if (dialogResult != true)
        {
            // 대화상자가 취소되었거나 닫힘
            return;
        }

        switch (dialog.Result)
        {
            case ComposeCloseResult.Delete:
                // 저장 없이 닫기
                Log4.Debug("메일 작성 취소 - 삭제");
                _closingConfirmed = true;
                Close();
                break;

            case ComposeCloseResult.SaveDraft:
                // 임시보관함에 저장 후 닫기
                var saved = await SaveToDraftAsync();
                if (saved)
                {
                    Log4.Info("메일 임시보관 완료");
                    _closingConfirmed = true;
                    Close();
                }
                break;

            case ComposeCloseResult.Cancel:
            default:
                // 아무 작업 안함 - 계속 작성
                break;
        }
    }

    /// <summary>
    /// 임시보관함에 메일 저장
    /// </summary>
    private async Task<bool> SaveToDraftAsync()
    {
        try
        {
            // 에디터에서 본문 가져오기
            var body = await GetEditorContentAsync();

            // Graph API로 임시보관함에 저장
            var graphMailService = ((App)System.Windows.Application.Current).GetService<GraphMailService>();
            if (graphMailService == null)
            {
                System.Windows.MessageBox.Show("메일 서비스를 사용할 수 없습니다.", "오류",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }

            // 메시지 객체 생성
            var message = new Message
            {
                Subject = _viewModel.Subject ?? "(제목 없음)",
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = body
                },
                ToRecipients = ParseRecipients(_viewModel.To),
                CcRecipients = ParseRecipients(_viewModel.Cc),
                BccRecipients = ParseRecipients(_viewModel.Bcc),
                IsDraft = true
            };

            var savedMessage = await graphMailService.SaveDraftAsync(message);

            if (savedMessage != null)
            {
                System.Windows.MessageBox.Show("임시보관함에 저장되었습니다.", "알림",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return true;
            }

            System.Windows.MessageBox.Show("임시보관 저장에 실패했습니다.", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return false;
        }
        catch (Exception ex)
        {
            Log4.Error($"임시보관 저장 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"임시보관 저장 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// 수신자 문자열을 Recipient 목록으로 파싱
    /// </summary>
    private List<Recipient> ParseRecipients(string? recipientString)
    {
        var recipients = new List<Recipient>();

        if (string.IsNullOrWhiteSpace(recipientString))
            return recipients;

        // ; 또는 , 로 구분된 수신자 분리
        var addresses = recipientString.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var address in addresses)
        {
            var trimmed = address.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string email;
            string? name = null;

            // "이름 <이메일>" 형식 파싱
            var ltIndex = trimmed.IndexOf('<');
            var gtIndex = trimmed.IndexOf('>');

            if (ltIndex >= 0 && gtIndex > ltIndex)
            {
                email = trimmed.Substring(ltIndex + 1, gtIndex - ltIndex - 1).Trim();
                if (ltIndex > 0)
                {
                    name = trimmed.Substring(0, ltIndex).Trim().Trim('"');
                }
            }
            else
            {
                email = trimmed;
            }

            if (!string.IsNullOrEmpty(email))
            {
                recipients.Add(new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = email,
                        Name = name ?? email
                    }
                });
            }
        }

        return recipients;
    }

    #region 연락처 자동완성

    /// <summary>
    /// 이메일 입력 필드 Tab 키 처리
    /// </summary>
    private async void EmailTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;

        var textBox = sender as Wpf.Ui.Controls.TextBox;
        if (textBox == null) return;

        // 현재 입력 중인 텍스트 추출 (마지막 구분자 이후)
        var text = textBox.Text ?? "";
        var lastSeparator = Math.Max(text.LastIndexOf(';'), text.LastIndexOf(','));
        var currentInput = text.Substring(lastSeparator + 1).Trim();

        // 이미 이메일 형식이거나 비어있으면 무시
        if (string.IsNullOrEmpty(currentInput) || currentInput.Contains('@') || currentInput.Contains('<'))
            return;

        // 연락처 검색
        var suggestions = await SearchContactsAsync(currentInput);

        if (suggestions.Count == 0)
            return;

        // 해당 TextBox에 맞는 Popup과 ListBox 찾기
        SetCurrentControls(textBox);

        if (suggestions.Count == 1)
        {
            // 1개면 바로 완성
            AutoCompleteEmail(textBox, currentInput, suggestions[0]);
            e.Handled = true;
        }
        else
        {
            // 여러 개면 Popup 표시
            ShowSuggestionPopup(suggestions);
            e.Handled = true;
        }
    }

    /// <summary>
    /// TextBox에 해당하는 Popup과 ListBox 설정
    /// </summary>
    private void SetCurrentControls(Wpf.Ui.Controls.TextBox textBox)
    {
        _currentTextBox = textBox;

        if (textBox == ToTextBox)
        {
            _currentPopup = ToSuggestionPopup;
            _currentListBox = ToSuggestionList;
        }
        else if (textBox == CcTextBox)
        {
            _currentPopup = CcSuggestionPopup;
            _currentListBox = CcSuggestionList;
        }
        else if (textBox == BccTextBox)
        {
            _currentPopup = BccSuggestionPopup;
            _currentListBox = BccSuggestionList;
        }
    }

    /// <summary>
    /// 연락처 검색 (로컬 DB)
    /// </summary>
    private async Task<List<ContactSuggestion>> SearchContactsAsync(string query)
    {
        var results = new List<ContactSuggestion>();

        try
        {
            // 로컬 DB에서 발신자 검색
            var optionsBuilder = new DbContextOptionsBuilder<MailXDbContext>();
            optionsBuilder.UseSqlite($"Data Source={App.DatabasePath}");
            using var context = new MailXDbContext(optionsBuilder.Options);
            var emails = await context.Emails
                .Select(e => e.From)
                .Distinct()
                .ToListAsync();

            // 메모리에서 필터링 (대소문자 무시)
            var filtered = emails
                .Where(f => !string.IsNullOrEmpty(f) &&
                           (f.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            ExtractDisplayName(f).Contains(query, StringComparison.OrdinalIgnoreCase)))
                .Take(20)
                .ToList();

            foreach (var emailStr in filtered)
            {
                var displayName = ExtractDisplayName(emailStr);
                var email = ExtractEmail(emailStr);

                if (!string.IsNullOrEmpty(email))
                {
                    results.Add(new ContactSuggestion
                    {
                        DisplayName = displayName,
                        Email = email
                    });
                }
            }

            // 중복 제거 및 정렬
            results = results
                .GroupBy(c => c.Email.ToLower())
                .Select(g => g.First())
                .OrderBy(c => c.DisplayName)
                .ThenBy(c => c.Email)
                .ToList();
        }
        catch (Exception ex)
        {
            Log4.Error($"연락처 검색 실패: {ex.Message}");
        }

        return results;
    }

    /// <summary>
    /// 이메일 문자열에서 표시 이름 추출
    /// 예: "김기로 <ryo@diquest.com>" → "김기로"
    /// </summary>
    private string ExtractDisplayName(string emailString)
    {
        if (string.IsNullOrEmpty(emailString)) return "";

        var ltIndex = emailString.IndexOf('<');
        if (ltIndex > 0)
        {
            return emailString.Substring(0, ltIndex).Trim().Trim('"');
        }

        // @ 앞부분을 이름으로 사용
        var atIndex = emailString.IndexOf('@');
        if (atIndex > 0)
        {
            return emailString.Substring(0, atIndex);
        }

        return emailString;
    }

    /// <summary>
    /// 이메일 문자열에서 이메일 주소 추출
    /// 예: "김기로 <ryo@diquest.com>" → "ryo@diquest.com"
    /// </summary>
    private string ExtractEmail(string emailString)
    {
        if (string.IsNullOrEmpty(emailString)) return "";

        var ltIndex = emailString.IndexOf('<');
        var gtIndex = emailString.IndexOf('>');

        if (ltIndex >= 0 && gtIndex > ltIndex)
        {
            return emailString.Substring(ltIndex + 1, gtIndex - ltIndex - 1).Trim();
        }

        // < > 없으면 전체가 이메일
        if (emailString.Contains('@'))
        {
            return emailString.Trim();
        }

        return "";
    }

    /// <summary>
    /// 이메일 자동완성
    /// </summary>
    private void AutoCompleteEmail(Wpf.Ui.Controls.TextBox textBox, string input, ContactSuggestion contact)
    {
        var text = textBox.Text ?? "";
        var lastSeparator = Math.Max(text.LastIndexOf(';'), text.LastIndexOf(','));
        var prefix = lastSeparator >= 0 ? text.Substring(0, lastSeparator + 1) + " " : "";

        textBox.Text = prefix + contact.FormattedAddress;
        textBox.CaretIndex = textBox.Text.Length;

        Log4.Debug($"연락처 자동완성: {contact.FormattedAddress}");
    }

    /// <summary>
    /// 자동완성 Popup 표시
    /// </summary>
    private void ShowSuggestionPopup(List<ContactSuggestion> suggestions)
    {
        if (_currentPopup == null || _currentListBox == null) return;

        _suggestions = suggestions;
        _currentListBox.Items.Clear();

        foreach (var suggestion in suggestions)
        {
            _currentListBox.Items.Add(suggestion.DisplayText);
        }

        _currentPopup.IsOpen = true;
        _currentListBox.SelectedIndex = 0;
        _currentListBox.Focus();
    }

    /// <summary>
    /// 자동완성 목록 선택 변경
    /// </summary>
    private void SuggestionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 더블클릭이나 Enter 키로 선택 시 처리됨
    }

    /// <summary>
    /// 자동완성 목록 더블클릭
    /// </summary>
    private void SuggestionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ApplySelectedSuggestion();
    }

    /// <summary>
    /// 자동완성 목록 키보드 처리
    /// </summary>
    private void SuggestionList_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            ApplySelectedSuggestion();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClosePopup();
            _currentTextBox?.Focus();
            e.Handled = true;
        }
    }

    /// <summary>
    /// 선택된 연락처 적용
    /// </summary>
    private void ApplySelectedSuggestion()
    {
        if (_currentListBox == null || _currentTextBox == null) return;

        var selectedIndex = _currentListBox.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _suggestions.Count) return;

        var contact = _suggestions[selectedIndex];

        // 현재 입력 중인 텍스트 추출
        var text = _currentTextBox.Text ?? "";
        var lastSeparator = Math.Max(text.LastIndexOf(';'), text.LastIndexOf(','));
        var currentInput = text.Substring(lastSeparator + 1).Trim();

        AutoCompleteEmail(_currentTextBox, currentInput, contact);
        ClosePopup();
        _currentTextBox.Focus();
    }

    /// <summary>
    /// Popup 닫기
    /// </summary>
    private void ClosePopup()
    {
        if (_currentPopup != null)
        {
            _currentPopup.IsOpen = false;
        }
    }

    #endregion
}
