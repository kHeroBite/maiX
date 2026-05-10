// OpenAI Transcription API 청크 기반 STT 서비스 (화자분리 ON 모드 — Jaccard dedup 포함)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models.Settings;
using mAIx.Services.AI.Testing;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 청크 기반 STT 서비스 인터페이스 (오버랩 + dedup 지원)
/// </summary>
public interface IOpenAiTranscribeSttService : IDisposable
{
    /// <summary>
    /// STT 전사 세그먼트 수신 이벤트 (시간, 텍스트)
    /// </summary>
    event Action<TimeSpan, string>? TranscriptSegmentReceived;

    /// <summary>
    /// STT 시작
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// STT 중지
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// 오디오 청크 수신 (AudioRecordingService.RealtimeAudioChunkReady에서 호출)
    /// </summary>
    Task ProcessAudioChunkAsync(byte[] pcmData, TimeSpan chunkStartTime);
}

/// <summary>
/// OpenAI /v1/audio/transcriptions (multipart) 기반 청크 STT 서비스
/// </summary>
public sealed class OpenAiTranscribeSttService : IOpenAiTranscribeSttService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;
    private readonly HttpClient _httpClient = new();

    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _disposed;

    // 오버랩 dedup용: 직전 전사 텍스트 끝 50자
    private string _prevTailText = string.Empty;
    // PCM 24kHz mono: bytes/sec
    private const int BytesPerSecond = 48000;
    // WAV 헤더 크기
    private const int WavHeaderBytes = 44;

    /// <inheritdoc/>
    public event Action<TimeSpan, string>? TranscriptSegmentReceived;

    public OpenAiTranscribeSttService(AppSettingsManager settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            _log.Warn("[TranscribeSTT] 이미 실행 중 — StartAsync 무시");
            return Task.CompletedTask;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = true;
        _prevTailText = string.Empty;
        _log.Info("[TranscribeSTT] 시작 (model={Model})", _settings.OaiRecording.TranscribeSttModel);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync()
    {
        _log.Info("[TranscribeSTT] 중지");
        _running = false;
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task ProcessAudioChunkAsync(byte[] pcmData, TimeSpan chunkStartTime)
    {
        _log.Info($"[OpenAi-Transcribe] ProcessAudioChunkAsync — bytes={pcmData.Length}, time={chunkStartTime}");
        if (!_running || _cts == null || _cts.IsCancellationRequested) return;

        // Mock 분기 — EnableMock=true 시 실호출 없이 즉시 반환
        if (MockOpenAiResponseInjector.TryHandleTranscribeSttChunk(chunkStartTime, (t, text) =>
                TranscriptSegmentReceived?.Invoke(t, text)))
            return;

        try
        {
            var model = _settings.OaiRecording.TranscribeSttModel;
            var baseUrl = _settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1";
            var url = baseUrl.TrimEnd('/') + "/audio/transcriptions";

            using var wavStream = BuildWavStream(pcmData);
            using var content = new MultipartFormDataContent();

            var audioContent = new StreamContent(wavStream);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");
            content.Add(new StringContent(model), "model");
            content.Add(new StringContent("verbose_json"), "response_format");
            content.Add(new StringContent("segment"), "timestamp_granularities[]");
            content.Add(new StringContent("ko"), "language");

            _log.Debug("[TranscribeSTT] API 요청: {Url}, chunk={Bytes}B", url, pcmData.Length);
            _log.Info($"[OpenAi-Transcribe] POST /v1/audio/transcriptions — model={model}, contentLength={wavStream.Length}");

            var response = await _httpClient.PostAsync(url, content, _cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(false);
                _log.Error("[TranscribeSTT] API 오류 {Status}: {Body}", response.StatusCode, errBody.Length > 200 ? errBody[..200] : errBody);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(_cts.Token).ConfigureAwait(false);
            _log.Info($"[OpenAi-Transcribe] 응답 — status={response.StatusCode}, body_length={json?.Length ?? 0}, snippet={(json?.Length > 0 ? json.Substring(0, Math.Min(200, json.Length)) : "(empty)")}");
            ProcessTranscriptionResponse(json, chunkStartTime);
        }
        catch (OperationCanceledException)
        {
            // 정상 중지
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[TranscribeSTT] 청크 처리 실패");
        }
    }

    private void ProcessTranscriptionResponse(string json, TimeSpan chunkStartTime)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // verbose_json: segments 배열 or text 단순 반환
            if (root.TryGetProperty("segments", out var segments))
            {
                foreach (var seg in segments.EnumerateArray())
                {
                    var text = seg.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var startSec = seg.TryGetProperty("start", out var s) ? s.GetDouble() : 0.0;
                    var segTime = chunkStartTime + TimeSpan.FromSeconds(startSec);

                    text = DeduplicateOverlap(text);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        TranscriptSegmentReceived?.Invoke(segTime, text);
                        _log.Debug("[TranscribeSTT] 전사 세그먼트: t={Time}, text={Text}", segTime, text.Length > 80 ? text[..80] : text);
                    }
                }
            }
            else if (root.TryGetProperty("text", out var textProp))
            {
                var text = textProp.GetString() ?? string.Empty;
                text = DeduplicateOverlap(text);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    TranscriptSegmentReceived?.Invoke(chunkStartTime, text);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[TranscribeSTT] 응답 파싱 실패");
        }
    }

    /// <summary>
    /// Jaccard 유사도 >= 0.8 이면 이전 텍스트 끝 50자와 겹치는 구간 제거
    /// </summary>
    private string DeduplicateOverlap(string newText)
    {
        if (string.IsNullOrEmpty(_prevTailText) || string.IsNullOrEmpty(newText))
        {
            UpdateTail(newText);
            return newText;
        }

        // 이전 끝 50자 vs 새 텍스트 앞 50자 Jaccard (단어 토큰 set 기반)
        var prevWords = TokenizeWords(_prevTailText);
        var newHead = newText.Length > 50 ? newText[..50] : newText;
        var newWords = TokenizeWords(newHead);

        var jaccard = CalcJaccard(prevWords, newWords);
        _log.Debug("[TranscribeSTT] Jaccard 유사도={J:F2}", jaccard);

        if (jaccard >= 0.8)
        {
            // 오버랩 구간(앞 50자) 제거
            var dedupedText = newText.Length > 50 ? newText[50..] : string.Empty;
            UpdateTail(newText);
            return dedupedText;
        }

        UpdateTail(newText);
        return newText;
    }

    private void UpdateTail(string text)
    {
        _prevTailText = text.Length > 50 ? text[^50..] : text;
    }

    private static HashSet<string> TokenizeWords(string text)
    {
        return new HashSet<string>(
            text.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '?', '!' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);
    }

    private static double CalcJaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    /// <summary>
    /// PCM 24kHz 16bit mono → WAV 메모리 스트림 변환
    /// </summary>
    private static MemoryStream BuildWavStream(byte[] pcmData)
    {
        var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);

        int sampleRate = 24000;
        short channels = 1;
        short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        // RIFF 헤더
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcmData.Length);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        // fmt 청크
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);           // 청크 크기
        writer.Write((short)1);     // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        // data 청크
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(pcmData.Length);
        writer.Write(pcmData);

        ms.Position = 0;
        return ms;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _httpClient.Dispose();
    }
}
