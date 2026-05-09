// TTS 재생 서비스 — OpenAI TTS 백엔드 위임 (Jarvis 서버 모드 제거)
using System;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Services.AI;
using NLog;

namespace mAIx.Services.Speech;

/// <summary>
/// TTS 재생 서비스 — IOpenAiTtsService에 위임
/// </summary>
public class TextToSpeechService : IDisposable
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly IOpenAiTtsService _ttsService;
    private bool _disposed;

    public TextToSpeechService(IOpenAiTtsService ttsService)
    {
        _ttsService = ttsService ?? throw new ArgumentNullException(nameof(ttsService));
    }

    /// <summary>현재 재생 중 여부</summary>
    public bool IsSpeaking => _ttsService.IsSpeaking;

    /// <summary>텍스트를 음성으로 재생</summary>
    public async Task SpeakAsync(string text, CancellationToken ct = default)
    {
        if (_disposed) return;

        try
        {
            _log.Debug("[TTS] SpeakAsync 시작 — len={Len}", text?.Length ?? 0);
            await _ttsService.SpeakAsync(text ?? string.Empty, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Info("[TTS] SpeakAsync 취소됨");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[TTS] SpeakAsync 실패");
        }
    }

    /// <summary>현재 재생 중지</summary>
    public void Stop()
    {
        if (_disposed) return;
        try
        {
            _ttsService.Stop();
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[TTS] Stop 중 오류");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // IOpenAiTtsService는 DI 컨테이너가 수명 관리 — 직접 Dispose 금지
    }
}
