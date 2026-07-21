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
using mAIx.Services.AI.Strategies;
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
    // L-440 + L-447: 활성 STT 모델별 동작 전략 (URL/페이로드/이벤트 타입/out-of-band 분기).
    // StartAsync 진입 시 SttStrategyFactory.Create(TranscriptionModel)로 주입되어 ReceiveLoop/StopAsync에서 참조.
    private ISttModelStrategy? _currentStrategy;

    // 재연결 제어: 사용자 명시 StopAsync에 의한 종료 여부 추적 (의도적 종료 vs 비정상 종료 구분)
    private volatile bool _userRequestedStop = false;
    private const int MaxReconnectAttempts = 3;
    private double _lastSpeechStartedMs = 0;
    private double _lastSpeechStoppedMs = 0;
    // 3.1: 오디오 타임라인 기준 턴 간 무음(초) — speech_started 시점에 갱신, [병합판정] gap 대체용.
    // double.MaxValue = 최초 발화(직전 speech_stopped 없음) → 항상 새 카드 판정.
    private double _lastTurnSilenceSec = double.MaxValue;
    private DateTime? _silenceStartedAt = null;
    private double _lastSilenceMarkerSec = 0;
    private Task? _silenceMonitorTask = null;

    // 항목9: VAD OFF(turn_detection=null) 시 OpenAI 서버가 자동 commit 하지 않으므로
    // 주기적 수동 input_audio_buffer.commit을 송신하여 녹음 중 실시간 전사를 유도.
    private Task? _manualCommitTask = null;

    // 보조 태스크 전용 CTS — 재연결 시 _cts(사용자 취소) 대신 _auxCts만 취소하여
    // ghost loop 누적 없이 보조 태스크를 확실히 종료 후 새 태스크로 교체. (수정 1: 교착 해소)
    private CancellationTokenSource? _auxCts = null;

    // [DEBUG] 강제 재연결 트리거 태스크 — MAIX_DEBUG_STT_RECONNECT_SEC 환경변수가 설정된 경우에만 활성화.
    // 미설정/0/파싱실패 시 _debugForceReconnectTask는 null로 유지되어 프로덕션 동작에 영향 없음.
    private Task? _debugForceReconnectTask = null;
    // 강제 재연결 주기 (초). 환경변수에서 파싱, 0이면 비활성.
    private static readonly int _debugReconnectSec = ParseDebugReconnectSec();
    private static int ParseDebugReconnectSec()
    {
        var raw = Environment.GetEnvironmentVariable("MAIX_DEBUG_STT_RECONNECT_SEC");
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        return int.TryParse(raw, out var v) && v > 0 ? v : 0;
    }
    // PeriodicTimer commit과 SendAudioChunkAsync append가 동시에 _ws.SendAsync를 호출하므로
    // ClientWebSocket InvalidOperationException 방지를 위해 송신 직렬화 (L-443).
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    // 빈 버퍼 commit 시 OpenAI input_audio_buffer_commit_empty 에러 → append 이후에만 commit.
    private volatile bool _audioAppendedSinceCommit = false;
    // VAD OFF 수동 commit 주기 (초). 짧으면 부분 전사 빈번, 길면 지연 → 3초 절충.
    private const double ManualCommitIntervalSec = 3.0;

    // 옵션 C: N초 침묵 기반 카드 분할 — delta 도착 사이 간격이 N초 이상이면 새 카드 키 생성
    // 카드 병합 임계값은 하드코딩 대신 설정값(_settings.OaiRecording.CardMergeSilenceThresholdSec) 참조로 전환.
    private string? _currentSpeechItemId = null;
    private DateTime _lastDeltaAt = DateTime.MinValue;
    // 최소 병합 임계값 clamp (0 이하 설정 시 매 delta 새 카드 = 병합 비활성 방지)
    private const double MinCardMergeSilenceThresholdSec = 0.1;

    // OpenAI item_id → 병합 카드 키 매핑 (2계층 VAD 카드 병합).
    // _deltaBuffers는 OpenAI item_id를 그대로 키로 유지(completed 이벤트가 item_id로 매칭하므로),
    // UI에는 병합 카드 키를 전달하여 침묵 갭이 짧으면 여러 OpenAI item이 하나의 카드로 보이게 한다.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _itemIdToCardKey = new();
    // 카드 키별 누적 텍스트 (같은 카드 키에 속한 여러 OpenAI item의 델타 텍스트를 이어붙인 누적)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cardAccumTexts = new();
    // 카드 키별 itemId 등장순 리스트 — 다중 item 인터리브를 순서보존하여 표현(단일 base 모델 결함 근본수정).
    // 각 item의 텍스트는 진행중이면 _deltaBuffers[itemId], 완료되면 _cardItemFinalTexts[itemId]에서 조회.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.List<string>> _cardItemOrder = new();
    // itemId → 완료 확정 텍스트 (completed 수신 후에는 _deltaBuffers에서 제거되므로 별도 보관).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _cardItemFinalTexts = new();
    // 카드 키별 "최초 delta 도착 시각"(ms) 고정 캡처 — StartTime=EndTime 표시 버그 수정용(H5).
    // _lastSpeechStartedMs/_lastSpeechStoppedMs는 클래스 필드 1개뿐이라 서버 VAD 이벤트마다 덮어써져
    // 카드별 시작시각을 못박지 못했다. 새 카드 생성 시점에 1회만 기록하고, completed 시 제거한다.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _cardStartTimes = new();
    // 카드 키별 "최신 종료시각"(ms) — start>end 음수 지속시간 버그 수정용(3.0).
    // _lastSpeechStoppedMs 전역 필드를 그대로 읽으면 새 카드 시작 직후 아직 이번 턴의
    // speech_stopped가 도착하지 않아 직전 카드(이전 발화 턴)의 종료값을 그대로 반환한다.
    // 카드 키로 스코프를 격리하고, 매 delta마다 Math.Max(start, candidate)로 하한 보정하여 갱신한다.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _cardEndTimes = new();

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

    // AC-017: 자동 분리 구분자 집합 (길이 기준은 _settings.OaiRecording에서 참조)
    private static readonly HashSet<char> AutoSplitTerminators = new() { '.', '!', '?', '。', '！', '？' };
    private static readonly HashSet<char> AutoSplitSoftSeparators = new() { ',', ';', '、', '；' };
    private static readonly HashSet<char> _autoSplitAllSeparators = new(AutoSplitTerminators.Concat(AutoSplitSoftSeparators));

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
        _userRequestedStop = false;

        var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
        var model = _settings.OaiRecording.RealtimeSttModel;

        _log.Info("[RealtimeSTT] 연결 시작: model={Model}", model);
        _log.Info($"[OpenAi-Realtime] StartAsync — model={model}, key={(apiKey?.Length >= 7 ? apiKey.Substring(0, 7) : "(short_or_empty)")}***");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ws = new ClientWebSocket();
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        // OpenAI-Beta 헤더 제거 — GA transcription 모드에서 불필요 (구 preview 전용)

        // L-440 + L-447: 모델별 Strategy 주입 — URL/페이로드/이벤트 타입/out-of-band/manual commit 분기.
        // 기존 동작 회귀 0 보장: gpt-4o-transcribe 선택 시 RealtimeTranscribeStrategy가 동일 URI + 동일 페이로드 반환.
        var transcriptionModel = string.IsNullOrWhiteSpace(_settings.OaiRecording.TranscriptionModel)
            ? "gpt-4o-transcribe"
            : _settings.OaiRecording.TranscriptionModel;
        _currentStrategy = SttStrategyFactory.Create(transcriptionModel);
        var uri = _currentStrategy.BuildConnectionUri();
        _log.Info("[OpenAi-Realtime] STT Strategy 활성화: {ModelId} (RequiresManualCommit={Manual})",
            _currentStrategy.ModelId, _currentStrategy.RequiresManualCommit(_settings.OaiRecording.ServerVadEnabled));

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

        // session.update 발송 — Strategy가 모델별 페이로드 구조를 결정.
        // gpt-4o-transcribe/mini: 기존 GA transcription nested 구조 (audio.input.* 경로).
        // gpt-realtime-whisper: prompt 필드 제외 + 조건부 delay.
        // gpt-realtime-2: instructions + input_audio_format + create_response=false (L-441).
        var sttLang = string.IsNullOrWhiteSpace(_settings.OaiRecording.SttLanguage) ? "ko" : _settings.OaiRecording.SttLanguage;
        var sttPrompt = _settings.OaiRecording.SttPrompt ?? string.Empty;
        var sessionPayload = _currentStrategy.BuildSessionUpdatePayload(
            language: sttLang,
            prompt: sttPrompt,
            serverVadEnabled: _settings.OaiRecording.ServerVadEnabled,
            vadThreshold: _settings.OaiRecording.VadThreshold,
            vadSilenceDurationMs: _settings.OaiRecording.VadSilenceDurationMs,
            whisperDelay: _settings.OaiRecording.WhisperDelay
        );
        await SendJsonAsync(sessionPayload).ConfigureAwait(false);
        _log.Info($"[OpenAi-Realtime] session.update 발송 (Strategy={_currentStrategy.ModelId}) — VAD={(_settings.OaiRecording.ServerVadEnabled ? $"server_vad(thr={_settings.OaiRecording.VadThreshold},sil={_settings.OaiRecording.VadSilenceDurationMs}ms)" : "OFF (수동 commit)")}, transcriptionModel={transcriptionModel}, language={sttLang}, prompt={sttPrompt.Length}자");

        _silenceStartedAt = DateTime.Now;
        _lastSilenceMarkerSec = 0;

        // 보조 태스크 전용 CTS 생성 — _cts와 독립. 재연결 시 _auxCts만 취소.
        _auxCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _silenceMonitorTask = SilenceMonitorLoopAsync(_auxCts.Token);
        _receiveTask = ReceiveLoopAsync(_cts.Token);

        // [DEBUG] 강제 재연결 트리거 — MAIX_DEBUG_STT_RECONNECT_SEC 환경변수 설정 시에만 활성화.
        // 미설정/0이면 프로덕션 동작 완전 보존 (이 블록 자체가 실행되지 않음).
        if (_debugReconnectSec > 0)
        {
            _log.Warn("[Realtime][DEBUG] 강제 재연결 트리거 활성화 — MAIX_DEBUG_STT_RECONNECT_SEC={Sec}초", _debugReconnectSec);
            _debugForceReconnectTask = DebugForceReconnectLoopAsync(_auxCts.Token);
        }

        // L-448 + L-440: Strategy가 모델별 수동 commit 필요 여부를 결정.
        // gpt-4o-transcribe: ServerVadEnabled=false일 때만 수동 commit.
        // gpt-realtime-whisper: 항상 수동 commit (서버 자동 commit 없음).
        // gpt-realtime-2: ServerVadEnabled=false일 때만 수동 commit.
        if (_currentStrategy.RequiresManualCommit(_settings.OaiRecording.ServerVadEnabled))
        {
            _audioAppendedSinceCommit = false;
            _manualCommitTask = ManualCommitLoopAsync(_auxCts.Token);
            _log.Info($"[OpenAi-Realtime] 수동 commit 루프 시작 (Strategy={_currentStrategy.ModelId}, 간격 {ManualCommitIntervalSec:F0}s)");
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

        // 사용자 명시 중지 — 재연결 루프가 이 플래그를 확인하여 재연결을 시도하지 않음
        _userRequestedStop = true;
        _log.Info("[RealtimeSTT] 중지 시작");

        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                // GA transcription 모드: response 자동 생성 — response.create 불필요 (제거됨)
                // server_vad 활성 시 OpenAI 자동 commit 수행 — 수동 commit은 더블 commit 유발 가능 (커뮤니티 권고)
                // L-440: Strategy 기반 commit 필요 여부 — 기존 분기와 동일한 결과 (whisper/VAD OFF 시 수동 commit).
                // _currentStrategy null 폴백: 기존 인라인 로직 그대로 유지 (회귀 0 안전망).
                bool needManualCommit;
                if (_currentStrategy != null)
                {
                    needManualCommit = _currentStrategy.RequiresManualCommit(_settings.OaiRecording.ServerVadEnabled);
                }
                else
                {
                    var isWhisper = (_settings.OaiRecording.TranscriptionModel ?? string.Empty).Contains("whisper");
                    var useServerVad = _settings.OaiRecording.ServerVadEnabled && !isWhisper;
                    needManualCommit = !useServerVad;
                }
                if (needManualCommit)
                {
                    await SendJsonAsync(new { type = "input_audio_buffer.commit" }).ConfigureAwait(false);

                    // L-441 out-of-band response.create — gpt-realtime-2 등 일반 세션만 페이로드 반환.
                    if (_currentStrategy != null)
                    {
                        var outOfBand = _currentStrategy.BuildOutOfBandResponsePayload(
                            _settings.OaiRecording.SttLanguage ?? "ko");
                        if (outOfBand != null)
                        {
                            await SendJsonAsync(outOfBand).ConfigureAwait(false);
                            _log.Info("[OpenAi-Realtime] Stop 시 out-of-band response.create 송신 (Strategy={ModelId})", _currentStrategy.ModelId);
                        }
                    }
                }

                // Close handshake (L-451: 취소 불가 CancellationToken.None 대신 5초 타임아웃 적용)
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", closeCts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "[RealtimeSTT] 종료 중 오류 (무시)");
        }
        finally
        {
            _silenceStartedAt = null;
            // _auxCts 먼저 취소 → 보조 태스크(SilenceMonitor/ManualCommit/DebugForceReconnect)를 즉시 종료 신호 전달
            _auxCts?.Cancel();
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
            // [DEBUG] 강제 재연결 트리거 정리 (환경변수 미설정 시 null이므로 no-op)
            if (_debugForceReconnectTask != null)
            {
                try { await _debugForceReconnectTask.ConfigureAwait(false); } catch { /* 취소 예외 무시 */ }
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
                    _log.Info($"[OpenAi-Realtime] VAD OFF 수동 commit 송신 (주기 {ManualCommitIntervalSec:F0}s) — committedCount={_committedCount} (이 commit 직후 speech_started의 audio_start_ms가 0 근처로 리셋될 수 있음 — 세그먼트 시간은 RecordingDuration 앵커로 보정됨)");

                    // L-441 out-of-band response.create — 일반 Realtime 세션(gpt-realtime-2)에서만 페이로드 반환.
                    // transcription 세션 모델(whisper/transcribe)은 null 반환 — 별도 응답 트리거 불필요.
                    if (_currentStrategy != null)
                    {
                        var outOfBand = _currentStrategy.BuildOutOfBandResponsePayload(
                            _settings.OaiRecording.SttLanguage ?? "ko");
                        if (outOfBand != null)
                        {
                            await SendJsonAsync(outOfBand).ConfigureAwait(false);
                            _log.Info("[OpenAi-Realtime] out-of-band response.create 송신 (Strategy={ModelId})", _currentStrategy.ModelId);
                        }
                    }
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

    /// <summary>
    /// [DEBUG 전용] 강제 재연결 트리거 루프 — MAIX_DEBUG_STT_RECONNECT_SEC 환경변수 설정 시에만 활성화.
    /// 미설정/0이면 이 메서드는 호출되지 않으므로 프로덕션 동작에 영향 없음.
    /// 기존 재연결 경로(_ws?.Dispose() → ReceiveLoopAsync 재연결 블록) 를 그대로 재사용.
    /// L-380: PeriodicTimer 콜백 외부 try-catch. L-376: ct 취소 시 정상 종료.
    /// </summary>
    private async Task DebugForceReconnectLoopAsync(CancellationToken ct)
    {
        _log.Warn("[Realtime][DEBUG] 강제 재연결 루프 시작 — {Sec}초마다 WS Dispose로 재연결 유발", _debugReconnectSec);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_debugReconnectSec));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_ws == null || _ws.State != WebSocketState.Open)
                {
                    _log.Info("[Realtime][DEBUG] 강제 재연결 트리거 — WS 이미 닫힘, 스킵");
                    continue;
                }
                _log.Warn("[Realtime][DEBUG] 강제 재연결 트리거 발동 ({Sec}초 경과) — WS Dispose로 재연결 유발", _debugReconnectSec);
                try
                {
                    // 기존 재연결 경로 재사용:
                    // WS를 강제 Close한 뒤 null로 만들면 ReceiveLoopAsync 내부 루프가
                    // _ws?.State != Open 조건으로 빠져나와 재연결 블록(_auxCts 정리 → 새 WS 생성)으로 진입.
                    _ws.Dispose();
                    _ws = null;
                }
                catch (Exception ex)
                {
                    _log.Warn(ex, "[Realtime][DEBUG] 강제 재연결 트리거 WS Dispose 실패 (무시)");
                }
            }
        }
        catch (OperationCanceledException) { /* 정상 종료 — _auxCts 취소 시 */ }
        catch (Exception ex) { _log.Error(ex, "[Realtime][DEBUG] 강제 재연결 루프 오류"); }
        _log.Info("[Realtime][DEBUG] 강제 재연결 루프 종료");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuffer = new List<byte>();
        int reconnectAttempt = 0;

        while (!ct.IsCancellationRequested)
        {
            // 내부 수신 루프 — WebSocket이 열려 있는 동안 메시지 수신
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
                        goto exitLoop;
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

            // 내부 루프 종료 — 취소 또는 재연결 판단
            if (ct.IsCancellationRequested || _userRequestedStop)
                break;

            // 비정상 종료 감지 → 재연결 시도
            if (reconnectAttempt >= MaxReconnectAttempts)
            {
                _log.Warn("[RealtimeSTT] 최대 재연결 횟수({Max}) 초과 — 재연결 중단", MaxReconnectAttempts);
                break;
            }

            reconnectAttempt++;
            var delaySeconds = (int)Math.Pow(2, reconnectAttempt); // 2s / 4s / 8s
            _log.Warn("[RealtimeSTT] WebSocket 비정상 종료 감지 — {Attempt}/{Max}회 재연결 시도 ({Delay}s 후)",
                reconnectAttempt, MaxReconnectAttempts, delaySeconds);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // L-442 전략 swap 5단계: Unsubscribe → DisposeAsync → Factory.New → Subscribe → StartAsync
            // 재연결: 기존 보조 태스크 취소(Unsubscribe) → WS Cleanup(Dispose) → 새 WS + 세션 재설정(Factory.New+Subscribe+StartAsync)
            // 단, _cts는 외부(사용자) 취소 토큰 연결이므로 교체하지 않고 WS만 재생성
            try
            {
                _log.Info("[Realtime] 재연결 시작 — 기존 보조 태스크 정리");

                // 수정 1 (교착 해소): _auxCts 취소로 보조 태스크를 즉시 종료 신호 전달 후 await.
                // _cts는 사용자 취소 신호이므로 절대 취소하지 않음.
                // ghost loop 누적 방지: 옛 _auxCts 취소 → 새 _auxCts 재생성 → 새 보조 태스크에 전달.
                _auxCts?.Cancel();
                if (_silenceMonitorTask != null)
                {
                    try { await _silenceMonitorTask.ConfigureAwait(false); } catch { }
                    _silenceMonitorTask = null;
                }
                if (_manualCommitTask != null)
                {
                    try { await _manualCommitTask.ConfigureAwait(false); } catch { }
                    _manualCommitTask = null;
                }
                // [DEBUG] 강제 재연결 트리거 태스크도 _auxCts 취소로 함께 종료 대기 (누수 방지, L-376/L-388)
                if (_debugForceReconnectTask != null)
                {
                    try { await _debugForceReconnectTask.ConfigureAwait(false); } catch { }
                    _debugForceReconnectTask = null;
                }
                _auxCts?.Dispose();
                _auxCts = null;
                _log.Info("[Realtime] 보조 태스크 정리 완료");

                // 기존 WS Dispose
                _ws?.Dispose();
                _ws = null;

                if (ct.IsCancellationRequested || _userRequestedStop)
                    break;

                // 새 WebSocket 생성 및 연결
                var apiKey = _settings.AIProviders?.OpenAI?.ApiKey ?? string.Empty;
                var uri = _currentStrategy!.BuildConnectionUri();
                var newWs = new ClientWebSocket();
                newWs.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                await newWs.ConnectAsync(uri, ct).ConfigureAwait(false);
                _ws = newWs;
                _log.Info("[RealtimeSTT] 재연결 WebSocket 연결 성공 (시도 {Attempt}/{Max})", reconnectAttempt, MaxReconnectAttempts);
                _log.Warn("[RealtimeSTT] 재연결로 세션 재시작 — audio_start_ms가 리셋될 수 있음 (세그먼트 시간은 ViewModel의 RecordingDuration 앵커로 보정됨. 3초 주기 수동 commit도 동일하게 audio_start_ms를 리셋시킬 수 있음 — ManualCommitLoopAsync 로그 참조)");

                // session.update 재발송
                var sttLang = string.IsNullOrWhiteSpace(_settings.OaiRecording.SttLanguage) ? "ko" : _settings.OaiRecording.SttLanguage;
                var sessionPayload = _currentStrategy.BuildSessionUpdatePayload(
                    language: sttLang,
                    prompt: _settings.OaiRecording.SttPrompt ?? string.Empty,
                    serverVadEnabled: _settings.OaiRecording.ServerVadEnabled,
                    vadThreshold: _settings.OaiRecording.VadThreshold,
                    vadSilenceDurationMs: _settings.OaiRecording.VadSilenceDurationMs,
                    whisperDelay: _settings.OaiRecording.WhisperDelay
                );
                await SendJsonAsync(sessionPayload).ConfigureAwait(false);
                _log.Info("[RealtimeSTT] 재연결 session.update 재발송 완료");

                // 새 _auxCts 생성 후 보조 태스크 재시작 (ghost loop 누적 0 보장)
                _auxCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _silenceMonitorTask = SilenceMonitorLoopAsync(_auxCts.Token);
                if (_currentStrategy.RequiresManualCommit(_settings.OaiRecording.ServerVadEnabled))
                {
                    _audioAppendedSinceCommit = false;
                    _manualCommitTask = ManualCommitLoopAsync(_auxCts.Token);
                }

                // [DEBUG] 강제 재연결 트리거 재시작 — 재연결 후에도 동일한 주기로 계속 발동 (누적 방지됨)
                if (_debugReconnectSec > 0)
                {
                    _debugForceReconnectTask = DebugForceReconnectLoopAsync(_auxCts.Token);
                    _log.Warn("[Realtime][DEBUG] 강제 재연결 트리거 재시작 — MAIX_DEBUG_STT_RECONNECT_SEC={Sec}초", _debugReconnectSec);
                }

                // 2계층 VAD 카드 병합 상태 리셋 — 재연결 후 첫 delta가 이전 세션의 카드 키와
                // 잘못 병합되지 않도록 함 (계획서 §11 엣지케이스).
                _currentSpeechItemId = null;
                _lastDeltaAt = DateTime.MinValue;
                _itemIdToCardKey.Clear();
                _cardAccumTexts.Clear();
                _cardItemOrder.Clear();        // 추가 — 재연결 후 이전 세션 인터리브 잔존 방지
                _cardItemFinalTexts.Clear();   // 추가
                _cardEndTimes.Clear();

                // messageBuffer 초기화 후 내부 루프 재진입
                messageBuffer.Clear();
                _log.Info("[Realtime] 재연결 완료 — 수신 루프 재개 (병합 카드 상태 리셋 완료)");
                reconnectAttempt = 0; // 성공 시 재시도 카운터 초기화
            }
            catch (Exception ex)
            {
                _log.Error(ex, "[RealtimeSTT] 재연결 실패 (시도 {Attempt}/{Max})", reconnectAttempt, MaxReconnectAttempts);
                // 루프 계속하여 다음 재시도 또는 최대 초과 시 종료
            }
        }

        exitLoop:
        _log.Info("[RealtimeSTT] 수신 루프 종료");
    }

    // 카드의 현재 전체 텍스트 = 등장순 itemId별 (완료확정텍스트 우선, 없으면 진행중 delta버퍼) join.
    // 다중 item 인터리브 시 중복/진동/순서뒤섞임을 방지하는 근본 수정(단일 base 모델 폐기).
    private string RebuildCardText(string cardKey)
    {
        if (!_cardItemOrder.TryGetValue(cardKey, out var order)) return string.Empty;
        var sb = new System.Text.StringBuilder();
        List<string> snapshot;
        lock (order) { snapshot = new List<string>(order); }   // 인터리브 동시성 보호 — 스냅샷 후 lock 밖에서 join
        foreach (var iid in snapshot)
        {
            var part = _cardItemFinalTexts.TryGetValue(iid, out var ft) ? ft
                     : (_deltaBuffers.TryGetValue(iid, out var db) ? db : string.Empty);
            sb.Append(part);
        }
        return sb.ToString();
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

                // 3.1: 오디오 타임라인 기준 무음(턴 간 실제 무음) — [병합판정] gap의 벽시계 대체용으로 저장.
                // 최초 발화(_lastSpeechStoppedMs==0)는 무조건 새 카드로 처리하도록 double.MaxValue 유지.
                _lastTurnSilenceSec = _lastSpeechStoppedMs > 0 ? (startMs - _lastSpeechStoppedMs) / 1000.0 : double.MaxValue;

                // 묵음 구간 표시: 직전 speech_stopped 이후 시간 차이가 10초 이상이면 발화
                if (_lastSpeechStoppedMs > 0)
                {
                    var silenceSec = _lastTurnSilenceSec;
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
            else if (_currentStrategy != null && type == _currentStrategy.TranscriptionDeltaEventType)
            {
                // 발화 중 부분 transcript 점진 표시 — OpenAI item_id를 그대로 사용하여 비동기 매칭 어긋남 방지.
                // L-440: Strategy의 이벤트 타입(예: transcription 세션은 "...transcription.delta",
                // gpt-realtime-2 일반 세션은 "response.text.delta")으로 분기.
                if (root.TryGetProperty("delta", out var deltaProp))
                {
                    var deltaText = deltaProp.GetString() ?? string.Empty;
                    var openAiItemId = root.TryGetProperty("item_id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty;
                    if (!string.IsNullOrEmpty(deltaText) && !string.IsNullOrEmpty(openAiItemId))
                    {
                        // _deltaBuffers는 completed 이벤트 매칭용 — OpenAI item_id 키 그대로 유지 (회귀 방지).
                        var accum = _deltaBuffers.AddOrUpdate(openAiItemId, deltaText, (_, prev) => prev + deltaText);

                        // 2계층 VAD 카드 병합 판정(3.1): 오디오 타임라인 기준 턴 간 무음(_lastTurnSilenceSec)이 임계
                        // 미만이면 기존 카드 키 유지, 이상이면 새 카드 키 생성. 벽시계 gap(_lastDeltaAt 간격)은
                        // OpenAI 서버 committed→STT변환→delta스트리밍 왕복 지연(~2~2.5초)이 섞여 실제 무음이
                        // 아닌데도 새카드생성으로 오판정하므로 참고 로그로만 남기고 판정 기준에서 제외한다.
                        var now = DateTime.Now;
                        var threshold = Math.Max(_settings.OaiRecording.CardMergeSilenceThresholdSec, MinCardMergeSilenceThresholdSec);
                        var gap = _lastDeltaAt == DateTime.MinValue ? double.MaxValue : (now - _lastDeltaAt).TotalSeconds;
                        var turnSilence = _lastTurnSilenceSec;
                        // 같은 openAiItemId가 이어지는 delta(정상 진행 중인 발화)는 항상 병합 — 카드 분할 판정은
                        // "새 openAiItemId가 등장했을 때"만 오디오 타임라인 무음 기준으로 결정한다.
                        bool sameItem = _itemIdToCardKey.ContainsKey(openAiItemId);
                        bool merged = sameItem || (_currentSpeechItemId != null && turnSilence < threshold);
                        if (!merged)
                        {
                            _currentSpeechItemId = $"card_{now.Ticks}";
                        }
                        var cardKey = _currentSpeechItemId!;
                        _itemIdToCardKey[openAiItemId] = cardKey;
                        _lastDeltaAt = now;
                        // 카드별 시작시각 최초 1회 고정 캡처(H5 수정) — 이미 있으면 덮어쓰지 않음
                        _cardStartTimes.TryAdd(cardKey, _lastSpeechStartedMs);

                        _log.Info($"[병합판정] turnSilence={(turnSilence == double.MaxValue ? -1 : turnSilence):F2}s(오디오) gap={(gap == double.MaxValue ? -1 : gap):F2}s(벽시계참고) 임계={threshold:F2}s → {(merged ? "기존카드유지" : "새카드생성")} cardKey={cardKey}");

                        // 카드 키 기준 순서보존 재조립: 이 item을 카드의 등장순 리스트에 (최초 1회) 추가한 뒤
                        // 카드 전체를 등장순으로 재조립하여 발행한다(단일 base 모델의 중복/진동/순서뒤섞임 근본수정).
                        var order = _cardItemOrder.GetOrAdd(cardKey, _ => new List<string>());
                        lock (order) { if (!order.Contains(openAiItemId)) order.Add(openAiItemId); }
                        var cardAccum = RebuildCardText(cardKey);   // _deltaBuffers[openAiItemId]는 위에서 이미 accum 반영됨
                        _cardAccumTexts[cardKey] = cardAccum;
                        _log.Info($"[카드누적] cardKey={cardKey} openAiItemId={openAiItemId} sameItem={sameItem} merged={merged} itemCount={order.Count} deltaAccumLen={accum.Length} cardAccumLen={cardAccum.Length}");

                        // H5 수정: StartTime은 카드별 고정 시작시각(_cardStartTimes), EndTime은 현재 시점 값으로 전진
                        // 3.0 수정: end는 전역 _lastSpeechStoppedMs를 그대로 쓰지 않는다 — 새 카드 시작 직후
                        // 아직 이번 턴의 speech_stopped가 도착하지 않은 시점엔 직전 카드(이전 발화 턴)의
                        // 종료값을 그대로 반환해 end<start(음수 지속시간)가 발생한다. 카드 키로 스코프
                        // 격리한 _cardEndTimes에 Math.Max(start, candidate) 하한 보정 후 기록한다.
                        // 카드별 end는 해당 카드 내에서만 단조 전진(과거로 되돌아가지 않음) — 다른 카드의
                        // _lastSpeechStoppedMs 잔존값이 섞여도 기존 _cardEndTimes[cardKey]보다 과거면 무시.
                        var cardStartMs = _cardStartTimes.TryGetValue(cardKey, out var csm) ? csm : _lastSpeechStartedMs;
                        var candidateEndMs = _lastSpeechStoppedMs > 0 ? _lastSpeechStoppedMs : _lastSpeechStartedMs;
                        var cardEndMs = Math.Max(cardStartMs,
                            _cardEndTimes.TryGetValue(cardKey, out var prevEndMs) ? Math.Max(prevEndMs, candidateEndMs) : candidateEndMs);
                        _cardEndTimes[cardKey] = cardEndMs;
                        var ts = TimeSpan.FromMilliseconds(cardStartMs);
                        var tsEnd = TimeSpan.FromMilliseconds(cardEndMs);
                        _log.Info($"[OpenAi-Realtime] delta — text='{deltaText}' openAiItemId={openAiItemId} cardKey={cardKey} accum_len={accum.Length}");
                        _log.Info($"[STT시각] cardKey={cardKey} itemId={openAiItemId} evt=delta start={ts} end={tsEnd} rawStartMs={_lastSpeechStartedMs} rawStopMs={_lastSpeechStoppedMs} cardStartMs={cardStartMs}");
                        TranscriptSegmentUpdated?.Invoke(cardKey, ts, tsEnd, cardAccum);

                        // AC-017: 자동 말풍선 분리 (tier1: 강마침표 전용, tier2: 강+약 구분자)
                        if (_settings.OaiRecording.AutoSplitEnabled)
                        {
                            var primaryMin = _settings.OaiRecording.AutoSplitPrimaryMinChars;
                            var secondaryMin = _settings.OaiRecording.AutoSplitSecondaryMinChars;

                            bool isTier2 = accum.Length >= secondaryMin;
                            bool isTier1 = !isTier2 && accum.Length >= primaryMin;

                            if (isTier1 || isTier2)
                            {
                                // tier2: 강+약 구분자, tier1: 강마침표만 탐색
                                var separators = isTier2 ? _autoSplitAllSeparators : AutoSplitTerminators;
                                int splitIdx = -1;
                                for (int i = accum.Length - 1; i >= 0; i--)
                                {
                                    if (separators.Contains(accum[i]))
                                    {
                                        splitIdx = i;
                                        break;
                                    }
                                }
                                if (splitIdx >= 0)
                                {
                                    var splitText = accum[..(splitIdx + 1)];
                                    var remainder = accum[(splitIdx + 1)..];
                                    if (isTier2)
                                        _log.Info($"[AC017-AutoSplit-L2] 약구분자 분리 — len={splitText.Length}, lastChar={accum[splitIdx]}, remainder_len={remainder.Length}");
                                    else
                                        _log.Info($"[AC017-AutoSplit] 분리 발화 — len={splitText.Length}, lastChar={accum[splitIdx]}, remainder_len={remainder.Length}");
                                    try { TranscriptSegmentReceived?.Invoke(ts, splitText); }
                                    catch (Exception ex) { _log.Error(ex, "[AC017-AutoSplit] TranscriptSegmentReceived 발화 예외"); }
                                    _deltaBuffers[openAiItemId] = remainder;
                                }
                            }
                        }
                    }
                }
            }
            else if (_currentStrategy != null && type == _currentStrategy.TranscriptionCompletedEventType)
            {
                // L-440: Strategy의 이벤트 타입으로 분기 (transcription 세션은 "...transcription.completed",
                // gpt-realtime-2 일반 세션은 "response.output_item.done").
                // L-분기: gpt-realtime-2(response.text.done)는 최상위 text 필드, 나머지는 transcript 필드
                string text;
                string openAiItemId;
                if (type == "response.text.done")
                {
                    // gpt-realtime-2 (RealtimeGptReasoningStrategy) — text 최상위 필드 직접 읽기
                    text = root.TryGetProperty("text", out var t2) ? (t2.GetString() ?? string.Empty) : string.Empty;
                    openAiItemId = root.TryGetProperty("item_id", out var idProp2) ? (idProp2.GetString() ?? string.Empty) : string.Empty;
                }
                else
                {
                    // 기존 transcription 세션 (Whisper/Transcribe/Whisper1) — transcript 필드 (기존 동작 100% 보존)
                    text = root.TryGetProperty("transcript", out var tr) ? (tr.GetString() ?? string.Empty) : string.Empty;
                    // OpenAI item_id를 그대로 사용 (delta와 정확히 매칭 — 비동기 어긋남 차단)
                    openAiItemId = root.TryGetProperty("item_id", out var idProp) ? (idProp.GetString() ?? string.Empty) : string.Empty;
                }
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
                    // H5 수정: completed 시점도 카드별 고정 시작시각(_cardStartTimes)을 우선 사용
                    // 3.0 수정: end도 delta 처리부와 동일하게 카드별 _cardEndTimes + Math.Max 하한 보정 사용.
                    var completedCardKeyForTs = _itemIdToCardKey.TryGetValue(itemId, out var ckForTs) ? ckForTs : itemId;
                    var completedCardStartMs = _cardStartTimes.TryGetValue(completedCardKeyForTs, out var ccsm) ? ccsm : _lastSpeechStartedMs;
                    var completedCandidateEndMs = _lastSpeechStoppedMs > 0 ? _lastSpeechStoppedMs : _lastSpeechStartedMs;
                    var completedCardEndMs = Math.Max(completedCardStartMs,
                        _cardEndTimes.TryGetValue(completedCardKeyForTs, out var ceEnd) ? Math.Max(ceEnd, completedCandidateEndMs) : completedCandidateEndMs);
                    var ts = TimeSpan.FromMilliseconds(completedCardStartMs);
                    var tsEnd = TimeSpan.FromMilliseconds(completedCardEndMs);
                    _log.Info($"[STT시각] cardKey={completedCardKeyForTs} itemId={itemId} evt=completed start={ts} end={tsEnd} rawStartMs={_lastSpeechStartedMs} rawStopMs={_lastSpeechStoppedMs} cardStartMs={completedCardStartMs}");
                    // delta가 도착했으면 LiveSTT UI는 Updated로 final 교체 — 병합 카드 키로 발행(2계층 VAD 카드 병합).
                    // itemId → 카드 키 매핑이 없으면(delta 없이 바로 completed) itemId 자체를 카드 키로 사용.
                    if (_deltaBuffers.ContainsKey(itemId))
                    {
                        var completedCardKey = completedCardKeyForTs;
                        // 이 item의 확정 전사(text)를 등장순 위치에 반영(완료 후 _deltaBuffers 제거돼도 유지되도록 별도 맵).
                        _cardItemFinalTexts[itemId] = text;
                        // item이 아직 순서리스트에 없으면(=delta 없이 바로 completed 진입 경계) 추가.
                        var completedOrder = _cardItemOrder.GetOrAdd(completedCardKey, _ => new List<string>());
                        lock (completedOrder) { if (!completedOrder.Contains(itemId)) completedOrder.Add(itemId); }
                        var cardFinal = RebuildCardText(completedCardKey);   // 등장순 전체 재조립(중복/순서뒤섞임 제거)
                        _cardAccumTexts[completedCardKey] = cardFinal;
                        TranscriptSegmentUpdated?.Invoke(completedCardKey, ts, tsEnd, cardFinal);
                    }
                    // ★ TopicExtractor/MinuteSummary 전달은 항상 Received로 한 번 더 발화 (delta 유무 무관)
                    // 이유: Updated 핸들러는 LiveSTTSegments UI만 갱신하므로 텍스트 통계 누락 방지
                    TranscriptSegmentReceived?.Invoke(ts, text);
                    _deltaBuffers.TryRemove(itemId, out _);
                    _itemIdToCardKey.TryRemove(itemId, out _);
                    // 카드가 완전히 종료됐는지(다른 itemId가 더 이상 이 cardKey를 참조하지 않는지) 확인 후 시작시각 정리
                    if (!_itemIdToCardKey.Values.Contains(completedCardKeyForTs))
                    {
                        _cardStartTimes.TryRemove(completedCardKeyForTs, out _);
                        _cardEndTimes.TryRemove(completedCardKeyForTs, out _);
                        _cardAccumTexts.TryRemove(completedCardKeyForTs, out _);
                        // 카드 완전종료 → 순서리스트 + 그 카드에 속한 item들의 확정텍스트도 정리.
                        if (_cardItemOrder.TryRemove(completedCardKeyForTs, out var doneOrder))
                        {
                            lock (doneOrder) { foreach (var iid in doneOrder) _cardItemFinalTexts.TryRemove(iid, out _); }
                        }
                    }
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
        // L-451: WaitAsync/SendAsync에 취소 토큰 전달 — 소켓 stall 시 StopAsync hang 방지.
        var ct = _cts?.Token ?? CancellationToken.None;
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
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
        _auxCts?.Dispose();
        _auxCts = null;
        _cts?.Dispose();
        _cts = null;
        _receiveTask = null;
        _silenceMonitorTask = null;
        _manualCommitTask = null;
        _debugForceReconnectTask = null;
        _currentStrategy = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _auxCts?.Cancel();
        _cts?.Cancel();
        Cleanup();
        // L-376: SemaphoreSlim은 IDisposable — 필드 보유 시 Dispose에서 해제.
        _sendLock.Dispose();
    }
}
