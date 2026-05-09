// OpenAI 녹음 STT/LLM 설정 (음성 모델 2슬롯 + LLM 4슬롯 + 누적주기 + 레이아웃)
using System;
using System.Xml.Serialization;

namespace mAIx.Models.Settings;

/// <summary>
/// OpenAI 녹음 STT/LLM 설정
/// </summary>
[Serializable]
[XmlRoot("OpenAiRecordingSettings")]
public class OpenAiRecordingSettings
{
    /// <summary>
    /// 실시간 STT 모델 (WebSocket 기반)
    /// </summary>
    [XmlElement("RealtimeSttModel")]
    public string RealtimeSttModel { get; set; } = "gpt-4o-realtime-preview";

    /// <summary>
    /// 화자분리 STT 모델 (청크 기반)
    /// </summary>
    [XmlElement("TranscribeSttModel")]
    public string TranscribeSttModel { get; set; } = "gpt-4o-transcribe";

    /// <summary>
    /// 주제어 추출 LLM 모델
    /// </summary>
    [XmlElement("KeywordExtractModel")]
    public string KeywordExtractModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// 1분 요약 LLM 모델
    /// </summary>
    [XmlElement("MinuteSummaryModel")]
    public string MinuteSummaryModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// 누적 요약 LLM 모델
    /// </summary>
    [XmlElement("CumulativeSummaryModel")]
    public string CumulativeSummaryModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// 최종 요약 LLM 모델
    /// </summary>
    [XmlElement("FinalSummaryModel")]
    public string FinalSummaryModel { get; set; } = "gpt-4o";

    /// <summary>
    /// 누적 요약 주기 (분 단위)
    /// </summary>
    [XmlElement("CumulativeSummaryIntervalMinutes")]
    public int CumulativeSummaryIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// 오디오 청크 길이 (초 단위)
    /// </summary>
    [XmlElement("ChunkSeconds")]
    public int ChunkSeconds { get; set; } = 10;

    /// <summary>
    /// 활성 프리셋 이름 (custom/lowcost/quality/streaming)
    /// </summary>
    [XmlElement("ActivePreset")]
    public string ActivePreset { get; set; } = "custom";

    /// <summary>
    /// 주제어 네비게이션 패널 방향 (Horizontal/Vertical)
    /// </summary>
    [XmlElement("TopicNavOrientation")]
    public string TopicNavOrientation { get; set; } = "Horizontal";
}
