using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MaiX.Services.Speech;

/// <summary>
/// STT/TTS 청크 결과 레코드
/// </summary>
public record SttChunkResult(string Text, int ChunkId, float Confidence, int LatencyMs, string Model);

/// <summary>
/// Jarvis 음성 서버 WebSocket 클라이언트
/// 실시간 STT/TTS 스트리밍 처리
/// </summary>
public class ServerWebSocketSpeechService : IDisposable
{
    private ClientWebSocket? _sttWebSocket;
    private ClientWebSocket? _ttsWebSocket;
    private CancellationTokenSource? _sttReceiveCts;
    private CancellationTokenSource? _ttsReceiveCts;
    private Task? _sttReceiveTask;
    private Task? _ttsReceiveTask;
    private bool _disposed;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>STT 청크 수신 이벤트</summary>
    public event Action<SttChunkResult>? SttChunkReceived;

    /// <summary>STT 최종 결과 수신 이벤트</summary>
    public event Action<string>? SttFinalReceived;

    /// <summary>모델 상태 변경 이벤트 ("model_loading:{model}" 또는 "model_ready:{model}")</summary>
    public event Action<string>? ModelStatusChanged;

    /// <summary>TTS 오디오 청크 수신 이벤트</summary>
    public event Action<byte[]>? TtsAudioReceived;

    /// <summary>오류 발생 이벤트</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>STT WebSocket 연결 여부</summary>
    public bool IsSttConnected => _sttWebSocket?.State == WebSocketState.Open;

    /// <summary>TTS WebSocket 연결 여부</summary>
    public bool IsTtsConnected => _ttsWebSocket?.State == WebSocketState.Open;

    /// <summary>STT 또는 TTS 연결 여부</summary>
    public bool IsConnected => IsSttConnected || IsTtsConnected;

    /// <summary>
    /// STT WebSocket 연결
    /// http://host:port → ws://host:port/ws/stt?model={model} 변환 후 연결
    /// model_loading → model_ready 이벤트 수신 대기
    /// </summary>
    public async Task ConnectSttAsync(string serverBaseUrl, string model, CancellationToken ct = default)
    {
        if (IsSttConnected)
            await DisconnectSttAsync();

        var wsUrl = BuildWebSocketUrl(serverBaseUrl, $"/ws/stt?model={Uri.EscapeDataString(model)}");
        _sttWebSocket = new ClientWebSocket();

        await _sttWebSocket.ConnectAsync(new Uri(wsUrl), ct);

        _sttReceiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _sttReceiveTask = Task.Run(() => SttReceiveLoopAsync(_sttReceiveCts.Token), _sttReceiveCts.Token);

        // model_loading → model_ready 대기 (최대 60초)
        var readyTcs = new TaskCompletionSource<bool>();
        void OnModelStatus(string status)
        {
            if (status.StartsWith("model_ready:"))
                readyTcs.TrySetResult(true);
        }

        ModelStatusChanged += OnModelStatus;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            timeoutCts.Token.Register(() => readyTcs.TrySetCanceled());

            await readyTcs.Task;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 타임아웃이지만 연결은 유지 — 모델 로딩이 느릴 수 있음
            ErrorOccurred?.Invoke("모델 준비 대기 타임아웃 (60초). 연결은 유지됩니다.");
        }
        finally
        {
            ModelStatusChanged -= OnModelStatus;
        }
    }

    /// <summary>
    /// STT 세션 시작 — start JSON 메시지 전송
    /// </summary>
    public async Task StartSttSessionAsync(string model, int sampleRate = 16000, int channels = 1, int bitDepth = 16, CancellationToken ct = default)
    {
        if (!IsSttConnected)
            throw new InvalidOperationException("STT WebSocket이 연결되지 않았습니다.");

        var startMsg = new
        {
            type = "start",
            model,
            chunkSeconds = 3.0,
            overlapSeconds = 0.5,
            sampleRate,
            channels,
            bitDepth,
        };

        await SendJsonAsync(_sttWebSocket!, startMsg, ct);
    }

    /// <summary>
    /// PCM 오디오 청크 전송
    /// </summary>
    public async Task SendAudioChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (!IsSttConnected)
            throw new InvalidOperationException("STT WebSocket이 연결되지 않았습니다.");

        await _sttWebSocket!.SendAsync(
            new ArraySegment<byte>(pcmData),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken: ct);
    }

    /// <summary>
    /// STT 세션 중지 — stop JSON 메시지 전송 + stt_final 이벤트 수신 대기 (5초 타임아웃)
    /// </summary>
    public async Task<string?> StopSttSessionAsync(CancellationToken ct = default)
    {
        if (!IsSttConnected)
            return null;

        var stopMsg = new { type = "stop" };
        await SendJsonAsync(_sttWebSocket!, stopMsg, ct);

        // stt_final 이벤트 대기 (5초 타임아웃)
        var finalTcs = new TaskCompletionSource<string>();
        void OnFinal(string text) => finalTcs.TrySetResult(text);

        SttFinalReceived += OnFinal;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            timeoutCts.Token.Register(() => finalTcs.TrySetResult(string.Empty));

            return await finalTcs.Task;
        }
        finally
        {
            SttFinalReceived -= OnFinal;
        }
    }

    /// <summary>
    /// TTS WebSocket 연결
    /// </summary>
    public async Task ConnectTtsAsync(string serverBaseUrl, CancellationToken ct = default)
    {
        if (IsTtsConnected)
            await DisconnectTtsAsync();

        var wsUrl = BuildWebSocketUrl(serverBaseUrl, "/ws/tts");
        _ttsWebSocket = new ClientWebSocket();

        await _ttsWebSocket.ConnectAsync(new Uri(wsUrl), ct);

        _ttsReceiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ttsReceiveTask = Task.Run(() => TtsReceiveLoopAsync(_ttsReceiveCts.Token), _ttsReceiveCts.Token);
    }

    /// <summary>
    /// TTS 합성 요청 — synthesize JSON 메시지 전송 + WAV 청크 수신
    /// </summary>
    public async Task SynthesizeTtsAsync(string text, int speakerId = 0, string engine = "vits2", CancellationToken ct = default)
    {
        if (!IsTtsConnected)
            throw new InvalidOperationException("TTS WebSocket이 연결되지 않았습니다.");

        var synthMsg = new
        {
            type = "synthesize",
            text,
            speaker_id = speakerId,
            engine,
        };

        await SendJsonAsync(_ttsWebSocket!, synthMsg, ct);
    }

    /// <summary>
    /// STT/TTS 모든 연결 해제
    /// </summary>
    public async Task DisconnectAsync()
    {
        await DisconnectSttAsync();
        await DisconnectTtsAsync();
    }

    /// <summary>
    /// REST API로 STT 모델 변경 요청
    /// POST {serverBaseUrl}/api/stt/model {"model":model}
    /// </summary>
    public async Task ChangeModelAsync(string serverBaseUrl, string model, CancellationToken ct = default)
    {
        var url = $"{serverBaseUrl.TrimEnd('/')}/api/stt/model";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var payload = new { model };
        var resp = await http.PostAsJsonAsync(url, payload, ct);
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sttReceiveCts?.Cancel();
        _ttsReceiveCts?.Cancel();

        try { _sttWebSocket?.Dispose(); } catch { }
        try { _ttsWebSocket?.Dispose(); } catch { }

        _sttReceiveCts?.Dispose();
        _ttsReceiveCts?.Dispose();
    }

    #region Private 메서드

    /// <summary>
    /// http(s)://host:port → ws(s)://host:port{path} 변환
    /// </summary>
    private static string BuildWebSocketUrl(string baseUrl, string path)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "wss://" + trimmed[8..] + path;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return "ws://" + trimmed[7..] + path;
        return "ws://" + trimmed + path;
    }

    /// <summary>
    /// JSON 메시지 전송
    /// </summary>
    private static async Task SendJsonAsync<T>(ClientWebSocket ws, T message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken: ct);
    }

    /// <summary>
    /// STT 수신 루프 — 백그라운드에서 메시지 수신 처리
    /// </summary>
    private async Task SttReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && _sttWebSocket?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _sttWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessSttMessage(text);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            ErrorOccurred?.Invoke($"STT WebSocket 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// TTS 수신 루프 — JSON 이벤트 + Binary WAV 청크 처리
    /// </summary>
    private async Task TtsReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536]; // TTS 오디오는 큰 버퍼 필요
        try
        {
            while (!ct.IsCancellationRequested && _ttsWebSocket?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ttsWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    TtsAudioReceived?.Invoke(ms.ToArray());
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessTtsMessage(text);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            ErrorOccurred?.Invoke($"TTS WebSocket 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// STT JSON 메시지 파싱 및 이벤트 발생
    /// </summary>
    private void ProcessSttMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event", out var eventProp))
                return;

            var eventName = eventProp.GetString();
            var data = root.TryGetProperty("data", out var dataProp) ? dataProp : default;

            switch (eventName)
            {
                case "model_loading":
                    var loadingModel = data.TryGetProperty("model", out var lm) ? lm.GetString() ?? "" : "";
                    ModelStatusChanged?.Invoke($"model_loading:{loadingModel}");
                    break;

                case "model_ready":
                    var readyModel = data.TryGetProperty("model", out var rm) ? rm.GetString() ?? "" : "";
                    ModelStatusChanged?.Invoke($"model_ready:{readyModel}");
                    break;

                case "stt_chunk":
                    var chunkText = data.TryGetProperty("text", out var ct) ? ct.GetString() ?? "" : "";
                    var chunkId = data.TryGetProperty("chunk_id", out var ci) ? ci.GetInt32() : 0;
                    var confidence = data.TryGetProperty("confidence", out var cf) ? cf.GetSingle() : 0f;
                    var latencyMs = data.TryGetProperty("latency_ms", out var lms) ? lms.GetInt32() : 0;
                    var chunkModel = data.TryGetProperty("model", out var cm) ? cm.GetString() ?? "" : "";
                    SttChunkReceived?.Invoke(new SttChunkResult(chunkText, chunkId, confidence, latencyMs, chunkModel));
                    break;

                case "stt_final":
                    var finalText = data.TryGetProperty("text", out var ft) ? ft.GetString() ?? "" : "";
                    SttFinalReceived?.Invoke(finalText);
                    break;

                case "error":
                    var errorMsg = data.TryGetProperty("message", out var em) ? em.GetString() ?? "" : "";
                    ErrorOccurred?.Invoke($"STT 서버 오류: {errorMsg}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            ErrorOccurred?.Invoke($"STT 메시지 파싱 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// TTS JSON 메시지 파싱
    /// </summary>
    private void ProcessTtsMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("event", out var eventProp))
                return;

            var eventName = eventProp.GetString();

            if (eventName == "error")
            {
                var data = root.TryGetProperty("data", out var dataProp) ? dataProp : default;
                var errorMsg = data.TryGetProperty("message", out var em) ? em.GetString() ?? "" : "";
                ErrorOccurred?.Invoke($"TTS 서버 오류: {errorMsg}");
            }
        }
        catch (JsonException ex)
        {
            ErrorOccurred?.Invoke($"TTS 메시지 파싱 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// STT WebSocket 연결 해제
    /// </summary>
    private async Task DisconnectSttAsync()
    {
        _sttReceiveCts?.Cancel();

        if (_sttWebSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _sttWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "종료", CancellationToken.None);
            }
            catch { }
        }

        if (_sttReceiveTask != null)
        {
            try { await _sttReceiveTask; } catch { }
        }

        _sttWebSocket?.Dispose();
        _sttWebSocket = null;
        _sttReceiveCts?.Dispose();
        _sttReceiveCts = null;
        _sttReceiveTask = null;
    }

    /// <summary>
    /// TTS WebSocket 연결 해제
    /// </summary>
    private async Task DisconnectTtsAsync()
    {
        _ttsReceiveCts?.Cancel();

        if (_ttsWebSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _ttsWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "종료", CancellationToken.None);
            }
            catch { }
        }

        if (_ttsReceiveTask != null)
        {
            try { await _ttsReceiveTask; } catch { }
        }

        _ttsWebSocket?.Dispose();
        _ttsWebSocket = null;
        _ttsReceiveCts?.Dispose();
        _ttsReceiveCts = null;
        _ttsReceiveTask = null;
    }

    #endregion
}
