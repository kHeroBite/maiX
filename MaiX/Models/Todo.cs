using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MaiX.Models;

/// <summary>
/// TODO 모델 - AI가 이메일에서 추출한 할 일 항목
/// </summary>
public class Todo
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 이메일 FK
    /// </summary>
    [Required]
    public int EmailId { get; set; }

    /// <summary>
    /// TODO 내용
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 마감일
    /// </summary>
    public DateTime? DueDate { get; set; }

    /// <summary>
    /// 상태 (pending, in_progress, completed, cancelled)
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// 우선순위 (1-5, 1이 가장 높음)
    /// </summary>
    public int Priority { get; set; } = 3;

    // ===== 네비게이션 프로퍼티 =====

    /// <summary>
    /// 이메일 참조
    /// </summary>
    [ForeignKey(nameof(EmailId))]
    public virtual Email? Email { get; set; }
}
