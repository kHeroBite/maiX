// 1분 요약의 감성 분석 결과 (점수 0~100% + 이모지/색상)
using System;
using System.Text.Json.Serialization;

namespace mAIx.Models;

/// <summary>
/// 1분 요약 감성 분석 결과 — 0(매우 부정) ~ 100(매우 긍정) 점수 + UI 헬퍼
/// </summary>
public class SentimentResult
{
    /// <summary>
    /// 감성 점수 (0~100). 0=매우 부정, 50=중립, 100=매우 긍정.
    /// </summary>
    public int Score { get; set; } = 50;

    /// <summary>
    /// 감성 레이블 (긍정/중립/부정)
    /// </summary>
    public string Label { get; set; } = "중립";

    /// <summary>
    /// 감성 이모지 — Score에 따라 자동 결정 (UI 전용, 직렬화 제외)
    /// </summary>
    [JsonIgnore]
    public string Emoji => Score >= 70 ? "😊" : Score >= 30 ? "😐" : "😞";

    /// <summary>
    /// 배경 색상 HEX — Score에 따라 자동 결정 (UI 전용, 직렬화 제외)
    /// </summary>
    [JsonIgnore]
    public string ColorHex => Score >= 70 ? "#E8F5E9" : Score >= 30 ? "#F5F5F5" : "#FFEBEE";

    /// <summary>
    /// 분석 완료 일시
    /// </summary>
    public DateTime AnalyzedAt { get; set; } = DateTime.Now;
}
