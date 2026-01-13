using System;
using System.ComponentModel.DataAnnotations;

namespace mailX.Models;

/// <summary>
/// 문서 변환기 설정 모델 - 확장자별 선택된 변환기 저장
/// </summary>
public class ConverterSetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 파일 확장자 (.hwp, .docx, .pdf 등)
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Extension { get; set; } = string.Empty;

    /// <summary>
    /// 선택된 변환기 이름 (OpenMcdfHwp, Pandoc, PdfPig 등)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string SelectedConverter { get; set; } = string.Empty;

    /// <summary>
    /// 마지막 수정 시간
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 활성화 여부 (비활성화 시 기본 변환기 사용)
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
