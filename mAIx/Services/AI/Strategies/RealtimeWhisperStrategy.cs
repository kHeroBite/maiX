// gpt-realtime-whisper 전용 STT Strategy — prompt 미지원 회피 + delay 파라미터 지원
using System;
using System.Collections.Generic;

namespace mAIx.Services.AI.Strategies;

/// <summary>
/// gpt-realtime-whisper 모델 전용 전략. OpenAI 공식: "For gpt-realtime-whisper in GA Realtime sessions,
/// prompt is not supported." 이므로 session.update 페이로드에서 prompt 필드를 제외한다.
/// whisper 계열은 서버 자동 commit이 없으므로 RequiresManualCommit은 항상 true (L-448).
/// delay 파라미터를 조건부로 포함하여 저지연/정확도 트레이드오프를 제어한다.
/// </summary>
public sealed class RealtimeWhisperStrategy : ISttModelStrategy
{
    public string ModelId => "gpt-realtime-whisper";

    public Uri BuildConnectionUri()
        => new Uri("wss://api.openai.com/v1/realtime?intent=transcription");

    public object BuildSessionUpdatePayload(string language, string prompt, bool serverVadEnabled,
        double vadThreshold, int vadSilenceDurationMs, string? whisperDelay)
    {
        // transcription 슬롯: prompt 필드 절대 제외 (gpt-realtime-whisper 미지원 — 포함 시 OpenAI 오류 응답).
        // delay 파라미터는 whisperDelay가 비어있지 않을 때만 포함 (Dictionary로 조건부 키 처리).
        var transcriptionDict = new Dictionary<string, object?>
        {
            ["model"] = ModelId,
            ["language"] = language
        };
        if (!string.IsNullOrWhiteSpace(whisperDelay))
        {
            transcriptionDict["delay"] = whisperDelay;
        }

        // turn_detection: whisper 계열은 서버 자동 commit이 없으므로 항상 null
        // (L-448 — 수동 PeriodicTimer commit으로 처리, RequiresManualCommit=true와 정합).
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
                        transcription = transcriptionDict,
                        turn_detection = (object?)null
                    }
                }
            }
        };
    }

    public bool RequiresManualCommit(bool serverVadEnabled) => true;

    public string TranscriptionCompletedEventType => "conversation.item.input_audio_transcription.completed";

    public string TranscriptionDeltaEventType => "conversation.item.input_audio_transcription.delta";

    public object? BuildOutOfBandResponsePayload(string language) => null;
}
