// 핵심요약 네비게이션 좌측 타임라인의 시간 눈금 데이터 모델
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mAIx.Models;

/// <summary>
/// 핵심요약 네비게이션 패널 좌측 타임라인 눈금 — 분 단위 시간 표시 + 절대 Y 좌표
/// </summary>
public class TimelineTick : INotifyPropertyChanged
{
    private TimeSpan _time;
    public TimeSpan Time
    {
        get => _time;
        set { _time = value; OnPropertyChanged(); }
    }

    private double _topPx;
    public double TopPx
    {
        get => _topPx;
        set { _topPx = value; OnPropertyChanged(); }
    }

    // 가로 모드 타임라인 — 좌→우 수평 배치용 절대 X 좌표 (Canvas.Left 바인딩)
    private double _leftPx;
    public double LeftPx
    {
        get => _leftPx;
        set { _leftPx = value; OnPropertyChanged(); }
    }

    private string _label = "";
    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
