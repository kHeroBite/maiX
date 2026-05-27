// whisper-1 (레거시) STT Strategy — Realtime transcription 세션 호환, prompt 지원, delay 미사용
using System;

namespace mAIx.Services.AI.Strategies;

/// <summary>
/// whisper-1 (레거시) 모델 전용 STT 전략. OpenAI Realtime transcription 세션에서 공식 지원 확인됨.
/// gpt-realtime-whisper와 달리 prompt 필드를 지원한다 (L-447). delay 파라미터는 미지원이므로 무시한다.
/// whisper 계열은 서버 자동 commit 미보장이므로 RequiresManualCommit은 항상 true (L-448).
/// </summary>
public sealed class Whisper1Strategy : ISttModelStrategy
{
    /// <inheritdoc/>
    public string ModelId => "whisper-1";

    /// <inheritdoc/>
    /// <remarks>
    /// Realtime API transcription 세션 URI — gpt-4o-transcribe / gpt-realtime-whisper와 동일.
    /// intent=transcription 고정, model 파라미터 미허용.
    /// </remarks>
    public Uri BuildConnectionUri()
        => new Uri("wss://api.openai.com/v1/realtime?intent=transcription");

    /// <inheritdoc/>
    /// <remarks>
    /// whisper-1은 prompt 필드를 지원하므로 transcription 슬롯에 포함한다 (L-447).
    /// delay 파라미터는 gpt-realtime-whisper 전용이므로 whisperDelay 인자는 무시한다.
    /// turn_detection은 whisper 계열 공통으로 null 고정 — 서버 자동 commit 없음, 수동 commit 루프 사용.
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
                        turn_detection = (object?)null
                    }
                }
            }
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// L-448: whisper-1은 레거시 모델로 server VAD 자동 commit 미보장 → 안전하게 항상 수동 commit.
    /// 절대 변경 금지.
    /// </remarks>
    public bool RequiresManualCommit(bool serverVadEnabled) => true;

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
