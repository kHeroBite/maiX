using System;
using System.Collections.Generic;

namespace mailX.Models;

/// <summary>
/// 녹음 AI 요약 결과
/// </summary>
public class RecordingSummary
{
    /// <summary>
    /// 녹음 파일 경로
    /// </summary>
    public string AudioFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 요약 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 전체 요약 텍스트
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 핵심 포인트 목록
    /// </summary>
    public List<string> KeyPoints { get; set; } = new();

    /// <summary>
    /// 액션 아이템 목록
    /// </summary>
    public List<ActionItem> ActionItems { get; set; } = new();

    /// <summary>
    /// 인식된 참여자 목록
    /// </summary>
    public List<string> Participants { get; set; } = new();

    /// <summary>
    /// 주요 주제/키워드
    /// </summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// 회의/녹음 유형 (예: "회의", "강의", "인터뷰")
    /// </summary>
    public string RecordingType { get; set; } = string.Empty;

    /// <summary>
    /// 분위기/톤 (예: "긍정적", "건설적")
    /// </summary>
    public string Sentiment { get; set; } = string.Empty;

    /// <summary>
    /// 사용된 AI 모델
    /// </summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 원본 STT 결과 경로
    /// </summary>
    public string? SourceSTTPath { get; set; }

    /// <summary>
    /// 핵심 포인트 개수
    /// </summary>
    public int KeyPointsCount => KeyPoints.Count;

    /// <summary>
    /// 액션 아이템 개수
    /// </summary>
    public int ActionItemsCount => ActionItems.Count;
}

/// <summary>
/// 액션 아이템 (할 일)
/// </summary>
public class ActionItem
{
    /// <summary>
    /// 액션 내용
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 담당자 (화자 기반)
    /// </summary>
    public string? Assignee { get; set; }

    /// <summary>
    /// 기한 (언급된 경우)
    /// </summary>
    public string? DueDate { get; set; }

    /// <summary>
    /// 우선순위 (높음/중간/낮음)
    /// </summary>
    public string Priority { get; set; } = "중간";

    /// <summary>
    /// 완료 여부
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 표시 문자열
    /// </summary>
    public string DisplayText
    {
        get
        {
            var parts = new List<string> { Description };
            if (!string.IsNullOrEmpty(Assignee))
                parts.Add($"[{Assignee}]");
            if (!string.IsNullOrEmpty(DueDate))
                parts.Add($"({DueDate})");
            return string.Join(" ", parts);
        }
    }
}
