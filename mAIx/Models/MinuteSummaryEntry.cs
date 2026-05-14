// 1분 요약 엔트리 (내부 압축용 — 누적요약 입력 자료)
using System;
using System.Text.Json.Serialization;
using mAIx.Services.AI;

namespace mAIx.Models;

/// <summary>
/// 1분 단위 요약 엔트리 — 누적 요약 서비스의 입력 자료
/// </summary>
public class MinuteSummaryEntry
{
    /// <summary>
    /// 순번 (0부터 시작, 시간순)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 해당 분 시작 시각 (녹음 시작 기준 상대 시간)
    /// </summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>
    /// 해당 분 종료 시각
    /// </summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// LLM 생성 1분 요약 텍스트
    /// </summary>
    public string SummaryText { get; set; } = string.Empty;

    /// <summary>
    /// 핵심요약 네비게이션 표시용 주제어 (5~20자, LLM JSON 응답에서 추출)
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// 엔트리 생성 일시
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// UI 표시용 시간 범위 (mm:ss ~ mm:ss)
    /// </summary>
    public string TimeRangeDisplay =>
        $"{(int)StartTime.TotalMinutes:D2}:{StartTime.Seconds:D2} ~ {(int)EndTime.TotalMinutes:D2}:{EndTime.Seconds:D2}";

    /// <summary>
    /// 감성 분석 결과 — null이면 미분석 (UI 회색 표시). 직렬화 시 null이면 생략.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SentimentResult? Sentiment { get; set; }

    /// <summary>
    /// 생성한 파이프라인 모드 — 디버깅 및 향후 비교용. null이면 구버전 파일.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioPipelineMode? CreatedByMode { get; set; }
}
