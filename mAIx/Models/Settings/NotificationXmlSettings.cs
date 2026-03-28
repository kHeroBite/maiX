using System;
using System.Xml.Serialization;

namespace mAIx.Models.Settings;

/// <summary>
/// 알림 설정 (ntfy 연동)
/// </summary>
[Serializable]
[XmlRoot("NotificationSettings")]
public class NotificationXmlSettings
{
    /// <summary>
    /// ntfy 서버 URL
    /// </summary>
    [XmlElement("NtfyServerUrl")]
    public string NtfyServerUrl { get; set; } = "https://ntfy.sh";

    /// <summary>
    /// ntfy 토픽 이름
    /// </summary>
    [XmlElement("NtfyTopic")]
    public string NtfyTopic { get; set; } = "mAIx";

    /// <summary>
    /// ntfy 인증 토큰 (선택)
    /// </summary>
    [XmlElement("NtfyAuthToken")]
    public string? NtfyAuthToken { get; set; }

    /// <summary>
    /// 새 메일 알림 활성화
    /// </summary>
    [XmlElement("EnableNewMailNotification")]
    public bool EnableNewMailNotification { get; set; } = true;

    /// <summary>
    /// 중요 메일 알림 활성화
    /// </summary>
    [XmlElement("EnableImportantMailNotification")]
    public bool EnableImportantMailNotification { get; set; } = true;

    /// <summary>
    /// 마감일 알림 활성화
    /// </summary>
    [XmlElement("EnableDeadlineReminder")]
    public bool EnableDeadlineReminder { get; set; } = true;

    /// <summary>
    /// 알림 발송 최소 우선순위 점수
    /// </summary>
    [XmlElement("MinPriorityForNotification")]
    public int MinPriorityForNotification { get; set; } = 70;

    /// <summary>
    /// 마감일 알림 사전 일수
    /// </summary>
    [XmlElement("DeadlineReminderDays")]
    public int DeadlineReminderDays { get; set; } = 3;

    /// <summary>
    /// 조용한 시간 시작 (HH:mm 형식)
    /// </summary>
    [XmlElement("QuietHoursStart")]
    public string QuietHoursStart { get; set; } = "22:00";

    /// <summary>
    /// 조용한 시간 종료 (HH:mm 형식)
    /// </summary>
    [XmlElement("QuietHoursEnd")]
    public string QuietHoursEnd { get; set; } = "07:00";

    /// <summary>
    /// 조용한 시간 활성화
    /// </summary>
    [XmlElement("EnableQuietHours")]
    public bool EnableQuietHours { get; set; } = true;

    /// <summary>
    /// 알림 배치 간격 (분)
    /// </summary>
    [XmlElement("BatchIntervalMinutes")]
    public int BatchIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// 최대 배치 크기
    /// </summary>
    [XmlElement("MaxBatchSize")]
    public int MaxBatchSize { get; set; } = 10;

    /// <summary>
    /// 비업무 메일 제외
    /// </summary>
    [XmlElement("ExcludeNonBusinessMail")]
    public bool ExcludeNonBusinessMail { get; set; } = true;

    /// <summary>
    /// Windows 네이티브 토스트 알림 활성화
    /// </summary>
    [XmlElement("ToastEnabled")]
    public bool ToastEnabled { get; set; } = true;
}
