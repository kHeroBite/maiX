// WebView2 + Markmap으로 STT/MAP/요약 데이터를 마인드맵으로 렌더링하는 오버레이 컨트롤
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using NLog;
using mAIx.Models;
using mAIx.Services.Theme;
using Wpf.Ui.Appearance;

namespace mAIx.Controls;

/// <summary>
/// STT/MAP/요약 데이터를 WebView2+Markmap 마인드맵으로 오버레이 표시하는 컨트롤
/// </summary>
public partial class MindMapOverlay : UserControl
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private bool _isWebViewReady;
    private DispatcherTimer? _debounceTimer;
    private const double DebounceMs = 1000;

    private ObservableCollection<TopicSegment>? _topicSegments;
    private ObservableCollection<MinuteSummaryEntry>? _minuteSummaries;
    private string _rootTitle = "녹음 데이터";

    // 묵음/무의미 토픽 필터 키워드
    private static readonly HashSet<string> _silenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "묵음", "무음", "(silence)", "(silent)", "(음)", "(어)",
        "...", "음...", "어...", "음", "어"
    };

    // 키워드 L2 노드 불필요 단어 (MinuteSummaryService._stopWords와 동일 세트)
    private static readonly HashSet<string> _stopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "이것", "그것", "저것", "이거", "그거", "저거",
        "이런", "그런", "저런", "이렇게", "그렇게", "저렇게",
        "있는", "있어", "있다", "없는", "없어", "없다"
    };

    // ThemeService 구독 핸들러 (Unbind에서 해제 필수)
    private EventHandler<ApplicationTheme>? _themeHandler;
    private EventHandler<CoreWebView2WebMessageReceivedEventArgs>? _webMessageHandler;

    /// <summary>
    /// 닫기 콜백 — 부모(MainWindow.OneNote.cs)에서 등록
    /// </summary>
    public Action? CloseRequested { get; set; }

    public MindMapOverlay()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            try
            {
                await InitializeAsync();
                Focus();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[AC-MM-실행] MindMapOverlay Loaded 핸들러 실패");
            }
        };
        IsVisibleChanged += (s, e) =>
        {
            if ((bool)e.NewValue) Focus();
        };
    }

    /// <summary>
    /// 우상단 X 버튼 클릭 → CloseRequested 콜백 호출
    /// </summary>
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _log.Info("[AC-MMX-실행] X 닫기 버튼 클릭");
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MMX-실행] CloseButton_Click 실패");
        }
    }

    /// <summary>
    /// ESC 키 감지 → CloseRequested 콜백 호출
    /// </summary>
    private void MindMapOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape)
            {
                _log.Info("[AC-MMX-실행] ESC 키 감지 — 닫기 요청");
                CloseRequested?.Invoke();
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MMX-실행] PreviewKeyDown 실패");
        }
    }

    /// <summary>
    /// WebView2 환경 초기화 — Loaded 이벤트에서 호출
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync();
            await MindMapWebView.EnsureCoreWebView2Async(env);

            var resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
            MindMapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "mindmap.local", resourcePath, CoreWebView2HostResourceAccessKind.Allow);

            MindMapWebView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                _isWebViewReady = true;
                _log.Info("[AC-MM-실행] WebView2 초기화 완료 — mindmap.html 로드");

                // WebMessageReceived 핸들러 등록 (HTML 내부 X 버튼 → close 메시지)
                _webMessageHandler = OnWebMessageReceived;
                MindMapWebView.CoreWebView2.WebMessageReceived += _webMessageHandler;

                // ThemeService 구독 + 초기 테마 적용
                _themeHandler = async (_, theme) =>
                {
                    try
                    {
                        await ApplyThemeAsync(theme);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "[AC-MMP-실행] ThemeHandler 실패");
                    }
                };
                ThemeService.Instance.ThemeChanged += _themeHandler;
                _ = ApplyThemeAsync(ThemeService.Instance.CurrentTheme);

                _ = RenderAsync();
            };

            MindMapWebView.CoreWebView2.Navigate("https://mindmap.local/mindmap.html");

            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DebounceMs)
            };
            _debounceTimer.Tick += async (s, e) =>
            {
                try
                {
                    _debounceTimer?.Stop();
                    await RenderAsync();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "[AC-MM-실행] 디바운스 Tick 처리 실패");
                }
            };
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MindMapWebView.Visibility = Visibility.Collapsed;
            ErrorMessage.Visibility = Visibility.Visible;
            ErrorMessage.Text =
                "WebView2 런타임이 설치되어 있지 않습니다.\n" +
                "https://developer.microsoft.com/microsoft-edge/webview2/ 에서 설치하세요.";
            _log.Warn("[AC-MM-실행] WebView2 런타임 미설치 — 안내 메시지 표시");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MM-실행] MindMapOverlay 초기화 실패");
        }
    }

    /// <summary>
    /// 컬렉션 데이터 바인딩 — TopicSegments/MinuteSummaries CollectionChanged 구독
    /// </summary>
    public void Bind(
        ObservableCollection<TopicSegment>? topics,
        ObservableCollection<MinuteSummaryEntry>? summaries,
        string rootTitle)
    {
        Unbind();
        _topicSegments = topics;
        _minuteSummaries = summaries;
        _rootTitle = string.IsNullOrWhiteSpace(rootTitle) ? "녹음 데이터" : rootTitle;

        if (_topicSegments != null)
            _topicSegments.CollectionChanged += OnDataChanged;
        if (_minuteSummaries != null)
            _minuteSummaries.CollectionChanged += OnDataChanged;

        TriggerRender();
        _log.Info($"[AC-MM-실행] 마인드맵 바인딩 완료 — rootTitle={_rootTitle}");
    }

    /// <summary>
    /// 컬렉션 이벤트 구독 해제
    /// </summary>
    public void Unbind()
    {
        if (_topicSegments != null)
            _topicSegments.CollectionChanged -= OnDataChanged;
        if (_minuteSummaries != null)
            _minuteSummaries.CollectionChanged -= OnDataChanged;

        // ThemeChanged 구독 해제 (메모리 누수 방지 — 위험 #2)
        if (_themeHandler != null)
        {
            ThemeService.Instance.ThemeChanged -= _themeHandler;
            _themeHandler = null;
        }

        // WebMessageReceived 구독 해제
        if (_webMessageHandler != null && _isWebViewReady)
        {
            try
            {
                MindMapWebView.CoreWebView2.WebMessageReceived -= _webMessageHandler;
            }
            catch (Exception ex)
            {
                _log.Warn(ex, "[AC-MMP-실행] WebMessageReceived 해제 실패");
            }
            _webMessageHandler = null;
        }

        _topicSegments = null;
        _minuteSummaries = null;
        _debounceTimer?.Stop();
    }

    /// <summary>
    /// 컬렉션 변경 시 디바운스 렌더 트리거
    /// </summary>
    private void OnDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => TriggerRender();

    /// <summary>
    /// 디바운스 타이머 리셋 — 마지막 변경 후 1초 뒤 렌더
    /// </summary>
    public void TriggerRender()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    /// <summary>
    /// JavaScript renderMindMap 호출로 마인드맵 재렌더
    /// </summary>
    private async Task RenderAsync()
    {
        if (!_isWebViewReady) return;
        try
        {
            var markdown = BuildMarkdown();
            var json = System.Text.Json.JsonSerializer.Serialize(markdown);
            var script = $"window.renderMindMap({json});";
            await MindMapWebView.CoreWebView2.ExecuteScriptAsync(script);
            _log.Info($"[AC-MM-실행] 마인드맵 재렌더 완료 (markdown {markdown.Length}자)");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MM-실행] 마인드맵 렌더 실패");
        }
    }

    /// <summary>
    /// TopicSegments 데이터로 Markmap 마크다운 트리 빌드
    /// (이슈 #2 MinuteSummaries 노드 제거, #3 묵음 필터, #5 Keywords/Context L2 활용)
    /// </summary>
    private string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {_rootTitle}");
        sb.AppendLine();

        var hasTopics = _topicSegments != null && _topicSegments.Count > 0;

        if (hasTopics)
        {
            foreach (var ts in _topicSegments!)
            {
                // 이슈 #3 — IsSilence 플래그 필터
                if (ts.IsSilence) continue;

                var title = !string.IsNullOrWhiteSpace(ts.Title) ? ts.Title : ts.BodyDisplay;
                if (string.IsNullOrWhiteSpace(title)) continue;

                // 이슈 #3 — 1글자 이하 및 묵음 문자열 패턴 필터
                var trimmed = title.Trim();
                if (trimmed.Length <= 1) continue;
                if (_silenceWords.Any(w => trimmed.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

                sb.AppendLine($"- {EscapeMd(title)}");

                // 이슈 #5 — Keywords L2 노드 (최대 3개, 2글자 이상, stopwords 제외)
                if (ts.Keywords is { Count: > 0 })
                {
                    var kws = ts.Keywords
                        .Where(k => !string.IsNullOrWhiteSpace(k) && k.Length >= 2
                                    && !_silenceWords.Contains(k.Trim())
                                    && !_stopWords.Contains(k.Trim()))
                        .Take(3)
                        .ToList();
                    foreach (var kw in kws)
                        sb.AppendLine($"  - {EscapeMd(kw)}");
                }
                // 이슈 #5 — Keywords 없으면 Context fallback (80자 트림)
                else if (!string.IsNullOrWhiteSpace(ts.Context))
                {
                    var ctx = ts.Context.Trim();
                    if (ctx.Length > 80) ctx = ctx[..80] + "…";
                    sb.AppendLine($"  - {EscapeMd(ctx)}");
                }
            }
        }

        if (sb.Length < 30)
            return "# 녹음 데이터 대기 중\n\n- 녹음 시작 또는 파일 선택";

        return sb.ToString();
    }

    /// <summary>
    /// 이슈 #6 — ThemeService 변경 이벤트 수신 → JS setTheme 호출
    /// </summary>
    private async Task ApplyThemeAsync(ApplicationTheme theme)
    {
        if (!_isWebViewReady) return;
        try
        {
            var mode = theme == ApplicationTheme.Dark ? "dark" : "light";
            var script = $"if (window.setTheme) window.setTheme('{mode}');";
            await MindMapWebView.CoreWebView2.ExecuteScriptAsync(script);
            _log.Info($"[AC-MMP-실행] 테마 적용 — {mode}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MMP-실행] ApplyThemeAsync 실패");
        }
    }

    /// <summary>
    /// 이슈 #4 — HTML 내부 X 버튼 PostMessage 수신 → CloseRequested 트리거
    /// </summary>
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = e.TryGetWebMessageAsString();
            if (msg == "close")
            {
                _log.Info("[AC-MMP-실행] HTML X 버튼 클릭 (WebMessageReceived)");
                Dispatcher.Invoke(() => CloseRequested?.Invoke());
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MMP-실행] WebMessageReceived 처리 실패");
        }
    }

    private static string EscapeMd(string s)
        => s.Replace("\n", " ").Replace("\r", "").Trim();
}
