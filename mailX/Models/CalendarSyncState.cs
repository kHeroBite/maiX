using System;
using System.ComponentModel.DataAnnotations;

namespace mailX.Models;

/// <summary>
/// 캘린더 동기화 상태 (Delta Query 토큰 관리)
/// </summary>
public class CalendarSyncState
{
    /// <summary>
    /// Primary Key
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// 계정 이메일
    /// </summary>
    [MaxLength(255)]
    [Required]
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// 캘린더 ID (null이면 기본 캘린더)
    /// </summary>
    [MaxLength(255)]
    public string? CalendarId { get; set; }

    /// <summary>
    /// 캘린더 이름
    /// </summary>
    [MaxLength(255)]
    public string? CalendarName { get; set; }

    /// <summary>
    /// Delta Link (변경분 추적용)
    /// </summary>
    [MaxLength(2000)]
    public string? DeltaLink { get; set; }

    /// <summary>
    /// 마지막 동기화 시간
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>
    /// 동기화 시작 날짜 (이 날짜 이후의 이벤트만 동기화)
    /// </summary>
    public DateTime SyncStartDate { get; set; }

    /// <summary>
    /// 동기화 종료 날짜 (이 날짜 이전의 이벤트만 동기화)
    /// </summary>
    public DateTime SyncEndDate { get; set; }

    /// <summary>
    /// 마지막 동기화에서 추가된 이벤트 수
    /// </summary>
    public int LastSyncAddedCount { get; set; }

    /// <summary>
    /// 마지막 동기화에서 수정된 이벤트 수
    /// </summary>
    public int LastSyncUpdatedCount { get; set; }

    /// <summary>
    /// 마지막 동기화에서 삭제된 이벤트 수
    /// </summary>
    public int LastSyncDeletedCount { get; set; }

    /// <summary>
    /// 동기화 활성화 여부
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 마지막 오류 메시지
    /// </summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>
    /// 마지막 오류 발생 시간
    /// </summary>
    public DateTime? LastErrorAt { get; set; }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 수정 시간
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
