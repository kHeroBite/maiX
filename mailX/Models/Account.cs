using System;
using System.ComponentModel.DataAnnotations;

namespace mailX.Models;

/// <summary>
/// 계정 모델 - Microsoft 365 계정 정보 및 인증 토큰
/// </summary>
public class Account
{
    /// <summary>
    /// 이메일 주소 (PK)
    /// </summary>
    [Key]
    [MaxLength(500)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 표시 이름
    /// </summary>
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// 인증 토큰 (암호화된 바이트 배열)
    /// MSAL 토큰 캐시 저장용
    /// </summary>
    public byte[]? Tokens { get; set; }

    /// <summary>
    /// 기본 계정 여부
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// 마지막 로그인 시간
    /// </summary>
    public DateTime? LastLoginAt { get; set; }
}
