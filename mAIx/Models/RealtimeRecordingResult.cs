// 녹음 실시간 분석 결과 (핵심요약·실시간요약·누적요약) 페어링 저장 모델
using System;
using System.Collections.Generic;

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
    /// 1분 단위 실시간 요약 목록 (진행 중 미완성 구간만 — 롤업된 구간은 CumulativeSummaries로 이동)
    /// </summary>
    public List<MinuteSummaryEntry> MinuteSummaries { get; set; } = new();

    /// <summary>
    /// 누적요약(5분) 롤업 카드 목록 — 완료된 구간별 5분요약. 각 항목은 MinuteSummaryEntry로 표현
    /// (StartTime/EndTime = 구간 경계, SummaryText = 5분 누적요약 텍스트).
    /// </summary>
    public List<MinuteSummaryEntry> CumulativeSummaries { get; set; } = new();

    /// <summary>
    /// 누적 요약 텍스트
    /// </summary>
    public string CumulativeSummaryText { get; set; } = string.Empty;

    /// <summary>
    /// 최종 전체 요약 텍스트
    /// </summary>
    public string FinalSummaryText { get; set; } = string.Empty;
}
