// STT 모델별 동작 전략 추상화 인터페이스 — Realtime API 페이로드 + 이벤트 타입 분기 추상화
using System;

namespace mAIx.Services.AI.Strategies;

/// <summary>
/// STT 모델별 동작 전략 인터페이스.
/// OpenAI Realtime API 모델별로 다른 엔드포인트 URL, session.update 페이로드 구조,
/// 수동 commit 필요 여부, 전사 이벤트 타입, out-of-band response.create 사용 여부를 추상화한다.
/// 구현체는 모델 ID당 1개 또는 동일 동작을 공유하는 모델 그룹당 1개로 작성한다.
/// </summary>
public interface ISttModelStrategy
{
    /// <summary>
    /// 이 전략이 담당하는 OpenAI STT 모델 ID (예: "gpt-realtime-whisper", "gpt-4o-transcribe").
    /// </summary>
    string ModelId { get; }

    /// <summary>
    /// Realtime API WebSocket 연결 URI를 생성한다.
    /// transcription 세션 모델은 intent=transcription, 일반 Realtime 모델은 model={id} 형태로 분기한다.
    /// </summary>
    Uri BuildConnectionUri();

    /// <summary>
    /// session.update 메시지의 페이로드 객체를 생성한다.
    /// 모델별로 prompt/delay/turn_detection/response_format 등 지원 필드가 다르므로 각 구현체에서 분기한다.
    /// </summary>
    /// <param name="language">전사 언어 코드 (예: "ko").</param>
    /// <param name="prompt">전사 힌트 프롬프트 (gpt-realtime-whisper는 미지원이므로 구현체에서 제외).</param>
    /// <param name="serverVadEnabled">서버측 VAD(turn_detection) 활성 여부.</param>
    /// <param name="vadThreshold">VAD 임계값 (server_vad 활성 시 사용).</param>
    /// <param name="vadSilenceDurationMs">VAD 무음 지속시간 ms (server_vad 활성 시 사용).</param>
    /// <param name="whisperDelay">gpt-realtime-whisper 전용 delay 파라미터 (예: "low", "balanced", "accurate"). 다른 모델은 무시.</param>
    object BuildSessionUpdatePayload(string language, string prompt, bool serverVadEnabled,
        double vadThreshold, int vadSilenceDurationMs, string? whisperDelay);

    /// <summary>
    /// 주기적 수동 input_audio_buffer.commit 송신이 필요한지 판정한다.
    /// VAD OFF(turn_detection=null) 또는 whisper-1 계열 등 서버 자동 commit이 없는 경우 true.
    /// </summary>
    /// <param name="serverVadEnabled">서버측 VAD 활성 여부.</param>
    bool RequiresManualCommit(bool serverVadEnabled);

    /// <summary>
    /// 전사 완료 시 수신되는 WebSocket 이벤트 type 문자열.
    /// transcription 세션 계열은 "conversation.item.input_audio_transcription.completed",
    /// 일반 Realtime 세션은 "response.output_item.done" 등으로 다르다.
    /// </summary>
    string TranscriptionCompletedEventType { get; }

    /// <summary>
    /// 전사 부분 결과(delta) 수신 이벤트 type 문자열.
    /// transcription 세션 계열은 "conversation.item.input_audio_transcription.delta",
    /// 일반 Realtime 세션은 "response.text.delta" 등으로 다르다.
    /// </summary>
    string TranscriptionDeltaEventType { get; }

    /// <summary>
    /// 음성 커밋 후 추가로 송신할 out-of-band response.create 페이로드를 생성한다.
    /// gpt-realtime-2 등 일반 Realtime 세션에서 conversation=none 으로 전사를 추출할 때 사용한다.
    /// transcription 세션 모델은 null을 반환하여 미사용을 표시한다 (L-441 패턴).
    /// </summary>
    /// <param name="language">전사 언어 코드.</param>
    object? BuildOutOfBandResponsePayload(string language);
}
