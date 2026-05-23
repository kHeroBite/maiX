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
    /// 실시간 STT 모델 (WebSocket 기반) — GA transcription 모드 호환 모델.
    /// 사용 가능: "gpt-realtime-whisper" (GA 스트리밍 전용), "gpt-4o-transcribe" (오프라인 파일 전용)
    /// </summary>
    [XmlElement("RealtimeSttModel")]
    public string RealtimeSttModel { get; set; } = "gpt-realtime-whisper";

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
    public int ChunkSeconds { get; set; } = 1;

    /// <summary>
    /// 활성 프리셋 이름 (custom/lowcost/quality/streaming)
    /// </summary>
    [XmlElement("ActivePreset")]
    public string ActivePreset { get; set; } = "custom";

    /// <summary>
    /// 주제어 네비게이션 패널 방향 (Horizontal/Vertical)
    /// </summary>
    [XmlElement("TopicNavOrientation")]
    public string TopicNavOrientation { get; set; } = "Vertical";

    /// <summary>
    /// 주제어 네비게이션 패널 폭 (세로 우측 도킹 Mode A, Col2 Width, 기본 147)
    /// </summary>
    [XmlElement("TopicNavPanelWidth")]
    public double TopicNavPanelWidth { get; set; } = 147;

    /// <summary>
    /// 주제어 네비게이션 패널 높이 (가로 하단 도킹 Mode B, Row2 Height, 기본 147)
    /// </summary>
    [XmlElement("TopicNavPanelHeight")]
    public double TopicNavPanelHeight { get; set; } = 147;

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

    /// <summary>STT input_audio_transcription 모델. GA 스트리밍 transcription 전용 — gpt-realtime-whisper 필수 (gpt-4o-transcribe는 오프라인 파일 전용으로 스트리밍 파이프라인 미활성화).</summary>
    [XmlElement("TranscriptionModel")]
    public string TranscriptionModel { get; set; } = "gpt-realtime-whisper";

    /// <summary>OpenAI server_vad 사용 여부 (true: 자동 발화 감지, false: 수동 종료 시 일괄 전사). 기본 true.</summary>
    [XmlElement("ServerVadEnabled")]
    public bool ServerVadEnabled { get; set; } = true;

    /// <summary>
    /// server_vad 감도 임계값 (0.0~1.0). 값이 낮을수록 민감하게 발화를 감지.
    /// 기본 0.5. ServerVadEnabled=true 시에만 적용.
    /// </summary>
    [XmlElement("VadThreshold")]
    public double VadThreshold { get; set; } = 0.5;

    /// <summary>
    /// server_vad 발화 종료 판정 침묵 시간 (밀리초, 100~3000).
    /// 침묵이 이 시간 이상 지속되면 발화 종료로 간주. 기본 500ms.
    /// </summary>
    [XmlElement("VadSilenceDurationMs")]
    public int VadSilenceDurationMs { get; set; } = 500;

    /// <summary>자동 말풍선 분리 활성화 여부 (기본 true — AC-017 분리 로직 적용).</summary>
    [XmlElement("AutoSplitEnabled")]
    public bool AutoSplitEnabled { get; set; } = true;

    /// <summary>
    /// 자동 분리 1단계(tier1) 최소 글자수 (강마침표 전용 탐색 기준, 기본 50).
    /// 누적 텍스트가 이 값 이상이면 강마침표(. ! ? 。 ！ ？)로 분리 시도.
    /// </summary>
    [XmlElement("AutoSplitPrimaryMinChars")]
    public int AutoSplitPrimaryMinChars { get; set; } = 50;

    /// <summary>
    /// 자동 분리 2단계(tier2) 최소 글자수 (강+약 구분자 탐색 기준, 기본 150).
    /// 누적 텍스트가 이 값 이상이면 강마침표 + 약구분자(, ; 、 ；)로도 분리 시도.
    /// </summary>
    [XmlElement("AutoSplitSecondaryMinChars")]
    public int AutoSplitSecondaryMinChars { get; set; } = 150;

    /// <summary>STT 화면에서 주제어 키워드를 하이라이트로 표시할지 여부. 기본 true.</summary>
    [XmlElement("KeywordHighlightEnabled")]
    public bool KeywordHighlightEnabled { get; set; } = true;

    /// <summary>처리 주기 (초 단위). 실시간 요약 + 핵심주제 추출 + 그루핑 평가를 모두 이 주기로 실행. 기본 60초.</summary>
    [XmlElement("ProcessingIntervalSeconds")]
    public int ProcessingIntervalSeconds { get; set; } = 60;

    /// <summary>청크 오버랩 (초). 청크 경계에서 단어 끊김 방지 — 화자분리 모드에서 의미 있음. 기본 0.5초.</summary>
    [XmlElement("ChunkOverlapSeconds")]
    public double ChunkOverlapSeconds { get; set; } = 0.5;

    /// <summary>실시간 STT 자동스크롤 여부 (기본 true — 새 세그먼트 추가 시 맨 아래로 이동).</summary>
    [XmlElement("SttAutoScroll")]
    public bool SttAutoScroll { get; set; } = true;

    /// <summary>실시간 요약 자동스크롤 여부 (기본 true — 새 요약 추가 시 맨 아래로 이동).</summary>
    [XmlElement("SummaryAutoScroll")]
    public bool SummaryAutoScroll { get; set; } = true;
}
