using System;
using System.Xml.Serialization;

namespace MaiX.Models.Settings;

/// <summary>
/// 메일 동기화 설정
/// </summary>
[Serializable]
[XmlRoot("SyncSettings")]
public class SyncSettings
{
    /// <summary>
    /// 동기화 주기 (분)
    /// </summary>
    [XmlElement("IntervalMinutes")]
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>
    /// 동기화당 최대 메시지 수
    /// </summary>
    [XmlElement("MaxMessagesPerSync")]
    public int MaxMessagesPerSync { get; set; } = 5;

    /// <summary>
    /// 자동 동기화 활성화
    /// </summary>
    [XmlElement("AutoSyncEnabled")]
    public bool AutoSyncEnabled { get; set; } = true;

    /// <summary>
    /// 시작 시 동기화 실행
    /// </summary>
    [XmlElement("SyncOnStartup")]
    public bool SyncOnStartup { get; set; } = true;
}
