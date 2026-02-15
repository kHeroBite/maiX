using MaiX.Services.Theme;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Wpf;

namespace MaiX.Services.Editor;

/// <summary>
/// TinyMCE 에디터 HTML 생성 서비스
/// 메일 작성, 임시보관함, OneNote 등 모든 TinyMCE 에디터에 공통 설정을 적용
/// </summary>
public static class TinyMCEEditorService
{
    /// <summary>
    /// 이미지 파일 최대 크기 (10MB)
    /// </summary>
    private const long 최대파일크기 = 10 * 1024 * 1024;

    /// <summary>
    /// 이미지 확장자 목록
    /// </summary>
    private static readonly HashSet<string> 이미지확장자 = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico"
    };
    /// <summary>
    /// 에디터 유형
    /// </summary>
    public enum EditorType
    {
        /// <summary>임시보관함 에디터 (MainWindow)</summary>
        Draft,
        /// <summary>메일 작성 에디터 (ComposeWindow)</summary>
        Compose,
        /// <summary>OneNote 에디터 (MainWindow)</summary>
        OneNote
    }

    /// <summary>
    /// 에디터 유형별 가상 호스트명을 반환합니다.
    /// WebView2 SetVirtualHostNameToFolderMapping 호출 시 사용.
    /// </summary>
    public static string GetHostName(EditorType editorType)
    {
        return editorType switch
        {
            EditorType.Draft => "tinymce-draft.local",
            EditorType.Compose => "tinymce.local",
            EditorType.OneNote => "tinymce-onenote.local",
            _ => "tinymce-draft.local"
        };
    }

    /// <summary>
    /// TinyMCE 에디터 HTML을 생성합니다.
    /// </summary>
    /// <param name="editorType">에디터 유형</param>
    /// <param name="isDark">다크모드 여부 (null이면 현재 테마 자동 감지)</param>
    /// <returns>에디터 HTML</returns>
    public static string GenerateEditorHtml(EditorType editorType, bool? isDark = null)
    {
        // 테마 자동 감지
        var dark = isDark ?? ThemeService.Instance.IsDarkMode;

        // 공통 설정
        var skin = dark ? "oxide-dark" : "oxide";
        var contentCss = dark ? "dark" : "default";
        var bgColor = dark ? "#1e1e1e" : "#ffffff";
        var textColor = dark ? "#e0e0e0" : "#333333";

        // 에디터별 base_url
        var hostName = GetHostName(editorType);
        var baseUrl = $"https://{hostName}";

        // 공통 content_style (다크모드 표 배경색/인라인 스타일 오버라이드 포함)
        var contentStyle = GenerateContentStyle(dark, textColor, bgColor);

        // 공통 설정
        var plugins = "table lists link image code checklist";
        var toolbar = "bold italic underline strikethrough | forecolor backcolor | fontfamily fontsize | alignleft aligncenter alignright alignjustify | bullist numlist checklist outdent indent | table | link image | code removeformat";
        var fontFamilyFormats = "Aptos=Aptos,sans-serif; 맑은 고딕=Malgun Gothic; 굴림=Gulim; 돋움=Dotum; 바탕=Batang; 궁서=Gungsuh; Segoe UI=Segoe UI,sans-serif; Arial=arial,helvetica,sans-serif; Arial Black=arial black,avant garde; Comic Sans MS=comic sans ms,sans-serif; Courier New=courier new,courier; Georgia=georgia,palatino; Helvetica=helvetica; Impact=impact,chicago; Tahoma=tahoma,arial,helvetica,sans-serif; Terminal=terminal,monaco; Times New Roman=times new roman,times; Verdana=verdana,geneva";

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <script src=""{baseUrl}/tinymce.min.js""></script>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        html, body {{
            height: 100%;
            overflow: hidden;
            background-color: {bgColor};
        }}
        .tox-tinymce {{ border: none !important; }}
        .tox .tox-edit-area::before {{ border: none !important; }}
        .tox .tox-edit-area__iframe {{ background-color: {bgColor} !important; }}
    </style>
</head>
<body>
    <textarea id=""editor""></textarea>
    <script>
        let editor;

        tinymce.init({{
            selector: '#editor',
            height: '100%',
            width: '100%',
            menubar: false,
            statusbar: false,
            base_url: '{baseUrl}',
            suffix: '.min',
            skin: '{skin}',
            skin_url: '{baseUrl}/skins/ui/{skin}',
            content_css: '{baseUrl}/skins/content/{contentCss}/content.min.css',
            plugins: '{plugins}',
            toolbar: '{toolbar}',
            toolbar_mode: 'wrap',
            font_family_formats: '{fontFamilyFormats}',
            content_style: '{contentStyle}',
            table_toolbar: 'tableprops tabledelete | tableinsertrowbefore tableinsertrowafter tabledeleterow | tableinsertcolbefore tableinsertcolafter tabledeletecol',
            table_appearance_options: true,
            table_default_attributes: {{ border: '1' }},
            table_default_styles: {{ 'border-collapse': 'collapse', 'width': '100%' }},
            browser_spellcheck: true,
            contextmenu: false,
            paste_data_images: true,
            file_picker_types: 'image file',
            file_picker_callback: function(callback, value, meta) {{
                // 콜백을 전역에 저장하여 C#에서 호출 가능하게 함
                window._filePickerCallback = callback;
                window.chrome.webview.postMessage({{
                    type: 'filePicker',
                    pickerType: meta.filetype || 'file'
                }});
            }},
            setup: function(ed) {{
                editor = ed;
                ed.on('init', function() {{
                    window.chrome.webview.postMessage({{ type: 'ready' }});
                }});
                ed.on('input change', function() {{
                    window.chrome.webview.postMessage({{ type: 'contentChanged', content: ed.getContent() }});
                }});
            }}
        }});

        // C#에서 호출하는 함수들
        window.setContent = function(html) {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.setContent(html || '');
            }}
        }};

        window.getContent = function() {{
            if (tinymce.activeEditor) {{
                return tinymce.activeEditor.getContent();
            }}
            return '';
        }};

        window.insertContent = function(html) {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.insertContent(html);
            }}
        }};

        window.setReadOnly = function(readOnly) {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.mode.set(readOnly ? 'readonly' : 'design');
            }}
        }};

        window.focus = function() {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.focus();
            }}
        }};

        // C#에서 파일 탐색기 결과를 전달하는 콜백
        window.filePickerResult = function(url, meta) {{
            if (window._filePickerCallback) {{
                window._filePickerCallback(url, meta || {{}});
                window._filePickerCallback = null;
            }}
        }};

        // C#에서 드롭된 이미지를 삽입하는 함수
        window.insertDroppedImage = function(dataUrl, fileName) {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.insertContent('<img src=""' + dataUrl + '"" alt=""' + (fileName || '') + '"" />');
            }}
        }};

        // C#에서 드롭된 파일 링크를 삽입하는 함수
        window.insertDroppedFileLink = function(filePath, fileName) {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.insertContent('<a href=""' + filePath + '"">' + (fileName || filePath) + '</a>');
            }}
        }};
    </script>
</body>
</html>";
    }

    /// <summary>
    /// content_style CSS를 생성합니다.
    /// </summary>
    private static string GenerateContentStyle(bool isDark, string textColor, string bgColor)
    {
        var tableBgColor = isDark ? "#2d2d2d" : "inherit";
        var tableHeaderBgColor = isDark ? "#333" : "#f5f5f5";
        var tableBorderColor = isDark ? "#555" : "#ccc";

        // 작은따옴표 내에서 큰따옴표는 그대로 사용 가능
        // 다크모드: 모든 인라인 background/color를 강제 오버라이드 (OneNote HTML 등 외부 콘텐츠 대응)
        var inlineOverride = isDark
            ? $@" [style*=""background""] {{ background-color: transparent !important; background: transparent !important; }} [style*=""color""] {{ color: {textColor} !important; }} p, li, div, span, font, b, strong, i, em, u, a, h1, h2, h3, h4, h5, h6 {{ color: {textColor} !important; background-color: transparent !important; }}"
            : "";

        return $@"body {{ font-family: Aptos, sans-serif; font-size: 14px; color: {textColor}; background-color: {bgColor}; padding: 16px; }} * {{ color: inherit; }} table {{ border-collapse: collapse; }} table td, table th {{ color: {textColor} !important; background-color: {tableBgColor} !important; border: 1px solid {tableBorderColor}; padding: 4px 8px; }} table td[style*=""background""], table th[style*=""background""] {{ background-color: {tableBgColor} !important; }} table td *, table th * {{ color: {textColor} !important; }} table th {{ background-color: {tableHeaderBgColor} !important; }} span, font, b, strong, i, em, u {{ color: inherit !important; }}{inlineOverride}";
    }

    /// <summary>
    /// content_style CSS만 가져옵니다 (외부에서 사용 시).
    /// </summary>
    public static string GetContentStyle(bool isDark)
    {
        var textColor = isDark ? "#e0e0e0" : "#333333";
        var bgColor = isDark ? "#1e1e1e" : "#ffffff";
        return GenerateContentStyle(isDark, textColor, bgColor);
    }

    /// <summary>
    /// 파일 탐색기를 열어 파일을 선택하고, 결과를 WebView2 에디터에 전달합니다.
    /// WebMessageReceived에서 type='filePicker' 수신 시 호출.
    /// </summary>
    /// <param name="webView">대상 WebView2 컨트롤</param>
    /// <param name="pickerType">'image' 또는 'file'</param>
    public static async Task HandleFilePickerAsync(WebView2 webView, string pickerType)
    {
        if (webView?.CoreWebView2 == null) return;

        var dialog = new OpenFileDialog();

        if (pickerType == "image")
        {
            dialog.Title = "이미지 선택";
            dialog.Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.svg;*.ico|모든 파일|*.*";
        }
        else
        {
            dialog.Title = "파일 선택";
            dialog.Filter = "모든 파일|*.*|이미지 파일|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|문서 파일|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.txt";
        }

        if (dialog.ShowDialog() != true) return;

        var filePath = dialog.FileName;
        var fileName = System.IO.Path.GetFileName(filePath);
        var ext = System.IO.Path.GetExtension(filePath);

        if (이미지확장자.Contains(ext))
        {
            // 이미지 → Base64 data URL
            var dataUrl = ConvertFileToDataUrl(filePath);
            if (dataUrl == null) return;

            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(dataUrl);
            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.filePickerResult({escapedUrl}, {{alt: {escapedName}}})");
        }
        else
        {
            // 비이미지 → file:/// 링크
            var fileUrl = "file:///" + filePath.Replace("\\", "/");
            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.filePickerResult({escapedUrl}, {{text: {escapedName}, title: {escapedName}}})");
        }
    }

    /// <summary>
    /// WPF Drop 이벤트에서 드롭된 파일을 처리하여 에디터에 삽입합니다.
    /// </summary>
    /// <param name="webView">대상 WebView2 컨트롤</param>
    /// <param name="e">DragEventArgs</param>
    public static async Task HandleDropAsync(WebView2 webView, System.Windows.DragEventArgs e)
    {
        if (webView?.CoreWebView2 == null) return;

        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
        if (files == null || files.Length == 0) return;

        foreach (var filePath in files)
        {
            if (!System.IO.File.Exists(filePath)) continue;

            var fileName = System.IO.Path.GetFileName(filePath);
            var ext = System.IO.Path.GetExtension(filePath);

            if (이미지확장자.Contains(ext))
            {
                // 이미지 → Base64 data URL로 삽입
                var dataUrl = ConvertFileToDataUrl(filePath);
                if (dataUrl == null) continue;

                var escapedUrl = System.Text.Json.JsonSerializer.Serialize(dataUrl);
                var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.insertDroppedImage({escapedUrl}, {escapedName})");
            }
            else
            {
                // 비이미지 → file:/// 링크로 삽입
                var fileUrl = "file:///" + filePath.Replace("\\", "/");
                var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
                var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.insertDroppedFileLink({escapedUrl}, {escapedName})");
            }
        }

        e.Handled = true;
    }

    /// <summary>
    /// 파일을 Base64 data URL로 변환합니다.
    /// </summary>
    /// <returns>data URL 문자열, 실패 시 null</returns>
    public static string? ConvertFileToDataUrl(string filePath)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (fileInfo.Length > 최대파일크기)
            {
                System.Windows.MessageBox.Show(
                    $"파일 크기가 10MB를 초과합니다: {fileInfo.Length / 1024 / 1024}MB",
                    "파일 크기 초과", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return null;
            }

            var bytes = System.IO.File.ReadAllBytes(filePath);
            var base64 = Convert.ToBase64String(bytes);
            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            var mimeType = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream"
            };

            return $"data:{mimeType};base64,{base64}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 파일 경로가 이미지인지 확인합니다.
    /// </summary>
    public static bool IsImageFile(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath);
        return 이미지확장자.Contains(ext);
    }
}
