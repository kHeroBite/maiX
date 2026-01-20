using System;
using System.ComponentModel.DataAnnotations;

namespace mailX.Models;

/// <summary>
/// 캘린더 이벤트 (Graph API 동기화용)
/// </summary>
public class CalendarEvent
{
    /// <summary>
    /// 로컬 Primary Key
    /// </summary>
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// Graph API 이벤트 ID
    /// </summary>
    [MaxLength(255)]
    public string? GraphId { get; set; }

    /// <summary>
    /// iCalendar UID (반복 일정 전체에 공유됨)
    /// </summary>
    [MaxLength(255)]
    public string? ICalUId { get; set; }

    /// <summary>
    /// 반복 일정의 마스터 이벤트 ID
    /// </summary>
    [MaxLength(255)]
    public string? SeriesMasterId { get; set; }

    /// <summary>
    /// 일정 제목
    /// </summary>
    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// 일정 본문 (HTML)
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// 본문 내용 유형 (text, html)
    /// </summary>
    [MaxLength(10)]
    public string? BodyContentType { get; set; }

    /// <summary>
    /// 장소
    /// </summary>
    [MaxLength(500)]
    public string? Location { get; set; }

    /// <summary>
    /// 시작 일시 (UTC)
    /// </summary>
    public DateTime StartDateTime { get; set; }

    /// <summary>
    /// 종료 일시 (UTC)
    /// </summary>
    public DateTime EndDateTime { get; set; }

    /// <summary>
    /// 시작 시간대 (예: Asia/Seoul)
    /// </summary>
    [MaxLength(50)]
    public string? StartTimeZone { get; set; }

    /// <summary>
    /// 종료 시간대 (예: Asia/Seoul)
    /// </summary>
    [MaxLength(50)]
    public string? EndTimeZone { get; set; }

    /// <summary>
    /// 종일 이벤트 여부
    /// </summary>
    public bool IsAllDay { get; set; }

    /// <summary>
    /// 반복 일정 여부
    /// </summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// 반복 패턴 (JSON)
    /// 예: {"type":"weekly","interval":1,"daysOfWeek":["monday","wednesday","friday"]}
    /// </summary>
    public string? RecurrencePattern { get; set; }

    /// <summary>
    /// 반복 범위 (JSON)
    /// 예: {"type":"endDate","startDate":"2024-01-01","endDate":"2024-12-31"}
    /// </summary>
    public string? RecurrenceRange { get; set; }

    /// <summary>
    /// 상태 표시 (free, tentative, busy, oof, workingElsewhere, unknown)
    /// </summary>
    [MaxLength(20)]
    public string? ShowAs { get; set; }

    /// <summary>
    /// 응답 상태 (none, organizer, tentativelyAccepted, accepted, declined, notResponded)
    /// </summary>
    [MaxLength(30)]
    public string? ResponseStatus { get; set; }

    /// <summary>
    /// 중요도 (low, normal, high)
    /// </summary>
    [MaxLength(10)]
    public string? Importance { get; set; }

    /// <summary>
    /// 민감도 (normal, personal, private, confidential)
    /// </summary>
    [MaxLength(15)]
    public string? Sensitivity { get; set; }

    /// <summary>
    /// 온라인 회의 여부
    /// </summary>
    public bool IsOnlineMeeting { get; set; }

    /// <summary>
    /// 온라인 회의 URL
    /// </summary>
    [MaxLength(2000)]
    public string? OnlineMeetingUrl { get; set; }

    /// <summary>
    /// 온라인 회의 제공자 (teamsForBusiness, skypeForBusiness, skypeForConsumer, unknown)
    /// </summary>
    [MaxLength(30)]
    public string? OnlineMeetingProvider { get; set; }

    /// <summary>
    /// 알림 시간 (분, 이벤트 시작 전)
    /// </summary>
    public int ReminderMinutesBeforeStart { get; set; } = 15;

    /// <summary>
    /// 알림 활성화 여부
    /// </summary>
    public bool IsReminderOn { get; set; } = true;

    /// <summary>
    /// 주최자 이메일
    /// </summary>
    [MaxLength(255)]
    public string? OrganizerEmail { get; set; }

    /// <summary>
    /// 주최자 이름
    /// </summary>
    [MaxLength(255)]
    public string? OrganizerName { get; set; }

    /// <summary>
    /// 참석자 목록 (JSON)
    /// 예: [{"email":"user@example.com","name":"User","type":"required","status":"accepted"}]
    /// </summary>
    public string? Attendees { get; set; }

    /// <summary>
    /// 카테고리 목록 (JSON 배열)
    /// 예: ["회의","중요"]
    /// </summary>
    public string? Categories { get; set; }

    /// <summary>
    /// 캘린더 ID (사용자의 여러 캘린더 지원)
    /// </summary>
    [MaxLength(255)]
    public string? CalendarId { get; set; }

    /// <summary>
    /// 캘린더 이름
    /// </summary>
    [MaxLength(255)]
    public string? CalendarName { get; set; }

    /// <summary>
    /// 계정 이메일
    /// </summary>
    [MaxLength(255)]
    [Required]
    public string AccountEmail { get; set; } = string.Empty;

    /// <summary>
    /// 웹 링크 (Outlook Web에서 열기용)
    /// </summary>
    [MaxLength(2000)]
    public string? WebLink { get; set; }

    /// <summary>
    /// Graph API에서 마지막 수정된 시간
    /// </summary>
    public DateTime? LastModifiedDateTime { get; set; }

    /// <summary>
    /// 생성 시간
    /// </summary>
    public DateTime? CreatedDateTime { get; set; }

    /// <summary>
    /// 마지막 동기화 시간
    /// </summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 로컬에서만 생성된 이벤트 (서버에 아직 동기화되지 않음)
    /// </summary>
    public bool IsLocalOnly { get; set; }

    /// <summary>
    /// 삭제된 이벤트 (소프트 삭제)
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// 삭제된 시간
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 취소된 이벤트 여부
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// 이벤트 유형 (singleInstance, occurrence, exception, seriesMaster)
    /// </summary>
    [MaxLength(20)]
    public string? EventType { get; set; }

    /// <summary>
    /// 원래 시작 시간 (반복 일정 예외용)
    /// </summary>
    public DateTime? OriginalStartTimeZone { get; set; }

    /// <summary>
    /// 원래 종료 시간 (반복 일정 예외용)
    /// </summary>
    public DateTime? OriginalEndTimeZone { get; set; }
}
