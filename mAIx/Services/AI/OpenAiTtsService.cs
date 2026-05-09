// OpenAI TTS 서비스 — POST /v1/audio/speech 래퍼 (NAudio 재생 포함)
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models.Settings;
using mAIx.Services.Storage;
using NAudio.Wave;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// OpenAI TTS 서비스 인터페이스 (POST /v1/audio/speech 래퍼)
/// </summary>
public interface IOpenAiTtsService : IDisposable
{
    /// <summary>텍스트를 MP3 오디오 바이트 배열로 변환</summary>
    Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default);

    /// <summary>텍스트를 음성으로 직접 재생</summary>
    Task SpeakAsync(string text, CancellationToken ct = default);

    /// <summary>현재 재생 중지</summary>
    void Stop();

    /// <summary>재생 중 여부</summary>
    bool IsSpeaking { get; }
}

/// <summary>
/// OpenAI TTS 서비스 구현 — /v1/audio/speech 호출 후 NAudio로 재생
/// </summary>
public sealed class OpenAiTtsService : IOpenAiTtsService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;
    private readonly HttpClient _http;

    private WaveOutEvent? _waveOut;
    private MemoryStream? _audioStream;
    private Mp3FileReader? _mp3Reader;
    private CancellationTokenSource? _playCts;
    private bool _disposed;

    public OpenAiTtsService(AppSettingsManager settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _http = new HttpClient();
    }

    /// <inheritdoc/>
    public bool IsSpeaking => _waveOut?.PlaybackState == PlaybackState.Playing;

    /// <inheritdoc/>
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenAI API 키가 설정되지 않았습니다.");

        var model = _settings.OaiRecording.TtsModel;
        var voice = _settings.OaiRecording.TtsVoice;

        var payload = new
        {
            model,
            input = text,
            voice,
            response_format = "mp3"
        };

        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/speech");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        _log.Debug("[TTS] 합성 요청 — model={Model}, voice={Voice}, len={Len}", model, voice, text.Length);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        _log.Debug("[TTS] 합성 완료 — {Bytes} bytes", bytes.Length);
        return bytes;
    }

    /// <inheritdoc/>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            Stop();

            _playCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var linked = _playCts.Token;

            var mp3Bytes = await SynthesizeAsync(text, linked).ConfigureAwait(false);

            if (linked.IsCancellationRequested) return;

            _audioStream = new MemoryStream(mp3Bytes);
            _mp3Reader = new Mp3FileReader(_audioStream);
            _waveOut = new WaveOutEvent();

            var tcs = new TaskCompletionSource<bool>();
            _waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult(true);

            _waveOut.Init(_mp3Reader);
            _waveOut.Play();

            using var reg = linked.Register(() => _waveOut?.Stop());

            await tcs.Task.ConfigureAwait(false);
            _log.Info("[TTS] 재생 완료");
        }
        catch (OperationCanceledException)
        {
            _log.Info("[TTS] 재생 취소됨");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[TTS] 재생 실패");
        }
    }

    /// <inheritdoc/>
    public void Stop()
    {
        if (_waveOut?.PlaybackState == PlaybackState.Playing)
            _waveOut.Stop();

        _playCts?.Cancel();
        CleanupPlayback();
    }

    private void CleanupPlayback()
    {
        try
        {
            _waveOut?.Dispose();
            _waveOut = null;
            _mp3Reader?.Dispose();
            _mp3Reader = null;
            _audioStream?.Dispose();
            _audioStream = null;
            _playCts?.Dispose();
            _playCts = null;
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[TTS] 재생 리소스 정리 중 오류");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Stop();
            _http.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[TTS] Dispose 중 오류");
        }
    }
}
