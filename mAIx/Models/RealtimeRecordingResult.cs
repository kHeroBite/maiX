// 녹음 실시간 분석 결과 (핵심요약·실시간요약·누적요약) 페어링 저장 모델
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using mAIx.Services.AI;

namespace mAIx.Models;

/// <summary>
/// 녹음 파일과 페어링되는 실시간 분석 결과 모델.
/// TopicSegments(핵심요약 네비게이션), MinuteSummaries(1분 요약), CumulativeSummaryText(누적 요약),
/// FinalSummaryText(전체 요약)를 .realtime.json 파일로 저장/로드.
/// </summary>
public class RealtimeRecordingResult
{
    /// <summary>
    /// 원본 녹음 파일 경로
    /// </summary>
    public string AudioFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 생성 일시
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 핵심요약 네비게이션 세그먼트 목록
    /// </summary>
    public List<TopicSegment> TopicSegments { get; set; } = new();

    /// <summary>
    /// 1분 단위 실시간 요약 목록
    /// </summary>
    public List<MinuteSummaryEntry> MinuteSummaries { get; set; } = new();

    /// <summary>
    /// 누적 요약 텍스트
    /// </summary>
    public string CumulativeSummaryText { get; set; } = string.Empty;

    /// <summary>
    /// 최종 전체 요약 텍스트
    /// </summary>
    public string FinalSummaryText { get; set; } = string.Empty;

    /// <summary>
    /// 녹음 시 사용된 파이프라인 모드 — null이면 구버전 파일 (하위 호환).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AudioPipelineMode? RecordedWithMode { get; set; }
}
