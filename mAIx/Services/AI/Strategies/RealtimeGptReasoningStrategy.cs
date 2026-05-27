// gpt-realtime-2 (GPT-5급 추론) STT Strategy — L-441 out-of-band response.create + 일반 Realtime 세션 + 다른 이벤트 타입
using System;

namespace mAIx.Services.AI.Strategies;

/// <summary>
/// gpt-realtime-2 (GPT-5급 추론) 전용 STT 전략.
/// transcription 세션이 아닌 일반 Realtime 세션을 사용하므로 다음과 같이 분기한다:
///  - 연결 URI: <c>model=gpt-realtime-2</c> (intent=transcription 아님).
///  - session.update: <c>input_audio_transcription</c> 미사용. instructions + input_audio_format + turn_detection.
///  - 전사 이벤트: <c>response.text.delta</c> / <c>response.output_item.done</c>.
///  - L-441 out-of-band: <c>response.create</c>에 <c>conversation = "none"</c>을 지정해 멀티턴 누적 없이 매 commit마다 1회성 전사 응답 추출.
/// </summary>
public sealed class RealtimeGptReasoningStrategy : ISttModelStrategy
{
    /// <inheritdoc/>
    public string ModelId => "gpt-realtime-2";

    /// <inheritdoc/>
    /// <remarks>
    /// 일반 Realtime 세션 — intent=transcription 미사용, model 파라미터로 분기.
    /// </remarks>
    public Uri BuildConnectionUri()
    {
        return new Uri("wss://api.openai.com/v1/realtime?model=gpt-realtime-2");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// transcription 세션과 완전히 다른 구조 — instructions로 전사를 지시하고,
    /// input_audio_transcription 필드는 사용하지 않는다 (일반 세션이므로).
    /// L-441 out-of-band 모드를 위해 turn_detection.create_response=false로 자동 응답을 비활성화한다.
    /// (server_vad 활성 시) — 실제 응답 트리거는 BuildOutOfBandResponsePayload가 담당.
    /// prompt/whisperDelay 파라미터는 일반 Realtime 세션에서 사용되지 않으므로 무시.
    /// </remarks>
    public object BuildSessionUpdatePayload(string language, string prompt, bool serverVadEnabled,
        double vadThreshold, int vadSilenceDurationMs, string? whisperDelay)
    {
        var displayLanguage = NormalizeLanguageDisplay(language);
        var instructions = $"입력된 음성을 그대로 {displayLanguage}로 전사하라. 다른 설명/요약 추가 금지.";

        return new
        {
            type = "session.update",
            session = new
            {
                instructions = instructions,
                input_audio_format = "pcm16",
                turn_detection = serverVadEnabled
                    ? (object)new
                    {
                        type = "server_vad",
                        threshold = vadThreshold,
                        prefix_padding_ms = 300,
                        silence_duration_ms = vadSilenceDurationMs,
                        create_response = false
                    }
                    : null,
                output_modalities = new[] { "text" }
            }
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// L-448: server_vad ON 시 OpenAI 서버가 음성 종료를 자동 감지하여 commit 수행.
    /// VAD OFF 시 turn_detection=null이므로 주기적 수동 input_audio_buffer.commit 필요.
    /// (gpt-realtime-2도 동일 — 서버 commit 트리거가 없으면 실시간 전사 0건.)
    /// </remarks>
    public bool RequiresManualCommit(bool serverVadEnabled) => !serverVadEnabled;

    /// <inheritdoc/>
    /// <remarks>
    /// 일반 Realtime 세션의 응답 완료 이벤트 — transcription 세션과 이벤트 타입이 다르다.
    /// </remarks>
    public string TranscriptionCompletedEventType => "response.output_item.done";

    /// <inheritdoc/>
    /// <remarks>
    /// 일반 Realtime 세션의 텍스트 부분 결과 이벤트 — transcription 세션과 이벤트 타입이 다르다.
    /// </remarks>
    public string TranscriptionDeltaEventType => "response.text.delta";

    /// <inheritdoc/>
    /// <remarks>
    /// L-441 out-of-band response.create 패턴.
    /// <list type="bullet">
    ///  <item><c>conversation = "none"</c>: 응답이 대화 히스토리에 추가되지 않아 멀티턴 누적 토큰 비용 0.</item>
    ///  <item><c>output_modalities = ["text"]</c>: 음성 합성 없이 텍스트만.</item>
    ///  <item><c>instructions</c>: 직전 commit된 음성을 1회성으로 전사하도록 지시.</item>
    /// </list>
    /// 호출자(OpenAiRealtimeSttService 등)는 input_audio_buffer.commit 직후 본 페이로드를 송신한다.
    /// </remarks>
    public object? BuildOutOfBandResponsePayload(string language)
    {
        var displayLanguage = NormalizeLanguageDisplay(language);
        return new
        {
            type = "response.create",
            response = new
            {
                conversation = "none",
                output_modalities = new[] { "text" },
                instructions = $"방금 입력된 음성을 그대로 {displayLanguage}로 전사하라. 다른 설명/요약 금지."
            }
        };
    }

    /// <summary>
    /// 언어 코드를 instructions에 삽입할 사람-친화적 표기로 변환한다.
    /// BCP-47 코드(예: "ko", "ko-KR", "en-US")와 자연어("한국어") 모두 처리.
    /// 알 수 없는 값은 원본을 그대로 반환한다.
    /// </summary>
    private static string NormalizeLanguageDisplay(string language)
    {
        if (string.IsNullOrWhiteSpace(language)) return "한국어";

        var primary = language.Trim();
        var dashIdx = primary.IndexOf('-');
        if (dashIdx > 0) primary = primary.Substring(0, dashIdx);

        return primary.ToLowerInvariant() switch
        {
            "ko" => "한국어",
            "en" => "영어",
            "ja" => "일본어",
            "zh" => "중국어",
            _ => language
        };
    }
}
