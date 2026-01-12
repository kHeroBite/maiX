using System;
using System.ComponentModel.DataAnnotations;

namespace mailX.Models;

/// <summary>
/// 동기화 상태 모델 - Graph API Delta 동기화 상태 관리
/// </summary>
public class SyncState
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 계정 이메일
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// 폴더 ID (null이면 전체 메일함)
    /// </summary>
    [MaxLength(500)]
    public string? FolderId { get; set; }

    /// <summary>
    /// Delta 동기화 링크 (다음 동기화에 사용)
    /// </summary>
    public string? DeltaLink { get; set; }

    /// <summary>
    /// 마지막 동기화 시간
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
}
