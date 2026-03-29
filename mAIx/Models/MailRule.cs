using System;
using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// 메일 규칙 모델 - 조건 기반 자동 메일 처리 규칙
/// </summary>
public class MailRule
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 규칙 이름
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 조건 타입 (FromContains/SubjectContains/HasAttachment/AiCategoryEquals/ToContains)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ConditionType { get; set; } = string.Empty;

    /// <summary>
    /// 조건 값 (HasAttachment 등 값 불필요 시 null)
    /// </summary>
    [MaxLength(500)]
    public string? ConditionValue { get; set; }

    /// <summary>
    /// 액션 타입 (MoveToFolder/SetCategory/SetFlag/MarkAsRead/Delete)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// 액션 값 (폴더명, 카테고리명 등 — 액션에 따라 null 가능)
    /// </summary>
    [MaxLength(500)]
    public string? ActionValue { get; set; }

    /// <summary>
    /// 규칙 활성화 여부
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 실행 우선순위 — 낮을수록 먼저 실행
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// 계정별 규칙 (null = 전체 계정 적용)
    /// </summary>
    [MaxLength(500)]
    public string? AccountEmail { get; set; }

    /// <summary>
    /// 규칙 생성 시각
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 규칙 최종 수정 시각
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
