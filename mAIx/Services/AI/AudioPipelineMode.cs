// 오디오 파이프라인 모드 - Legacy(별도 모델) 또는 Unified(gpt-realtime 단일)
namespace mAIx.Services.AI;

/// <summary>
/// 오디오 파이프라인 처리 모드.
/// Legacy: 기존 STT + 별도 MinuteSummary + 별도 Sentiment 서비스.
/// Unified: gpt-realtime 계열 단일 WebSocket으로 STT + 1분 요약 + 감성 동시 처리.
/// </summary>
public enum AudioPipelineMode
{
    /// <summary>기존 모드 — 3개 별도 모델 (STT + 요약 + 감성)</summary>
    Legacy = 0,

    /// <summary>통합 모드 — gpt-realtime 단일 WebSocket으로 동시 처리</summary>
    Unified = 1,
}
