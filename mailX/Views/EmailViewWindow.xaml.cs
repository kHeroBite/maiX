using System.Text.Json;
using System.Windows;
using mailX.Models;
using mailX.Utils;
using Wpf.Ui.Controls;

namespace mailX.Views;

/// <summary>
/// 메일 보기 창
/// </summary>
public partial class EmailViewWindow : FluentWindow
{
    private readonly Email _email;

    public EmailViewWindow(Email email)
    {
        InitializeComponent();
        _email = email;

        Loaded += EmailViewWindow_Loaded;
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

            // 날짜
            DateText.Text = _email.ReceivedDateTime?.ToString("yyyy-MM-dd HH:mm") ?? "";

            // WebView2 초기화 및 본문 로드
            await BodyWebView.EnsureCoreWebView2Async();

            // 다크/라이트 모드에 따른 배경색 설정
            var isDarkMode = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;
            var bgColor = isDarkMode ? "#1e1e1e" : "#ffffff";
            var textColor = isDarkMode ? "#e0e0e0" : "#333333";

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
    </style>
</head>
<body>
{_email.Body ?? ""}
</body>
</html>";

            BodyWebView.NavigateToString(htmlContent);

            // 외부 링크 새 브라우저에서 열기
            BodyWebView.CoreWebView2.NavigationStarting += (s, args) =>
            {
                if (args.Uri.StartsWith("http://") || args.Uri.StartsWith("https://"))
                {
                    args.Cancel = true;
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = args.Uri,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Log4.Warn($"링크 열기 실패: {ex.Message}");
                    }
                }
            };
        }
        catch (Exception ex)
        {
            Log4.Error($"메일 보기 창 로드 실패: {ex.Message}");
        }
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
}
