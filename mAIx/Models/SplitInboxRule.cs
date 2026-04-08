using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// Split Inbox 규칙 — 받은편지함을 커스텀 탭으로 분류
/// </summary>
public class SplitInboxRule
{
    public int Id { get; set; }

    /// <summary>탭 이름 (예: "뉴스레터", "팀 메일")</summary>
    [Required, MaxLength(100)]
    public string TabName { get; set; } = "";

    /// <summary>탭 색상 (HEX 코드)</summary>
    [MaxLength(20)]
    public string Color { get; set; } = "#0078D4";

    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>JSON 직렬화된 매처 목록 (SenderDomain, SenderEmail, SubjectContains, HasLabel)</summary>
    public string MatchersJson { get; set; } = "[]";
}
