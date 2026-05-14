// 모드 B - 단일 WebSocket으로 STT(메인)와 out-of-band 1분 요약+감성 동시 처리
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Services.AI.Helpers;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// Unified 오디오 파이프라인 — 단일 gpt-realtime 계열 WebSocket으로 STT(메인 conversation) +
/// 1분 요약·감성(out-of-band response.create)을 동시에 처리한다.
/// 3회 연속 에러 시 <see cref="PipelineFallback"/> 이벤트를 발화하여 호출자(ViewModel)가
/// Legacy 파이프라인으로 자동 전환할 수 있도록 한다.
/// </summary>
public sealed class UnifiedRealtimeAudioPipeline : IRealtimeAudioPipeline
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly AppSettingsManager _settings;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private Task? _minuteTickTask;
    private PeriodicTimer? _minuteTimer;

    // WebSocket 동시 송신 직렬화 (ClientWebSocket.SendAsync는 다중 호출 비안전)
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // 메인 STT용 — speech 타임스탬프
    private double _lastSpeechStartedMs;
    private double _lastSpeechStoppedMs;

    // 최근 1분치 발화 item_id 큐 (out-of-band response.create의 input=item_reference[]에 사용)
    private readonly ConcurrentQueue<string> _recentItemIds = new();

    // delta 누적 버퍼 (item_id → 누적 텍스트) — completed 도착 시 LiveSTT UI 교체
    private readonly ConcurrentDictionary<string, string> _deltaBuffers = new();

    // 1분 분석 응답 누적 버퍼 (response_id → function_call_arguments 누적 JSON 문자열)
    private readonly ConcurrentDictionary<string, StringBuilder> _functionCallBuffers = new();

    // 폴백 카운터 (3회 연속 에러 시 PipelineFallback 발화)
    private int _consecutiveErrors;

    // 1분 요약 인덱스 (entry.Index 채움)
    private int _entryIndex;

    // 녹음 시작 기준점 (entry.StartTime/EndTime 계산용)
    private DateTime _recordingStartedAt;

    private bool _disposed;

    /// <inheritdoc/>
    public event Action<TimeSpan, string>? TranscriptSegmentReceived;
    /// <inheritdoc/>
    public event Action<string, TimeSpan, TimeSpan, string>? TranscriptSegmentUpdated;
    /// <inheritdoc/>
    public event Action<string>? TranscriptSegmentRemoved;
    /// <inheritdoc/>
    public event Action<MinuteSummaryEntry>? MinuteSummaryCreated;
    /// <inheritdoc/>
    public event Action<AudioPipelineMode>? PipelineFallback;
    /// <inheritdoc/>
    public event Action<string>? ErrorOccurred;

    /// <inheritdoc/>
    public AudioPipelineMode Mode => AudioPipelineMode.Unified;
    /// <inheritdoc/>
    public bool IsActive { get; private set; }

    public UnifiedRealtimeAudioPipeline(AppSettingsManager settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_ws != null)
        {
            _log.Warn("[UnifiedPipeline] 이미 실행 중 — StartAsync 무시");
            return;
        }

        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new UnifiedStartupException("OpenAI API Key가 설정되지 않았습니다.");
        }

        var model = string.IsNullOrWhiteSpace(_settings.OaiRecording.UnifiedRealtimeModel)
            ? "gpt-realtime"
            : _settings.OaiRecording.UnifiedRealtimeModel;

        _log.Info("[UnifiedPipeline] 시작 — model={Model}, fallback_threshold={Threshold}",
            model, _settings.OaiRecording.UnifiedFallbackThreshold);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        _ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

        var uri = new Uri($"wss://api.openai.com/v1/realtime?model={Uri.EscapeDataString(model)}");

        try
        {
            await _ws.ConnectAsync(uri, _cts.Token).ConfigureAwait(false);
            _log.Info("[UnifiedPipeline] WebSocket 연결 성공 — state={State}", _ws.State);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[UnifiedPipeline] WebSocket 연결 실패 — Legacy 폴백 필요");
            Cleanup();
            // ViewModel이 catch하여 Legacy로 폴백
            throw new UnifiedStartupException($"Unified WebSocket 연결 실패: {ex.Message}", ex);
        }

        // session.update — STT 전용 모드 + server_vad (메인 응답은 생성 안 함 — out-of-band response.create로만 1분 요약 생성)
        var sttLang = string.IsNullOrWhiteSpace(_settings.OaiRecording.SttLanguage) ? "ko" : _settings.OaiRecording.SttLanguage;
        var sttPrompt = _settings.OaiRecording.SttPrompt ?? string.Empty;
        var transcriptionModel = string.IsNullOrWhiteSpace(_settings.OaiRecording.TranscriptionModel)
            ? "gpt-4o-mini-transcribe"
            : _settings.OaiRecording.TranscriptionModel;

        try
        {
            await SendJsonAsync(new
            {
                type = "session.update",
                session = new
                {
                    modalities = new[] { "text" },
                    instructions = "You are a transcription-only service. Do NOT generate spontaneous responses. Only transcribe the user's audio input.",
                    input_audio_format = "pcm16",
                    input_audio_transcription = new
                    {
                        model = transcriptionModel,
                        language = sttLang,
                        prompt = sttPrompt,
                    },
                    turn_detection = _settings.OaiRecording.ServerVadEnabled
                        ? (object)new
                        {
                            type = "server_vad",
                            threshold = 0.7,
                            prefix_padding_ms = 300,
                            silence_duration_ms = 300,
                            create_response = false,        // 메인 응답 차단 — 1분 분석은 out-of-band로 별도 발송
                            interrupt_response = false,
                        }
                        : null,
                }
            }).ConfigureAwait(false);
            _log.Info("[UnifiedPipeline] session.update 발송 — model={Model}, transcription={Transcription}, lang={Lang}",
                model, transcriptionModel, sttLang);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[UnifiedPipeline] session.update 발송 실패");
            Cleanup();
            throw new UnifiedStartupException($"session.update 발송 실패: {ex.Message}", ex);
        }

        _recordingStartedAt = DateTime.Now;
        _entryIndex = 0;
        _consecutiveErrors = 0;
        IsActive = true;

        // 수신 루프 + 1분 tick 루프 시작
        _receiveTask = ReceiveLoopAsync(_cts.Token);

        var intervalSeconds = Math.Max(5, _settings.OaiRecording.ProcessingIntervalSeconds);
        _minuteTimer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        _minuteTickTask = MinuteTickLoopAsync(_cts.Token);

        _log.Info("[UnifiedPipeline] 시작 완료 — interval={IntervalSec}s", intervalSeconds);
    }

    /// <inheritdoc/>
    public async Task SendAudioChunkAsync(byte[] pcmData, TimeSpan chunkStartTime)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            var base64Audio = Convert.ToBase64String(pcmData);
            var message = JsonSerializer.Serialize(new
            {
                type = "input_audio_buffer.append",
                audio = base64Audio,
            });
            await SendRawAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[UnifiedPipeline] 오디오 청크 전송 실패");
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync()
    {
        if (_ws == null) return;

        _log.Info("[UnifiedPipeline] 중지 시작");

        IsActive = false;

        try
        {
            _minuteTimer?.Dispose();
            _minuteTimer = null;
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[UnifiedPipeline] PeriodicTimer Dispose 실패 (무시)");
        }

        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                // 남은 오디오 commit (선택적 — Unified는 메인 응답을 발생시키지 않음)
                try
                {
                    await SendJsonAsync(new { type = "input_audio_buffer.commit" }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Debug(ex, "[UnifiedPipeline] commit 발송 실패 (무시)");
                }

                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[UnifiedPipeline] WebSocket 종료 중 오류 (무시)");
        }
        finally
        {
            _cts?.Cancel();

            if (_minuteTickTask != null)
            {
                try { await _minuteTickTask.ConfigureAwait(false); }
                catch { /* 취소 예외 무시 */ }
            }
            if (_receiveTask != null)
            {
                try { await _receiveTask.ConfigureAwait(false); }
                catch { /* 취소 예외 무시 */ }
            }

            Cleanup();
        }

        _log.Info("[UnifiedPipeline] 중지 완료");
    }

    // ────────── 수신 루프 ──────────

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
                    _log.Info("[UnifiedPipeline] 서버에서 연결 종료");
                    break;
                }

                messageBuffer.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.Clear();
                    ProcessMessage(json);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.Error(ex, "[UnifiedPipeline] 수신 루프 오류");
        }

        _log.Info("[UnifiedPipeline] 수신 루프 종료");
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            switch (type)
            {
                case "input_audio_buffer.speech_started":
                    HandleSpeechStarted(root);
                    break;

                case "input_audio_buffer.speech_stopped":
                    HandleSpeechStopped(root);
                    break;

                case "conversation.item.input_audio_transcription.delta":
                    HandleTranscriptionDelta(root);
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    HandleTranscriptionCompleted(root);
                    break;

                case "conversation.item.input_audio_transcription.failed":
                    _log.Warn("[UnifiedPipeline] transcription.failed — json={Json}",
                        json.Length > 200 ? json[..200] : json);
                    break;

                case "response.function_call_arguments.delta":
                    HandleFunctionCallDelta(root);
                    break;

                case "response.function_call_arguments.done":
                    HandleFunctionCallDone(root);
                    break;

                case "response.done":
                    HandleResponseDone(root);
                    break;

                case "error":
                    HandleErrorEvent(root);
                    break;

                default:
                    // 미사용 이벤트 (session.created, session.updated, response.created 등)는 무시
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[UnifiedPipeline] 메시지 파싱 실패: {Json}",
                json.Length > 200 ? json[..200] : json);
        }
    }

    private void HandleSpeechStarted(JsonElement root)
    {
        var startMs = root.TryGetProperty("audio_start_ms", out var sMs) ? sMs.GetDouble() : 0;
        _lastSpeechStartedMs = startMs;
        _log.Debug("[UnifiedPipeline] speech_started — audio_start_ms={Ms}", startMs);
    }

    private void HandleSpeechStopped(JsonElement root)
    {
        var endMs = root.TryGetProperty("audio_end_ms", out var eMs) ? eMs.GetDouble() : 0;
        _lastSpeechStoppedMs = endMs;
        _log.Debug("[UnifiedPipeline] speech_stopped — audio_end_ms={Ms}", endMs);
    }

    private void HandleTranscriptionDelta(JsonElement root)
    {
        if (!root.TryGetProperty("delta", out var deltaProp)) return;
        var deltaText = deltaProp.GetString() ?? string.Empty;
        var itemId = root.TryGetProperty("item_id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty;
        if (string.IsNullOrEmpty(deltaText) || string.IsNullOrEmpty(itemId)) return;

        var accum = _deltaBuffers.AddOrUpdate(itemId, deltaText, (_, prev) => prev + deltaText);
        var ts = TimeSpan.FromMilliseconds(_lastSpeechStartedMs);
        var tsEnd = TimeSpan.FromMilliseconds(_lastSpeechStoppedMs > 0 ? _lastSpeechStoppedMs : _lastSpeechStartedMs);

        try
        {
            TranscriptSegmentUpdated?.Invoke(itemId, ts, tsEnd, accum);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[UnifiedPipeline] TranscriptSegmentUpdated 이벤트 호출 실패");
        }
    }

    private void HandleTranscriptionCompleted(JsonElement root)
    {
        if (!root.TryGetProperty("transcript", out var tr)) return;

        var text = tr.GetString() ?? string.Empty;
        var itemId = root.TryGetProperty("item_id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty;
        if (string.IsNullOrEmpty(itemId))
        {
            itemId = $"item_fallback_{DateTime.Now.Ticks}";
        }

        if (string.IsNullOrEmpty(text))
        {
            _deltaBuffers.TryRemove(itemId, out _);
            return;
        }

        // 환각(hallucination) 차단 — Helpers/HallucinationFilter 위임
        if (HallucinationFilter.IsHallucination(text))
        {
            _log.Warn("[UnifiedPipeline] hallucination 차단 — text={Preview}",
                text.Substring(0, Math.Min(80, text.Length)));
            _deltaBuffers.TryRemove(itemId, out _);
            try { TranscriptSegmentRemoved?.Invoke(itemId); }
            catch (Exception ex) { _log.Warn(ex, "[UnifiedPipeline] TranscriptSegmentRemoved 이벤트 호출 실패"); }
            return;
        }

        var ts = TimeSpan.FromMilliseconds(_lastSpeechStartedMs);
        var tsEnd = TimeSpan.FromMilliseconds(_lastSpeechStoppedMs > 0 ? _lastSpeechStoppedMs : _lastSpeechStartedMs);

        // delta 누적된 항목은 final 텍스트로 in-place 교체
        if (_deltaBuffers.ContainsKey(itemId))
        {
            try { TranscriptSegmentUpdated?.Invoke(itemId, ts, tsEnd, text); }
            catch (Exception ex) { _log.Warn(ex, "[UnifiedPipeline] TranscriptSegmentUpdated 이벤트 호출 실패"); }
        }

        // TopicExtractor/통계용 — 항상 Received로 한 번 더 발화
        try { TranscriptSegmentReceived?.Invoke(ts, text); }
        catch (Exception ex) { _log.Warn(ex, "[UnifiedPipeline] TranscriptSegmentReceived 이벤트 호출 실패"); }

        _deltaBuffers.TryRemove(itemId, out _);

        // 1분 분석 요청에 사용할 item_id 큐에 추가 (다음 tick에 item_reference[]로 전송)
        _recentItemIds.Enqueue(itemId);

        _log.Info("[UnifiedPipeline] transcription.completed — itemId={ItemId}, text={Preview}",
            itemId, text.Substring(0, Math.Min(50, text.Length)));
    }

    private void HandleFunctionCallDelta(JsonElement root)
    {
        var responseId = root.TryGetProperty("response_id", out var rid) ? (rid.GetString() ?? string.Empty) : string.Empty;
        var delta = root.TryGetProperty("delta", out var d) ? (d.GetString() ?? string.Empty) : string.Empty;
        if (string.IsNullOrEmpty(responseId) || string.IsNullOrEmpty(delta)) return;

        var sb = _functionCallBuffers.GetOrAdd(responseId, _ => new StringBuilder());
        lock (sb)
        {
            sb.Append(delta);
        }
    }

    private void HandleFunctionCallDone(JsonElement root)
    {
        var responseId = root.TryGetProperty("response_id", out var rid) ? (rid.GetString() ?? string.Empty) : string.Empty;
        string argumentsJson;

        // arguments는 보통 done 이벤트에 직접 들어있거나 delta 누적분에서 가져옴
        if (root.TryGetProperty("arguments", out var argProp))
        {
            argumentsJson = argProp.GetString() ?? string.Empty;
        }
        else if (!string.IsNullOrEmpty(responseId) && _functionCallBuffers.TryRemove(responseId, out var sb))
        {
            argumentsJson = sb.ToString();
        }
        else
        {
            _log.Warn("[UnifiedPipeline] function_call_arguments.done — arguments 없음 (responseId={Rid})", responseId);
            return;
        }

        if (string.IsNullOrEmpty(responseId))
        {
            // 만약 delta가 다른 responseId로 누적되어 있다면 남은 버퍼 정리
        }
        else
        {
            _functionCallBuffers.TryRemove(responseId, out _);
        }

        ParseAndEmitMinuteAnalysis(argumentsJson);
    }

    private void HandleResponseDone(JsonElement root)
    {
        // response 객체 내 status=failed 또는 status_details.error 검사
        if (!root.TryGetProperty("response", out var responseEl)) return;

        var status = responseEl.TryGetProperty("status", out var sProp) ? sProp.GetString() : null;
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var errMsg = "response.failed";
            if (responseEl.TryGetProperty("status_details", out var detailsEl) &&
                detailsEl.TryGetProperty("error", out var errEl) &&
                errEl.TryGetProperty("message", out var msgEl))
            {
                errMsg = msgEl.GetString() ?? errMsg;
            }
            _log.Warn("[UnifiedPipeline] response.done failed — {Msg}", errMsg);
            _ = HandleMinuteAnalysisErrorAsync(errMsg);
        }
    }

    private void HandleErrorEvent(JsonElement root)
    {
        var errMsg = "(unknown)";
        if (root.TryGetProperty("error", out var errEl) &&
            errEl.TryGetProperty("message", out var msgEl))
        {
            errMsg = msgEl.GetString() ?? errMsg;
        }
        _log.Warn("[UnifiedPipeline] error 이벤트 수신 — {Msg}", errMsg);

        try { ErrorOccurred?.Invoke(errMsg); }
        catch (Exception ex) { _log.Warn(ex, "[UnifiedPipeline] ErrorOccurred 이벤트 호출 실패"); }

        _ = HandleMinuteAnalysisErrorAsync(errMsg);
    }

    private void ParseAndEmitMinuteAnalysis(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            _ = HandleMinuteAnalysisErrorAsync("function_call arguments empty");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            var summary = root.TryGetProperty("summary", out var sumProp) ? (sumProp.GetString() ?? string.Empty) : string.Empty;
            var topic = root.TryGetProperty("topic", out var topProp) ? (topProp.GetString() ?? string.Empty) : string.Empty;
            var score = root.TryGetProperty("sentiment_score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number
                ? scoreProp.GetInt32()
                : 50;
            var label = root.TryGetProperty("sentiment_label", out var labelProp) ? (labelProp.GetString() ?? "중립") : "중립";

            if (string.IsNullOrWhiteSpace(summary))
            {
                _ = HandleMinuteAnalysisErrorAsync("function_call summary 비어있음");
                return;
            }

            // 점수 범위 보정
            if (score < 0) score = 0;
            if (score > 100) score = 100;

            // 1분 경계 계산 — 녹음 시작 후 경과 시간을 기준으로 [startMin*60, endMin*60) 구간
            var elapsed = DateTime.Now - _recordingStartedAt;
            var endTime = elapsed;
            var startTime = endTime - TimeSpan.FromMinutes(1);
            if (startTime < TimeSpan.Zero) startTime = TimeSpan.Zero;

            var entry = new MinuteSummaryEntry
            {
                Index = _entryIndex++,
                StartTime = startTime,
                EndTime = endTime,
                SummaryText = summary,
                Topic = topic,
                CreatedAt = DateTime.Now,
                Sentiment = new SentimentResult
                {
                    Score = score,
                    Label = label,
                    AnalyzedAt = DateTime.Now,
                },
                CreatedByMode = AudioPipelineMode.Unified,
            };

            // 성공 시 폴백 카운터 리셋
            _consecutiveErrors = 0;

            _log.Info("[UnifiedPipeline] MinuteSummaryCreated #{Idx} — topic={Topic}, score={Score}({Label}), summary={Preview}",
                entry.Index, topic, score, label,
                summary.Length > 60 ? summary[..60] : summary);

            try { MinuteSummaryCreated?.Invoke(entry); }
            catch (Exception ex) { _log.Error(ex, "[UnifiedPipeline] MinuteSummaryCreated 이벤트 호출 실패"); }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[UnifiedPipeline] function_call arguments 파싱 실패 — json={Json}",
                argumentsJson.Length > 200 ? argumentsJson[..200] : argumentsJson);
            _ = HandleMinuteAnalysisErrorAsync($"파싱 실패: {ex.Message}");
        }
    }

    private async Task HandleMinuteAnalysisErrorAsync(string reason)
    {
        try
        {
            _consecutiveErrors++;
            var threshold = Math.Max(1, _settings.OaiRecording.UnifiedFallbackThreshold);
            _log.Warn("[UnifiedPipeline] 1분 분석 실패 #{Count}/{Threshold} — reason={Reason}",
                _consecutiveErrors, threshold, reason);

            try { ErrorOccurred?.Invoke($"Unified 1분 분석 실패: {reason}"); }
            catch (Exception ex) { _log.Warn(ex, "[UnifiedPipeline] ErrorOccurred 이벤트 호출 실패"); }

            if (_consecutiveErrors >= threshold)
            {
                _log.Warn("[UnifiedPipeline] 연속 에러 {Count}회 → Legacy 폴백 발화", _consecutiveErrors);

                try { PipelineFallback?.Invoke(AudioPipelineMode.Legacy); }
                catch (Exception ex) { _log.Error(ex, "[UnifiedPipeline] PipelineFallback 이벤트 호출 실패"); }

                // ViewModel이 새 Legacy 인스턴스를 생성해 swap 할 것이므로 자신은 정리
                await StopAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[UnifiedPipeline] HandleMinuteAnalysisErrorAsync 자체 실패");
        }
    }

    // ────────── 1분 tick 루프 ──────────

    private async Task MinuteTickLoopAsync(CancellationToken ct)
    {
        try
        {
            while (_minuteTimer != null && await _minuteTimer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await SendMinuteAnalysisRequestAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "[UnifiedPipeline] 1분 tick 처리 실패");
                    await HandleMinuteAnalysisErrorAsync($"tick 처리 실패: {ex.Message}").ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* 정상 종료 */
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[UnifiedPipeline] 1분 tick 루프 오류");
        }

        _log.Info("[UnifiedPipeline] 1분 tick 루프 종료");
    }

    private async Task SendMinuteAnalysisRequestAsync()
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            _log.Debug("[UnifiedPipeline] tick skip — WebSocket 미연결");
            return;
        }

        // 최근 1분치 item_id 큐를 비우면서 수집
        var itemIds = new List<string>();
        while (_recentItemIds.TryDequeue(out var id))
        {
            if (!string.IsNullOrEmpty(id)) itemIds.Add(id);
        }

        if (itemIds.Count == 0)
        {
            _log.Debug("[UnifiedPipeline] tick skip — 1분치 발화 0건");
            return;
        }

        _log.Info("[UnifiedPipeline] 1분 분석 요청 발송 — item 개수={Count}", itemIds.Count);

        // out-of-band response.create — conversation=none, input=item_reference[], tools=submit_minute_analysis(strict)
        var inputArray = new List<object>(itemIds.Count);
        foreach (var id in itemIds)
        {
            inputArray.Add(new { type = "item_reference", id });
        }

        var instructions =
            "이 1분치 한국어 발화를 분석하라. " +
            "1) summary: 30~150자의 자연스러운 한국어 요약. " +
            "2) topic: 5~20자의 주제어(명사구). " +
            "3) sentiment_score: 0~100 정수 감성 점수 (0=매우 부정, 50=중립, 100=매우 긍정). " +
            "4) sentiment_label: '긍정'/'중립'/'부정' 중 하나. " +
            "반드시 submit_minute_analysis 함수를 호출하여 결과를 반환하라.";

        var payload = new
        {
            type = "response.create",
            response = new
            {
                conversation = "none",
                modalities = new[] { "text" },
                instructions,
                input = inputArray,
                tools = new object[]
                {
                    new
                    {
                        type = "function",
                        name = "submit_minute_analysis",
                        description = "1분치 발화의 요약, 주제어, 감성 분석 결과를 제출한다.",
                        strict = true,
                        parameters = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "summary", "topic", "sentiment_score", "sentiment_label" },
                            properties = new
                            {
                                summary = new { type = "string", description = "30~150자의 자연스러운 한국어 요약" },
                                topic = new { type = "string", description = "5~20자의 주제어" },
                                sentiment_score = new { type = "integer", minimum = 0, maximum = 100, description = "0~100 감성 점수" },
                                sentiment_label = new { type = "string", @enum = new[] { "긍정", "중립", "부정" } },
                            }
                        }
                    }
                },
                tool_choice = new { type = "function", name = "submit_minute_analysis" },
            }
        };

        await SendJsonAsync(payload).ConfigureAwait(false);
    }

    // ────────── 전송 유틸 ──────────

    private async Task SendJsonAsync(object payload)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;
        var json = JsonSerializer.Serialize(payload);
        await SendRawAsync(json).ConfigureAwait(false);
    }

    private async Task SendRawAsync(string json)
    {
        if (_ws == null || _ws.State != WebSocketState.Open) return;

        var bytes = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    // ────────── 정리 ──────────

    private void Cleanup()
    {
        try { _ws?.Dispose(); } catch { /* 무시 */ }
        _ws = null;

        try { _cts?.Dispose(); } catch { /* 무시 */ }
        _cts = null;

        try { _minuteTimer?.Dispose(); } catch { /* 무시 */ }
        _minuteTimer = null;

        _receiveTask = null;
        _minuteTickTask = null;

        _deltaBuffers.Clear();
        _functionCallBuffers.Clear();
        while (_recentItemIds.TryDequeue(out _)) { /* drain */ }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[UnifiedPipeline] DisposeAsync 중 StopAsync 실패 (무시)");
        }

        try { _sendLock.Dispose(); } catch { /* 무시 */ }
    }
}

/// <summary>
/// Unified 파이프라인 시작 실패 시 던지는 예외 — ViewModel이 catch하여 Legacy 폴백을 트리거한다.
/// </summary>
public sealed class UnifiedStartupException : Exception
{
    public UnifiedStartupException(string message) : base(message) { }
    public UnifiedStartupException(string message, Exception inner) : base(message, inner) { }
}
