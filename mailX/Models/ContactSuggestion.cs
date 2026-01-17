namespace mailX.Models;

/// <summary>
/// 연락처 자동완성 제안 모델
/// </summary>
public class ContactSuggestion
{
    /// <summary>
    /// 표시 이름 (예: 김기로)
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// 이메일 주소 (예: ryo@diquest.com)
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// 부서 (선택적)
    /// </summary>
    public string? Department { get; set; }

    /// <summary>
    /// 포맷된 주소 (예: 김기로 <ryo@diquest.com>)
    /// </summary>
    public string FormattedAddress =>
        string.IsNullOrEmpty(DisplayName)
            ? Email
            : $"{DisplayName} <{Email}>";

    /// <summary>
    /// Popup에 표시할 텍스트
    /// </summary>
    public string DisplayText =>
        string.IsNullOrEmpty(Department)
            ? FormattedAddress
            : $"{FormattedAddress} ({Department})";
}
