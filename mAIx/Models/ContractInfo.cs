using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mAIx.Models;

/// <summary>
/// 계약정보 모델 - AI가 이메일에서 추출한 계약/사업 관련 정보
/// </summary>
public class ContractInfo
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 이메일 FK (1:1 관계, unique)
    /// </summary>
    [Required]
    public int EmailId { get; set; }

    /// <summary>
    /// 계약 금액 (원 단위)
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// 계약 기간 (예: "2025.01 ~ 2025.12", "6개월")
    /// </summary>
    [MaxLength(200)]
    public string? Period { get; set; }

    /// <summary>
    /// 투입 공수 (Man-Month)
    /// </summary>
    public decimal? ManMonth { get; set; }

    /// <summary>
    /// 근무 위치
    /// </summary>
    [MaxLength(500)]
    public string? Location { get; set; }

    /// <summary>
    /// 원격근무 가능 여부
    /// </summary>
    public bool? IsRemote { get; set; }

    /// <summary>
    /// 업무 범위 / 프로젝트 설명
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// 사업 유형 (SI, SM, 컨설팅, 솔루션 등)
    /// </summary>
    [MaxLength(100)]
    public string? BusinessType { get; set; }

    // ===== 네비게이션 프로퍼티 =====

    /// <summary>
    /// 이메일 참조
    /// </summary>
    [ForeignKey(nameof(EmailId))]
    public virtual Email? Email { get; set; }
}
