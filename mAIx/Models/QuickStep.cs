using System;
using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// Quick Step — 반복 작업 자동화 규칙
/// </summary>
public class QuickStep
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(255)]
    public string Description { get; set; } = "";

    /// <summary>
    /// JSON 직렬화된 액션 목록 (MoveToFolder, MarkAsRead, Delete, Flag, Category 등)
    /// </summary>
    public string ActionsJson { get; set; } = "[]";

    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
