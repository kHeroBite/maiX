using System;
using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// 스크리너 항목 — 발신자 차단/허용 규칙
/// </summary>
public class ScreenerEntry
{
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string SenderEmail { get; set; } = "";

    [MaxLength(100)]
    public string SenderName { get; set; } = "";

    /// <summary>blocked, allowed</summary>
    public string Action { get; set; } = "blocked";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
