using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// 프롬프트 모델 - AI 프롬프트 템플릿 관리
/// </summary>
public class Prompt
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 프롬프트 고유 키 (예: "email_summary", "priority_analysis")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string PromptKey { get; set; } = string.Empty;

    /// <summary>
    /// 카테고리 (analysis, summary, extraction, generation 등)
    /// </summary>
    [MaxLength(50)]
    public string? Category { get; set; }

    /// <summary>
    /// 프롬프트 표시 이름
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 프롬프트 템플릿 ({{variable}} 형식 치환자 포함)
    /// </summary>
    [Required]
    public string Template { get; set; } = string.Empty;

    /// <summary>
    /// 사용 가능한 변수 목록 (JSON 배열)
    /// 예: ["subject", "body", "from", "attachments"]
    /// </summary>
    public string? Variables { get; set; }

    /// <summary>
    /// 시스템 프롬프트 여부 (수정 불가)
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// 활성화 여부
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // ===== 네비게이션 프로퍼티 =====

    /// <summary>
    /// 테스트 이력
    /// </summary>
    public virtual ICollection<PromptTestHistory> TestHistories { get; set; } = new List<PromptTestHistory>();
}
