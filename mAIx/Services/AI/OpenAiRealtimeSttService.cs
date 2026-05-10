// OpenAI Realtime API WebSocket 기반 실시간 STT 서비스 (화자분리 OFF 모드)
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Models.Settings;
using mAIx.Services.AI.Testing;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 실시간 STT 서비스 인터페이스 (WebSocket 기반)
/// </summary>
public interface IOpenAiRealtimeSttService : IDisposable
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
    /// PCM 16kHz mono 오디오 청크를 Realtime API로 전송
    /// (AudioRecordingService.RealtimeAudioChunkReady에서 호출)
    /// </summary>
    Task SendAudioChunkAsync(byte[] pcmData, TimeSpan chunkStartTime);

    /// <summary>
    /// STT 중지
    /// </summary>
    Task StopAsync();
}

/// <summary>
/// OpenAI Realtime API WebSocket 연결을 통한 실시간 STT 서비스
/// </summary>
public sealed class OpenAiRealtimeSttService : IOpenAiRealtimeSttService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _disposed;
    private double _lastSpeechStartedMs = 0;
    private double _lastSpeechStoppedMs = 0;

    // PCM 16kHz mono 기준 bytes/sec
    private const int BytesPerSecond = 16000 * 2;

    /// <inheritdoc/>
    public event Action<TimeSpan, string>? TranscriptSegmentReceived;

    public OpenAiRealtimeSttService(AppSettingsManager settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_ws != null)
        {
            _log.Warn("[RealtimeSTT] 이미 실행 중 — StartAsync 무시");
            return;
        }

        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        var model = _settings.OaiRecording.RealtimeSttModel;

        _log.Info("[RealtimeSTT] 연결 시작: model={Model}", model);
        _log.Info($"[OpenAi-Realtime] StartAsync — model={model}, key={(apiKey?.Length >= 7 ? apiKey.Substring(0, 7) : "(short_or_empty)")}***");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        _ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

        var uri = new Uri($"wss://api.openai.com/v1/realtime?model={Uri.EscapeDataString(model)}");

        try
        {
            await _ws.ConnectAsync(uri, _cts.Token).ConfigureAwait(false);
            _log.Info("[RealtimeSTT] WebSocket 연결 성공");
            _log.Info($"[OpenAi-Realtime] WebSocket 연결 결과 — state={_ws?.State}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[RealtimeSTT] WebSocket 연결 실패");
            Cleanup();
            throw;
        }

        // session.update 발송 — STT 전용 모드 활성화 (modalities=text + server VAD + whisper-1)
        await SendJsonAsync(new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "text" },
                input_audio_format = "pcm16",
                input_audio_transcription = new { model = "whisper-1" },
                turn_detection = new { type = "server_vad" }
            }
        }).ConfigureAwait(false);
        _log.Info("[OpenAi-Realtime] session.update 발송 — modalities=text, VAD=server_vad, transcription=whisper-1");

        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    /// <summary>
    /// PCM 16kHz mono 바이트 배열을 Realtime API로 전송
    /// </summary>
    /// <param name="pcmData">PCM 16kHz mono 원시 바이트</param>
    /// <param name="chunkStartTime">청크 시작 시각 (녹음 기준 상대 시간)</param>
    public async Task SendAudioChunkAsync(byte[] pcmData, TimeSpan chunkStartTime)
    {
        // Mock 분기 — EnableMock=true 시 실호출 없이 즉시 반환
        if (MockOpenAiResponseInjector.TryHandleRealtimeSttChunk(chunkStartTime, (t, text) =>
                TranscriptSegmentReceived?.Invoke(t, text)))
            return;

        _log.Info($"[OpenAi-Realtime] SendAudioChunkAsync 진입 — bytes={pcmData.Length}, ws={(_ws == null ? "null" : _ws.State.ToString())}");
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            _log.Warn($"[OpenAi-Realtime] SendAudioChunkAsync silent return — _ws={(_ws == null ? "null" : _ws.State.ToString())}");
            return;
        }

        try
        {
            var base64Audio = Convert.ToBase64String(pcmData);
            var message = JsonSerializer.Serialize(new
            {
                type = "input_audio_buffer.append",
                audio = base64Audio
            });
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[RealtimeSTT] 오디오 청크 전송 실패");
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (_ws == null) return;

        _log.Info("[RealtimeSTT] 중지 시작");

        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                // 버퍼 커밋 + 응답 요청
                await SendJsonAsync(new { type = "input_audio_buffer.commit" }).ConfigureAwait(false);
                await SendJsonAsync(new { type = "response.create" }).ConfigureAwait(false);

                // Close handshake
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[RealtimeSTT] 종료 중 오류 (무시)");
        }
        finally
        {
            _cts?.Cancel();
            if (_receiveTask != null)
            {
                try { await _receiveTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
            }
            Cleanup();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuffer = new List<byte>();

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _log.Info("[RealtimeSTT] 서버에서 연결 종료");
                    break;
                }

                messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.Clear();
                    _log.Debug($"[OpenAi-Realtime] WS 수신 — type={result.MessageType}, snippet={(json?.Length > 0 ? json.Substring(0, Math.Min(120, json.Length)) : "(empty)")}");
                    ProcessMessage(json);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.Error(ex, "[RealtimeSTT] 수신 루프 오류");
        }

        _log.Info("[RealtimeSTT] 수신 루프 종료");
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            // response.audio_transcript.delta → 부분 전사 텍스트
            if (type == "response.audio_transcript.delta")
            {
                if (root.TryGetProperty("delta", out var delta))
                {
                    var text = delta.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(text))
                    {
                        // 시간 추정: 이벤트 기반으로 현재 시각 사용
                        var ts = TimeSpan.Zero;
                        if (root.TryGetProperty("item_id", out _) && root.TryGetProperty("audio_end_ms", out var endMs))
                        {
                            ts = TimeSpan.FromMilliseconds(endMs.GetDouble());
                        }
                        TranscriptSegmentReceived?.Invoke(ts, text);
                    }
                }
            }
            else if (type == "input_audio_buffer.speech_started")
            {
                var startMs = root.TryGetProperty("audio_start_ms", out var sMs) ? sMs.GetDouble() : 0;
                _lastSpeechStartedMs = startMs;
                _log.Info($"[OpenAi-Realtime] speech_started — audio_start_ms={startMs}");

                // 묵음 구간 표시: 직전 speech_stopped 이후 시간 차이가 1초 이상이면 발화
                if (_lastSpeechStoppedMs > 0)
                {
                    var silenceSec = (startMs - _lastSpeechStoppedMs) / 1000.0;
                    if (silenceSec >= 1.0)
                    {
                        var ts = TimeSpan.FromMilliseconds(_lastSpeechStoppedMs);
                        TranscriptSegmentReceived?.Invoke(ts, $"[묵음 {silenceSec:F1}초]");
                    }
                }
            }
            else if (type == "input_audio_buffer.speech_stopped")
            {
                var endMs = root.TryGetProperty("audio_end_ms", out var eMs) ? eMs.GetDouble() : 0;
                _lastSpeechStoppedMs = endMs;
                _log.Info($"[OpenAi-Realtime] speech_stopped — audio_end_ms={endMs}");
            }
            else if (type == "conversation.item.input_audio_transcription.completed")
            {
                if (root.TryGetProperty("transcript", out var tr))
                {
                    var text = tr.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(text))
                    {
                        var ts = TimeSpan.FromMilliseconds(_lastSpeechStartedMs);
                        TranscriptSegmentReceived?.Invoke(ts, text);
                        _log.Info($"[OpenAi-Realtime] transcription.completed — text={text.Substring(0, Math.Min(50, text.Length))}");
                    }
                }
            }
            else if (type == "conversation.item.input_audio_transcription.failed")
            {
                _log.Warn($"[OpenAi-Realtime] transcription.failed — json={json.Substring(0, Math.Min(200, json.Length))}");
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[RealtimeSTT] 메시지 파싱 실패: {Json}", json.Length > 200 ? json[..200] : json);
        }
    }

    private async Task SendJsonAsync(object payload)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
    }

    private void Cleanup()
    {
        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        Cleanup();
    }
}
