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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using NLog;
using mAIx.Models;
using mAIx.Services.AI;
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

    // LLM 트리 서비스 + 캐시 (X2 — LLM 패스스루)
    private IMindMapTreeService? _treeService;
    private string? _llmTreeMarkdown;

    // ThemeService 구독 핸들러 (Unbind에서 해제 필수)
    private EventHandler<ApplicationTheme>? _themeHandler;
    private EventHandler<CoreWebView2WebMessageReceivedEventArgs>? _webMessageHandler;
    private bool _webMessageHooked;

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
            _log.Info($"[AC-MMX3-click] WPF CloseButton 클릭 발화 타임스탬프={DateTime.Now:HH:mm:ss.fff}");
            _log.Info("[AC-MMX-실행] X 닫기 버튼 클릭");
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MMX-실행] CloseButton_Click 실패");
        }
    }

    /// <summary>
    /// WPF 보조 X 버튼 클릭 (Airspace 대응) → CloseRequested 콜백 호출
    /// </summary>
    private void WpfCloseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _log.Info($"[AC-MMX3-click] WPF WpfCloseButton 클릭 발화 타임스탬프={DateTime.Now:HH:mm:ss.fff}");
            _log.Info("[AC-MMX-실행] WPF 보조 X 닫기 버튼 클릭");
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[AC-MMX-실행] WpfCloseButton_Click 실패");
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

            // WebMessageReceived 핸들러 등록 — InitializeAsync는 1회만 호출되므로 자연스럽게 단발 등록
            MindMapWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _webMessageHandler = OnWebMessageReceived;
            MindMapWebView.CoreWebView2.WebMessageReceived += _webMessageHandler;
            _webMessageHooked = true;
            _log.Info("[AC-MMR-실행] WebMessageReceived 핸들러 등록 완료 (단발 등록 — 토글 재진입에도 유지)");

            var resourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
            MindMapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "mindmap.local", resourcePath, CoreWebView2HostResourceAccessKind.Allow);

            MindMapWebView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                _isWebViewReady = true;
                _log.Info("[AC-MM-실행] WebView2 초기화 완료 — mindmap.html 로드");

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
        // [MMF-실행] 옛 녹음파일 캐시 무효화 — 할루시네이션 방지
        if (_treeService != null)
        {
            _treeService.Reset();
            _log.Info("[MMF-실행] Bind — TreeService Reset 호출");
        }
        _llmTreeMarkdown = null;  // 옛 응답 즉시 무효화

        Unbind();
        _topicSegments = topics;
        _minuteSummaries = summaries;
        _rootTitle = string.IsNullOrWhiteSpace(rootTitle) ? "녹음 데이터" : rootTitle;

        if (_topicSegments != null)
            _topicSegments.CollectionChanged += OnDataChanged;
        if (_minuteSummaries != null)
            _minuteSummaries.CollectionChanged += OnDataChanged;

        // LLM 트리 서비스 구독 (X2 — DI에서 싱글턴 가져오기)
        _treeService = (Application.Current as App)?.ServiceProvider?.GetService<IMindMapTreeService>();
        if (_treeService != null)
        {
            _treeService.TreeMarkdownGenerated += OnTreeMarkdownGenerated;
            // 캐시된 응답이 있으면 즉시 사용
            if (!string.IsNullOrWhiteSpace(_treeService.LastTreeMarkdown))
            {
                _llmTreeMarkdown = _treeService.LastTreeMarkdown;
            }
            // 첫 LLM 트리 요청
            RequestTreeUpdate();
        }

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

        // WebMessageReceived 구독은 단발 등록 유지 (Unbind 시 해제 안 함 — §8 재설계)
        // InitializeAsync 1회 호출로 자연스럽게 단발 등록, 토글 재진입에도 핸들러 살아있음

        // LLM 트리 서비스 구독 해제 (X2)
        if (_treeService != null)
        {
            _treeService.TreeMarkdownGenerated -= OnTreeMarkdownGenerated;
            _treeService = null;
        }
        _llmTreeMarkdown = null;

        _topicSegments = null;
        _minuteSummaries = null;
        _debounceTimer?.Stop();
    }

    /// <summary>
    /// 컬렉션 변경 시 디바운스 렌더 트리거
    /// </summary>
    private void OnDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RequestTreeUpdate();
        TriggerRender();
    }

    /// <summary>
    /// 디바운스 타이머 리셋 — 마지막 변경 후 1초 뒤 렌더
    /// </summary>
    public void TriggerRender()
    {
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    /// <summary>
    /// LLM 트리 갱신 요청 — _treeService.RequestTreeUpdate 위임 (X2)
    /// </summary>
    private void RequestTreeUpdate()
    {
        if (_treeService == null || _topicSegments == null) return;
        var minuteSummaries = _minuteSummaries?
            .Select(m => m.SummaryText ?? string.Empty)
            .ToList() ?? new List<string>();
        _treeService.RequestTreeUpdate(_topicSegments.ToList(), minuteSummaries);
    }

    /// <summary>
    /// LLM 트리 생성 완료 이벤트 핸들러 — UI 스레드로 마샬링 후 재렌더 (X2)
    /// </summary>
    private async void OnTreeMarkdownGenerated(object? sender, string markdown)
    {
        try
        {
            _llmTreeMarkdown = markdown;
            _log.Info($"[MMT-실행] LLM 트리 수신 — 줄수={markdown.Split('\n').Length}");
            await Dispatcher.InvokeAsync(() => RenderAsync()).Task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[MMT-실행] OnTreeMarkdownGenerated 처리 실패");
        }
    }

    /// <summary>
    /// 루트 타이틀 즉시 갱신 + 재렌더 트리거 (odev-3 호출용 — 녹음전환 동기화) (X2)
    /// </summary>
    public void UpdateRootTitle(string newTitle)
    {
        var oldTitle = _rootTitle;
        _rootTitle = string.IsNullOrWhiteSpace(newTitle) ? "녹음 데이터" : newTitle;
        _log.Info($"[MMT-실행] UpdateRootTitle: {oldTitle} → {_rootTitle}");
        if (_isWebViewReady)
        {
            _ = Dispatcher.InvokeAsync(() => RenderAsync()).Task;
        }
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
    /// (이슈 5 회귀+개선 — Jaccard 0.5 그룹핑으로 중복 토픽 병합, 키워드 누적)
    /// </summary>
    private string BuildMarkdown()
    {
        // LLM 트리 우선 (무한 깊이, 무제한 개수) — X2
        if (!string.IsNullOrWhiteSpace(_llmTreeMarkdown))
        {
            _log.Info("[MMF-실행] BuildMarkdown — LLM 트리 사용");
            return _llmTreeMarkdown!;
        }

        // 폴백: Jaccard 그룹핑
        _log.Info("[MMT-실행] BuildMarkdown — 폴백 (Jaccard 그룹핑)");

        var sb = new StringBuilder();
        sb.AppendLine($"# {_rootTitle}");
        sb.AppendLine();

        var hasTopics = _topicSegments != null && _topicSegments.Count > 0;
        if (!hasTopics)
        {
            // 빈 상태 — 루트만 반환 (안내 텍스트 제거)
            _log.Info("[MMF-실행] BuildMarkdown — 빈 상태 (루트만)");
            return $"# {_rootTitle}";
        }

        // 1단계 — 필터 통과 segment를 그룹으로 병합 (Title Jaccard 0.5)
        var groups = new List<(string Representative, List<string> Keywords)>();
        int totalSegments = 0, acceptedSegments = 0;

        foreach (var ts in _topicSegments!)
        {
            totalSegments++;
            if (ts.IsSilence) continue;
            var title = !string.IsNullOrWhiteSpace(ts.Title) ? ts.Title : ts.BodyDisplay;
            if (string.IsNullOrWhiteSpace(title)) continue;
            var trimmed = title.Trim();
            if (trimmed.Length <= 1) continue;
            if (_silenceWords.Any(w => trimmed.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

            acceptedSegments++;

            var titleTokens = Tokenize(trimmed);
            var keywords = FilterValidKeywords(ts.Keywords);

            // 기존 그룹과 Jaccard 비교 — 첫 매칭(>= 0.5)에 병합
            bool merged = false;
            for (int i = 0; i < groups.Count; i++)
            {
                var gTokens = Tokenize(groups[i].Representative);
                if (Jaccard(titleTokens, gTokens) >= 0.5)
                {
                    MergeKeywordsUnique(groups[i].Keywords, keywords);
                    merged = true;
                    break;
                }
            }
            if (!merged)
            {
                groups.Add((trimmed, new List<string>(keywords)));
            }
        }

        _log.Debug($"[AC-MMR-실행] BuildMarkdown — segments_total={totalSegments} accepted={acceptedSegments} groups={groups.Count}");

        foreach (var g in groups)
        {
            sb.AppendLine($"- {EscapeMd(g.Representative)}");
            foreach (var kw in g.Keywords)
            {
                sb.AppendLine($"  - {EscapeMd(kw)}");
            }
        }

        // [MMF-실행] 30자 미만 가드 제거 — 루트 헤더가 항상 있어 안전
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
            _log.Info($"[AC-MMR-실행] WebMessageReceived 진입 — msg='{msg}'");
            if (msg == "close")
            {
                _log.Info("[AC-MMR-실행] HTML X 버튼/ESC close 수신 → CloseRequested 호출");
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

    // === Helper 메서드 (이슈 5 — Jaccard + Keywords 누적) ===

    private static HashSet<string> Tokenize(string s)
    {
        return new HashSet<string>(
            s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
             .Select(t => t.Trim().ToLowerInvariant())
             .Where(t => t.Length > 0),
            StringComparer.OrdinalIgnoreCase);
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 0.0;
        var inter = a.Intersect(b, StringComparer.OrdinalIgnoreCase).Count();
        var union = a.Union(b, StringComparer.OrdinalIgnoreCase).Count();
        return union == 0 ? 0.0 : (double)inter / union;
    }

    private List<string> FilterValidKeywords(IEnumerable<string>? src)
    {
        if (src is null) return new List<string>();
        return src
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Where(k => k.Length >= 2
                        && !_silenceWords.Contains(k)
                        && !_stopWords.Contains(k))
            .ToList();
    }

    private static void MergeKeywordsUnique(List<string> dst, IEnumerable<string> src)
    {
        var existing = new HashSet<string>(dst, StringComparer.OrdinalIgnoreCase);
        foreach (var k in src)
        {
            var trimmed = k.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (existing.Add(trimmed))
            {
                dst.Add(trimmed);
            }
        }
    }
}
