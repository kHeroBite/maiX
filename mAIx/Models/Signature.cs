using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// 서명 모델 - 이메일 서명 템플릿
/// </summary>
public class Signature
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 서명 이름 (예: "기본 서명", "공식 서명")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// HTML 형식 서명 내용
    /// </summary>
    public string? HtmlContent { get; set; }

    /// <summary>
    /// 일반 텍스트 형식 서명 내용
    /// </summary>
    public string? PlainTextContent { get; set; }

    /// <summary>
    /// 기본 서명 여부
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 소속 계정 이메일
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string AccountEmail { get; set; } = string.Empty;
}
