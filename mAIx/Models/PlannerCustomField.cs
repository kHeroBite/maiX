using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// Planner 커스텀 필드 — 작업에 추가 메타데이터 저장
/// </summary>
public class PlannerCustomField
{
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string TaskId { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string FieldName { get; set; } = "";

    [MaxLength(1000)]
    public string FieldValue { get; set; } = "";

    /// <summary>
    /// 필드 타입: text, number, date, checkbox
    /// </summary>
    [MaxLength(20)]
    public string FieldType { get; set; } = "text";
}
