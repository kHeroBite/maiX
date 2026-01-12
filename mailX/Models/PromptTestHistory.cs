using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mailX.Models;

/// <summary>
/// 프롬프트 테스트 이력 모델 - 프롬프트 테스트 결과 저장
/// </summary>
public class PromptTestHistory
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 프롬프트 FK
    /// </summary>
    [Required]
    public int PromptId { get; set; }

    /// <summary>
    /// 입력 데이터 (JSON 형식)
    /// </summary>
    public string? InputData { get; set; }

    /// <summary>
    /// 출력 결과
    /// </summary>
    public string? OutputResult { get; set; }

    /// <summary>
    /// 사용된 AI 제공자
    /// </summary>
    [MaxLength(50)]
    public string? Provider { get; set; }

    /// <summary>
    /// 사용된 토큰 수
    /// </summary>
    public int? Tokens { get; set; }

    /// <summary>
    /// 실행 시간 (밀리초)
    /// </summary>
    public long? ExecutionTime { get; set; }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ===== 네비게이션 프로퍼티 =====

    /// <summary>
    /// 프롬프트 참조
    /// </summary>
    [ForeignKey(nameof(PromptId))]
    public virtual Prompt? Prompt { get; set; }
}
