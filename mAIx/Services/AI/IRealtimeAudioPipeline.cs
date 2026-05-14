// 두 모드 공통 인터페이스 - STT + 1분요약 + 감성 통합 파이프라인
using System;
using System.Threading;
using System.Threading.Tasks;
using mAIx.Models;

namespace mAIx.Services.AI;

/// <summary>
/// Legacy / Unified 두 파이프라인 모드의 공통 인터페이스.
/// OneNoteViewModel은 이 인터페이스만 참조하여 모드 전환을 투명하게 처리.
/// </summary>
public interface IRealtimeAudioPipeline : IAsyncDisposable
{
    // ────────── 이벤트 ──────────

    /// <summary>STT 세그먼트 수신 (발화 완료 시점). TimeSpan=세그먼트 시작, string=전사 텍스트.</summary>
    event Action<TimeSpan, string>? TranscriptSegmentReceived;

    /// <summary>STT 세그먼트 업데이트 (item_id, 시작, 종료, 텍스트).</summary>
    event Action<string, TimeSpan, TimeSpan, string>? TranscriptSegmentUpdated;

    /// <summary>STT 세그먼트 제거 (item_id).</summary>
    event Action<string>? TranscriptSegmentRemoved;

    /// <summary>
    /// 1분 요약 생성 완료 — entry.Sentiment 포함 (SentimentEnabled=true 시).
    /// null Sentiment는 UI에서 회색으로 표시.
    /// </summary>
    event Action<MinuteSummaryEntry>? MinuteSummaryCreated;

    /// <summary>
    /// 파이프라인 폴백 발생 — Unified → Legacy 자동 전환 알림.
    /// 인자는 전환된 새 모드.
    /// </summary>
    event Action<AudioPipelineMode>? PipelineFallback;

    /// <summary>파이프라인 에러 발생 (메시지 문자열).</summary>
    event Action<string>? ErrorOccurred;

    // ────────── 속성 ──────────

    /// <summary>현재 파이프라인 모드.</summary>
    AudioPipelineMode Mode { get; }

    /// <summary>현재 파이프라인이 활성(녹음 중)이면 true.</summary>
    bool IsActive { get; }

    // ────────── 메서드 ──────────

    /// <summary>파이프라인 시작 (WebSocket 연결 + 서비스 초기화).</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>PCM16 오디오 청크 전송.</summary>
    Task SendAudioChunkAsync(byte[] pcmData, TimeSpan chunkStartTime);

    /// <summary>파이프라인 중단 및 정리.</summary>
    Task StopAsync();
}
