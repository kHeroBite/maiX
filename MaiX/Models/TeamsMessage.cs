using System;
using System.ComponentModel.DataAnnotations;

namespace MaiX.Models;

/// <summary>
/// Teams 메시지 모델 - Microsoft Teams 메시지 연동
/// </summary>
public class TeamsMessage
{
    /// <summary>
    /// Graph API 메시지 ID (문자열 PK)
    /// </summary>
    [Key]
    [MaxLength(500)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 채팅 ID
    /// </summary>
    [MaxLength(500)]
    public string? ChatId { get; set; }

    /// <summary>
    /// 메시지 내용
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 발신자
    /// </summary>
    [MaxLength(500)]
    public string? FromUser { get; set; }

    /// <summary>
    /// 연결된 이메일 ID (이메일에서 Teams 스레드로 이동 시)
    /// </summary>
    public int? LinkedEmailId { get; set; }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime? CreatedDateTime { get; set; }
}
