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
    /// STT 항목 갱신 이벤트 (itemId, startTime, endTime, 누적/최종 텍스트) — delta 누적 + completed 보정 시 기존 itemId 항목 교체용
    /// </summary>
    event Action<string, TimeSpan, TimeSpan, string>? TranscriptSegmentUpdated;

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

    // 항목9: VAD OFF(turn_detection=null) 시 OpenAI 서버가 자동 commit 하지 않으므로
    // 주기적 수동 input_audio_buffer.commit을 송신하여 녹음 중 실시간 전사를 유도.
    private Task? _manualCommitTask = null;
    // PeriodicTimer commit과 SendAudioChunkAsync append가 동시에 _ws.SendAsync를 호출하므로
    // ClientWebSocket InvalidOperationException 방지를 위해 송신 직렬화 (L-443).
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    // 빈 버퍼 commit 시 OpenAI input_audio_buffer_commit_empty 에러 → append 이후에만 commit.
    private volatile bool _audioAppendedSinceCommit = false;
    // VAD OFF 수동 commit 주기 (초). 짧으면 부분 전사 빈번, 길면 지연 → 3초 절충.
    private const double ManualCommitIntervalSec = 3.0;

    // 옵션 C: N초 침묵 기반 카드 분할 — delta 도착 사이 간격이 N초 이상이면 새 itemId 생성
    private string? _currentSpeechItemId = null;
    private DateTime _lastDeltaAt = DateTime.MinValue;
    private const double SilenceCardThresholdSec = 2.0; // 2초 침묵 시 새 카드

    // 스트리밍 미활성 조기 감지 — session.updated 수신 시각 기록
    private DateTime? _sessionUpdatedAt = null;
    private bool _speechActivityDetected = false;

    // 회귀 감지: committed 횟수 vs completed 횟수 추적 (N회 committed 후 completed 0건 시 WARN)
    private int _committedCount = 0;
    private int _completedCount = 0;
    private const int CommittedWarnThreshold = 3;

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
        // prompt leak 패턴 (L-446/L-447)
        new System.Text.RegularExpressions.Regex(@"한국어\s*회의\s*녹음", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"회의\s*녹음\s*입니다", System.Text.RegularExpressions.RegexOptions.Compiled),
        new System.Text.RegularExpressions.Regex(@"녹음\s*시작합니다", System.Text.RegularExpressions.RegexOptions.Compiled),
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
    public event Action<string, TimeSpan, TimeSpan, string>? TranscriptSegmentUpdated;
    /// <inheritdoc/>
    public event Action<string>? TranscriptSegmentRemoved;

    // delta 누적 버퍼 (item_id → 누적 텍스트)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _deltaBuffers = new();

    // AC-017: 50자+마침 자동 분리 상수
    private const int AutoSplitMinLength = 50;
    private static readonly HashSet<char> AutoSplitTerminators = new() { '.', '!', '?', '。', '！', '？' };

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
        // OpenAI-Beta 헤더 제거 — GA transcription 모드에서 불필요 (구 preview 전용)

        // GA transcription 모드 URL — model 파라미터 미허용, intent=transcription 고정
        var uri = new Uri("wss://api.openai.com/v1/realtime?intent=transcription");

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

        // session.update 발송 — GA transcription nested 구조 (audio.input.* 경로)
        var sttLang = string.IsNullOrWhiteSpace(_settings.OaiRecording.SttLanguage) ? "ko" : _settings.OaiRecording.SttLanguage;
        var sttPrompt = _settings.OaiRecording.SttPrompt ?? string.Empty;
        var transcriptionModel = string.IsNullOrWhiteSpace(_settings.OaiRecording.TranscriptionModel)
            ? "gpt-4o-transcribe"
            : _settings.OaiRecording.TranscriptionModel;

        // GA shape: session.type="transcription" + audio.input.format/transcription/turn_detection nested
        // ⚠️ format은 object 타입으로 우선 시도. OpenAI가 거부 시 string "pcm16" 폴백 필요 (TODO)
        await SendJsonAsync(new
        {
            type = "session.update",
            session = new
            {
                type = "transcription",
                audio = new
                {
                    input = new
                    {
                        format = new { type = "audio/pcm", rate = 24000 },
                        transcription = new
                        {
                            model = transcriptionModel,
                            language = sttLang,
                            prompt = sttPrompt
                        },
                        turn_detection = (transcriptionModel != null && transcriptionModel.Contains("whisper"))
                            ? null
                            : (_settings.OaiRecording.ServerVadEnabled
                                ? (object)new { type = "server_vad", threshold = _settings.OaiRecording.VadThreshold, prefix_padding_ms = 300, silence_duration_ms = _settings.OaiRecording.VadSilenceDurationMs }
                                : null)
                    }
                }
            }
        }).ConfigureAwait(false);
        _log.Info($"[OpenAi-Realtime] session.update 발송 (GA transcription shape) — VAD={(_settings.OaiRecording.ServerVadEnabled ? $"server_vad(thr={_settings.OaiRecording.VadThreshold},sil={_settings.OaiRecording.VadSilenceDurationMs}ms)" : "OFF (수동 commit)")}, transcriptionModel={transcriptionModel}, language={sttLang}, prompt={sttPrompt.Length}자");

        _silenceStartedAt = DateTime.Now;
        _lastSilenceMarkerSec = 0;
        _silenceMonitorTask = SilenceMonitorLoopAsync(_cts.Token);
        _receiveTask = ReceiveLoopAsync(_cts.Token);

        // 항목9: turn_detection==null(서버 VAD 비활성) 시 OpenAI 자동 commit 없음 → 주기적 수동 commit 필요.
        // 조건은 session.update의 turn_detection 분기와 동일: whisper 계열 또는 ServerVadEnabled=false.
        var isWhisperModel = transcriptionModel.Contains("whisper", StringComparison.OrdinalIgnoreCase);
        var serverVadActive = _settings.OaiRecording.ServerVadEnabled && !isWhisperModel;
        if (!serverVadActive)
        {
            _audioAppendedSinceCommit = false;
            _manualCommitTask = ManualCommitLoopAsync(_cts.Token);
            _log.Info($"[OpenAi-Realtime] VAD OFF 감지 — 주기적 수동 commit 루프 시작 (간격 {ManualCommitIntervalSec:F0}s)");
        }
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
            await _sendLock.WaitAsync(_cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
            _audioAppendedSinceCommit = true;
            _log.Info("[OpenAi-Realtime] SendAudioChunkAsync 송신 완료 — bytes={Bytes}", pcmData.Length);
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
                // GA transcription 모드: response 자동 생성 — response.create 불필요 (제거됨)
                // server_vad 활성 시 OpenAI 자동 commit 수행 — 수동 commit은 더블 commit 유발 가능 (커뮤니티 권고)
                var isWhisper = (_settings.OaiRecording.TranscriptionModel ?? string.Empty).Contains("whisper");
                var useServerVad = _settings.OaiRecording.ServerVadEnabled && !isWhisper;
                if (!useServerVad)
                {
                    await SendJsonAsync(new { type = "input_audio_buffer.commit" }).ConfigureAwait(false);
                }

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
            if (_manualCommitTask != null)
            {
                try { await _manualCommitTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
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

    /// <summary>
    /// 항목9: VAD OFF(turn_detection=null) 시 OpenAI 서버가 자동 commit을 수행하지 않으므로
    /// 주기적으로 input_audio_buffer.commit을 송신하여 녹음 중 실시간 전사를 유도.
    /// append가 한 번도 없었으면 빈 버퍼 commit(commit_empty 에러) 회피를 위해 스킵.
    /// L-380: PeriodicTimer 콜백 전체를 외부 try-catch로 래핑.
    /// </summary>
    private async Task ManualCommitLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ManualCommitIntervalSec));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_ws == null || _ws.State != WebSocketState.Open) continue;
                // 빈 버퍼 commit 스킵 — 직전 commit 이후 append가 있었을 때만 commit.
                if (!_audioAppendedSinceCommit) continue;

                _audioAppendedSinceCommit = false;
                try
                {
                    await SendJsonAsync(new { type = "input_audio_buffer.commit" }).ConfigureAwait(false);
                    _committedCount++;
                    _log.Info($"[OpenAi-Realtime] VAD OFF 수동 commit 송신 (주기 {ManualCommitIntervalSec:F0}s) — committedCount={_committedCount}");
                }
                catch (Exception ex)
                {
                    _log.Warn(ex, "[OpenAi-Realtime] VAD OFF 수동 commit 송신 실패");
                }
            }
        }
        catch (OperationCanceledException) { /* 정상 종료 */ }
        catch (Exception ex) { _log.Error(ex, "[OpenAi-Realtime] 수동 commit 루프 오류"); }
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
                _speechActivityDetected = true;
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
                // server_vad 자동 commit 발생 시점 추적 (committed 카운터 증가)
                _committedCount++;
                if (_committedCount >= CommittedWarnThreshold && _completedCount == 0)
                {
                    _log.Warn($"[RealtimeSTT] transcript 미수신 — committed {_committedCount}회 후 .completed 0건. transcription.prompt/모델권한/include 확인 필요");
                }
            }
            else if (type == "conversation.item.input_audio_transcription.delta")
            {
                // 발화 중 부분 transcript 점진 표시 — OpenAI item_id를 그대로 사용하여 비동기 매칭 어긋남 방지
                if (root.TryGetProperty("delta", out var deltaProp))
                {
                    var deltaText = deltaProp.GetString() ?? string.Empty;
                    var openAiItemId = root.TryGetProperty("item_id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty;
                    if (!string.IsNullOrEmpty(deltaText) && !string.IsNullOrEmpty(openAiItemId))
                    {
                        var accum = _deltaBuffers.AddOrUpdate(openAiItemId, deltaText, (_, prev) => prev + deltaText);
                        _lastDeltaAt = DateTime.Now;
                        _currentSpeechItemId = openAiItemId;
                        var ts = TimeSpan.FromMilliseconds(_lastSpeechStartedMs);
                        var tsEnd = TimeSpan.FromMilliseconds(_lastSpeechStoppedMs > 0 ? _lastSpeechStoppedMs : _lastSpeechStartedMs);
                        _log.Info($"[OpenAi-Realtime] delta — text='{deltaText}' openAiItemId={openAiItemId} accum_len={accum.Length}");
                        TranscriptSegmentUpdated?.Invoke(openAiItemId, ts, tsEnd, accum);

                        // AC-017: 50자 이상 + 마침 문자 감지 시 강제 분리
                        if (accum.Length >= AutoSplitMinLength)
                        {
                            // 마지막 마침 문자 위치를 역방향 탐색
                            int splitIdx = -1;
                            for (int i = accum.Length - 1; i >= 0; i--)
                            {
                                if (AutoSplitTerminators.Contains(accum[i]))
                                {
                                    splitIdx = i;
                                    break;
                                }
                            }
                            if (splitIdx >= 0)
                            {
                                var splitText = accum[..(splitIdx + 1)];
                                var remainder = accum[(splitIdx + 1)..];
                                _log.Info($"[AC017-AutoSplit] 분리 발화 — len={splitText.Length}, lastChar={accum[splitIdx]}, remainder_len={remainder.Length}");
                                try { TranscriptSegmentReceived?.Invoke(ts, splitText); }
                                catch (Exception ex) { _log.Error(ex, "[AC017-AutoSplit] TranscriptSegmentReceived 발화 예외"); }
                                _deltaBuffers[openAiItemId] = remainder;
                            }
                        }
                    }
                }
            }
            else if (type == "conversation.item.input_audio_transcription.completed")
            {
                if (root.TryGetProperty("transcript", out var tr))
                {
                    var text = tr.GetString() ?? string.Empty;
                    // OpenAI item_id를 그대로 사용 (delta와 정확히 매칭 — 비동기 어긋남 차단)
                    var openAiItemId = root.TryGetProperty("item_id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty;
                    var itemId = !string.IsNullOrEmpty(openAiItemId) ? openAiItemId : $"item_fallback_{DateTime.Now.Ticks}";
                    _lastDeltaAt = DateTime.Now;
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
                        var tsEnd = TimeSpan.FromMilliseconds(_lastSpeechStoppedMs > 0 ? _lastSpeechStoppedMs : _lastSpeechStartedMs);
                        // delta가 도착했으면 LiveSTT UI는 Updated로 final 교체 (itemId 매칭 in-place)
                        if (_deltaBuffers.ContainsKey(itemId))
                        {
                            TranscriptSegmentUpdated?.Invoke(itemId, ts, tsEnd, text);
                        }
                        // ★ TopicExtractor/MinuteSummary 전달은 항상 Received로 한 번 더 발화 (delta 유무 무관)
                        // 이유: Updated 핸들러는 LiveSTTSegments UI만 갱신하므로 텍스트 통계 누락 방지
                        TranscriptSegmentReceived?.Invoke(ts, text);
                        _deltaBuffers.TryRemove(itemId, out _);
                        _completedCount++;
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
            else if (type == "session.updated")
            {
                _sessionUpdatedAt = DateTime.Now;
                _speechActivityDetected = false;
                _log.Info("[OpenAi-Realtime] session.updated 수신 — 스트리밍 미활성 감지 타이머 시작 (15초 내 speech 이벤트 미수신 시 WARN)");
                // 15초 후 speech 이벤트 미발생이면 모델 설정 오류 가능성 경고
                var capturedAt = _sessionUpdatedAt.Value;
                _ = Task.Delay(TimeSpan.FromSeconds(15)).ContinueWith(_ =>
                {
                    if (!_speechActivityDetected && _sessionUpdatedAt == capturedAt)
                        _log.Warn("[RealtimeSTT] 스트리밍 미활성 의심 — session.updated 후 15초간 speech 이벤트 0건. transcriptionModel 확인 필요 (gpt-4o-transcribe + server_vad 권장 조합)");
                }, TaskScheduler.Default);
            }
            else if (type == "error")
            {
                var errorObj = root.GetProperty("error");
                var code = errorObj.TryGetProperty("code", out var c) ? c.GetString() : "unknown";
                var message = errorObj.TryGetProperty("message", out var m) ? m.GetString() : "no message";
                _log.Error("[RealtimeSTT] OpenAI error 이벤트 수신 — code={Code}, message={Message}", code, message);
                // 사용자 가시 알림 — TranscriptSegmentUpdated 이벤트로 에러 메시지 발행
                TranscriptSegmentUpdated?.Invoke($"[STT 에러] {message}", TimeSpan.Zero, TimeSpan.Zero, "");
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
        // L-443: PeriodicTimer 수동 commit과 audio append가 동시에 _ws.SendAsync 호출 가능 → 직렬화.
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
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
        _manualCommitTask = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        Cleanup();
        // L-376: SemaphoreSlim은 IDisposable — 필드 보유 시 Dispose에서 해제.
        _sendLock.Dispose();
    }
}
