// 1분 요약 엔트리 (내부 압축용 — 누적요약 입력 자료)
using System;
using System.Collections.Generic;

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
    /// 핵심요약 네비게이션 표시용 주제어 (5~20자, LLM JSON 응답에서 추출)
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// STT 화면 하이라이트용 핵심 키워드 목록 (LLM JSON 응답 keywords 배열에서 추출).
    /// 구버전 .realtime.json 역직렬화 시 누락되면 빈 목록 (graceful).
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// 묵음 구간 엔트리 여부 — true 시 회색 표시 (컨버터 연동).
    /// 구버전 .realtime.json 역직렬화 시 누락되면 기본값 false (graceful).
    /// </summary>
    public bool IsSilence { get; set; } = false;

    /// <summary>
    /// 엔트리 생성 일시
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// UI 표시용 시간 범위 (mm:ss ~ mm:ss)
    /// </summary>
    public string TimeRangeDisplay =>
        $"{(int)StartTime.TotalMinutes:D2}:{StartTime.Seconds:D2} ~ {(int)EndTime.TotalMinutes:D2}:{EndTime.Seconds:D2}";
}
