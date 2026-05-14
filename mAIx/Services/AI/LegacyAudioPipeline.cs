// 모드 A - 기존 STT/요약 서비스를 wrap하고 신규 감성 서비스를 hook으로 호출
using System;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;
using mAIx.Models.Settings;
using mAIx.Services.Storage;
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// Legacy 오디오 파이프라인 — 기존 OpenAiRealtimeSttService + MinuteSummaryService를 wrap하고
/// SentimentAnalysisService를 hook으로 호출하여 MinuteSummaryEntry에 감성 점수를 채운다.
/// IRealtimeAudioPipeline 인터페이스를 구현하여 OneNoteViewModel에서 투명하게 사용 가능.
/// </summary>
public sealed class LegacyAudioPipeline : IRealtimeAudioPipeline
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly IOpenAiRealtimeSttService _stt;
    private readonly IMinuteSummaryService _minuteSummary;
    private readonly ISentimentAnalysisService? _sentiment;
    private readonly AppSettingsManager _settings;

    private bool _isActive;
    private bool _disposed;

    // ────────── 이벤트 (IRealtimeAudioPipeline) ──────────

    public event Action<TimeSpan, string>? TranscriptSegmentReceived;
    public event Action<string, TimeSpan, TimeSpan, string>? TranscriptSegmentUpdated;
    public event Action<string>? TranscriptSegmentRemoved;
    public event Action<MinuteSummaryEntry>? MinuteSummaryCreated;
    public event Action<AudioPipelineMode>? PipelineFallback;
    public event Action<string>? ErrorOccurred;

    // ────────── 속성 (IRealtimeAudioPipeline) ──────────

    public AudioPipelineMode Mode => AudioPipelineMode.Legacy;
    public bool IsActive => _isActive;

    public LegacyAudioPipeline(
        IOpenAiRealtimeSttService stt,
        IMinuteSummaryService minuteSummary,
        ISentimentAnalysisService? sentiment,
        AppSettingsManager settings)
    {
        _stt = stt ?? throw new ArgumentNullException(nameof(stt));
        _minuteSummary = minuteSummary ?? throw new ArgumentNullException(nameof(minuteSummary));
        _sentiment = sentiment;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // ────────── 메서드 (IRealtimeAudioPipeline) ──────────

    /// <summary>
    /// STT + 1분 요약 서비스 시작 및 이벤트 구독.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        // 외부 try-catch — 시작 실패 시 호출자에게 예외 전파 (L-377)
        try
        {
            _log.Info("LegacyAudioPipeline 시작");

            SubscribeEvents();

            // STT 먼저 시작, 이후 MinuteSummary 시작
            await _stt.StartAsync(ct).ConfigureAwait(false);
            await _minuteSummary.StartAsync(ct).ConfigureAwait(false);

            _isActive = true;
            _log.Info("LegacyAudioPipeline 시작 완료 — STT + MinuteSummary 모두 활성");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "LegacyAudioPipeline 시작 실패");
            UnsubscribeEvents();
            throw;
        }
    }

    /// <summary>
    /// PCM16 오디오 청크를 STT 서비스로 위임.
    /// </summary>
    public async Task SendAudioChunkAsync(byte[] pcmData, TimeSpan chunkStartTime)
    {
        // 외부 try-catch (L-377)
        try
        {
            await _stt.SendAudioChunkAsync(pcmData, chunkStartTime).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "LegacyAudioPipeline 오디오 청크 전송 실패");
            ErrorOccurred?.Invoke($"오디오 청크 전송 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 이벤트 구독 해제 후 STT + 1분 요약 서비스 중단.
    /// </summary>
    public async Task StopAsync()
    {
        // 외부 try-catch (L-377)
        try
        {
            _log.Info("LegacyAudioPipeline 중단 시작");
            _isActive = false;

            UnsubscribeEvents();

            try { await _minuteSummary.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log.Warn(ex, "MinuteSummaryService 중단 오류 (무시)"); }

            try { await _stt.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log.Warn(ex, "OpenAiRealtimeSttService 중단 오류 (무시)"); }

            _log.Info("LegacyAudioPipeline 중단 완료");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "LegacyAudioPipeline StopAsync 예외");
        }
    }

    /// <summary>
    /// IAsyncDisposable 구현 — 정리 후 내부 서비스 Dispose.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_isActive)
                await StopAsync().ConfigureAwait(false);

            _stt.Dispose();
            _minuteSummary.Dispose();
            _sentiment?.Dispose();

            _log.Debug("LegacyAudioPipeline Dispose 완료");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "LegacyAudioPipeline DisposeAsync 예외");
        }
    }

    // ────────── 이벤트 구독/해제 ──────────

    private void SubscribeEvents()
    {
        _stt.TranscriptSegmentReceived += OnSttTranscriptSegmentReceived;
        _stt.TranscriptSegmentUpdated += OnSttTranscriptSegmentUpdated;
        _stt.TranscriptSegmentRemoved += OnSttTranscriptSegmentRemoved;
        _minuteSummary.MinuteSummaryCreated += OnMinuteSummaryCreatedInternal;
    }

    private void UnsubscribeEvents()
    {
        _stt.TranscriptSegmentReceived -= OnSttTranscriptSegmentReceived;
        _stt.TranscriptSegmentUpdated -= OnSttTranscriptSegmentUpdated;
        _stt.TranscriptSegmentRemoved -= OnSttTranscriptSegmentRemoved;
        _minuteSummary.MinuteSummaryCreated -= OnMinuteSummaryCreatedInternal;
    }

    // ────────── 이벤트 핸들러 ──────────

    /// <summary>
    /// STT 세그먼트 수신 시 두 경로로 분기:
    /// 1. IRealtimeAudioPipeline.TranscriptSegmentReceived 재발행 (OneNoteViewModel → UI)
    /// 2. MinuteSummaryService.AddTranscriptAsync 비동기 호출 (버퍼 누적)
    /// </summary>
    private async void OnSttTranscriptSegmentReceived(TimeSpan startTime, string text)
    {
        // 외부 try-catch — async void 핸들러 전체 보호 (L-377, L-380)
        try
        {
            // 1. 재발행 (즉시)
            TranscriptSegmentReceived?.Invoke(startTime, text);

            // 2. MinuteSummary 버퍼에 추가 (비동기 — 예외 격리)
            try
            {
                await _minuteSummary.AddTranscriptAsync(text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "MinuteSummaryService AddTranscript 실패 — STT 계속 진행");
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "OnSttTranscriptSegmentReceived 처리 실패");
        }
    }

    private void OnSttTranscriptSegmentUpdated(string itemId, TimeSpan startTime, TimeSpan endTime, string text)
    {
        try
        {
            TranscriptSegmentUpdated?.Invoke(itemId, startTime, endTime, text);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "OnSttTranscriptSegmentUpdated 재발행 실패");
        }
    }

    private void OnSttTranscriptSegmentRemoved(string itemId)
    {
        try
        {
            TranscriptSegmentRemoved?.Invoke(itemId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "OnSttTranscriptSegmentRemoved 재발행 실패");
        }
    }

    /// <summary>
    /// 1분 요약 생성 완료 이벤트 수신 시:
    /// SentimentEnabled이고 _sentiment != null이면 감성 분석 후 entry.Sentiment 채움.
    /// 이후 IRealtimeAudioPipeline.MinuteSummaryCreated 재발행.
    /// </summary>
    private async void OnMinuteSummaryCreatedInternal(MinuteSummaryEntry entry)
    {
        // 외부 try-catch — async void 핸들러 전체 보호 (L-377)
        try
        {
            entry.CreatedByMode = AudioPipelineMode.Legacy;

            if (_settings.OaiRecording.SentimentEnabled && _sentiment != null)
            {
                try
                {
                    var sentimentText = string.IsNullOrWhiteSpace(entry.SummaryText)
                        ? entry.Topic
                        : entry.SummaryText;

                    var result = await _sentiment.AnalyzeAsync(sentimentText).ConfigureAwait(false);
                    entry.Sentiment = result;

                    if (result != null)
                        _log.Info("감성 분석 완료 — Sentiment Score={0}, Label={1}", result.Score, result.Label);
                    else
                        _log.Warn("감성 분석 반환값 null — 중립 표시");
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "감성 분석 실패 — Sentiment=null으로 계속");
                    entry.Sentiment = null;
                }
            }

            // 재발행 (Sentiment 채운 후)
            MinuteSummaryCreated?.Invoke(entry);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "OnMinuteSummaryCreatedInternal 처리 실패");
        }
    }
}
