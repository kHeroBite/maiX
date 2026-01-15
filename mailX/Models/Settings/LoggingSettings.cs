using System;
using System.Xml.Serialization;

namespace mailX.Models.Settings;

/// <summary>
/// 로깅 설정
/// </summary>
[Serializable]
[XmlRoot("LoggingSettings")]
public class LoggingSettings
{
    /// <summary>
    /// 최소 로그 레벨 (Verbose, Debug, Information, Warning, Error, Fatal)
    /// </summary>
    [XmlElement("MinimumLevel")]
    public string MinimumLevel { get; set; } = "Debug";

    /// <summary>
    /// 보관할 로그 파일 수
    /// </summary>
    [XmlElement("RetainedFileCountLimit")]
    public int RetainedFileCountLimit { get; set; } = 30;

    /// <summary>
    /// 로그 파일 경로 (기본: %APPDATA%\mailX\logs)
    /// </summary>
    [XmlElement("LogPath")]
    public string? LogPath { get; set; }
}
