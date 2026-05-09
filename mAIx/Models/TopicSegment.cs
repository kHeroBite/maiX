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
    /// 8색 파스텔 팔레트 (인접 세그먼트 충돌 회피용 — TopicExtractorService가 사용)
    /// </summary>
    public static readonly string[] PastelPalette = new[]
    {
        "#E3F2FD", "#FFF3E0", "#E8F5E9", "#FCE4EC",
        "#F3E5F5", "#FFF9C4", "#E0F7FA", "#EFEBE9"
    };

    private int _id;
    private TimeSpan _startTime;
    private TimeSpan _endTime;
    private List<string> _keywords = new();
    private string _displayTitle = string.Empty;
    private string _summaryPreview = string.Empty;
    private string _backgroundColorHex = "#E3F2FD";

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
        set { _keywords = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeywordsDisplay)); OnPropertyChanged(nameof(ToolTipText)); }
    }

    /// <summary>
    /// 카드 표시 제목 (키워드 조합 또는 자동 생성)
    /// </summary>
    public string DisplayTitle
    {
        get => _displayTitle;
        set { _displayTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); }
    }

    /// <summary>
    /// 1분 요약 미리보기 (ToolTip에 표시)
    /// </summary>
    public string SummaryPreview
    {
        get => _summaryPreview;
        set { _summaryPreview = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToolTipText)); }
    }

    /// <summary>
    /// 카드 배경색 (PastelPalette에서 할당)
    /// </summary>
    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set { _backgroundColorHex = value; OnPropertyChanged(); }
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
    /// 마우스오버 ToolTip 전체 텍스트
    /// </summary>
    public string ToolTipText =>
        $"[{DisplayTitle}]\n{TimeRangeDisplay}\n주제어: {KeywordsDisplay}\n{SummaryPreview}";

    // ─── INotifyPropertyChanged ─────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
