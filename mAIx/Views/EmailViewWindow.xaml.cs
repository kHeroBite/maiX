using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph.Models;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services;
using mAIx.Services.AI;
using mAIx.Services.Graph;
using mAIx.Services.Notification;
using mAIx.Services.Speech;
using mAIx.Utils;
using mAIx.ViewModels;
using Wpf.Ui.Controls;
using FileAttachment = Microsoft.Graph.Models.FileAttachment;

namespace mAIx.Views;

/// <summary>
/// 메일 보기 창
/// </summary>
public partial class EmailViewWindow : FluentWindow
{
    private readonly Email _email;
    private readonly AiMailService? _aiMailService;
    private readonly TextToSpeechService? _ttsService;
    private readonly ToastNotificationService? _toastService;
    private readonly GraphAuthService? _graphAuthService;
    private bool _webView2Initialized = false;
    private CancellationTokenSource? _ttsCts;
    private bool _threadSummaryLoaded = false;

    /// <summary>
    /// 첨부파일 표시용 DTO
    /// </summary>
    private class AttachmentInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public string SizeText => FormatSize(Size);
        public string ContentType { get; set; } = "";

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"({bytes} B)";
            if (bytes < 1024 * 1024) return $"({bytes / 1024.0:F1} KB)";
            return $"({bytes / (1024.0 * 1024.0):F1} MB)";
        }
    }

    public EmailViewWindow(Email email)
    {
        InitializeComponent();
        _email = email;

        _aiMailService = (App.Current as App)?.GetService<AiMailService>();
        _ttsService = (App.Current as App)?.GetService<TextToSpeechService>();
        _toastService = (App.Current as App)?.GetService<ToastNotificationService>();
        _graphAuthService = (App.Current as App)?.GetService<GraphAuthService>();

        Loaded += EmailViewWindow_Loaded;
        Closing += EmailViewWindow_Closing;
        KeyDown += EmailViewWindow_KeyDown;
    }

    /// <summary>
    /// 키보드 단축키 처리 (ESC: 닫기, Ctrl+P: 인쇄)
    /// </summary>
    private void EmailViewWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Ctrl+P: 인쇄
            e.Handled = true;
            _ = PrintEmailAsync();
        }
    }

    /// <summary>
    /// 메일 인쇄 (WebView2 window.print())
    /// </summary>
    private async Task PrintEmailAsync()
    {
        try
        {
            if (_webView2Initialized && BodyWebView.CoreWebView2 != null)
            {
                Log4.Info($"메일 인쇄: {_email.Subject}");
                await BodyWebView.CoreWebView2.ExecuteScriptAsync("window.print();");
            }
            else
            {
                Log4.Warn("WebView2가 초기화되지 않아 인쇄할 수 없습니다.");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"인쇄 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// EML 내보내기
    /// </summary>
    private async Task ExportAsEmlAsync()
    {
        if (string.IsNullOrEmpty(_email.EntryId))
        {
            Log4.Warn("EntryId가 없어 EML 내보내기 불가");
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "EML 파일 (*.eml)|*.eml",
                FileName = SanitizeFileName(_email.Subject ?? "메일") + ".eml",
                DefaultExt = ".eml"
            };

            if (dialog.ShowDialog() == true)
            {
                var exportService = (App.Current as App)?.GetService<ExportService>();
                if (exportService != null)
                {
                    await exportService.ExportAsEmlAsync(_email.EntryId, dialog.FileName);
                    Log4.Info($"EML 내보내기 완료: {dialog.FileName}");
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"EML 내보내기 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"EML 내보내기 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// PDF 내보내기
    /// </summary>
    private async Task ExportAsPdfAsync()
    {
        try
        {
            if (!_webView2Initialized || BodyWebView.CoreWebView2 == null)
            {
                Log4.Warn("WebView2가 초기화되지 않아 PDF 내보내기 불가");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF 파일 (*.pdf)|*.pdf",
                FileName = SanitizeFileName(_email.Subject ?? "메일") + ".pdf",
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() == true)
            {
                var exportService = (App.Current as App)?.GetService<ExportService>();
                if (exportService != null)
                {
                    await exportService.ExportAsPdfAsync(BodyWebView.CoreWebView2, dialog.FileName);
                    Log4.Info($"PDF 내보내기 완료: {dialog.FileName}");
                }
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"PDF 내보내기 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"PDF 내보내기 실패: {ex.Message}", "오류",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 파일명에 사용할 수 없는 문자 제거
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? "메일" : sanitized.Trim();
    }

    /// <summary>
    /// 창 닫기 시 WebView2 정리
    /// </summary>
    private void EmailViewWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // TTS 중지
            _ttsCts?.Cancel();
            _ttsService?.Stop();

            if (_webView2Initialized && BodyWebView.CoreWebView2 != null)
            {
                // 이벤트 핸들러 해제
                BodyWebView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                BodyWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                BodyWebView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
            }
        }
        catch (Exception ex)
        {
            Log4.Warn($"WebView2 정리 중 오류 (무시): {ex.Message}");
        }
    }

    private async void EmailViewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 제목 설정
            var subject = string.IsNullOrWhiteSpace(_email.Subject) ? "(제목 없음)" : _email.Subject;
            TitleText.Text = subject;
            SubjectText.Text = subject;
            Title = subject;

            // 발신자
            FromText.Text = _email.From ?? "";

            // 수신자 (JSON 배열이면 파싱)
            ToText.Text = ParseJsonArrayToString(_email.To);

            // 참조 (CC)
            var ccStr = ParseJsonArrayToString(_email.Cc);
            if (!string.IsNullOrWhiteSpace(ccStr))
            {
                CcText.Text = ccStr;
                CcRow.Visibility = Visibility.Visible;
            }

            // 숨은 참조 (BCC)
            var bccStr = ParseJsonArrayToString(_email.Bcc);
            if (!string.IsNullOrWhiteSpace(bccStr))
            {
                BccText.Text = bccStr;
                BccRow.Visibility = Visibility.Visible;
            }

            // 날짜
            DateText.Text = _email.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "";

            // WebView2 초기화 및 본문 로드
            await BodyWebView.EnsureCoreWebView2Async();
            _webView2Initialized = true;

            // 이벤트 핸들러 등록
            BodyWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            BodyWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            BodyWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;

            // 다크/라이트 모드에 따른 배경색 설정
            var isDarkMode = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;
            var bgColor = isDarkMode ? "#1e1e1e" : "#ffffff";
            var textColor = isDarkMode ? "#e0e0e0" : "#333333";
            var scrollbarThumbColor = isDarkMode ? "#555555" : "#c0c0c0";
            var scrollbarThumbHoverColor = isDarkMode ? "#777777" : "#a0a0a0";
            var scrollbarTrackColor = isDarkMode ? "#2d2d2d" : "#f0f0f0";

            // 다크모드일 때 인라인 스타일 강제 덮어쓰기 (MainWindow와 동일)
            var darkModeOverride = isDarkMode ? @"
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

            var htmlContent = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>
        body {{
            font-family: 'Segoe UI', 'Malgun Gothic', sans-serif;
            font-size: 14px;
            line-height: 1.6;
            color: {textColor};
            background-color: {bgColor};
            padding: 20px;
            margin: 0;
        }}
        img {{ max-width: 100%; height: auto; }}
        a {{ color: #0078d4; }}
        pre, code {{
            background-color: {(isDarkMode ? "#2d2d2d" : "#f5f5f5")};
            padding: 8px;
            border-radius: 4px;
            overflow-x: auto;
        }}
        /* 스크롤바 스타일 (6px 두께) */
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
{_email.Body ?? ""}
</body>
</html>";

            BodyWebView.NavigateToString(htmlContent);

            // 첨부파일이 있으면 첨부파일 목록 로드
            if (_email.HasAttachments && !string.IsNullOrEmpty(_email.EntryId))
            {
                _ = LoadAttachmentsAsync();
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 보기 창 로드 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// WebView2 네비게이션 시작 이벤트 - mailto: 링크 및 외부 링크 처리
    /// </summary>
    private void CoreWebView2_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        // mailto: 링크 처리
        if (e.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            OpenComposeWindowWithMailto(e.Uri);
            return;
        }

        // 외부 링크 새 브라우저에서 열기
        if (e.Uri.StartsWith("http://") || e.Uri.StartsWith("https://"))
        {
            e.Cancel = true;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log4.Warn($"링크 열기 실패: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// WebView2 새 창 요청 처리 - mailto: 링크 클릭 시
    /// </summary>
    private void CoreWebView2_NewWindowRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs e)
    {
        Log4.Debug($"NewWindowRequested: {e.Uri}");

        // mailto: 링크인 경우 새 메일 작성 창 열기
        if (e.Uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            var mailtoUri = e.Uri;
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
        catch (Exception ex)
        {
            Log4.Error($"새 창 링크 열기 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// WebView2 우클릭 컨텍스트 메뉴 처리
    /// </summary>
    private void CoreWebView2_ContextMenuRequested(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var menuItems = e.MenuItems;

        // 링크 위에서 우클릭한 경우
        if (e.ContextMenuTarget.HasLinkUri)
        {
            var linkUri = e.ContextMenuTarget.LinkUri;

            // 기존 메뉴 지우고 커스텀 메뉴 추가
            menuItems.Clear();

            // mailto: 링크인 경우
            if (linkUri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                // 이메일 주소 추출
                var emailAddress = linkUri.Substring(7).Split('?')[0];

                // 새 메일 작성 메뉴
                var composeItem = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
                    "새 메일 작성", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
                var capturedLinkUri = linkUri;
                composeItem.CustomItemSelected += (s, args) =>
                {
                    Dispatcher.BeginInvoke(new Action(() => OpenComposeWindowWithMailto(capturedLinkUri)));
                };
                menuItems.Add(composeItem);

                // 메일 주소 복사 메뉴
                var copyEmailItem = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
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
                var openLinkItem = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
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
                    }
                    catch (Exception ex)
                    {
                        Log4.Error($"링크 열기 실패: {ex.Message}");
                    }
                };
                menuItems.Add(openLinkItem);

                // 링크 복사 메뉴
                var copyLinkItem = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
                    "링크 복사", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
                copyLinkItem.CustomItemSelected += (s, args) =>
                {
                    Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(linkUri));
                    Log4.Debug($"링크 복사됨: {linkUri}");
                };
                menuItems.Add(copyLinkItem);
            }
        }
        // 구분선 + 인쇄/내보내기 메뉴 추가 (모든 컨텍스트에서)
        var separator = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
            "", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Separator);
        menuItems.Add(separator);

        // 인쇄 메뉴
        var printItem = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
            "인쇄 (Ctrl+P)", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
        printItem.CustomItemSelected += (s, args) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await PrintEmailAsync();
                }
                catch (Exception ex)
                {
                    Log4.Error($"[EmailViewWindow] PrintEmailAsync Dispatcher 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
                }
            });
        };
        menuItems.Add(printItem);

        // EML 내보내기 메뉴
        var emlItem = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
            "EML 파일로 저장", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
        emlItem.CustomItemSelected += (s, args) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await ExportAsEmlAsync();
                }
                catch (Exception ex)
                {
                    Log4.Error($"[EmailViewWindow] ExportAsEmlAsync Dispatcher 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
                }
            });
        };
        menuItems.Add(emlItem);

        // PDF 내보내기 메뉴
        var pdfItem = BodyWebView.CoreWebView2.Environment.CreateContextMenuItem(
            "PDF 파일로 저장", null, Microsoft.Web.WebView2.Core.CoreWebView2ContextMenuItemKind.Command);
        pdfItem.CustomItemSelected += (s, args) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await ExportAsPdfAsync();
                }
                catch (Exception ex)
                {
                    Log4.Error($"[EmailViewWindow] ExportAsPdfAsync Dispatcher 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
                }
            });
        };
        menuItems.Add(pdfItem);
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

            var graphMailService = (App.Current as App)?.GraphMailService;
            if (graphMailService == null)
            {
                Log4.Error("GraphMailService를 찾을 수 없습니다.");
                return;
            }

            var syncService = (App.Current as App)?.BackgroundSyncService;
            var viewModel = new ComposeViewModel(graphMailService, syncService, null, ComposeMode.New, null);

            viewModel.To = emailWithName;
            if (!string.IsNullOrEmpty(subject)) viewModel.Subject = subject;
            if (!string.IsNullOrEmpty(cc)) viewModel.Cc = cc;
            if (!string.IsNullOrEmpty(bcc)) viewModel.Bcc = bcc;
            if (!string.IsNullOrEmpty(body)) viewModel.Body = body;

            var composeWindow = new ComposeWindow(viewModel);
            composeWindow.Owner = this;
            composeWindow.Show();

            Log4.Debug($"mailto 링크로 새 메일 작성 창 열림: {emailWithName}");
        }
        catch (Exception ex)
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
            // P4-06: DI 우회(new mAIxDbContext) → IDbContextFactory 패턴으로 전환
            var dbFactory = (App.Current as App)?.GetService<Microsoft.EntityFrameworkCore.IDbContextFactory<Data.mAIxDbContext>>();
            using var context = dbFactory != null
                ? dbFactory.CreateDbContext()
                : new Data.mAIxDbContext(
                    new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Data.mAIxDbContext>()
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
        catch (Exception ex)
        {
            Log4.Warn($"DB에서 이름 조회 실패 (무시): {ex.Message}");
        }

        // 찾지 못하면 이메일만 반환
        return emailString;
    }

    /// <summary>
    /// JSON 배열 문자열을 세미콜론 구분 문자열로 변환
    /// </summary>
    private static string ParseJsonArrayToString(string? jsonArray)
    {
        if (string.IsNullOrWhiteSpace(jsonArray))
            return "";

        if (!jsonArray.StartsWith("["))
            return jsonArray;

        try
        {
            var items = JsonSerializer.Deserialize<string[]>(jsonArray);
            if (items == null || items.Length == 0)
                return "";

            return string.Join("; ", items);
        }
        catch
        {
            return jsonArray;
        }
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
    // ─────────────────────────────────────────
    // Task 1: AI 답장 초안
    // ─────────────────────────────────────────

    /// <summary>AI 답장 버튼 클릭 — 톤 선택 후 ComposeWindow 열기</summary>
    private async void AiReplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_aiMailService == null)
            {
                Log4.Warn("[EmailView] AiMailService 미등록 — AI 답장 불가");
                return;
            }

            var tone = (ToneComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "정중";
            AiReplyButton.IsEnabled = false;
            AiReplyButton.Content = "생성 중...";

            try
            {
                Log4.Debug($"[EmailView] AI 답장 초안 생성 시작 — Tone={tone}");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var draft = await _aiMailService.GenerateReplyDraftAsync(_email, tone, cts.Token);

                var graphMailService = (App.Current as App)?.GraphMailService;
                if (graphMailService == null) return;

                var syncService = (App.Current as App)?.BackgroundSyncService;
                var vm = new ComposeViewModel(graphMailService, syncService, null, ComposeMode.Reply, _email);
                vm.InitialBody = draft + "\r\n\r\n" + vm.InitialBody;

                var composeWindow = new ComposeWindow(vm);
                composeWindow.Owner = this;
                composeWindow.Show();
                Log4.Debug("[EmailView] AI 답장 초안 ComposeWindow 열림");
            }
            catch (Exception ex)
            {
                Log4.Error($"[EmailView] AI 답장 초안 생성 실패: {ex.Message}");
            }
            finally
            {
                AiReplyButton.IsEnabled = true;
                AiReplyButton.Content = "AI 답장";
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[EmailViewWindow] AiReplyButton_Click 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ─────────────────────────────────────────
    // Task 2: TTS 읽어주기
    // ─────────────────────────────────────────

    /// <summary>읽어주기/중지 버튼 클릭</summary>
    private async void TtsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_ttsService == null)
            {
                Log4.Warn("[EmailView] TextToSpeechService 미등록");
                return;
            }

            if (_ttsService.IsSpeaking)
            {
                _ttsCts?.Cancel();
                _ttsService.Stop();
                TtsButton.Content = "읽어주기";
                Log4.Debug("[EmailView] TTS 중지");
                return;
            }

            var text = _email.PreviewOrSummary ?? _email.Body ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                Log4.Warn("[EmailView] TTS 대상 텍스트 없음");
                return;
            }

            TtsButton.Content = "중지";
            _ttsCts = new CancellationTokenSource();

            try
            {
                Log4.Debug("[EmailView] TTS 읽기 시작");
                await _ttsService.SpeakAsync(text, _ttsCts.Token);
            }
            catch (OperationCanceledException)
            {
                Log4.Debug("[EmailView] TTS 취소됨");
            }
            catch (Exception ex)
            {
                Log4.Error($"[EmailView] TTS 실패: {ex.Message}");
            }
            finally
            {
                TtsButton.Content = "읽어주기";
                _ttsCts?.Dispose();
                _ttsCts = null;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[EmailViewWindow] TtsButton_Click 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ─────────────────────────────────────────
    // Task 3: 스누즈
    // ─────────────────────────────────────────

    /// <summary>스누즈 버튼 클릭 — 팝업 열기</summary>
    private void SnoozeButton_Click(object sender, RoutedEventArgs e)
    {
        SnoozePopup.IsOpen = true;
    }

    /// <summary>스누즈 메뉴 항목 선택</summary>
    private async void SnoozeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SnoozePopup.IsOpen = false;

            if (sender is not System.Windows.Controls.MenuItem menuItem) return;
            var tag = menuItem.Tag?.ToString() ?? "";

            var now = DateTime.Now;
            DateTime snoozedUntil = tag switch
            {
                "1h"         => now.AddHours(1),
                "3h"         => now.AddHours(3),
                "tomorrow"   => now.Date.AddDays(1).AddHours(9),
                "nextmonday" => GetNextMonday(now).AddHours(9),
                _            => now.AddHours(1)
            };

            _email.SnoozedUntil = snoozedUntil;

            try
            {
                var dbFactory = (App.Current as App)?.GetService<IDbContextFactory<mAIxDbContext>>();
                if (dbFactory != null)
                {
                    using var ctx = dbFactory.CreateDbContext();
                    ctx.Emails.Update(_email);
                    await ctx.SaveChangesAsync();
                }

                var label = tag switch
                {
                    "1h"         => "1시간",
                    "3h"         => "3시간",
                    "tomorrow"   => "내일 아침",
                    "nextmonday" => "다음 주 월요일",
                    _            => ""
                };
                var msg = $"{label} 후 다시 알려드립니다 ({snoozedUntil:MM-dd HH:mm})";
                Log4.Info($"[EmailView] 스누즈 설정: {msg}");

                _toastService?.ShowNewMailNotification("스누즈 설정", msg, "");
            }
            catch (Exception ex)
            {
                Log4.Error($"[EmailView] 스누즈 DB 저장 실패: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[EmailViewWindow] SnoozeMenuItem_Click 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static DateTime GetNextMonday(DateTime from)
    {
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)from.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        return from.Date.AddDays(daysUntilMonday);
    }

    // ─────────────────────────────────────────
    // Task 4: 스레드 AI 요약
    // ─────────────────────────────────────────

    /// <summary>스레드 요약 패널 토글</summary>
    private async void ThreadSummaryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ThreadSummaryPanel.Visibility == Visibility.Visible)
            {
                ThreadSummaryPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ThreadSummaryPanel.Visibility = Visibility.Visible;

            if (_threadSummaryLoaded) return;

            if (_aiMailService == null)
            {
                ThreadSummaryText.Text = "AI 서비스를 사용할 수 없습니다.";
                return;
            }

            ThreadSummaryLoading.Visibility = Visibility.Visible;
            ThreadSummaryText.Text = "스레드를 분석하는 중...";

            try
            {
                List<Email> threadEmails;
                var dbFactory = (App.Current as App)?.GetService<IDbContextFactory<mAIxDbContext>>();
                if (dbFactory != null && !string.IsNullOrEmpty(_email.ConversationId))
                {
                    using var ctx = dbFactory.CreateDbContext();
                    threadEmails = await ctx.Emails
                        .Where(e => e.ConversationId == _email.ConversationId)
                        .OrderBy(e => e.ReceivedDateTime)
                        .ToListAsync();
                }
                else
                {
                    threadEmails = new List<Email> { _email };
                }

                Log4.Debug($"[EmailView] 스레드 요약 — 메일 {threadEmails.Count}건");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var summary = await _aiMailService.GenerateThreadSummaryAsync(threadEmails, cts.Token);

                ThreadSummaryText.Text = summary;
                _threadSummaryLoaded = true;
            }
            catch (Exception ex)
            {
                Log4.Error($"[EmailView] 스레드 요약 실패: {ex.Message}");
                ThreadSummaryText.Text = "요약 생성에 실패했습니다.";
            }
            finally
            {
                ThreadSummaryLoading.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[EmailViewWindow] ThreadSummaryButton_Click 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

    // ─────────────────────────────────────────
    // Task 5: 첨부파일 목록 + 다운로드/열기
    // ─────────────────────────────────────────

    /// <summary>Graph API로 첨부파일 목록 로드</summary>
    private async Task LoadAttachmentsAsync()
    {
        if (_graphAuthService == null || string.IsNullOrEmpty(_email.EntryId))
            return;

        try
        {
            AttachmentPanel.Visibility = Visibility.Visible;
            AttachmentLoading.Visibility = Visibility.Visible;

            var client = _graphAuthService.GetGraphClient();
            var response = await client.Me.Messages[_email.EntryId].Attachments.GetAsync();

            if (response?.Value == null || response.Value.Count == 0)
            {
                AttachmentPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var attachments = new List<AttachmentInfo>();
            foreach (var att in response.Value)
            {
                // 인라인 이미지는 제외 (본문에 이미 표시됨)
                if (att is FileAttachment fileAtt && fileAtt.IsInline == true)
                    continue;

                attachments.Add(new AttachmentInfo
                {
                    Id = att.Id ?? "",
                    Name = att.Name ?? "첨부파일",
                    Size = att.Size ?? 0,
                    ContentType = att.ContentType ?? ""
                });
            }

            if (attachments.Count == 0)
            {
                AttachmentPanel.Visibility = Visibility.Collapsed;
                return;
            }

            AttachmentHeaderText.Text = $"첨부파일 ({attachments.Count})";
            AttachmentList.ItemsSource = attachments;
            Log4.Debug2($"[EmailView] 첨부파일 {attachments.Count}개 로드됨");
        }
        catch (Exception ex)
        {
            Log4.Warn($"[EmailView] 첨부파일 목록 로드 실패: {ex.Message}");
            AttachmentPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            AttachmentLoading.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>첨부파일 클릭 — 다운로드 후 열기</summary>
    private async void Attachment_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement element || element.DataContext is not AttachmentInfo info)
                return;

            if (_graphAuthService == null || string.IsNullOrEmpty(_email.EntryId))
                return;

            try
            {
                Log4.Debug($"[EmailView] 첨부파일 다운로드 시작: {info.Name}");

                var client = _graphAuthService.GetGraphClient();
                var attachment = await client.Me.Messages[_email.EntryId]
                    .Attachments[info.Id].GetAsync();

                if (attachment is not FileAttachment fileAtt || fileAtt.ContentBytes == null)
                {
                    Log4.Warn($"[EmailView] 첨부파일 콘텐츠를 가져올 수 없음: {info.Name}");
                    return;
                }

                // 임시 폴더에 저장
                var tempDir = Path.Combine(Path.GetTempPath(), "MaiX", "attachments");
                Directory.CreateDirectory(tempDir);
                var filePath = Path.Combine(tempDir, info.Name);

                // 동일 파일명 충돌 방지
                if (File.Exists(filePath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(info.Name);
                    var ext = Path.GetExtension(info.Name);
                    filePath = Path.Combine(tempDir, $"{nameWithoutExt}_{DateTime.Now:HHmmss}{ext}");
                }

                await File.WriteAllBytesAsync(filePath, fileAtt.ContentBytes);

                // 기본 프로그램으로 열기
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });

                Log4.Info($"[EmailView] 첨부파일 열림: {filePath}");
            }
            catch (Exception ex)
            {
                Log4.Error($"[EmailView] 첨부파일 다운로드/열기 실패: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[EmailViewWindow] Attachment_Click 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>첨부파일 열기 버튼 클릭 — 다운로드 후 열기 (기존 클릭 동작과 동일)</summary>
    private async void AttachmentOpen_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement element || element.DataContext is not AttachmentInfo info)
                return;

            if (_graphAuthService == null || string.IsNullOrEmpty(_email.EntryId))
                return;

            try
            {
                var client = _graphAuthService.GetGraphClient();
                var attachment = await client.Me.Messages[_email.EntryId]
                    .Attachments[info.Id].GetAsync();

                if (attachment is not FileAttachment fileAtt || fileAtt.ContentBytes == null)
                    return;

                var tempDir = Path.Combine(Path.GetTempPath(), "MaiX", "attachments");
                Directory.CreateDirectory(tempDir);
                var filePath = Path.Combine(tempDir, info.Name);

                if (File.Exists(filePath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(info.Name);
                    var ext = Path.GetExtension(info.Name);
                    filePath = Path.Combine(tempDir, $"{nameWithoutExt}_{DateTime.Now:HHmmss}{ext}");
                }

                await File.WriteAllBytesAsync(filePath, fileAtt.ContentBytes);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
                Log4.Info($"[EmailView] 첨부파일 열림: {filePath}");
            }
            catch (Exception ex)
            {
                Log4.Error($"[EmailView] 첨부파일 열기 실패: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[EmailViewWindow] AttachmentOpen_Click 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>첨부파일 다운로드 버튼 클릭 — SaveFileDialog로 저장</summary>
    private async void AttachmentDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement element || element.DataContext is not AttachmentInfo info)
                return;

            if (_graphAuthService == null || string.IsNullOrEmpty(_email.EntryId))
                return;

            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = info.Name,
                    Filter = "모든 파일 (*.*)|*.*"
                };

                if (saveDialog.ShowDialog() != true) return;

                var client = _graphAuthService.GetGraphClient();
                var attachment = await client.Me.Messages[_email.EntryId]
                    .Attachments[info.Id].GetAsync();

                if (attachment is not FileAttachment fileAtt || fileAtt.ContentBytes == null)
                {
                    Log4.Warn($"[EmailView] 첨부파일 콘텐츠 없음: {info.Name}");
                    return;
                }

                await File.WriteAllBytesAsync(saveDialog.FileName, fileAtt.ContentBytes);
                Log4.Info($"[EmailView] 첨부파일 저장됨: {saveDialog.FileName}");
            }
            catch (Exception ex)
            {
                Log4.Error($"[EmailView] 첨부파일 다운로드 실패: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[EmailViewWindow] AttachmentDownload_Click 실패: {ex.Message}\n{ex.StackTrace}");
        }
    }

}
