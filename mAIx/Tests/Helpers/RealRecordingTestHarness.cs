// 사용자 실제 녹음 WAV 파일을 OpenAI Transcribe API로 실호출하여 STT 응답 검증하는 테스트 헬퍼
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NLog;
using mAIx.Services.AI;
using mAIx.Services.AI.Testing;

namespace mAIx.Tests.Helpers;

/// <summary>
/// 실제 녹음 WAV 파일을 OpenAI Transcribe API로 실호출하여 STT 응답을 검증하는 테스트 헬퍼.
/// MockOpenAiResponseInjector.EnableMock = false로 강제 설정하여 실호출을 보장한다.
/// </summary>
public static class RealRecordingTestHarness
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    // evidence 저장 경로
    private static readonly string EvidenceDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "mAIx", "evidence");

    // 청크 크기: 16kHz mono 16bit PCM 1초 = 16000 * 1 * 2 = 32000 bytes
    private const int ChunkSizeBytes = 32000;

    // 청크 간 rate limit 보호 대기 (ms)
    private const int ChunkIntervalMs = 50;

    // 모든 청크 처리 후 응답 도착 대기 (ms)
    private const int FinalWaitMs = 5000;

    /// <summary>
    /// 실제 녹음 WAV 파일을 OpenAI Transcribe API로 실호출하여 STT 응답 텍스트 목록을 반환한다.
    /// </summary>
    /// <param name="wavPath">WAV 파일 경로 (16kHz mono PCM 권장)</param>
    /// <param name="transcribeSvc">IOpenAiTranscribeSttService 인스턴스</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>수신된 STT 텍스트 목록 (청크 인덱스 순)</returns>
    public static async Task<List<string>> RunRealRecordingScenarioAsync(
        string wavPath,
        IOpenAiTranscribeSttService transcribeSvc,
        CancellationToken ct = default)
    {
        // EnableMock 원래 값 보존 후 실호출 강제
        bool originalEnableMock = MockOpenAiResponseInjector.EnableMock;
        MockOpenAiResponseInjector.EnableMock = false;  // ★ 실호출 보장

        var receivedTexts = new List<string>();

        try
        {
            // TranscriptSegmentReceived 이벤트 구독 → 응답 텍스트 누적
            void OnSegmentReceived(TimeSpan time, string text)
            {
                _log.Debug($"[RealRecording] STT 응답: t={time}, text={text}");
                receivedTexts.Add(text);
            }
            transcribeSvc.TranscriptSegmentReceived += OnSegmentReceived;

            try
            {
                // WAV 파일 읽기
                if (!File.Exists(wavPath))
                {
                    _log.Warn($"[RealRecording] WAV 파일 없음: {wavPath}");
                    return receivedTexts;
                }

                byte[] allPcmBytes;
                using (var reader = new WaveFileReader(wavPath))
                {
                    // PCM 16kHz mono 검증
                    var fmt = reader.WaveFormat;
                    if (fmt.SampleRate != 16000 || fmt.Channels != 1)
                    {
                        _log.Warn($"[RealRecording] WAV 형식 불일치 — sampleRate={fmt.SampleRate}, channels={fmt.Channels}. 16kHz mono PCM 필요.");
                        return receivedTexts;
                    }

                    // 전체 PCM 데이터 읽기
                    using var ms = new MemoryStream();
                    await reader.CopyToAsync(ms).ConfigureAwait(false);
                    allPcmBytes = ms.ToArray();
                }

                _log.Info($"[RealRecording] WAV 로드 완료 — 총 {allPcmBytes.Length} bytes, 예상 청크 수={allPcmBytes.Length / ChunkSizeBytes + (allPcmBytes.Length % ChunkSizeBytes > 0 ? 1 : 0)}");

                // 1초 청크 단위로 분할하여 전송
                int chunkIndex = 0;
                var currentTime = TimeSpan.Zero;
                int offset = 0;

                while (offset < allPcmBytes.Length && !ct.IsCancellationRequested)
                {
                    int remaining = allPcmBytes.Length - offset;
                    int chunkSize = Math.Min(ChunkSizeBytes, remaining);
                    var chunk = new byte[chunkSize];
                    Array.Copy(allPcmBytes, offset, chunk, 0, chunkSize);

                    _log.Debug($"[RealRecording] 청크 {chunkIndex} 전송 — offset={offset}, size={chunkSize}, t={currentTime}");
                    await transcribeSvc.ProcessAudioChunkAsync(chunk, currentTime).ConfigureAwait(false);

                    offset += chunkSize;
                    currentTime += TimeSpan.FromSeconds(1);
                    chunkIndex++;

                    // rate limit 보호
                    await Task.Delay(ChunkIntervalMs, ct).ConfigureAwait(false);
                }

                _log.Info($"[RealRecording] 모든 청크 전송 완료 ({chunkIndex}개). {FinalWaitMs}ms 응답 대기 중...");

                // 응답 도착 대기
                await Task.Delay(FinalWaitMs, ct).ConfigureAwait(false);

                _log.Info($"[RealRecording] 최종 수신 텍스트 수: {receivedTexts.Count}");
            }
            finally
            {
                transcribeSvc.TranscriptSegmentReceived -= OnSegmentReceived;
            }

            // evidence 저장
            Directory.CreateDirectory(EvidenceDir);
            var evidencePath = Path.Combine(EvidenceDir, "real_recording_stt_result.txt");
            using (var writer = new StreamWriter(evidencePath, append: false, encoding: System.Text.Encoding.UTF8))
            {
                for (int i = 0; i < receivedTexts.Count; i++)
                {
                    await writer.WriteLineAsync($"[{i}] {receivedTexts[i]}").ConfigureAwait(false);
                }
            }
            _log.Info($"[RealRecording] evidence 저장 완료: {evidencePath}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[RealRecording] RunRealRecordingScenarioAsync 실패");
        }
        finally
        {
            // EnableMock 원복 (원래 false면 false 유지, 원래 true면 true 복원)
            MockOpenAiResponseInjector.EnableMock = originalEnableMock;
        }

        return receivedTexts;
    }
}
