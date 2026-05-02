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

namespace mAIx.Services.Speech;

/// <summary>
/// STT/TTS 청크 결과 레코드
/// </summary>
public record SttChunkResult(string Text, int ChunkId, float Confidence, int LatencyMs, string Model,
    float StartSeconds = 0f, float EndSeconds = 0f);

/// <summary>
/// 화자분리 청크 결과 레코드
/// </summary>
public record DiarizeChunkResult(string Speaker, float Start, float End, int ChunkId);

/// <summary>
/// Jarvis 음성 서버 WebSocket 클라이언트
/// 실시간 STT/TTS 스트리밍 처리
/// </summary>
public class ServerWebSocketSpeechService : IDisposable
{
    private ClientWebSocket? _sttWebSocket;
    private ClientWebSocket? _ttsWebSocket;
    private ClientWebSocket? _splitWebSocket;
    private CancellationTokenSource? _sttReceiveCts;
    private CancellationTokenSource? _ttsReceiveCts;
    private CancellationTokenSource? _splitReceiveCts;
    private Task? _sttReceiveTask;
    private Task? _ttsReceiveTask;
    private Task? _splitReceiveTask;
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

    /// <summary>화자분리 청크 수신 이벤트</summary>
    public event Action<DiarizeChunkResult>? DiarizeChunkReceived;

    /// <summary>오류 발생 이벤트</summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>STT WebSocket 연결 여부</summary>
    public bool IsSttConnected => _sttWebSocket?.State == WebSocketState.Open;

    /// <summary>TTS WebSocket 연결 여부</summary>
    public bool IsTtsConnected => _ttsWebSocket?.State == WebSocketState.Open;

    /// <summary>Split(STT+화자분리) WebSocket 연결 여부</summary>
    public bool IsSplitConnected => _splitWebSocket?.State == WebSocketState.Open;

    /// <summary>STT 또는 TTS 또는 Split 연결 여부</summary>
    public bool IsConnected => IsSttConnected || IsTtsConnected || IsSplitConnected;

    /// <summary>
    /// STT WebSocket 연결
    /// http://host:port → ws://host:port/ws/stt?model={model} 변환 후 연결
    /// model_loading → model_ready 이벤트 수신 대기
    /// </summary>
    public async Task ConnectSttAsync(string serverBaseUrl, string model, string wsPath = "/ws/stt", CancellationToken ct = default)
    {
        if (IsSttConnected)
            await DisconnectSttAsync().ConfigureAwait(false);

        var wsUrl = BuildWebSocketUrl(serverBaseUrl, $"{wsPath}?model={Uri.EscapeDataString(model)}");
        _sttWebSocket = new ClientWebSocket();

        await _sttWebSocket.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);

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
            chunkSeconds = 2.0,
            overlapSeconds = 0.5,
            sampleRate,
            channels,
            bitDepth,
        };

        await SendJsonAsync(_sttWebSocket!, startMsg, ct).ConfigureAwait(false);
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
            cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// STT 세션 중지 — stop JSON 메시지 전송 + stt_final 이벤트 수신 대기 (5초 타임아웃)
    /// </summary>
    public async Task<string?> StopSttSessionAsync(CancellationToken ct = default)
    {
        if (!IsSttConnected)
            return null;

        var stopMsg = new { type = "stop" };
        await SendJsonAsync(_sttWebSocket!, stopMsg, ct).ConfigureAwait(false);

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
    public async Task ConnectTtsAsync(string serverBaseUrl, string wsPath = "/ws/tts", CancellationToken ct = default)
    {
        if (IsTtsConnected)
            await DisconnectTtsAsync().ConfigureAwait(false);

        var wsUrl = BuildWebSocketUrl(serverBaseUrl, wsPath);
        _ttsWebSocket = new ClientWebSocket();

        await _ttsWebSocket.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);

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

        await SendJsonAsync(_ttsWebSocket!, synthMsg, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Split(STT+화자분리) WebSocket 연결
    /// http://host:port → ws://host:port/ws/split?model={model} 변환 후 연결
    /// </summary>
    public async Task ConnectSplitAsync(string? baseUrl, string model = "small", string wsPath = "/ws/split", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("서버 URL이 지정되지 않았습니다.", nameof(baseUrl));

        if (IsSplitConnected)
            await DisconnectSplitAsync().ConfigureAwait(false);

        var wsUrl = BuildWebSocketUrl(baseUrl, $"{wsPath}?model={Uri.EscapeDataString(model)}");
        _splitWebSocket = new ClientWebSocket();

        await _splitWebSocket.ConnectAsync(new Uri(wsUrl), ct).ConfigureAwait(false);

        _splitReceiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _splitReceiveTask = Task.Run(() => SplitReceiveLoopAsync(_splitReceiveCts.Token), _splitReceiveCts.Token);

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
            ErrorOccurred?.Invoke("모델 준비 대기 타임아웃 (60초). 연결은 유지됩니다.");
        }
        finally
        {
            ModelStatusChanged -= OnModelStatus;
        }
    }

    /// <summary>
    /// Split 세션 시작 — start JSON 메시지 전송
    /// </summary>
    public async Task StartSplitSessionAsync(string model, int sampleRate = 16000, int channels = 1, int bitDepth = 16, CancellationToken ct = default)
    {
        if (!IsSplitConnected)
            throw new InvalidOperationException("Split WebSocket이 연결되지 않았습니다.");

        var startMsg = new
        {
            type = "config",
            sample_rate = sampleRate,
            channels,
            bit_depth = bitDepth,
        };

        await SendJsonAsync(_splitWebSocket!, startMsg, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Split WebSocket으로 PCM 오디오 청크 전송
    /// </summary>
    public async Task SendSplitAudioChunkAsync(byte[] pcmData, CancellationToken ct = default)
    {
        if (!IsSplitConnected)
            throw new InvalidOperationException("Split WebSocket이 연결되지 않았습니다.");

        await _splitWebSocket!.SendAsync(
            new ArraySegment<byte>(pcmData),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Split 세션 중지 — stop JSON 메시지 전송 + stt_final 대기
    /// </summary>
    public async Task<string?> StopSplitSessionAsync(CancellationToken ct = default)
    {
        if (!IsSplitConnected)
            return null;

        var stopMsg = new { type = "end" };
        await SendJsonAsync(_splitWebSocket!, stopMsg, ct).ConfigureAwait(false);

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
    /// STT/TTS/Split 모든 연결 해제
    /// </summary>
    public async Task DisconnectAsync()
    {
        await DisconnectSttAsync().ConfigureAwait(false);
        await DisconnectTtsAsync().ConfigureAwait(false);
        await DisconnectSplitAsync().ConfigureAwait(false);
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
        var resp = await http.PostAsJsonAsync(url, payload, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sttReceiveCts?.Cancel();
        _ttsReceiveCts?.Cancel();
        _splitReceiveCts?.Cancel();

        try { _sttWebSocket?.Dispose(); } catch { }
        try { _ttsWebSocket?.Dispose(); } catch { }
        try { _splitWebSocket?.Dispose(); } catch { }

        _sttReceiveCts?.Dispose();
        _ttsReceiveCts?.Dispose();
        _splitReceiveCts?.Dispose();
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
            cancellationToken: ct).ConfigureAwait(false);
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
                    result = await _sttWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
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
                    result = await _ttsWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
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
    /// Split 수신 루프 — STT+화자분리 메시지 수신 처리
    /// /ws/split은 {"type":"stt|diarize|stt_final|error", "data":{...}} 형식
    /// </summary>
    private async Task SplitReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && _splitWebSocket?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _splitWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return;
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(ms.ToArray());
                    ProcessSplitMessage(text);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            ErrorOccurred?.Invoke($"Split WebSocket 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// Split JSON 메시지 파싱 — type 필드 기반 분기
    /// {"type":"stt", "data":{"text":"...", "chunk_id":1, "confidence":0.95, "latency_ms":150}}
    /// {"type":"diarize", "data":{"speaker":"Speaker 1", "start":0.0, "end":2.5, "chunk_id":1}}
    /// {"type":"stt_final", "data":{"text":"..."}}
    /// {"type":"model_loading|model_ready", "data":{"model":"..."}}
    /// {"type":"error", "data":{"message":"..."}}
    /// </summary>
    private void ProcessSplitMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
                return;

            var msgType = typeProp.GetString();
            var data = root.TryGetProperty("data", out var dataProp) ? dataProp : default;

            switch (msgType)
            {
                case "stt":
                    var sttText = data.TryGetProperty("text", out var st) ? st.GetString() ?? "" : "";
                    var sttChunkId = data.TryGetProperty("chunk_id", out var sci) ? sci.GetInt32() : 0;
                    var sttConfidence = data.TryGetProperty("confidence", out var scf) ? scf.GetSingle() : 0f;
                    var sttLatency = data.TryGetProperty("latency_ms", out var slm) ? slm.GetInt32() : 0;
                    var isFinal = data.TryGetProperty("is_final", out var isFinalProp) && isFinalProp.GetBoolean();
                    // 시간 필드 파싱 (서버가 제공 시 사용, 없으면 chunk_id × 1.5초 폴백)
                    var splitStart = data.TryGetProperty("start_time", out var ssf) ? ssf.GetSingle() :
                                     data.TryGetProperty("start", out var ssf2) ? ssf2.GetSingle() : sttChunkId * 1.5f;
                    var splitEnd = data.TryGetProperty("end_time", out var sef) ? sef.GetSingle() :
                                   data.TryGetProperty("end", out var sef2) ? sef2.GetSingle() : (sttChunkId + 1) * 1.5f;
                    SttChunkReceived?.Invoke(new SttChunkResult(sttText, sttChunkId, sttConfidence, sttLatency, "", splitStart, splitEnd));
                    if (isFinal)
                        SttFinalReceived?.Invoke(sttText);
                    break;

                case "diarize":
                    var speaker = data.TryGetProperty("speaker", out var sp) ? sp.GetString() ?? "" : "";
                    var start = data.TryGetProperty("start", out var ds) ? ds.GetSingle() : 0f;
                    var end = data.TryGetProperty("end", out var de) ? de.GetSingle() : 0f;
                    var diarChunkId = data.TryGetProperty("chunk_id", out var dci) ? dci.GetInt32() : 0;
                    DiarizeChunkReceived?.Invoke(new DiarizeChunkResult(speaker, start, end, diarChunkId));
                    break;

                case "stt_final":
                    var finalText = data.TryGetProperty("text", out var ft) ? ft.GetString() ?? "" : "";
                    SttFinalReceived?.Invoke(finalText);
                    break;

                case "model_loading":
                    var loadingModel = data.TryGetProperty("model", out var lm) ? lm.GetString() ?? "" : "";
                    ModelStatusChanged?.Invoke($"model_loading:{loadingModel}");
                    break;

                case "model_ready":
                    var readyModel = data.TryGetProperty("model", out var rm) ? rm.GetString() ?? "" : "";
                    ModelStatusChanged?.Invoke($"model_ready:{readyModel}");
                    break;

                case "error":
                    var errorMsg = data.TryGetProperty("message", out var em) ? em.GetString() ?? "" : "";
                    ErrorOccurred?.Invoke($"Split 서버 오류: {errorMsg}");
                    break;
            }
        }
        catch (JsonException ex)
        {
            ErrorOccurred?.Invoke($"Split 메시지 파싱 오류: {ex.Message}");
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
                    // 시간 필드 파싱 (서버가 제공 시 사용, 없으면 chunk_id × 1.5초 폴백)
                    var sttStart = data.TryGetProperty("start_time", out var stf) ? stf.GetSingle() :
                                   data.TryGetProperty("start", out var stf2) ? stf2.GetSingle() : chunkId * 1.5f;
                    var sttEnd = data.TryGetProperty("end_time", out var etf) ? etf.GetSingle() :
                                 data.TryGetProperty("end", out var etf2) ? etf2.GetSingle() : (chunkId + 1) * 1.5f;
                    SttChunkReceived?.Invoke(new SttChunkResult(chunkText, chunkId, confidence, latencyMs, chunkModel, sttStart, sttEnd));
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
                await _sttWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "종료", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }

        if (_sttReceiveTask != null)
        {
            try { await _sttReceiveTask.ConfigureAwait(false); } catch { }
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
                await _ttsWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "종료", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }

        if (_ttsReceiveTask != null)
        {
            try { await _ttsReceiveTask.ConfigureAwait(false); } catch { }
        }

        _ttsWebSocket?.Dispose();
        _ttsWebSocket = null;
        _ttsReceiveCts?.Dispose();
        _ttsReceiveCts = null;
        _ttsReceiveTask = null;
    }

    /// <summary>
    /// Split WebSocket 연결 해제
    /// </summary>
    private async Task DisconnectSplitAsync()
    {
        _splitReceiveCts?.Cancel();

        if (_splitWebSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _splitWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "종료", CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
        }

        if (_splitReceiveTask != null)
        {
            try { await _splitReceiveTask.ConfigureAwait(false); } catch { }
        }

        _splitWebSocket?.Dispose();
        _splitWebSocket = null;
        _splitReceiveCts?.Dispose();
        _splitReceiveCts = null;
        _splitReceiveTask = null;
    }

    #endregion
}
