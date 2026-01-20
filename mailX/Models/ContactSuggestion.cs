using CommunityToolkit.Mvvm.ComponentModel;

namespace mailX.Models;

/// <summary>
/// 연락처 소스 유형
/// </summary>
public enum ContactSource
{
    /// <summary>
    /// 로컬 DB (최근 메일 발신자)
    /// </summary>
    Local,

    /// <summary>
    /// Microsoft 연락처 (개인 주소록)
    /// </summary>
    Contact,

    /// <summary>
    /// 조직 디렉터리 (회사 동료)
    /// </summary>
    Organization
}

/// <summary>
/// 연락처 자동완성 제안 모델
/// ObservableObject 상속으로 PropertyChanged 지원 (프로필 사진 비동기 로딩용)
/// </summary>
public partial class ContactSuggestion : ObservableObject
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
    /// 직위 (선택적)
    /// </summary>
    public string? JobTitle { get; set; }

    /// <summary>
    /// 회사명 (선택적)
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// 연락처 소스
    /// </summary>
    public ContactSource Source { get; set; } = ContactSource.Local;

    /// <summary>
    /// 연락 빈도 (정렬용)
    /// </summary>
    public int ContactFrequency { get; set; }

    /// <summary>
    /// Graph API용 ID (연락처: contactId, 조직: userId)
    /// </summary>
    public string? ContactId { get; set; }

    /// <summary>
    /// 프로필 사진 Base64 (null이면 이니셜 표시)
    /// ObservableProperty로 선언하여 비동기 로딩 시 UI 자동 갱신
    /// </summary>
    [ObservableProperty]
    private string? _photoBase64;

    /// <summary>
    /// 사진 로드 완료 여부 (중복 로드 방지)
    /// </summary>
    public bool PhotoLoaded { get; set; }

    /// <summary>
    /// 사진 존재 여부 (UI 바인딩용)
    /// </summary>
    public bool HasPhoto => !string.IsNullOrEmpty(PhotoBase64);

    /// <summary>
    /// PhotoBase64 변경 시 HasPhoto도 알림
    /// </summary>
    partial void OnPhotoBase64Changed(string? value)
    {
        OnPropertyChanged(nameof(HasPhoto));
    }

    /// <summary>
    /// 포맷된 주소 (예: 김기로 <ryo@diquest.com>)
    /// </summary>
    public string FormattedAddress =>
        string.IsNullOrEmpty(DisplayName)
            ? Email
            : $"{DisplayName} <{Email}>";

    /// <summary>
    /// 이니셜 (아바타용, 최대 2자)
    /// </summary>
    public string Initials
    {
        get
        {
            if (string.IsNullOrEmpty(DisplayName))
            {
                // 이메일에서 @ 앞부분 첫 글자
                var atIndex = Email.IndexOf('@');
                if (atIndex > 0)
                    return Email[0].ToString().ToUpper();
                return Email.Length > 0 ? Email[0].ToString().ToUpper() : "?";
            }

            // 한글/영어 이름에서 이니셜 추출
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                // "홍 길동" → "홍길" 또는 "John Doe" → "JD"
                return $"{parts[0][0]}{parts[^1][0]}".ToUpper();
            }

            // 단일 이름: 첫 2글자
            return DisplayName.Length >= 2
                ? DisplayName.Substring(0, 2).ToUpper()
                : DisplayName.ToUpper();
        }
    }

    /// <summary>
    /// 소스 라벨 (UI 표시용)
    /// </summary>
    public string SourceLabel => Source switch
    {
        ContactSource.Local => "최근",
        ContactSource.Contact => "연락처",
        ContactSource.Organization => "조직",
        _ => ""
    };

    /// <summary>
    /// Popup에 표시할 텍스트
    /// </summary>
    public string DisplayText
    {
        get
        {
            var text = FormattedAddress;
            var details = new List<string>();

            if (!string.IsNullOrEmpty(Department))
                details.Add(Department);
            if (!string.IsNullOrEmpty(JobTitle))
                details.Add(JobTitle);

            if (details.Count > 0)
                text += $" ({string.Join(" - ", details)})";

            return text;
        }
    }

    /// <summary>
    /// 부서/직위 문자열 (부서 - 직위)
    /// </summary>
    public string? PositionString
    {
        get
        {
            if (!string.IsNullOrEmpty(Department) && !string.IsNullOrEmpty(JobTitle))
                return $"{Department} - {JobTitle}";

            return Department ?? JobTitle;
        }
    }
}
