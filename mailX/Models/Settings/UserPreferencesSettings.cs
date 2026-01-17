using System.Xml.Serialization;

namespace mailX.Models.Settings;

/// <summary>
/// 사용자 환경 설정 (테마, UI 설정 등)
/// </summary>
[XmlRoot("UserPreferences")]
public class UserPreferencesSettings
{
    /// <summary>
    /// 테마 설정 (Dark, Light)
    /// </summary>
    [XmlElement("Theme")]
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// 다크 모드 여부 (편의 프로퍼티)
    /// </summary>
    [XmlIgnore]
    public bool IsDarkMode
    {
        get => Theme == "Dark";
        set => Theme = value ? "Dark" : "Light";
    }

    /// <summary>
    /// GPU 모드 사용 여부 (기본값: false = CPU 소프트웨어 렌더링)
    /// true: GPU 가속 사용, false: CPU 소프트웨어 렌더링
    /// </summary>
    [XmlElement("UseGpuMode")]
    public bool UseGpuMode { get; set; } = false;
}
