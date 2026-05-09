// 1분 요약 엔트리 (내부 압축용 — 누적요약 입력 자료)
using System;

namespace mAIx.Models;

/// <summary>
/// 1분 단위 요약 엔트리 — 누적 요약 서비스의 입력 자료
/// </summary>
public class MinuteSummaryEntry
{
    /// <summary>
    /// 순번 (0부터 시작, 시간순)
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// 해당 분 시작 시각 (녹음 시작 기준 상대 시간)
    /// </summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>
    /// 해당 분 종료 시각
    /// </summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// LLM 생성 1분 요약 텍스트
    /// </summary>
    public string SummaryText { get; set; } = string.Empty;

    /// <summary>
    /// 엔트리 생성 일시
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
