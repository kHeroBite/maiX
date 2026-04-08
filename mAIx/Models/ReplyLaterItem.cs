using System;
using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// Reply Later 큐 항목 — 나중에 답장할 이메일
/// </summary>
public class ReplyLaterItem
{
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string EmailId { get; set; } = "";      // Graph API 메일 ID

    [MaxLength(500)]
    public string Subject { get; set; } = "";

    [MaxLength(255)]
    public string SenderEmail { get; set; } = "";

    public DateTime? RemindAt { get; set; }        // null이면 알림 없음
    public bool IsCompleted { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}
