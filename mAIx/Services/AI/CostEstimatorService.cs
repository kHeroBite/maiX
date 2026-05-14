// 분당 추정 비용 계산 - 모드별/모델별 토큰 단가 적용
using NLog;

namespace mAIx.Services.AI;

/// <summary>
/// 오디오 파이프라인 모드별 분당 예상 비용을 계산하는 서비스.
/// 모드/모델 변경 시 즉시 재계산하여 UI에 표시.
/// </summary>
public sealed class CostEstimatorService
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    // ────────── 단가 상수 (OpenAI API 공식 단가 기준, USD/분) ──────────

    /// <summary>Legacy 모드 분당 비용: gpt-4o-mini-transcribe STT + gpt-4o-mini 요약 + 감성 분석</summary>
    private const decimal PriceLegacy = 0.00240m;   // $0.0019 STT + $0.0005 요약/감성 (gpt-4o-mini 기준)

    /// <summary>gpt-realtime 분당 비용 (audio input $32/1M 기준, 약 10초 오디오/분 가정)</summary>
    private const decimal PriceGptRealtime = 0.00500m;

    /// <summary>gpt-realtime-2 분당 비용 (text output $24/1M, audio input $32/1M 기준)</summary>
    private const decimal PriceGptRealtime2 = 0.00800m;

    /// <summary>gpt-realtime-mini 분당 비용 (저비용 모델 추정 — 공식 단가 확인 시 보정 필요)</summary>
    private const decimal PriceGptRealtimeMini = 0.00300m;

    /// <summary>
    /// 모드 및 Unified 모델 ID에 따른 분당 예상 비용(USD)을 반환.
    /// </summary>
    /// <param name="mode">파이프라인 모드 (Legacy / Unified)</param>
    /// <param name="unifiedModel">Unified 모드 모델 ID (Legacy일 때는 무시)</param>
    /// <returns>분당 예상 비용 (USD)</returns>
    public decimal EstimateCostPerMinute(AudioPipelineMode mode, string unifiedModel)
    {
        if (mode == AudioPipelineMode.Legacy)
        {
            _log.Debug("비용 계산 — Legacy 모드: {0:F5} USD/분", PriceLegacy);
            return PriceLegacy;
        }

        // Unified 모드 — 모델별 분기
        var cost = unifiedModel switch
        {
            "gpt-realtime"      => PriceGptRealtime,
            "gpt-realtime-2"    => PriceGptRealtime2,
            "gpt-realtime-mini" => PriceGptRealtimeMini,
            _                   => PriceGptRealtime,  // 알 수 없는 모델은 기본값
        };

        _log.Debug("비용 계산 — Unified({0}): {1:F5} USD/분", unifiedModel, cost);
        return cost;
    }

    /// <summary>
    /// 분당 비용을 사용자 표시용 문자열로 포맷.
    /// 예: "$0.0050/분"
    /// </summary>
    public string FormatPerMinute(decimal usd) => $"${usd:F4}/분";

    /// <summary>
    /// 모드+모델 기준의 전체 표시 문자열 (UI TextBlock 바인딩용).
    /// 예: "예상: $0.0050/분 (Unified)"
    /// </summary>
    public string FormatDisplay(AudioPipelineMode mode, string unifiedModel)
    {
        var cost = EstimateCostPerMinute(mode, unifiedModel);
        return $"예상: {FormatPerMinute(cost)} ({mode})";
    }
}
