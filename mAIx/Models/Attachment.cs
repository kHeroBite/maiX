using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mAIx.Models;

/// <summary>
/// 첨부파일 모델 - 이메일 첨부파일과 변환 상태 관리
/// </summary>
public class Attachment
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 이메일 FK
    /// </summary>
    [Required]
    public int EmailId { get; set; }

    /// <summary>
    /// 첨부파일 이름
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// MIME 타입 (application/pdf, image/png 등)
    /// </summary>
    [MaxLength(200)]
    public string? ContentType { get; set; }

    /// <summary>
    /// 파일 크기 (바이트)
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 로컬 저장 경로
    /// </summary>
    [MaxLength(1000)]
    public string? LocalPath { get; set; }

    /// <summary>
    /// 원본 파일 경로 (다운로드 전 원격 경로 또는 임시 경로)
    /// </summary>
    [MaxLength(1000)]
    public string? OriginalFile { get; set; }

    /// <summary>
    /// Markdown 변환 파일 경로
    /// </summary>
    [MaxLength(1000)]
    public string? MarkdownPath { get; set; }

    /// <summary>
    /// Markdown 변환 내용 (DB에 직접 저장)
    /// </summary>
    public string? MarkdownContent { get; set; }

    /// <summary>
    /// 변환 상태 (pending, converting, completed, failed, skipped)
    /// </summary>
    [MaxLength(20)]
    public string ConversionStatus { get; set; } = "pending";

    /// <summary>
    /// 사용된 변환기 (markitdown, docling, none)
    /// </summary>
    [MaxLength(50)]
    public string? ConverterUsed { get; set; }

    /// <summary>
    /// 파일 만료 시간 (임시 파일 정리용)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    // ===== 네비게이션 프로퍼티 =====

    /// <summary>
    /// 이메일 참조
    /// </summary>
    [ForeignKey(nameof(EmailId))]
    public virtual Email? Email { get; set; }
}
