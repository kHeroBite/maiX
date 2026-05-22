// WebView2 + Markmap으로 STT/MAP/요약 데이터를 마인드맵으로 렌더링하는 오버레이 컨트롤
using System;
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
    /// TopicSegments + MinuteSummaries 데이터로 Markmap 마크다운 트리 빌드
    /// </summary>
    private string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {_rootTitle}");
        sb.AppendLine();

        var hasTopics = _topicSegments != null && _topicSegments.Count > 0;
        var hasSummaries = _minuteSummaries != null && _minuteSummaries.Count > 0;

        if (hasTopics)
        {
            foreach (var ts in _topicSegments!)
            {
                var title = !string.IsNullOrWhiteSpace(ts.Title) ? ts.Title : ts.BodyDisplay;
                if (string.IsNullOrWhiteSpace(title)) continue;
                sb.AppendLine($"- {EscapeMd(title)}");
            }
        }

        if (hasSummaries)
        {
            sb.AppendLine("- 요약");
            foreach (var ms in _minuteSummaries!
                .Where(m => !string.IsNullOrWhiteSpace(m.SummaryText))
                .Take(10))
            {
                sb.AppendLine($"  - {EscapeMd(ms.SummaryText)}");
            }
        }

        if (sb.Length < 30)
            return "# 녹음 데이터 대기 중\n\n- 녹음 시작 또는 파일 선택";

        return sb.ToString();
    }

    private static string EscapeMd(string s)
        => s.Replace("\n", " ").Replace("\r", "").Trim();
}
