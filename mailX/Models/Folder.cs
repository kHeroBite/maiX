using System.ComponentModel.DataAnnotations;

namespace mailX.Models;

/// <summary>
/// 폴더 모델 - Outlook 메일 폴더 정보
/// </summary>
public class Folder
{
    /// <summary>
    /// Graph API 폴더 ID (문자열 PK)
    /// </summary>
    [Key]
    [MaxLength(500)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 폴더 표시 이름
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 상위 폴더 ID
    /// </summary>
    [MaxLength(500)]
    public string? ParentFolderId { get; set; }

    /// <summary>
    /// 전체 아이템 수
    /// </summary>
    public int TotalItemCount { get; set; }

    /// <summary>
    /// 읽지 않은 아이템 수
    /// </summary>
    public int UnreadItemCount { get; set; }

    /// <summary>
    /// 소속 계정 이메일
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string AccountEmail { get; set; } = string.Empty;
}
