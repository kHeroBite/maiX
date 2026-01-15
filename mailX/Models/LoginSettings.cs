using System;
using System.Xml.Serialization;
using mailX.Models.Settings;

namespace mailX.Models;

/// <summary>
/// 로그인 설정 모델
/// 저장 위치: %APPDATA%\mailX\conf\autologin.xml
/// </summary>
[Serializable]
[XmlRoot("LoginSettings")]
public class LoginSettings
{
    /// <summary>
    /// Microsoft 365 이메일 주소
    /// </summary>
    [XmlElement("Email")]
    public string? Email { get; set; }

    /// <summary>
    /// 사용자 표시 이름
    /// </summary>
    [XmlElement("DisplayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// 자동 로그인 여부
    /// </summary>
    [XmlElement("AutoLogin")]
    public bool AutoLogin { get; set; } = false;

    /// <summary>
    /// 마지막 로그인 시각
    /// </summary>
    [XmlElement("LastLoginAt")]
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Azure AD 인증 설정 (ClientId, TenantId, Scopes)
    /// </summary>
    [XmlElement("AzureAd")]
    public AzureAdSettings AzureAd { get; set; } = new();
}
