using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MaiX.Models;

/// <summary>
/// 녹음 파일 출처 구분
/// </summary>
public enum RecordingSource
{
    /// <summary>
    /// MaiX에서 직접 녹음한 파일
    /// </summary>
    MaiX,

    /// <summary>
    /// OneNote 페이지에 첨부된 녹음
    /// </summary>
    OneNote,

    /// <summary>
    /// 외부에서 가져온 파일
    /// </summary>
    External
}

/// <summary>
/// 녹음 파일 정보 모델
/// </summary>
public class RecordingInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private DateTime _createdTime;
    private TimeSpan _duration;
    private RecordingSource _source = RecordingSource.External;
    private bool _isPlaying;
    private TimeSpan _currentPosition;
    private bool _isSTTInProgress;
    private bool _isSummaryInProgress;

    /// <summary>
    /// 파일 경로
    /// </summary>
    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 파일 이름
    /// </summary>
    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedTime
    {
        get => _createdTime;
        set { _createdTime = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 녹음 길이
    /// </summary>
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            _duration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DurationDisplay));
            OnPropertyChanged(nameof(Progress));
        }
    }

    /// <summary>
    /// 녹음 길이 표시 문자열
    /// </summary>
    public string DurationDisplay => Duration.ToString(@"mm\:ss");

    /// <summary>
    /// 파일 출처 (MaiX 녹음 vs 외부 파일)
    /// </summary>
    public RecordingSource Source
    {
        get => _source;
        set { _source = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceDisplay)); OnPropertyChanged(nameof(SourceIcon)); }
    }

    /// <summary>
    /// 현재 재생 중 여부 (UI 바인딩용)
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// 현재 재생 위치 (UI 바인딩용)
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        set
        {
            _currentPosition = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentPositionDisplay));
            OnPropertyChanged(nameof(Progress));
        }
    }

    /// <summary>
    /// 출처 표시 텍스트
    /// </summary>
    public string SourceDisplay => Source switch
    {
        RecordingSource.MaiX => "MaiX",
        RecordingSource.OneNote => "OneNote",
        _ => "외부"
    };

    /// <summary>
    /// 출처별 아이콘 심볼
    /// </summary>
    public string SourceIcon => Source switch
    {
        RecordingSource.MaiX => "Mic24",
        RecordingSource.OneNote => "Note24",
        _ => "Document24"
    };

    /// <summary>
    /// OneNote 리소스 ID (OneNote 녹음인 경우)
    /// </summary>
    public string? OneNoteResourceId { get; set; }

    /// <summary>
    /// OneNote 리소스 URL (다운로드용)
    /// </summary>
    public string? OneNoteResourceUrl { get; set; }

    /// <summary>
    /// 연결된 OneNote 페이지 ID (파일명에서 추출)
    /// 형식: recording_{pageId}_{timestamp}.wav
    /// </summary>
    public string? LinkedPageId { get; set; }

    /// <summary>
    /// 페이지 연결 여부
    /// </summary>
    public bool IsLinkedToPage => !string.IsNullOrEmpty(LinkedPageId);

    /// <summary>
    /// STT 결과 JSON 파일 경로
    /// </summary>
    public string? STTResultPath { get; set; }

    /// <summary>
    /// AI 요약 결과 JSON 파일 경로
    /// </summary>
    public string? SummaryResultPath { get; set; }

    /// <summary>
    /// STT 분석 진행 중 여부
    /// </summary>
    public bool IsSTTInProgress
    {
        get => _isSTTInProgress;
        set
        {
            _isSTTInProgress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(STTButtonContent));
            OnPropertyChanged(nameof(STTButtonAppearance));
        }
    }

    /// <summary>
    /// AI 요약 진행 중 여부
    /// </summary>
    public bool IsSummaryInProgress
    {
        get => _isSummaryInProgress;
        set
        {
            _isSummaryInProgress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SummaryButtonContent));
            OnPropertyChanged(nameof(SummaryButtonAppearance));
        }
    }

    /// <summary>
    /// STT 버튼 텍스트
    /// </summary>
    public string STTButtonContent => IsSTTInProgress ? "분석 중..." : "STT 분석";

    /// <summary>
    /// 요약 버튼 텍스트
    /// </summary>
    public string SummaryButtonContent => IsSummaryInProgress ? "요약 중..." : "요약";

    /// <summary>
    /// STT 버튼 외관 (Primary: 진행 중, Secondary: 기본)
    /// </summary>
    public string STTButtonAppearance => IsSTTInProgress ? "Primary" : "Secondary";

    /// <summary>
    /// 요약 버튼 외관
    /// </summary>
    public string SummaryButtonAppearance => IsSummaryInProgress ? "Primary" : "Secondary";

    /// <summary>
    /// STT 완료 여부
    /// </summary>
    public bool HasSTT => !string.IsNullOrEmpty(STTResultPath) && System.IO.File.Exists(STTResultPath);

    /// <summary>
    /// 요약 완료 여부
    /// </summary>
    public bool HasSummary => !string.IsNullOrEmpty(SummaryResultPath) && System.IO.File.Exists(SummaryResultPath);

    /// <summary>
    /// STT 상태 표시 아이콘
    /// </summary>
    public string STTStatusIcon => HasSTT ? "CheckmarkCircle24" : "Circle24";

    /// <summary>
    /// 요약 상태 표시 아이콘
    /// </summary>
    public string SummaryStatusIcon => HasSummary ? "CheckmarkCircle24" : "Circle24";

    /// <summary>
    /// 현재 재생 위치 표시
    /// </summary>
    public string CurrentPositionDisplay => CurrentPosition.ToString(@"mm\:ss");

    /// <summary>
    /// 진행률 (0.0 ~ 1.0)
    /// </summary>
    public double Progress => Duration.TotalSeconds > 0 ? CurrentPosition.TotalSeconds / Duration.TotalSeconds : 0;

    /// <summary>
    /// 녹음 생성 시간 표시 (사용자 친화적)
    /// 예: "01/30 06:49" 또는 "어제 14:30"
    /// </summary>
    public string CreatedTimeDisplay
    {
        get
        {
            var now = DateTime.Now;
            var created = CreatedTime;

            if (created.Date == now.Date)
            {
                // 오늘
                return $"오늘 {created:HH:mm}";
            }
            else if (created.Date == now.Date.AddDays(-1))
            {
                // 어제
                return $"어제 {created:HH:mm}";
            }
            else if (created.Year == now.Year)
            {
                // 올해
                return created.ToString("MM/dd HH:mm");
            }
            else
            {
                // 다른 해
                return created.ToString("yy/MM/dd HH:mm");
            }
        }
    }

    /// <summary>
    /// 짧은 표시명 (날짜/시간 기반)
    /// </summary>
    public string DisplayName
    {
        get
        {
            var prefix = Source switch
            {
                RecordingSource.MaiX => "🎙️",
                RecordingSource.OneNote => "📓",
                _ => "📁"
            };
            return $"{prefix} {CreatedTimeDisplay}";
        }
    }
}

/// <summary>
/// 채팅 메시지 아이템 모델 (AI 에이전트용)
/// </summary>
public class ChatMessageItem
{
    /// <summary>
    /// 메시지 내용
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 사용자 메시지 여부
    /// </summary>
    public bool IsUser { get; set; }

    /// <summary>
    /// 정렬 (사용자: 오른쪽, AI: 왼쪽)
    /// </summary>
    public System.Windows.HorizontalAlignment Alignment { get; set; }

    /// <summary>
    /// 배경색
    /// </summary>
    public System.Windows.Media.Brush? Background { get; set; }

    /// <summary>
    /// 전경색
    /// </summary>
    public System.Windows.Media.Brush? Foreground { get; set; }
}
