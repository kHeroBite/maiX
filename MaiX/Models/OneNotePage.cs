using System;
using System.ComponentModel.DataAnnotations;

namespace MaiX.Models;

/// <summary>
/// OneNote 페이지 모델 - Microsoft OneNote 연동
/// </summary>
public class OneNotePage
{
    /// <summary>
    /// Graph API 페이지 ID (문자열 PK)
    /// </summary>
    [Key]
    [MaxLength(500)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 섹션 ID
    /// </summary>
    [MaxLength(500)]
    public string? SectionId { get; set; }

    /// <summary>
    /// 페이지 제목
    /// </summary>
    [MaxLength(500)]
    public string? Title { get; set; }

    /// <summary>
    /// 콘텐츠 URL (OneNote 페이지 내용 가져오기용)
    /// </summary>
    [MaxLength(2000)]
    public string? ContentUrl { get; set; }

    /// <summary>
    /// 연결된 이메일 ID (이메일 저장 시)
    /// </summary>
    public int? LinkedEmailId { get; set; }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime? CreatedDateTime { get; set; }
}
