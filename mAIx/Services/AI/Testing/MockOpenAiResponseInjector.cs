// OpenAI API 실호출 없이 mock 응답을 주입하는 테스트 전용 인터셉터 (production 영향 없음)
using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace mAIx.Services.AI.Testing;

/// <summary>
/// OpenAI API 실호출 없이 mock 응답을 주입하는 테스트 전용 인터셉터.
/// production 영향 없음 — EnableMock = false가 기본값.
/// </summary>
public static class MockOpenAiResponseInjector
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Mock 모드 활성화 플래그 (기본 false — production 영향 없음)
    /// </summary>
    public static bool EnableMock { get; set; } = false;

    // ─── Mock STT 응답 (Realtime / Transcribe 공통) ───────────────────

    private static readonly string[] MockSttTexts =
    {
        "테스트 녹취 텍스트 1",
        "테스트 녹취 텍스트 2",
        "화자분리 모드 테스트",
        "오픈에이아이 목 응답 확인",
        "E2E 검증 녹취 시나리오",
    };

    private static int _sttCallIndex = 0;

    // ─── Mock 화자 라벨 ───────────────────────────────────────────────

    private static readonly string[] MockSpeakerLabels = { "speaker_1", "speaker_2" };

    private static int _speakerCallIndex = 0;

    // ─── Mock 요약 텍스트 ─────────────────────────────────────────────

    /// <summary>Mock 1분 요약 텍스트</summary>
    public static string MockMinuteSummary { get; set; } = "1분 요약 테스트";

    /// <summary>Mock 누적 요약 텍스트</summary>
    public static string MockCumulativeSummary { get; set; } = "누적 요약 테스트";

    /// <summary>Mock 최종 요약 텍스트</summary>
    public static string MockFinalSummary { get; set; } = "최종 요약 테스트";

    // ─── Mock 데이터 반환 헬퍼 ───────────────────────────────────────

    /// <summary>
    /// 순환 인덱스로 다음 Mock STT 텍스트 반환
    /// </summary>
    public static string GetNextSttText()
    {
        var idx = Interlocked.Increment(ref _sttCallIndex) % MockSttTexts.Length;
        var text = MockSttTexts[idx];
        _log.Debug("[MockInjector] STT mock 반환: {Text}", text);
        return text;
    }

    /// <summary>
    /// 순환 인덱스로 다음 Mock 화자 라벨 반환 (speaker_1 / speaker_2 교대)
    /// </summary>
    public static string GetNextSpeakerLabel()
    {
        var idx = Interlocked.Increment(ref _speakerCallIndex) % MockSpeakerLabels.Length;
        return MockSpeakerLabels[idx];
    }

    /// <summary>
    /// Mock 모드 상태 초기화 (카운터 리셋)
    /// </summary>
    public static void Reset()
    {
        _sttCallIndex = 0;
        _speakerCallIndex = 0;
        _log.Info("[MockInjector] 상태 초기화 완료");
    }

    // ─── Mock 분기 헬퍼 메서드 ───────────────────────────────────────

    /// <summary>
    /// Realtime STT SendAudioChunkAsync mock 분기.
    /// EnableMock=true 시 즉시 mock TranscriptSegmentReceived 이벤트 발화.
    /// </summary>
    /// <param name="chunkStartTime">청크 시작 시간</param>
    /// <param name="onTranscript">TranscriptSegmentReceived 이벤트 발화 콜백</param>
    /// <returns>mock 처리 여부 (true=mock 분기 사용됨)</returns>
    public static bool TryHandleRealtimeSttChunk(
        TimeSpan chunkStartTime,
        Action<TimeSpan, string> onTranscript)
    {
        if (!EnableMock) return false;

        var text = GetNextSttText();
        _log.Info("[MockInjector] Realtime STT mock — t={Time}, text={Text}", chunkStartTime, text);
        onTranscript?.Invoke(chunkStartTime, text);
        return true;
    }

    /// <summary>
    /// Transcribe STT ProcessAudioChunkAsync mock 분기.
    /// EnableMock=true 시 즉시 mock 응답 반환.
    /// </summary>
    /// <param name="chunkStartTime">청크 시작 시간</param>
    /// <param name="onTranscript">TranscriptSegmentReceived 이벤트 발화 콜백</param>
    /// <returns>mock 처리 여부</returns>
    public static bool TryHandleTranscribeSttChunk(
        TimeSpan chunkStartTime,
        Action<TimeSpan, string> onTranscript)
    {
        if (!EnableMock) return false;

        var text = GetNextSttText();
        var speaker = GetNextSpeakerLabel();
        var combined = $"[{speaker}] {text}";
        _log.Info("[MockInjector] Transcribe STT mock — t={Time}, speaker={Speaker}, text={Text}",
            chunkStartTime, speaker, text);
        onTranscript?.Invoke(chunkStartTime, combined);
        return true;
    }

    /// <summary>
    /// TTS SynthesizeAsync mock 분기.
    /// EnableMock=true 시 빈 바이트 배열 반환 (재생 없음).
    /// </summary>
    /// <param name="text">합성할 텍스트</param>
    /// <param name="result">mock 결과 (빈 배열)</param>
    /// <returns>mock 처리 여부</returns>
    public static bool TryHandleTtsSynthesize(string text, out byte[] result)
    {
        if (!EnableMock)
        {
            result = Array.Empty<byte>();
            return false;
        }

        _log.Info("[MockInjector] TTS mock — text={Text}", text?.Length > 20 ? text[..20] + "..." : text);
        result = Array.Empty<byte>(); // 재생 없음 — 빈 배열 반환
        return true;
    }

    /// <summary>
    /// 1분 요약 mock 분기.
    /// EnableMock=true 시 MockMinuteSummary 즉시 반환.
    /// </summary>
    /// <param name="result">mock 요약 텍스트</param>
    /// <returns>mock 처리 여부</returns>
    public static bool TryHandleMinuteSummary(out string result)
    {
        if (!EnableMock)
        {
            result = string.Empty;
            return false;
        }

        _log.Info("[MockInjector] 1분 요약 mock 반환");
        result = MockMinuteSummary;
        return true;
    }

    /// <summary>
    /// 누적 요약 mock 분기.
    /// EnableMock=true 시 MockCumulativeSummary 즉시 반환.
    /// </summary>
    /// <param name="result">mock 누적 요약 텍스트</param>
    /// <returns>mock 처리 여부</returns>
    public static bool TryHandleCumulativeSummary(out string result)
    {
        if (!EnableMock)
        {
            result = string.Empty;
            return false;
        }

        _log.Info("[MockInjector] 누적 요약 mock 반환");
        result = MockCumulativeSummary;
        return true;
    }

    /// <summary>
    /// 최종 요약 mock 분기.
    /// EnableMock=true 시 MockFinalSummary 즉시 반환.
    /// </summary>
    /// <param name="result">mock 최종 요약 텍스트</param>
    /// <returns>mock 처리 여부</returns>
    public static bool TryHandleFinalSummary(out string result)
    {
        if (!EnableMock)
        {
            result = string.Empty;
            return false;
        }

        _log.Info("[MockInjector] 최종 요약 mock 반환");
        result = MockFinalSummary;
        return true;
    }
}
