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
    /// STT 전사 세그먼트 수신 이벤트 (시간, 텍스트) — 신규 항목 추가용
    /// </summary>
    event Action<TimeSpan, string>? TranscriptSegmentReceived;

    /// <summary>
    /// STT 항목 갱신 이벤트 (itemId, 시간, 누적/최종 텍스트) — delta 누적 + completed 보정 시 기존 itemId 항목 교체용
    /// </summary>
    event Action<string, TimeSpan, string>? TranscriptSegmentUpdated;

    /// <summary>
    /// STT 항목 제거 이벤트 (itemId) — hallucination 차단 시 누적된 delta 항목 제거용
    /// </summary>
    event Action<string>? TranscriptSegmentRemoved;

    /// <summary>
    /// STT 시작
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// PCM 24kHz mono 오디오 청크를 Realtime API로 전송
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
    private DateTime? _silenceStartedAt = null;
    private double _lastSilenceMarkerSec = 0;
    private Task? _silenceMonitorTask = null;

    // 옵션 C: N초 침묵 기반 카드 분할 — delta 도착 사이 간격이 N초 이상이면 새 itemId 생성
    private string? _currentSpeechItemId = null;
    private DateTime _lastDeltaAt = DateTime.MinValue;
    private const double SilenceCardThresholdSec = 2.0; // 2초 침묵 시 새 카드

    // PCM 24kHz mono 기준 bytes/sec
    private const int BytesPerSecond = 24000 * 2;

    // Whisper 한국어 hallucination 블랙리스트 (YouTube 자막 학습 잔재)
    private static readonly System.Text.RegularExpressions.Regex[] _hallucinationPatterns = new[]
    {
        new System.Text.RegularExpressions.Regex(@"구독.*좋아요.*댓글", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"구독.*과.*좋아요", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"좋아요.*구독", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"한국어.*자막.*도움", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"매주.*업로드", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"시청해.*감사", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"알림.*설정", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"채널.*구독", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"댓글.*부탁", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"영상.*보러", System.Text.RegularExpressions.RegexOptions.Compiled),
    };

    private static bool IsHallucination(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var pattern in _hallucinationPatterns)
        {
            if (pattern.IsMatch(text)) return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public event Action<TimeSpan, string>? TranscriptSegmentReceived;
    /// <inheritdoc/>
    public event Action<string, TimeSpan, string>? TranscriptSegmentUpdated;
    /// <inheritdoc/>
    public event Action<string>? TranscriptSegmentRemoved;

    // delta 누적 버퍼 (item_id → 누적 텍스트)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _deltaBuffers = new();

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

        // session.update 발송 — STT 전용 모드 활성화 (modalities=text + server VAD + 한국어 특화)
        var sttLang = string.IsNullOrWhiteSpace(_settings.OaiRecording.SttLanguage) ? "ko" : _settings.OaiRecording.SttLanguage;
        var sttPrompt = _settings.OaiRecording.SttPrompt ?? string.Empty;
        await SendJsonAsync(new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "text" },
                input_audio_format = "pcm16",
                input_audio_transcription = new
                {
                    model = string.IsNullOrWhiteSpace(_settings.OaiRecording.TranscriptionModel) ? "gpt-4o-mini-transcribe" : _settings.OaiRecording.TranscriptionModel,
                    language = sttLang,
                    prompt = sttPrompt
                },
                turn_detection = _settings.OaiRecording.ServerVadEnabled
                    ? (object)new { type = "server_vad", threshold = 0.7, prefix_padding_ms = 300, silence_duration_ms = 300 }
                    : null
            }
        }).ConfigureAwait(false);
        _log.Info($"[OpenAi-Realtime] session.update 발송 — VAD={(_settings.OaiRecording.ServerVadEnabled ? "server_vad(thr=0.7,sil=300ms)" : "OFF (수동 commit)")}, model={(_settings.OaiRecording.TranscriptionModel ?? "(default)")}, language={sttLang}, prompt={sttPrompt.Length}자");

        _silenceStartedAt = DateTime.Now;
        _lastSilenceMarkerSec = 0;
        _silenceMonitorTask = SilenceMonitorLoopAsync(_cts.Token);
        _receiveTask = ReceiveLoopAsync(_cts.Token);
    }

    /// <summary>
    /// PCM 24kHz mono 바이트 배열을 Realtime API로 전송
    /// </summary>
    /// <param name="pcmData">PCM 24kHz mono 원시 바이트</param>
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
            _silenceStartedAt = null;
            _cts?.Cancel();
            if (_receiveTask != null)
            {
                try { await _receiveTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
            }
            if (_silenceMonitorTask != null)
            {
                try { await _silenceMonitorTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
            }
            Cleanup();
        }
    }

    private async Task SilenceMonitorLoopAsync(CancellationToken ct)
    {
        const double thresholdSec = 10.0;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_silenceStartedAt == null) continue;
                var elapsed = (DateTime.Now - _silenceStartedAt.Value).TotalSeconds;
                if (elapsed >= thresholdSec && elapsed - _lastSilenceMarkerSec >= thresholdSec)
                {
                    var ts = TimeSpan.FromMilliseconds(_lastSpeechStoppedMs);
                    try { TranscriptSegmentReceived?.Invoke(ts, $"[묵음 {elapsed:F0}초]"); }
                    catch (Exception ex) { _log.Warn(ex, "[OpenAi-Realtime] 묵음 마커 발화 실패"); }
                    _lastSilenceMarkerSec = elapsed;
                    _log.Info($"[OpenAi-Realtime] 클라이언트 묵음 마커 발화 — elapsed={elapsed:F1}s");
                }
            }
        }
        catch (OperationCanceledException) { /* 정상 종료 */ }
        catch (Exception ex) { _log.Error(ex, "[OpenAi-Realtime] 묵음 모니터 루프 오류"); }
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
                    _log.Debug($"[OpenAi-Realtime] WS 수신 RAW ({json?.Length ?? 0}자) — {json}");
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
                _silenceStartedAt = null;
                _lastSilenceMarkerSec = 0;
                // 옵션 C: itemId는 delta 도착 시 N초 침묵 기반으로 결정 (speech_started에서 변경 안 함)
                _log.Info($"[OpenAi-Realtime] speech_started — audio_start_ms={startMs}");

                // 묵음 구간 표시: 직전 speech_stopped 이후 시간 차이가 10초 이상이면 발화
                if (_lastSpeechStoppedMs > 0)
                {
                    var silenceSec = (startMs - _lastSpeechStoppedMs) / 1000.0;
                    if (silenceSec >= 10.0)
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
                _silenceStartedAt = DateTime.Now;
                _log.Info($"[OpenAi-Realtime] speech_stopped — audio_end_ms={endMs}");
            }
            else if (type == "conversation.item.input_audio_transcription.delta")
            {
                // 발화 중 부분 transcript 점진 표시 — gpt-4o-mini-transcribe/gpt-4o-transcribe에서 음절 단위 streaming
                if (root.TryGetProperty("delta", out var deltaProp))
                {
                    var deltaText = deltaProp.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(deltaText))
                    {
                        var now = DateTime.Now;
                        var sinceLastDelta = (now - _lastDeltaAt).TotalSeconds;
                        var prevItemId = _currentSpeechItemId;
                        var isNewCard = (_currentSpeechItemId == null || sinceLastDelta >= SilenceCardThresholdSec);
                        if (isNewCard)
                        {
                            _currentSpeechItemId = $"card_{now.Ticks}";
                        }
                        _lastDeltaAt = now;
                        var itemId = _currentSpeechItemId!;
                        var accum = _deltaBuffers.AddOrUpdate(itemId, deltaText, (_, prev) => prev + deltaText);
                        var ts = TimeSpan.FromMilliseconds(_lastSpeechStartedMs);
                        _log.Info($"[OpenAi-Realtime] delta — text='{deltaText}' itemId={itemId} sinceLastDelta={sinceLastDelta:F2}s isNewCard={isNewCard} prevItemId={prevItemId} accum_len={accum.Length}");
                        TranscriptSegmentUpdated?.Invoke(itemId, ts, accum);
                    }
                }
            }
            else if (type == "conversation.item.input_audio_transcription.completed")
            {
                if (root.TryGetProperty("transcript", out var tr))
                {
                    var text = tr.GetString() ?? string.Empty;
                    // 옵션 C: 현재 카드의 itemId 사용 (delta와 매칭) + _lastDeltaAt 갱신
                    _lastDeltaAt = DateTime.Now;
                    var itemId = _currentSpeechItemId ?? $"card_{DateTime.Now.Ticks}";
                    if (!string.IsNullOrEmpty(text))
                    {
                        // hallucination 차단 — 누적된 delta 항목도 제거
                        if (IsHallucination(text))
                        {
                            _log.Warn($"[OpenAi-Realtime] hallucination 차단 — text={text.Substring(0, Math.Min(80, text.Length))}");
                            _deltaBuffers.TryRemove(itemId, out _);
                            TranscriptSegmentRemoved?.Invoke(itemId);
                            return;
                        }
                        var ts = TimeSpan.FromMilliseconds(_lastSpeechStartedMs);
                        // delta가 한 번이라도 도착했으면 Updated로 final 교체, 아니면 Received로 신규 추가
                        if (_deltaBuffers.ContainsKey(itemId))
                        {
                            TranscriptSegmentUpdated?.Invoke(itemId, ts, text);
                        }
                        else
                        {
                            TranscriptSegmentReceived?.Invoke(ts, text);
                        }
                        _deltaBuffers.TryRemove(itemId, out _);
                        _log.Info($"[OpenAi-Realtime] transcription.completed — text={text.Substring(0, Math.Min(50, text.Length))}");

                        // 옵션: GPT-4o-mini로 오타 후처리 (EnableTypoFix=true 시) — fire-and-forget
                        if (_settings.OaiRecording.EnableTypoFix)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var fixedText = await FixTypoAsync(text).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(fixedText) && fixedText != text)
                                    {
                                        TranscriptSegmentReceived?.Invoke(ts, $"[보정] {fixedText}");
                                        _log.Info($"[OpenAi-Realtime] 오타 후처리 완료 — {text.Length}자 → {fixedText.Length}자");
                                    }
                                }
                                catch (Exception ex) { _log.Warn(ex, "[OpenAi-Realtime] 오타 후처리 실패"); }
                            });
                        }
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

    /// <summary>
    /// GPT-4o-mini로 transcript 오타/맞춤법 보정 (한국어 특화)
    /// </summary>
    private async Task<string> FixTypoAsync(string transcript)
    {
        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(transcript))
            return transcript;

        var model = _settings.OaiRecording.TypoFixModel ?? "gpt-4o-mini";
        var baseUrl = (_settings.AIProviders?.OpenAI?.BaseUrl ?? "https://api.openai.com/v1").TrimEnd('/');
        var url = baseUrl + "/chat/completions";

        var systemPrompt = "당신은 한국어 STT 전사 결과를 자연스러운 한국어로 보정하는 도우미입니다. 다음 규칙을 지키세요: 1) 의미를 절대 바꾸지 마세요. 2) 명백한 오타/맞춤법 오류만 수정. 3) 띄어쓰기를 자연스럽게 정리. 4) 문장 부호를 적절히 추가. 5) 결과 텍스트만 반환 (설명 없이).";
        var payload = new
        {
            model = model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = transcript }
            },
            temperature = 0.0,
            max_tokens = Math.Max(64, transcript.Length * 2)
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(url, content).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _log.Warn($"[OpenAi-Realtime] FixTypoAsync HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            return transcript;
        }
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var fixedText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? transcript;
        return fixedText.Trim();
    }

    private void Cleanup()
    {
        _ws?.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
        _silenceMonitorTask = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        Cleanup();
    }
}
