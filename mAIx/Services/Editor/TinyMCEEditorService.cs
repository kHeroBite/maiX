using mAIx.Services.Theme;
using mAIx.Utils;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Wpf;

namespace mAIx.Services.Editor;

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
    /// DragOver에서 저장한 파일 경로 (파일명 → 전체 경로 매핑)
    /// JS drop 이벤트에서는 파일명만 알 수 있으므로, 이 딕셔너리에서 전체 경로를 조회합니다.
    /// </summary>
    private static readonly Dictionary<string, string> _최근드롭파일경로 = new();

    /// <summary>
    /// WPF DragOver 이벤트에서 드래그 중인 파일 경로를 저장합니다.
    /// </summary>
    public static void 드래그파일경로저장(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        var files = e.Data.GetData(System.Windows.DataFormats.FileDrop) as string[];
        if (files == null) return;

        _최근드롭파일경로.Clear();
        foreach (var f in files)
        {
            var name = System.IO.Path.GetFileName(f);
            _최근드롭파일경로[name] = f;
            Log4.Debug($"[TinyMCE] DragOver 파일경로 저장: {name} → {f}");
        }
    }

    /// <summary>
    /// 파일명으로 최근 드롭 경로를 조회합니다.
    /// </summary>
    public static string? 최근드롭경로가져오기(string fileName)
    {
        return _최근드롭파일경로.TryGetValue(fileName, out var path) ? path : null;
    }

    /// <summary>
    /// JS drop 이벤트에서 전달받은 파일명으로 첨부 링크를 에디터에 삽입합니다.
    /// </summary>
    public static async Task 비이미지파일드롭처리Async(WebView2 webView, string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || webView?.CoreWebView2 == null) return;

        // DragOver에서 저장한 경로 조회
        if (_최근드롭파일경로.TryGetValue(fileName, out var fullPath))
        {
            Log4.Debug($"[TinyMCE] 비이미지 파일 드롭 (경로 확인): {fileName} → {fullPath}");
            var fileUrl = "file:///" + fullPath.Replace("\\", "/");
            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.insertDroppedFileLink({escapedUrl}, {escapedName})").ConfigureAwait(false);
        }
        else
        {
            // 경로 없으면 파일명만 텍스트로 삽입
            Log4.Warn($"[TinyMCE] 비이미지 파일 드롭 (경로 미확인): {fileName}");
            var escaped = System.Text.Json.JsonSerializer.Serialize($"📎 {fileName} ");
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"tinymce.activeEditor && tinymce.activeEditor.insertContent({escaped})").ConfigureAwait(false);
        }
    }

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
        var plugins = "table lists link image code checklist autolink";
        var toolbar = "bold italic underline strikethrough | forecolor backcolor | fontfamily fontsize | alignleft aligncenter alignright alignjustify | bullist numlist checklist outdent indent | table | link image insertfile | code removeformat";
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
            block_unsupported_drop: false,
            convert_urls: false,
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
                ed.ui.registry.addButton('insertfile', {{
                    icon: 'browse',
                    tooltip: '파일 삽입',
                    onAction: function() {{
                        window.chrome.webview.postMessage({{
                            type: 'filePicker',
                            pickerType: 'file'
                        }});
                    }}
                }});
                ed.on('init', function() {{
                    var iframeDoc = ed.getDoc();
                    if (iframeDoc) {{
                        iframeDoc.addEventListener('drop', function(evt) {{
                            var dt = evt.dataTransfer;
                            if (!dt || !dt.files || dt.files.length === 0) return;
                            var file = dt.files[0];
                            var idx = file.name.lastIndexOf('.');
                            var ext = idx >= 0 ? file.name.substring(idx).toLowerCase() : '';
                            var imageExts = ['.jpg','.jpeg','.png','.gif','.bmp','.webp','.svg','.ico','.tif','.tiff'];
                            if (imageExts.indexOf(ext) < 0) {{
                                // 비이미지 파일 → 브라우저 기본 동작 차단 + FileReader로 base64 전달
                                evt.preventDefault();
                                evt.stopImmediatePropagation();
                                var reader = new FileReader();
                                var droppedFile = file;
                                reader.onload = function(e) {{
                                    var bytes = new Uint8Array(e.target.result);
                                    var binary = '';
                                    for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
                                    var base64 = btoa(binary);
                                    var msg = {{
                                        type: 'nonImageFileDropWithData',
                                        fileName: droppedFile.name,
                                        fileSize: '' + droppedFile.size,
                                        base64: base64
                                    }};
                                    try {{
                                        window.chrome.webview.postMessage(msg);
                                    }} catch(ex) {{
                                        try {{ window.parent.chrome.webview.postMessage(msg); }} catch(ex2) {{}}
                                    }}
                                }};
                                reader.onerror = function() {{
                                    var errMsg = {{
                                        type: 'nonImageFileDrop',
                                        fileName: droppedFile.name,
                                        filePath: ''
                                    }};
                                    try {{
                                        window.chrome.webview.postMessage(errMsg);
                                    }} catch(ex) {{
                                        try {{ window.parent.chrome.webview.postMessage(errMsg); }} catch(ex2) {{}}
                                    }}
                                }};
                                reader.readAsArrayBuffer(file);
                            }}
                            // 이미지 파일은 TinyMCE 기본 처리에 맡김
                        }}, true);
                        // 링크 더블클릭 → postMessage로 C#에 전달 (싱글클릭 비활성화)
                        iframeDoc.addEventListener('click', function(evt) {{
                            var el = evt.target;
                            while (el && el.tagName !== 'A') el = el.parentElement;
                            if (el && el.tagName === 'A' && el.href) {{
                                evt.preventDefault();
                                evt.stopImmediatePropagation();
                            }}
                        }}, true);
                        iframeDoc.addEventListener('dblclick', function(evt) {{
                            var el = evt.target;
                            while (el && el.tagName !== 'A') el = el.parentElement;
                            if (el && el.tagName === 'A' && el.href) {{
                                evt.preventDefault();
                                evt.stopImmediatePropagation();
                                try {{
                                    window.chrome.webview.postMessage({{ type: 'linkClick', url: el.href, fileName: el.getAttribute('data-attachment') || '' }});
                                }} catch(ex) {{
                                    try {{ window.parent.chrome.webview.postMessage({{ type: 'linkClick', url: el.href, fileName: el.getAttribute('data-attachment') || '' }}); }} catch(ex2) {{}}
                                }}
                            }}
                        }}, true);
                    }}
                    // OneNote 레이어 드래그 핸들 주입 + 드래그 이동 로직
                    (function() {{
                        var doc = ed.getDoc();
                        if (!doc) return;

                        function addHandles() {{
                            var layers = doc.querySelectorAll('div[style*=""position:absolute""], div[style*=""position: absolute""]');
                            layers.forEach(function(layer) {{
                                if (layer.getAttribute('data-layer-type') === 'card') return;
                                if (layer.querySelector('.layer-handle')) return;
                                var handle = doc.createElement('div');
                                handle.className = 'layer-handle';
                                handle.textContent = '⠿';
                                handle.setAttribute('contenteditable', 'false');
                                layer.insertBefore(handle, layer.firstChild);
                            }});
                        }}
                        addHandles();

                        // setContent 후 핸들 재주입
                        ed.on('SetContent', function() {{ setTimeout(addHandles, 50); }});

                        // 클릭 시 선택 토글
                        doc.addEventListener('mousedown', function(e) {{
                            var layers = doc.querySelectorAll('.mce-layer-selected');
                            layers.forEach(function(l) {{ l.classList.remove('mce-layer-selected'); }});
                            var layer = e.target.closest('div[style*=""position:absolute""], div[style*=""position: absolute""]');
                            if (layer) layer.classList.add('mce-layer-selected');
                        }});

                        // 카드 레이어 내부 요소의 기본 드래그 차단
                        doc.addEventListener('dragstart', function(e) {{
                            var cardLayer = e.target.closest('div[data-layer-type=""card""]');
                            if (cardLayer) e.preventDefault();
                        }});

                        // 드래그 이동 (핸들 또는 카드 레이어 내부)
                        var dragState = null;
                        doc.addEventListener('mousedown', function(e) {{
                            var isHandle = e.target.classList.contains('layer-handle');
                            var cardLayer = e.target.closest('div[data-layer-type=""card""]');
                            if (!isHandle && !cardLayer) return;
                            e.preventDefault();
                            var layer = isHandle ? e.target.parentElement : cardLayer;
                            var startX = e.clientX, startY = e.clientY;
                            var origLeft = parseInt(layer.style.left) || 0;
                            var origTop = parseInt(layer.style.top) || 0;
                            dragState = {{ layer: layer, startX: startX, startY: startY, origLeft: origLeft, origTop: origTop }};
                            layer.style.cursor = 'grabbing';
                        }});
                        doc.addEventListener('mousemove', function(e) {{
                            if (!dragState) return;
                            e.preventDefault();
                            var dx = e.clientX - dragState.startX;
                            var dy = e.clientY - dragState.startY;
                            dragState.layer.style.left = (dragState.origLeft + dx) + 'px';
                            dragState.layer.style.top = (dragState.origTop + dy) + 'px';
                        }});
                        doc.addEventListener('mouseup', function(e) {{
                            if (!dragState) return;
                            dragState.layer.style.cursor = '';
                            var handle = dragState.layer.querySelector('.layer-handle');
                            if (handle) handle.style.cursor = 'grab';
                            dragState = null;
                            // 위치 변경을 contentChanged로 전파
                            ed.fire('change');
                        }});
                    }})();

                    window.chrome.webview.postMessage({{ type: 'ready' }});
                }});
                ed.on('input change', function() {{
                    var raw = ed.getContent();
                    var tmp = document.createElement('div');
                    tmp.innerHTML = raw;
                    // 표시용 요소/클래스 제거 (PATCH에 포함되면 안 됨)
                    tmp.querySelectorAll('.layer-handle').forEach(function(h) {{ h.remove(); }});
                    tmp.querySelectorAll('.mce-layer-selected').forEach(function(el) {{ el.classList.remove('mce-layer-selected'); }});
                    window.chrome.webview.postMessage({{ type: 'contentChanged', content: tmp.innerHTML }});
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
                var raw = tinymce.activeEditor.getContent();
                var tmp = document.createElement('div');
                tmp.innerHTML = raw;
                tmp.querySelectorAll('.layer-handle').forEach(function(h) {{ h.remove(); }});
                tmp.querySelectorAll('.mce-layer-selected').forEach(function(el) {{ el.classList.remove('mce-layer-selected'); }});
                return tmp.innerHTML;
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

        // C#에서 드롭된 파일 첨부를 삽입하는 함수 (file:/// 링크 — 클릭 시 NavigationStarting에서 처리)
        window.insertDroppedFileLink = function(filePath, fileName) {{
            if (tinymce.activeEditor) {{
                var html = '<a href=""' + filePath + '"" ' +
                    'style=""display:inline-block;padding:4px 10px;margin:2px;border:1px solid #ccc;border-radius:6px;background:#f5f5f5;color:#333;text-decoration:none;font-size:13px;cursor:pointer;"" ' +
                    'title=""클릭하여 열기"" contenteditable=""false"">📎 ' + (fileName || filePath) + '</a>&nbsp;';
                tinymce.activeEditor.insertContent(html);
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

        // OneNote 레이어 박스/리사이즈 CSS
        var layerBorderColor = isDark ? "#666" : "#ccc";
        
        var handleBg = isDark ? "#333" : "#e8e8e8";
        var handleBorder = isDark ? "#555" : "#ccc";
        var handleHoverBg = isDark ? "#444" : "#ddd";
        var layerCss = $@" div[style*=""position:absolute""], div[style*=""position: absolute""] {{ border: 1px solid transparent; resize: both; overflow: visible; box-sizing: border-box; position: absolute; }} div[style*=""position:absolute""]:hover, div[style*=""position: absolute""]:hover {{ border: 1px dashed {layerBorderColor}; }} div[style*=""position:absolute""].mce-layer-selected, div[style*=""position: absolute""].mce-layer-selected {{ border: 1px solid {layerBorderColor}; }} div[style*=""position:absolute""] > .layer-handle, div[style*=""position: absolute""] > .layer-handle {{ display: none; position: absolute; top: -10px; left: -1px; right: -1px; height: 9px; background: {handleBg}; border: 1px solid {handleBorder}; border-bottom: none; border-radius: 2px 2px 0 0; cursor: grab; font-size: 7px; line-height: 9px; text-align: center; color: {layerBorderColor}; user-select: none; z-index: 10; outline: none; }} div[style*=""position:absolute""]:hover > .layer-handle, div[style*=""position: absolute""]:hover > .layer-handle, div[style*=""position:absolute""].mce-layer-selected > .layer-handle, div[style*=""position: absolute""].mce-layer-selected > .layer-handle {{ display: block; }} div[style*=""position:absolute""] > .layer-handle:hover, div[style*=""position: absolute""] > .layer-handle:hover {{ background: {handleHoverBg}; cursor: grabbing; }} div[data-layer-type=""card""] {{ border: none !important; }} div[data-layer-type=""card""]:hover {{ border: none !important; }} div[data-layer-type=""card""].mce-layer-selected {{ border: none !important; }} div[data-layer-type=""card""] > .layer-handle {{ display: none !important; }} .layer-handle:focus, .layer-handle:active, div[style*=""position:absolute""]:focus, div[style*=""position: absolute""]:focus, div[style*=""position:absolute""] *:focus, div[style*=""position: absolute""] *:focus {{ outline: none !important; box-shadow: none !important; }}";

        return $@"body {{ font-family: Aptos, sans-serif; font-size: 14px; color: {textColor}; background-color: {bgColor}; padding: 16px; }} * {{ color: inherit; }} table {{ border-collapse: collapse; }} table td, table th {{ color: {textColor} !important; background-color: {tableBgColor} !important; border: 1px solid {tableBorderColor}; padding: 4px 8px; }} table td[style*=""background""], table th[style*=""background""] {{ background-color: {tableBgColor} !important; }} table td *, table th * {{ color: {textColor} !important; }} table th {{ background-color: {tableHeaderBgColor} !important; }} span, font, b, strong, i, em, u {{ color: inherit !important; }} a {{ cursor: pointer !important; }}{inlineOverride}{layerCss}";
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
                $"window.filePickerResult({escapedUrl}, {{alt: {escapedName}}})").ConfigureAwait(false);
        }
        else
        {
            // 비이미지 → file:/// 링크
            var fileUrl = "file:///" + filePath.Replace("\\", "/");
            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.filePickerResult({escapedUrl}, {{text: {escapedName}, title: {escapedName}}})").ConfigureAwait(false);
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

        Log4.Debug($"[TinyMCE] WPF Drop 처리: {files.Length}개 파일");

        foreach (var filePath in files)
        {
            if (!System.IO.File.Exists(filePath)) continue;

            var fileName = System.IO.Path.GetFileName(filePath);
            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

            if (이미지확장자.Contains(ext))
            {
                // 이미지 → Base64 data URL로 삽입
                var dataUrl = ConvertFileToDataUrl(filePath);
                if (dataUrl == null) continue;

                var escapedUrl = System.Text.Json.JsonSerializer.Serialize(dataUrl);
                var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.insertDroppedImage({escapedUrl}, {escapedName})").ConfigureAwait(false);
            }
            else
            {
                // 비이미지 → 첨부 파일 스타일로 삽입
                Log4.Debug($"[TinyMCE] 비이미지 파일 첨부 삽입: {fileName} → {filePath}");
                var fileUrl = "file:///" + filePath.Replace("\\", "/");
                var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
                var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.insertDroppedFileLink({escapedUrl}, {escapedName})").ConfigureAwait(false);
            }
        }
    }

    public static async Task 비이미지파일드롭처리Async(WebView2 webView, string fileName, string filePath = "")
    {
        if (string.IsNullOrEmpty(fileName) || webView?.CoreWebView2 == null) return;

        // 1순위: JS dataTransfer에서 전달된 file:/// 경로
        string? resolvedPath = null;
        if (!string.IsNullOrEmpty(filePath))
        {
            // file:///C:/... 형식 → C:\... 로 변환
            var path = filePath.Trim();
            if (path.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                path = Uri.UnescapeDataString(path.Substring("file:///".Length)).Replace("/", "\\");
            else if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                path = Uri.UnescapeDataString(path.Substring("file://".Length)).Replace("/", "\\");

            if (System.IO.File.Exists(path))
                resolvedPath = path;
        }

        // 2순위: DragOver에서 저장한 경로 조회 (Fallback)
        if (resolvedPath == null && _최근드롭파일경로.TryGetValue(fileName, out var fullPath))
            resolvedPath = fullPath;

        if (resolvedPath != null)
        {
            Log4.Debug($"[TinyMCE] 비이미지 파일 드롭 (경로 확인): {fileName} → {resolvedPath}");
            var fileUrl = "file:///" + resolvedPath.Replace("\\", "/");
            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.insertDroppedFileLink({escapedUrl}, {escapedName})").ConfigureAwait(false);
        }
        else
        {
            // 경로 없으면 파일명만 텍스트로 삽입
            Log4.Warn($"[TinyMCE] 비이미지 파일 드롭 (경로 미확인): {fileName}, JS경로: {filePath}");
            var escaped = System.Text.Json.JsonSerializer.Serialize($"📎 {fileName} ");
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"tinymce.activeEditor && tinymce.activeEditor.insertContent({escaped})").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// JS FileReader에서 전달받은 base64 데이터를 임시 파일로 저장합니다.
    /// </summary>
    public static async Task<string?> 파일드롭데이터저장Async(string fileName, string base64Data)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(base64Data))
                return null;

            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mAIx_Drop", Guid.NewGuid().ToString("N")[..8]);
            System.IO.Directory.CreateDirectory(tempDir);

            // 원본 파일명 유지 (충돌 방지는 GUID 서브디렉토리)
            var safeName = string.Join("_", fileName.Split(System.IO.Path.GetInvalidFileNameChars()));
            var tempPath = System.IO.Path.Combine(tempDir, safeName);

            var bytes = Convert.FromBase64String(base64Data);
            await System.IO.File.WriteAllBytesAsync(tempPath, bytes).ConfigureAwait(false);

            Log4.Debug2($"[TinyMCE] 파일드롭 임시저장: {fileName} → {tempPath} ({bytes.Length:N0} bytes)");
            return tempPath;
        }
        catch (Exception ex)
        {
            Log4.Error($"[TinyMCE] 파일드롭 임시저장 실패: {fileName} - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 에디터 WebView2의 NavigationStarting 이벤트 핸들러.
    /// 비이미지 파일 드래그&amp;드롭 시 file:/// URL을 링크로 삽입하고,
    /// 외부 링크 클릭 시 기본 브라우저로 엽니다.

    /// <summary>
    /// JS postMessage로 전달된 링크 클릭을 처리합니다.
    /// TinyMCE 편집 모드에서는 NavigationStarting이 발생하지 않으므로
    /// JS click 이벤트에서 postMessage로 전달받아 처리합니다.
    /// </summary>
    public static async void HandleLinkClick(string url, string fileName = "")
    {
        if (string.IsNullOrEmpty(url)) return;

        Log4.Debug($"[TinyMCE] 링크 클릭: {url}");

        try
        {
            if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                var filePath = Uri.UnescapeDataString(url.Substring("file:///".Length)).Replace("/", "\\");
                if (System.IO.File.Exists(filePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Log4.Warn($"[TinyMCE] 링크 파일 없음: {filePath}");
                }
            }
            else if (url.Contains("graph.microsoft.com", StringComparison.OrdinalIgnoreCase) &&
                     url.Contains("/onenote/resources/", StringComparison.OrdinalIgnoreCase))
            {
                // Graph API OneNote 리소스 URL → 인증 다운로드 후 로컬 파일 열기
                Log4.Debug($"[TinyMCE] OneNote 리소스 다운로드 시작 (linkClick): {url}, 파일명: {fileName}");
                var graphService = ((App)System.Windows.Application.Current).GetService<Services.Graph.GraphOneNoteService>();
                if (graphService != null)
                {
                    var downloadFileName = !string.IsNullOrEmpty(fileName) ? fileName : "downloaded_file";
                    var saveDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "mAIx", "mAIx_Drop");
                    var localPath = await graphService.DownloadAudioResourceAsync(url, downloadFileName, saveDir).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(localPath) && System.IO.File.Exists(localPath))
                    {
                        Log4.Info($"[TinyMCE] OneNote 리소스 다운로드 완료, 열기: {localPath}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = localPath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        Log4.Warn($"[TinyMCE] OneNote 리소스 다운로드 실패: {url}");
                    }
                }
                else
                {
                    Log4.Warn("[TinyMCE] GraphOneNoteService를 찾을 수 없음 — 브라우저로 열기");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"[TinyMCE] 링크 열기 실패: {url} — {ex}");
        }
    }
    /// </summary>
    public static async void HandleEditorNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        try
        {
        // 초기 로드(about:blank) 및 data: URI는 허용
        if (e.Uri.StartsWith("about:") || e.Uri.StartsWith("data:"))
            return;

        // 가상 호스트(TinyMCE 리소스 로드)는 허용
        if (e.Uri.Contains(".tinymce.local/"))
            return;

        Log4.Debug($"[TinyMCE] NavigationStarting 감지: {e.Uri}");

        // 모든 외부 navigation 차단
        e.Cancel = true;

        if (e.Uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            // file:/// URL → 로컬 파일 경로 추출
            var filePath = Uri.UnescapeDataString(e.Uri.Substring("file:///".Length)).Replace("/", "\\");
            var fileName = System.IO.Path.GetFileName(filePath);
            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

            if (sender is Microsoft.Web.WebView2.Wpf.WebView2 webView && webView.CoreWebView2 != null)
            {
                if (이미지확장자.Contains(ext) && System.IO.File.Exists(filePath))
                {
                    // 이미지 → Base64 data URL로 삽입
                    var dataUrl = ConvertFileToDataUrl(filePath);
                    if (dataUrl != null)
                    {
                        var escapedUrl = System.Text.Json.JsonSerializer.Serialize(dataUrl);
                        var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
                        await webView.CoreWebView2.ExecuteScriptAsync(
                            $"tinymce.activeEditor && tinymce.activeEditor.insertContent('<img src=\"' + {escapedUrl} + '\" alt=\"' + {escapedName} + '\" />')").ConfigureAwait(false);
                    }
                }
                else
                {
                    // 비이미지 file:/// 링크 클릭 → 파일 열기
                    Log4.Debug($"[TinyMCE] 첨부 파일 열기: {filePath}");
                    try
                    {
                        if (System.IO.File.Exists(filePath))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = filePath,
                                UseShellExecute = true
                            });
                        }
                        else
                        {
                            Log4.Warn($"[TinyMCE] 첨부 파일 없음: {filePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log4.Error($"[TinyMCE] 첨부 파일 열기 실패: {filePath} — {ex.Message}");
                    }
                }
            }
        }
        else if (e.Uri.Contains("graph.microsoft.com", StringComparison.OrdinalIgnoreCase) &&
                 e.Uri.Contains("/onenote/resources/", StringComparison.OrdinalIgnoreCase))
        {
            // Graph API onenote resource URL → 인증 다운로드 후 로컬 파일 열기
            Log4.Debug($"[TinyMCE] OneNote 리소스 다운로드 시작: {e.Uri}");
            try
            {
                var graphService = ((App)System.Windows.Application.Current).GetService<Services.Graph.GraphOneNoteService>();
                if (graphService != null)
                {
                    // URL에서 파일명 추출 시도 — 카드 HTML의 data-filename 속성에서 가져올 수 없으므로
                    // NavigationStarting 이벤트 시점에 WebView에서 클릭된 요소의 파일명을 알 수 없음
                    // → 다운로드 후 Content-Disposition 헤더 또는 URL에서 추정
                    var fileName = "downloaded_file";

                    // sender에서 WebView를 가져와 클릭된 링크의 파일명 추출 시도
                    if (sender is Microsoft.Web.WebView2.Wpf.WebView2 wv && wv.CoreWebView2 != null)
                    {
                        try
                        {
                            var jsResult = await wv.CoreWebView2.ExecuteScriptAsync(
                                "(() => { var el = document.querySelector('a[href*=\"graph.microsoft.com\"][data-attachment]').ConfigureAwait(false); return el ? el.getAttribute('data-attachment') : ''; })()");
                            var parsed = jsResult?.Trim('"');
                            if (!string.IsNullOrEmpty(parsed))
                                fileName = parsed;
                        }
                        catch { /* JS 실패 시 기본 파일명 사용 */ }
                    }

                    var saveDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "mAIx", "mAIx_Drop");
                    var localPath = await graphService.DownloadAudioResourceAsync(e.Uri, fileName, saveDir).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(localPath) && System.IO.File.Exists(localPath))
                    {
                        Log4.Info($"[TinyMCE] OneNote 리소스 다운로드 완료, 열기: {localPath}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = localPath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        Log4.Warn($"[TinyMCE] OneNote 리소스 다운로드 실패: {e.Uri}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log4.Error($"[TinyMCE] OneNote 리소스 다운로드 오류: {e.Uri} — {ex.Message}");
            }
        }
        else
        {
            // http/https 등 외부 링크 → 기본 브라우저로 열기
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
                Log4.Error($"[TinyMCE] 외부 링크 열기 실패: {e.Uri} — {ex.Message}");
            }
        }
        } // try
        catch (Exception ex)
        {
            Log4.Error($"[TinyMCE] NavigationStarting 처리 실패: {e.Uri} — {ex}");
        }
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

    /// <summary>
    /// 단일 파일을 에디터에 삽입합니다.
    /// 이미지 → Base64 data URL로 인라인 삽입, 비이미지 → file:/// 링크 삽입.
    /// </summary>
    public static async Task InsertFileToEditorAsync(WebView2 webView, string filePath)
    {
        if (webView?.CoreWebView2 == null || !System.IO.File.Exists(filePath)) return;

        var fileName = System.IO.Path.GetFileName(filePath);

        if (IsImageFile(filePath))
        {
            var dataUrl = ConvertFileToDataUrl(filePath);
            if (dataUrl == null) return;

            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(dataUrl);
            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.insertDroppedImage({escapedUrl}, {escapedName})").ConfigureAwait(false);
        }
        else
        {
            var fileUrl = "file:///" + filePath.Replace("\\", "/");
            var escapedUrl = System.Text.Json.JsonSerializer.Serialize(fileUrl);
            var escapedName = System.Text.Json.JsonSerializer.Serialize(fileName);
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"window.insertDroppedFileLink({escapedUrl}, {escapedName})").ConfigureAwait(false);
        }
    }
}
