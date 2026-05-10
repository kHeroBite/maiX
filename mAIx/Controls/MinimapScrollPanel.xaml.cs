// VS2022 미니맵 스타일 컨트롤 — STT ScrollViewer 컨텐츠를 RenderTargetBitmap으로 축소 캡처 + Y 좌표 양방향 스크롤
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace mAIx.Controls;

public partial class MinimapScrollPanel : UserControl
{
    private ScrollViewer? _sourceScroll;
    private ScrollViewer? _syncScroll;
    private DispatcherTimer? _refreshTimer;
    private bool _isDragging;
    private bool _suppressSync;

    public MinimapScrollPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 미니맵에 연결할 source ScrollViewer (STT 영역) 및 동기화 대상(주제어 패널) 지정
    /// </summary>
    public void Attach(ScrollViewer source, ScrollViewer? sync = null)
    {
        DetachInternal();
        _sourceScroll = source;
        _syncScroll = sync;
        if (_sourceScroll != null)
        {
            _sourceScroll.ScrollChanged += SourceScroll_ScrollChanged;
        }
        UpdateMinimap();
        UpdateViewportIndicator();
    }

    private void DetachInternal()
    {
        if (_sourceScroll != null)
        {
            _sourceScroll.ScrollChanged -= SourceScroll_ScrollChanged;
            _sourceScroll = null;
        }
        _syncScroll = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 1초마다 미니맵 재렌더링 (STT 항목 추가 반영)
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => UpdateMinimap();
        _refreshTimer.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        DetachInternal();
    }

    /// <summary>
    /// source ScrollViewer 컨텐츠를 RenderTargetBitmap으로 축소 렌더링하여 미니맵 이미지로 표시
    /// </summary>
    private void UpdateMinimap()
    {
        if (_sourceScroll == null) return;
        if (_sourceScroll.ExtentWidth <= 0 || _sourceScroll.ExtentHeight <= 0) return;
        try
        {
            // ScrollViewer 내부 컨텐츠 전체를 캡처 — Content는 일반적으로 ItemsControl
            var content = _sourceScroll.Content as UIElement;
            if (content == null) return;

            // Layout 강제 갱신
            content.Measure(new Size(_sourceScroll.ExtentWidth, double.PositiveInfinity));
            content.Arrange(new Rect(0, 0, _sourceScroll.ExtentWidth, _sourceScroll.ExtentHeight));

            var w = (int)Math.Max(1, _sourceScroll.ExtentWidth);
            var h = (int)Math.Max(1, _sourceScroll.ExtentHeight);
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(content);
            rtb.Freeze();
            MinimapImage.Source = rtb;
        }
        catch
        {
            // 렌더링 실패 시 조용히 스킵 (다음 tick 재시도)
        }
    }

    /// <summary>
    /// source ScrollViewer 위치 변경 → 미니맵 viewport 사각형 갱신 + 주제어 패널 동기화
    /// </summary>
    private void SourceScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_suppressSync) return;
        UpdateViewportIndicator();
        // 주제어 패널 동기화 (Y 좌표 비율)
        if (_syncScroll != null && _sourceScroll != null && _sourceScroll.ScrollableHeight > 0 && _syncScroll.ScrollableHeight > 0)
        {
            var ratio = _sourceScroll.VerticalOffset / _sourceScroll.ScrollableHeight;
            _suppressSync = true;
            _syncScroll.ScrollToVerticalOffset(ratio * _syncScroll.ScrollableHeight);
            _suppressSync = false;
        }
    }

    private void UpdateViewportIndicator()
    {
        if (_sourceScroll == null || ActualHeight <= 0) return;
        if (_sourceScroll.ExtentHeight <= 0) { ViewportIndicator.Visibility = Visibility.Collapsed; return; }
        ViewportIndicator.Visibility = Visibility.Visible;
        var minimapHeight = ActualHeight;
        var ratio = _sourceScroll.VerticalOffset / _sourceScroll.ExtentHeight;
        var sizeRatio = _sourceScroll.ViewportHeight / _sourceScroll.ExtentHeight;
        var top = ratio * minimapHeight;
        var height = Math.Max(20, sizeRatio * minimapHeight);
        ViewportIndicator.Margin = new Thickness(0, top, 0, 0);
        ViewportIndicator.Height = height;
    }

    /// <summary>
    /// 미니맵 클릭 → source ScrollViewer + sync ScrollViewer 같은 Y 비율로 스크롤
    /// </summary>
    private void ClickLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        ClickLayer.CaptureMouse();
        ScrollToClick(e.GetPosition(ClickLayer).Y);
    }

    private void ClickLayer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            ScrollToClick(e.GetPosition(ClickLayer).Y);
        }
    }

    private void ClickLayer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ClickLayer.ReleaseMouseCapture();
    }

    private void ScrollToClick(double y)
    {
        if (_sourceScroll == null || ActualHeight <= 0) return;
        var ratio = Math.Max(0, Math.Min(1, y / ActualHeight));
        if (_sourceScroll.ScrollableHeight > 0)
        {
            _sourceScroll.ScrollToVerticalOffset(ratio * _sourceScroll.ScrollableHeight);
        }
    }
}
