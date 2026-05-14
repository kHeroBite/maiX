// 주제어 대화 네비게이션 세그먼트 모델 (실시간 가변 길이)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mAIx.Models;

/// <summary>
/// 주제어 기반 대화 세그먼트 — 실시간 가변 길이, 색상 구분, 네비게이션 지원
/// </summary>
public class TopicSegment : INotifyPropertyChanged
{
    /// <summary>
    /// 8색 파스텔 팔레트 — 라이트 모드용 (밝은 파스텔 + 검정 글자)
    /// </summary>
    public static readonly string[] PastelPalette = new[]
    {
        "#E3F2FD", "#FFF3E0", "#E8F5E9", "#FCE4EC",
        "#F3E5F5", "#FFF9C4", "#E0F7FA", "#EFEBE9"
    };

    /// <summary>
    /// 8색 다크 팔레트 — 다크 모드용 (저명도 + 흰 글자 가독성 확보)
    /// </summary>
    public static readonly string[] DarkPalette = new[]
    {
        "#1E3A5F", "#5C3A1E", "#1E4D2C", "#5C2E40",
        "#3F2A4D", "#5C4D1E", "#1E4D55", "#3D332E"
    };

    /// <summary>
    /// 현재 테마에 맞는 팔레트 반환 (다크/라이트 자동 선택)
    /// </summary>
    public static string[] GetPaletteForCurrentTheme()
    {
        try
        {
            var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            return theme == Wpf.Ui.Appearance.ApplicationTheme.Dark ? DarkPalette : PastelPalette;
        }
        catch
        {
            return PastelPalette;
        }
    }

    private int _id;
    private TimeSpan _startTime;
    private TimeSpan _endTime;
    private List<string> _keywords = new();
    private string _displayTitle = string.Empty;
    private string _summaryPreview = string.Empty;
    private string _backgroundColorHex = "#E3F2FD";
    private double _displayHeight = 60;

    /// <summary>
    /// 세그먼트 고유 ID (0부터 시작)
    /// </summary>
    public int Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 세그먼트 시작 시각 (녹음 시작 기준 상대 시간)
    /// </summary>
    public TimeSpan StartTime
    {
        get => _startTime;
        set { _startTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeRangeDisplay)); OnPropertyChanged(nameof(ToolTipText)); }
    }

    /// <summary>
    /// 세그먼트 종료 시각 (현재 진행 중이면 갱신됨)
    /// </summary>
    public TimeSpan EndTime
    {
        get => _endTime;
        set { _endTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimeRangeDisplay)); OnPropertyChanged(nameof(ToolTipText)); }
    }

    /// <summary>
    /// 주제어 목록
    /// </summary>
    public List<string> Keywords
    {
        get => _keywords;
        set { _keywords = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeywordsDisplay)); OnPropertyChanged(nameof(ToolTipText)); OnPropertyChanged(nameof(BodyDisplay)); }
    }

    /// <summary>
    /// 카드 표시 제목 (키워드 조합 또는 자동 생성)
    /// </summary>
    public string DisplayTitle
    {
        get => _displayTitle;
        set { _displayTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); OnPropertyChanged(nameof(BodyDisplay)); }
    }

    /// <summary>
    /// 핵심요약 1줄 (카드 본문 + ToolTip에 표시)
    /// </summary>
    public string SummaryPreview
    {
        get => _summaryPreview;
        set { _summaryPreview = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); OnPropertyChanged(nameof(BodyDisplay)); }
    }

    /// <summary>
    /// 카드 배경색 (PastelPalette에서 할당)
    /// </summary>
    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set { _backgroundColorHex = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// StackPanel 레이아웃에서 카드 픽셀 높이 (시간 비례 계산값)
    /// </summary>
    public double DisplayHeight
    {
        get => _displayHeight;
        set
        {
            if (Math.Abs(_displayHeight - value) > 0.5)
            {
                _displayHeight = value;
                RaisePropertyChanged(nameof(DisplayHeight));
            }
        }
    }

    // ─── 표시용 파생 프로퍼티 ───────────────────────────────────────────

    /// <summary>
    /// 키워드 쉼표 조합 표시 문자열
    /// </summary>
    public string KeywordsDisplay =>
        Keywords.Count > 0 ? string.Join(", ", Keywords) : "(주제어 없음)";

    /// <summary>
    /// 시간 범위 표시 문자열 (예: 00:00 ~ 05:23)
    /// </summary>
    public string TimeRangeDisplay =>
        $"{StartTime:mm\\:ss} ~ {EndTime:mm\\:ss}";

    /// <summary>
    /// 카드 본문 표시 — 핵심요약(우선) 또는 키워드 fallback
    /// </summary>
    public string BodyDisplay =>
        !string.IsNullOrWhiteSpace(SummaryPreview) ? SummaryPreview : DisplayTitle;

    /// <summary>
    /// 마우스오버 ToolTip 전체 텍스트
    /// </summary>
    public string ToolTipText =>
        $"[{DisplayTitle}]\n{TimeRangeDisplay}\n핵심요약: {SummaryPreview}\n키워드: {KeywordsDisplay}";

    // ─── INotifyPropertyChanged ─────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// 외부에서 강제로 PropertyChanged 발화 (MultiBinding 재계산 trigger용).
    /// 새 세그먼트 추가로 총 녹음 시간 변경 시 기존 카드들의 비례 Height 재계산에 사용.
    /// </summary>
    public void RaisePropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
