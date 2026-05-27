// STT 모델 ID → ISttModelStrategy 인스턴스 매핑 팩토리 — L-440 (인터페이스+팩토리 패턴)
namespace mAIx.Services.AI.Strategies;

/// <summary>
/// 모델 ID 문자열을 받아 해당 ISttModelStrategy 구현 인스턴스를 반환하는 정적 팩토리.
/// 미등록 모델 ID는 회귀 위험 최소화를 위해 gpt-4o-transcribe로 폴백한다(기존 동작 유지).
/// </summary>
public static class SttStrategyFactory
{
    /// <summary>모델 ID에 해당하는 Strategy 인스턴스 생성. 미등록 모델은 gpt-4o-transcribe로 폴백.</summary>
    public static ISttModelStrategy Create(string modelId)
    {
        return modelId switch
        {
            "gpt-realtime-whisper" => new RealtimeWhisperStrategy(),
            "gpt-4o-transcribe" => new RealtimeTranscribeStrategy("gpt-4o-transcribe"),
            "gpt-4o-mini-transcribe" => new RealtimeTranscribeStrategy("gpt-4o-mini-transcribe"),
            "whisper-1" => new Whisper1Strategy(),
            "gpt-realtime-2" => new RealtimeGptReasoningStrategy(),
            _ => new RealtimeTranscribeStrategy("gpt-4o-transcribe")  // 폴백 — 기존 동작
        };
    }
}
