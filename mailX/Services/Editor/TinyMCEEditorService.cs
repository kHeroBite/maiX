using mailX.Services.Theme;

namespace mailX.Services.Editor;

/// <summary>
/// TinyMCE 에디터 HTML 생성 서비스
/// 메일 작성, OneNote 등 모든 TinyMCE 에디터에 공통 설정을 적용
/// </summary>
public static class TinyMCEEditorService
{
    /// <summary>
    /// 에디터 유형
    /// </summary>
    public enum EditorType
    {
        /// <summary>메일 작성 에디터</summary>
        Draft,
        /// <summary>OneNote 에디터</summary>
        OneNote
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
        var baseUrl = editorType switch
        {
            EditorType.Draft => "https://tinymce-draft.local",
            EditorType.OneNote => "https://tinymce-onenote.local",
            _ => "https://tinymce-draft.local"
        };

        // 공통 content_style (다크모드 표 배경색 포함)
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
        function setContent(html) {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.setContent(html || '');
            }}
        }}

        function getContent() {{
            if (tinymce.activeEditor) {{
                return tinymce.activeEditor.getContent();
            }}
            return '';
        }}

        function setReadOnly(readOnly) {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.mode.set(readOnly ? 'readonly' : 'design');
            }}
        }}

        function focus() {{
            if (tinymce.activeEditor) {{
                tinymce.activeEditor.focus();
            }}
        }}
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
        return $@"body {{ font-family: Aptos, sans-serif; font-size: 14px; color: {textColor}; background-color: {bgColor}; padding: 16px; }} * {{ color: inherit; }} table {{ border-collapse: collapse; }} table td, table th {{ color: {textColor} !important; background-color: {tableBgColor} !important; border: 1px solid {tableBorderColor}; padding: 4px 8px; }} table td[style*=""background""], table th[style*=""background""] {{ background-color: {tableBgColor} !important; }} table td *, table th * {{ color: {textColor} !important; }} table th {{ background-color: {tableHeaderBgColor} !important; }} span, font, b, strong, i, em, u {{ color: inherit !important; }}";
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
}
