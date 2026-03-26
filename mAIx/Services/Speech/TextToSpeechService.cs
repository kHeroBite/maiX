using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using NAudio.Wave;

namespace mAIx.Services.Speech;

/// <summary>
/// TTS 재생 서비스 — 서버/클라이언트 모드 지원
/// </summary>
public class TextToSpeechService : IDisposable
{
    private WaveOutEvent? _waveOut;
    private WaveFileReader? _waveReader;
    private MemoryStream? _audioStream;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>현재 재생 중 여부</summary>
    public bool IsSpeaking => _waveOut?.PlaybackState == PlaybackState.Playing;

    /// <summary>텍스트를 음성으로 재생</summary>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        var prefs = App.Settings?.UserPreferences;
        if (prefs?.TtsMode != "server")
        {
            Utils.Log4.Warn("[TTS] 클라이언트 모드 미구현 — 스킵");
            return;
        }

        try
        {
            // 기존 재생 중지
            Stop();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            // 서버에서 WAV 데이터 수신
            using var serverSvc = new ServerSpeechService(prefs.SpeechServerUrl);
            var wavBytes = await serverSvc.SynthesizeAsync(text, 0, _cts.Token);

            if (_cts.Token.IsCancellationRequested) return;

            // NAudio로 재생
            _audioStream = new MemoryStream(wavBytes);
            _waveReader = new WaveFileReader(_audioStream);
            _waveOut = new WaveOutEvent();

            var tcs = new TaskCompletionSource<bool>();
            _waveOut.PlaybackStopped += (_, _) => tcs.TrySetResult(true);

            _waveOut.Init(_waveReader);
            _waveOut.Play();

            // 취소 시 재생 중지
            using var reg = _cts.Token.Register(() =>
            {
                _waveOut?.Stop();
            });

            await tcs.Task;
            Utils.Log4.Info("[TTS] 서버 모드 재생 완료");
        }
        catch (OperationCanceledException)
        {
            Utils.Log4.Info("[TTS] 재생 취소됨");
        }
        catch (Exception ex)
        {
            Utils.Log4.Error($"[TTS] 재생 실패: {ex.Message}");
        }
    }

    /// <summary>현재 재생 중지</summary>
    public void Stop()
    {
        if (_waveOut?.PlaybackState == PlaybackState.Playing)
        {
            _waveOut.Stop();
        }

        _cts?.Cancel();
        CleanupPlayback();
    }

    private void CleanupPlayback()
    {
        _waveOut?.Dispose();
        _waveOut = null;
        _waveReader?.Dispose();
        _waveReader = null;
        _audioStream?.Dispose();
        _audioStream = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
