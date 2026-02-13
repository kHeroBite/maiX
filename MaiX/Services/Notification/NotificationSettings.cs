using System;

namespace MaiX.Services.Notification;

/// <summary>
/// 알림 설정 모델 - ntfy 및 알림 동작 설정
/// </summary>
public class NotificationSettings
{
    /// <summary>
    /// ntfy 서버 URL
    /// </summary>
    public string NtfyServerUrl { get; set; } = "https://ntfy.sh";

    /// <summary>
    /// ntfy 토픽 이름
    /// </summary>
    public string NtfyTopic { get; set; } = "MaiX";

    /// <summary>
    /// ntfy 인증 토큰 (선택적)
    /// </summary>
    public string? NtfyAuthToken { get; set; }

    /// <summary>
    /// 새 메일 알림 활성화
    /// </summary>
    public bool EnableNewMailNotification { get; set; } = true;

    /// <summary>
    /// 중요 메일 알림 활성화 (우선순위 기반)
    /// </summary>
    public bool EnableImportantMailNotification { get; set; } = true;

    /// <summary>
    /// 마감일 임박 알림 활성화
    /// </summary>
    public bool EnableDeadlineReminder { get; set; } = true;

    /// <summary>
    /// 알림 발송 최소 우선순위 점수 (0-100)
    /// 이 점수 이상인 메일만 중요 알림 발송
    /// </summary>
    public int MinPriorityForNotification { get; set; } = 70;

    /// <summary>
    /// 마감일 알림 발송 기준 일수 (마감 N일 전부터 알림)
    /// </summary>
    public int DeadlineReminderDays { get; set; } = 3;

    /// <summary>
    /// 방해 금지 시간 시작 (로컬 시간)
    /// </summary>
    public TimeSpan QuietHoursStart { get; set; } = new TimeSpan(22, 0, 0); // 22:00

    /// <summary>
    /// 방해 금지 시간 종료 (로컬 시간)
    /// </summary>
    public TimeSpan QuietHoursEnd { get; set; } = new TimeSpan(7, 0, 0); // 07:00

    /// <summary>
    /// 방해 금지 시간 활성화
    /// </summary>
    public bool EnableQuietHours { get; set; } = true;

    /// <summary>
    /// 동일 발신자 알림 묶음 처리 간격 (분)
    /// 이 시간 내 동일 발신자 메일은 하나의 알림으로 묶음
    /// </summary>
    public int BatchIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// 알림 배치 최대 대기 수
    /// 이 수에 도달하면 즉시 발송
    /// </summary>
    public int MaxBatchSize { get; set; } = 10;

    /// <summary>
    /// 광고/뉴스레터 알림 제외
    /// </summary>
    public bool ExcludeNonBusinessMail { get; set; } = true;

    /// <summary>
    /// 캘린더 일정 알림 활성화
    /// </summary>
    public bool EnableCalendarReminder { get; set; } = true;

    /// <summary>
    /// 캘린더 알림 시간 목록 (분 단위)
    /// 일정 시작 전 몇 분에 알림을 발송할지 설정
    /// 예: [15, 60] - 15분 전, 60분(1시간) 전
    /// </summary>
    public List<int>? ReminderMinutesBefore { get; set; } = new List<int> { 15, 60 };

    /// <summary>
    /// 현재 시간이 방해 금지 시간인지 확인
    /// </summary>
    public bool IsQuietHoursNow()
    {
        if (!EnableQuietHours)
            return false;

        var now = DateTime.Now.TimeOfDay;

        // 방해 금지 시간이 자정을 넘어가는 경우 (예: 22:00 ~ 07:00)
        if (QuietHoursStart > QuietHoursEnd)
        {
            return now >= QuietHoursStart || now < QuietHoursEnd;
        }

        // 같은 날 내 시간 범위 (예: 13:00 ~ 14:00)
        return now >= QuietHoursStart && now < QuietHoursEnd;
    }

    /// <summary>
    /// ntfy 전체 URL 반환
    /// </summary>
    public string GetNtfyUrl() => $"{NtfyServerUrl.TrimEnd('/')}/{NtfyTopic}";
}

/// <summary>
/// 알림 우선순위 레벨 (ntfy 호환)
/// </summary>
public enum NotificationPriority
{
    /// <summary>
    /// 최소 (1) - 무음
    /// </summary>
    Min = 1,

    /// <summary>
    /// 낮음 (2) - 무음
    /// </summary>
    Low = 2,

    /// <summary>
    /// 기본 (3) - 알림음
    /// </summary>
    Default = 3,

    /// <summary>
    /// 높음 (4) - 알림음
    /// </summary>
    High = 4,

    /// <summary>
    /// 긴급 (5) - 지속적 알림
    /// </summary>
    Urgent = 5
}

/// <summary>
/// 알림 메시지 모델
/// </summary>
public class NotificationMessage
{
    /// <summary>
    /// 알림 제목
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 알림 본문
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 우선순위
    /// </summary>
    public NotificationPriority Priority { get; set; } = NotificationPriority.Default;

    /// <summary>
    /// 태그 목록 (이모지 지원)
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// 클릭 시 열릴 URL
    /// </summary>
    public string? ClickUrl { get; set; }

    /// <summary>
    /// 첨부 파일 URL
    /// </summary>
    public string? AttachmentUrl { get; set; }

    /// <summary>
    /// 액션 버튼 목록
    /// </summary>
    public List<NotificationAction> Actions { get; set; } = new();

    /// <summary>
    /// 관련 이메일 ID (내부용)
    /// </summary>
    public int? EmailId { get; set; }

    /// <summary>
    /// 알림 생성 시간
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 발신자 (배치 처리용)
    /// </summary>
    public string? Sender { get; set; }
}

/// <summary>
/// 알림 액션 버튼
/// </summary>
public class NotificationAction
{
    /// <summary>
    /// 액션 타입 (view, http, broadcast)
    /// </summary>
    public string Action { get; set; } = "view";

    /// <summary>
    /// 버튼 레이블
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 액션 URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// HTTP 메서드 (http 액션용)
    /// </summary>
    public string? Method { get; set; }

    /// <summary>
    /// 요청 본문 (http 액션용)
    /// </summary>
    public string? Body { get; set; }
}
