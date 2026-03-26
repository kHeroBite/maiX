using System.ComponentModel.DataAnnotations;

namespace mAIx.Models;

/// <summary>
/// AI 설정 모델 - LLM 제공자 및 API 설정
/// </summary>
public class AISetting
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// AI 제공자 (openai, anthropic, google, ollama 등)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// API 키 (암호화 저장 권장)
    /// </summary>
    [MaxLength(500)]
    public string? ApiKey { get; set; }

    /// <summary>
    /// API 기본 URL (ollama 등 로컬 서버용)
    /// </summary>
    [MaxLength(500)]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// 모델 이름 (gpt-4, claude-3-opus, gemini-pro 등)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 기본 설정 여부
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 활성화 여부
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
