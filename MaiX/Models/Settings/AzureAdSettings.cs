using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace MaiX.Models.Settings;

/// <summary>
/// Azure AD 인증 설정
/// </summary>
[Serializable]
public class AzureAdSettings
{
    /// <summary>
    /// Azure AD 애플리케이션 (클라이언트) ID
    /// </summary>
    [XmlElement("ClientId")]
    public string? ClientId { get; set; }

    /// <summary>
    /// 테넌트 ID (단일 테넌트 앱은 실제 테넌트 ID 필요)
    /// </summary>
    [XmlElement("TenantId")]
    public string TenantId { get; set; } = "";

    /// <summary>
    /// 요청할 권한 범위 (XML에서 로드되지 않으면 기본값 사용)
    /// </summary>
    [XmlArray("Scopes")]
    [XmlArrayItem("Scope")]
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// 기본 권한 범위 (Scopes가 비어있을 때 사용)
    /// </summary>
    [XmlIgnore]
    public static readonly string[] DefaultScopes = new[]
    {
        "User.Read",
        "Mail.Read",
        "Mail.Send",
        "Mail.ReadWrite",
        "Files.Read.All",
        "Sites.Read.All"
    };

    /// <summary>
    /// 유효한 Scopes 반환 (비어있으면 기본값 사용)
    /// </summary>
    [XmlIgnore]
    public IEnumerable<string> EffectiveScopes => Scopes.Count > 0 ? Scopes : DefaultScopes;

    /// <summary>
    /// ClientId가 설정되었는지 확인
    /// </summary>
    [XmlIgnore]
    public bool IsConfigured => !string.IsNullOrEmpty(ClientId);
}
