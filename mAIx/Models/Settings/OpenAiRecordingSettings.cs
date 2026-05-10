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

    /// <summary>
    /// TTS 모델 (tts-1, tts-1-hd)
    /// </summary>
    [XmlElement("TtsModel")]
    public string TtsModel { get; set; } = "tts-1";

    /// <summary>
    /// TTS 음성 (alloy, echo, fable, onyx, nova, shimmer)
    /// </summary>
    [XmlElement("TtsVoice")]
    public string TtsVoice { get; set; } = "alloy";

    /// <summary>주제어 추출 최소 단위 (초, 기본 12)</summary>
    [XmlElement("TopicExtractorIntervalSec")]
    public int TopicExtractorIntervalSec { get; set; } = 12;

    /// <summary>
    /// 디버그 타이머 배율 (기본 1.0 = 정상 속도, 0.1 = 10배 빠름).
    /// 환경변수 MAIX_DEBUG_TIMER_SCALE이 설정된 경우 해당 값이 우선 적용됨.
    /// production에서는 1.0 유지.
    /// </summary>
    [XmlElement("DebugTimerScale")]
    public double DebugTimerScale { get; set; } = 1.0;

    /// <summary>녹음 종료 시 최종 요약 자동 실행 여부 (기본 false — 옵트인)</summary>
    [XmlElement("AutoFinalSummary")]
    public bool AutoFinalSummary { get; set; } = false;

    /// <summary>STT 언어 코드 (Whisper input_audio_transcription.language, 기본 ko)</summary>
    [XmlElement("SttLanguage")]
    public string SttLanguage { get; set; } = "ko";

    /// <summary>STT prompt — 도메인 사전/문맥 힌트 (한국어 기본 사전)</summary>
    [XmlElement("SttPrompt")]
    public string SttPrompt { get; set; } = "한국어 회의 녹음입니다. 자연스러운 한국어 문장으로 전사하세요. 일상 대화, 업무 회의, IT 용어가 포함될 수 있습니다.";

    /// <summary>transcript 오타 수정 후처리 활성화 (GPT-4o-mini, 기본 false 옵트인)</summary>
    [XmlElement("EnableTypoFix")]
    public bool EnableTypoFix { get; set; } = false;

    /// <summary>오타 수정 모델 (기본 gpt-4o-mini)</summary>
    [XmlElement("TypoFixModel")]
    public string TypoFixModel { get; set; } = "gpt-4o-mini";
}
