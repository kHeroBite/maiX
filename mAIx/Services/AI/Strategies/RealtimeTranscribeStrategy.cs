// gpt-4o-transcribe 계열 STT Strategy — prompt + server_vad 지원, 기존 동작 회귀 0 보장
using System;

namespace mAIx.Services.AI.Strategies;

/// <summary>
/// gpt-4o-transcribe / gpt-4o-mini-transcribe 공통 STT 전략.
/// 기존 OpenAiRealtimeSttService.StartAsync의 transcription 세션 페이로드를
/// 정확히 복제하여 회귀 0을 보장한다. (생성자 modelId 주입으로 두 모델 모두 처리.)
/// </summary>
public sealed class RealtimeTranscribeStrategy : ISttModelStrategy
{
    /// <summary>
    /// 생성자 — modelId 주입 (gpt-4o-transcribe 또는 gpt-4o-mini-transcribe).
    /// </summary>
    public RealtimeTranscribeStrategy(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("modelId는 비어 있을 수 없습니다.", nameof(modelId));
        ModelId = modelId;
    }

    /// <inheritdoc/>
    public string ModelId { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// 기존 OpenAiRealtimeSttService와 동일한 transcription 세션 URI.
    /// GA transcription 모드 URL — model 파라미터 미허용, intent=transcription 고정.
    /// </remarks>
    public Uri BuildConnectionUri()
    {
        return new Uri("wss://api.openai.com/v1/realtime?intent=transcription");
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 기존 OpenAiRealtimeSttService.StartAsync의 session.update 페이로드를 정확히 복제.
    /// GA shape: session.type="transcription" + audio.input.{format, transcription, turn_detection} nested.
    /// gpt-4o-transcribe 계열은 prompt 필드 지원 (L-447). whisperDelay는 무시.
    /// </remarks>
    public object BuildSessionUpdatePayload(string language, string prompt, bool serverVadEnabled,
        double vadThreshold, int vadSilenceDurationMs, string? whisperDelay)
    {
        return new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = 24000 },
                        transcription = new
                        {
                            model = ModelId,
                            language = language,
                            prompt = prompt ?? string.Empty
                        },
                        turn_detection = serverVadEnabled
                            ? (object)new
                            {
                                type = "server_vad",
                                threshold = vadThreshold,
                                prefix_padding_ms = 300,
                                silence_duration_ms = vadSilenceDurationMs
                            }
                            : null
                    }
                }
            }
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// L-448: server_vad ON 시 OpenAI 자동 commit 수행 → 수동 commit 불필요.
    /// VAD OFF 시 turn_detection=null이므로 주기적 수동 commit 필수.
    /// </remarks>
    public bool RequiresManualCommit(bool serverVadEnabled) => !serverVadEnabled;

    /// <inheritdoc/>
    public string TranscriptionCompletedEventType => "conversation.item.input_audio_transcription.completed";

    /// <inheritdoc/>
    public string TranscriptionDeltaEventType => "conversation.item.input_audio_transcription.delta";

    /// <inheritdoc/>
    /// <remarks>
    /// transcription 세션 모델은 out-of-band response.create 미사용 (L-441 패턴).
    /// </remarks>
    public object? BuildOutOfBandResponsePayload(string language) => null;
}
