// 가짜 PCM 주입 디버그 헬퍼 — E2E 검증 전용 (오테스트 단계에서 활용)
using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace mAIx.Services.Audio;

/// <summary>
/// E2E 검증용 가짜 PCM 주입 헬퍼 — AudioRecordingService.RealtimeAudioChunkReady 이벤트 직접 트리거
/// </summary>
public static class DebugPcmInjectHelper
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// 가짜 16kHz mono PCM 청크 생성 (무음 또는 사인파)
    /// </summary>
    /// <param name="durationMs">청크 길이 (밀리초, 기본 2000)</param>
    /// <param name="sampleRate">샘플레이트 (기본 16000)</param>
    /// <param name="sine">true=440Hz 사인파, false=무음</param>
    /// <returns>PCM 16bit 리틀엔디안 바이트 배열</returns>
    public static byte[] GenerateFakePcm(int durationMs = 2000, int sampleRate = 16000, bool sine = false)
    {
        var sampleCount = sampleRate * durationMs / 1000;
        var bytes = new byte[sampleCount * 2]; // 16bit = 2 bytes/sample

        if (sine)
        {
            // 440Hz 사인파
            const double freq = 440.0;
            const double amplitude = 8000.0;
            for (var i = 0; i < sampleCount; i++)
            {
                var sample = (short)(amplitude * Math.Sin(2.0 * Math.PI * freq * i / sampleRate));
                bytes[i * 2] = (byte)(sample & 0xFF);
                bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
        }
        // sine=false: 무음 — bytes는 이미 0으로 초기화됨

        _log.Debug("[DebugPcmInject] 가짜 PCM 생성 — {Ms}ms, {Rate}Hz, {Bytes}bytes, sine={Sine}",
            durationMs, sampleRate, bytes.Length, sine);
        return bytes;
    }

    /// <summary>
    /// 테스트용 표준 PCM 데이터 반환 (1초 440Hz 사인파, 16kHz mono)
    /// </summary>
    /// <returns>1초 길이의 440Hz 사인파 PCM 16bit 리틀엔디안 바이트 배열</returns>
    public static byte[] GetTestAudioBuffer()
    {
        return GenerateFakePcm(durationMs: 1000, sampleRate: 16000, sine: true);
    }

    /// <summary>
    /// AudioRecordingService.RealtimeAudioChunkReady 이벤트를 직접 트리거 (가짜 PCM 주입)
    /// </summary>
    /// <param name="service">대상 AudioRecordingService 인스턴스</param>
    /// <param name="durationMs">PCM 청크 길이 (밀리초, 기본 2000)</param>
    /// <param name="sine">true=440Hz 사인파, false=무음</param>
    public static void InjectFakeChunk(AudioRecordingService service, int durationMs = 2000, bool sine = false)
    {
        ArgumentNullException.ThrowIfNull(service);

        var pcm = GenerateFakePcm(durationMs, sine: sine);
        var startTime = TimeSpan.Zero;

        _log.Info("[DebugPcmInject] InjectFakeChunk — {Bytes}bytes, startTime={Start}", pcm.Length, startTime);

        // Reflection을 통해 이벤트 트리거 (internal 이벤트 접근 — 디버그 전용)
        var eventField = typeof(AudioRecordingService)
            .GetField("RealtimeAudioChunkReady",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

        if (eventField?.GetValue(service) is Action<byte[], TimeSpan> handler)
        {
            handler.Invoke(pcm, startTime);
            _log.Info("[DebugPcmInject] 이벤트 트리거 완료");
        }
        else
        {
            // Reflection 실패 시 직접 호출 시도 (public event)
            _log.Warn("[DebugPcmInject] Reflection 실패 — 직접 Invoke 시도");
            service.GetType()
                .GetEvent("RealtimeAudioChunkReady")?
                .RaiseMethod?
                .Invoke(service, new object[] { pcm, startTime });
        }
    }

    /// <summary>
    /// 여러 PCM 청크를 지정 간격으로 순차 주입 (E2E 시나리오용)
    /// </summary>
    /// <param name="service">대상 AudioRecordingService 인스턴스</param>
    /// <param name="chunks">주입할 PCM 청크 배열</param>
    /// <param name="interval">청크 사이 대기 간격</param>
    /// <param name="ct">취소 토큰</param>
    public static async Task InjectFakeChunkSequenceAsync(
        AudioRecordingService service,
        byte[][] chunks,
        TimeSpan interval,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(chunks);

        _log.Info("[DebugPcmInject] InjectFakeChunkSequence 시작 — {Count}개 청크, 간격={Interval}ms",
            chunks.Length, interval.TotalMilliseconds);

        var eventField = typeof(AudioRecordingService)
            .GetField("RealtimeAudioChunkReady",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

        var elapsed = TimeSpan.Zero;

        for (var i = 0; i < chunks.Length; i++)
        {
            if (ct.IsCancellationRequested) break;

            var chunk = chunks[i];
            var chunkStart = elapsed;

            if (eventField?.GetValue(service) is Action<byte[], TimeSpan> handler)
            {
                handler.Invoke(chunk, chunkStart);
            }
            else
            {
                service.GetType()
                    .GetEvent("RealtimeAudioChunkReady")?
                    .RaiseMethod?
                    .Invoke(service, new object[] { chunk, chunkStart });
            }

            _log.Debug("[DebugPcmInject] 청크 {Index}/{Total} 주입 — {Bytes}bytes, t={Time}",
                i + 1, chunks.Length, chunk.Length, chunkStart);

            elapsed += interval;

            if (i < chunks.Length - 1)
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
        }

        _log.Info("[DebugPcmInject] InjectFakeChunkSequence 완료 — {Count}개 청크", chunks.Length);
    }
}
