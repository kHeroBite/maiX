namespace mAIx.Models;

/// <summary>
/// Teams 채널 알림 설정 모델 - 채널별 알림 구성 저장
/// </summary>
public class ChannelNotificationSetting
{
    /// <summary>
    /// 기본 키
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Teams 채널 ID
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// Teams 팀 ID
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// 새 게시물 알림 여부 (기본값: true)
    /// </summary>
    public bool NotifyOnNewPost { get; set; } = true;

    /// <summary>
    /// 멘션 알림 여부 (기본값: true)
    /// </summary>
    public bool NotifyOnMention { get; set; } = true;

    /// <summary>
    /// 채널 음소거 여부 (기본값: false)
    /// </summary>
    public bool IsMuted { get; set; } = false;
}
