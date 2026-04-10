using System;

namespace mAIx.Models;

/// <summary>
/// Teams 채널 노트 모델 - 채널별 메모/위키 항목 저장
/// </summary>
public class ChannelNote
{
    /// <summary>
    /// 기본 키
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Teams 채널 ID
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Teams 팀 ID
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// 노트 제목 (기본값: "제목 없음")
    /// </summary>
    public string Title { get; set; } = "제목 없음";

    /// <summary>
    /// 노트 내용 (마크다운/서식 텍스트)
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 생성 일시
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 마지막 수정 일시
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 작성자 (사용자 ID 또는 이메일)
    /// </summary>
    public string? CreatedBy { get; set; }
}
