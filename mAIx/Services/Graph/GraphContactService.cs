using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MaiX.Services.Analysis;
using Serilog;

namespace MaiX.Services.Graph;

/// <summary>
/// Microsoft 연락처 연동 서비스
/// </summary>
public class GraphContactService
{
    private readonly GraphAuthService _authService;
    private readonly PriorityCalculator _priorityCalculator;
    private readonly ILogger _logger;

    // VIP 연락처 캐시
    private readonly HashSet<string> _vipContacts = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _vipCacheExpiry = DateTime.MinValue;
    private const int VipCacheMinutes = 30;

    public GraphContactService(GraphAuthService authService, PriorityCalculator priorityCalculator)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _priorityCalculator = priorityCalculator ?? throw new ArgumentNullException(nameof(priorityCalculator));
        _logger = Log.ForContext<GraphContactService>();
    }

    /// <summary>
    /// 연락처 목록 조회
    /// </summary>
    /// <param name="top">조회 수</param>
    /// <returns>연락처 목록</returns>
    public async Task<IEnumerable<Contact>> GetContactsAsync(int top = 100)
    {
        try
        {
            var client = _authService.GetGraphClient();
            var response = await client.Me.Contacts.GetAsync(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Orderby = new[] { "displayName" };
                config.QueryParameters.Select = new[]
                {
                    "id", "displayName", "emailAddresses", "companyName",
                    "department", "jobTitle", "mobilePhone", "businessPhones",
                    "personalNotes", "categories"
                };
            });

            _logger.Debug("연락처 {Count}개 조회", response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Contact>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "연락처 목록 조회 실패");
            throw;
        }
    }

    /// <summary>
    /// 연락처 검색
    /// </summary>
    /// <param name="searchQuery">검색어 (이름 또는 이메일)</param>
    /// <returns>검색된 연락처 목록</returns>
    public async Task<IEnumerable<Contact>> SearchContactsAsync(string searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
            return new List<Contact>();

        try
        {
            var client = _authService.GetGraphClient();

            // OData 필터를 사용한 검색
            var filter = $"startswith(displayName,'{EscapeODataString(searchQuery)}') " +
                        $"or startswith(givenName,'{EscapeODataString(searchQuery)}') " +
                        $"or startswith(surname,'{EscapeODataString(searchQuery)}')";

            var response = await client.Me.Contacts.GetAsync(config =>
            {
                config.QueryParameters.Filter = filter;
                config.QueryParameters.Top = 50;
                config.QueryParameters.Select = new[]
                {
                    "id", "displayName", "emailAddresses", "companyName",
                    "department", "jobTitle", "photo"
                };
            });

            _logger.Debug("연락처 검색 '{Query}': {Count}개 발견", searchQuery, response?.Value?.Count ?? 0);
            return response?.Value ?? new List<Contact>();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "연락처 검색 실패: Query={Query}", searchQuery);
            throw;
        }
    }

    /// <summary>
    /// 이메일 주소로 연락처 조회
    /// </summary>
    /// <param name="email">이메일 주소</param>
    /// <returns>연락처 정보 (없으면 null)</returns>
    public async Task<Contact?> GetContactByEmailAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        try
        {
            var client = _authService.GetGraphClient();

            // 모든 연락처에서 이메일로 검색
            var response = await client.Me.Contacts.GetAsync(config =>
            {
                config.QueryParameters.Top = 500;
                config.QueryParameters.Select = new[]
                {
                    "id", "displayName", "emailAddresses", "companyName",
                    "department", "jobTitle", "photo"
                };
            });

            var contact = response?.Value?.FirstOrDefault(c =>
                c.EmailAddresses?.Any(e =>
                    e.Address?.Equals(email, StringComparison.OrdinalIgnoreCase) == true) == true);

            if (contact != null)
            {
                _logger.Debug("이메일로 연락처 찾음: {Email} -> {Name}", email, contact.DisplayName);
            }

            return contact;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "이메일로 연락처 조회 실패: {Email}", email);
            throw;
        }
    }

    /// <summary>
    /// 발신자 정보 보강
    /// </summary>
    /// <param name="senderEmail">발신자 이메일</param>
    /// <returns>보강된 발신자 정보</returns>
    public async Task<EnrichedSenderInfo> EnrichSenderInfoAsync(string senderEmail)
    {
        var result = new EnrichedSenderInfo
        {
            Email = senderEmail
        };

        if (string.IsNullOrWhiteSpace(senderEmail))
            return result;

        try
        {
            // 1. 연락처에서 검색
            var contact = await GetContactByEmailAsync(senderEmail);

            if (contact != null)
            {
                result.DisplayName = contact.DisplayName;
                result.CompanyName = contact.CompanyName;
                result.Department = contact.Department;
                result.JobTitle = contact.JobTitle;
                result.IsInContacts = true;
                result.IsVip = await IsVipContactAsync(senderEmail);

                // 프로필 사진 가져오기
                result.PhotoBase64 = await GetContactPhotoAsync(contact.Id);
            }
            else
            {
                // 2. 조직 디렉터리에서 검색 (조직 계정인 경우)
                var orgUser = await GetOrganizationUserAsync(senderEmail);

                if (orgUser != null)
                {
                    result.DisplayName = orgUser.DisplayName;
                    result.CompanyName = orgUser.CompanyName;
                    result.Department = orgUser.Department;
                    result.JobTitle = orgUser.JobTitle;
                    result.IsInOrganization = true;

                    // 조직 사용자 프로필 사진
                    result.PhotoBase64 = await GetOrganizationUserPhotoAsync(orgUser.Id);
                }
            }

            _logger.Debug("발신자 정보 보강 완료: {Email} -> VIP={IsVip}, Org={IsOrg}",
                senderEmail, result.IsVip, result.IsInOrganization);

            return result;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "발신자 정보 보강 실패: {Email}", senderEmail);
            return result;
        }
    }

    /// <summary>
    /// VIP 연락처 여부 확인
    /// </summary>
    /// <param name="email">이메일 주소</param>
    /// <returns>VIP 여부</returns>
    public async Task<bool> IsVipContactAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        // 캐시 갱신
        if (DateTime.Now > _vipCacheExpiry)
        {
            await RefreshVipCacheAsync();
        }

        return _vipContacts.Contains(email);
    }

    /// <summary>
    /// VIP 연락처 목록 설정 (PriorityCalculator와 연동)
    /// </summary>
    /// <param name="vipEmails">VIP 이메일 목록</param>
    public void SetVipContacts(IEnumerable<string> vipEmails)
    {
        _vipContacts.Clear();
        foreach (var email in vipEmails)
        {
            _vipContacts.Add(email);
        }

        // PriorityCalculator에도 동기화
        _priorityCalculator.SetVipSenders(vipEmails);

        _logger.Information("VIP 연락처 {Count}명 설정", _vipContacts.Count);
    }

    /// <summary>
    /// VIP 연락처 추가
    /// </summary>
    /// <param name="email">이메일 주소</param>
    public void AddVipContact(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        _vipContacts.Add(email);

        // PriorityCalculator에도 동기화
        _priorityCalculator.SetVipSenders(_vipContacts);

        _logger.Information("VIP 연락처 추가: {Email}", email);
    }

    /// <summary>
    /// VIP 연락처 제거
    /// </summary>
    /// <param name="email">이메일 주소</param>
    public void RemoveVipContact(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        _vipContacts.Remove(email);

        // PriorityCalculator에도 동기화
        _priorityCalculator.SetVipSenders(_vipContacts);

        _logger.Information("VIP 연락처 제거: {Email}", email);
    }

    /// <summary>
    /// VIP 연락처 목록 반환
    /// </summary>
    /// <returns>VIP 이메일 목록</returns>
    public IEnumerable<string> GetVipContacts()
    {
        return _vipContacts.ToList();
    }

    /// <summary>
    /// 조직 디렉터리에서 사용자 검색
    /// </summary>
    /// <param name="email">이메일 주소</param>
    /// <returns>사용자 정보</returns>
    private async Task<User?> GetOrganizationUserAsync(string email)
    {
        try
        {
            var client = _authService.GetGraphClient();

            var response = await client.Users.GetAsync(config =>
            {
                config.QueryParameters.Filter = $"mail eq '{EscapeODataString(email)}' or userPrincipalName eq '{EscapeODataString(email)}'";
                config.QueryParameters.Top = 1;
                config.QueryParameters.Select = new[]
                {
                    "id", "displayName", "mail", "companyName",
                    "department", "jobTitle", "officeLocation"
                };
            });

            return response?.Value?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.Debug("조직 사용자 조회 실패 (권한 없음 가능): {Email} - {Error}", email, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 연락처 프로필 사진 가져오기
    /// </summary>
    /// <param name="contactId">연락처 ID</param>
    /// <returns>Base64 인코딩된 사진</returns>
    private async Task<string?> GetContactPhotoAsync(string? contactId)
    {
        if (string.IsNullOrEmpty(contactId))
            return null;

        try
        {
            var client = _authService.GetGraphClient();
            var photoStream = await client.Me.Contacts[contactId].Photo.Content.GetAsync();

            if (photoStream == null)
                return null;

            using var memoryStream = new MemoryStream();
            await photoStream.CopyToAsync(memoryStream);
            return Convert.ToBase64String(memoryStream.ToArray());
        }
        catch
        {
            // 사진이 없는 경우 예외 발생 - 무시
            return null;
        }
    }

    /// <summary>
    /// 조직 사용자 프로필 사진 가져오기
    /// </summary>
    /// <param name="userId">사용자 ID</param>
    /// <returns>Base64 인코딩된 사진</returns>
    private async Task<string?> GetOrganizationUserPhotoAsync(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
            return null;

        try
        {
            var client = _authService.GetGraphClient();
            var photoStream = await client.Users[userId].Photo.Content.GetAsync();

            if (photoStream == null)
                return null;

            using var memoryStream = new MemoryStream();
            await photoStream.CopyToAsync(memoryStream);
            return Convert.ToBase64String(memoryStream.ToArray());
        }
        catch
        {
            // 사진이 없는 경우 예외 발생 - 무시
            return null;
        }
    }

    /// <summary>
    /// VIP 캐시 갱신 (카테고리가 "VIP"인 연락처)
    /// </summary>
    private async Task RefreshVipCacheAsync()
    {
        try
        {
            var contacts = await GetContactsAsync(500);

            _vipContacts.Clear();

            foreach (var contact in contacts)
            {
                // "VIP" 또는 "중요" 카테고리가 있는 연락처
                if (contact.Categories?.Any(c =>
                    c.Equals("VIP", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("중요", StringComparison.OrdinalIgnoreCase) ||
                    c.Equals("Important", StringComparison.OrdinalIgnoreCase)) == true)
                {
                    foreach (var emailAddress in contact.EmailAddresses ?? Enumerable.Empty<EmailAddress>())
                    {
                        if (!string.IsNullOrEmpty(emailAddress.Address))
                        {
                            _vipContacts.Add(emailAddress.Address);
                        }
                    }
                }
            }

            // PriorityCalculator에도 동기화
            _priorityCalculator.SetVipSenders(_vipContacts);

            _vipCacheExpiry = DateTime.Now.AddMinutes(VipCacheMinutes);
            _logger.Debug("VIP 캐시 갱신: {Count}명", _vipContacts.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "VIP 캐시 갱신 실패");
            _vipCacheExpiry = DateTime.Now.AddMinutes(5); // 실패 시 5분 후 재시도
        }
    }

    /// <summary>
    /// OData 문자열 이스케이프
    /// </summary>
    private string EscapeODataString(string value)
    {
        return value.Replace("'", "''");
    }
}

/// <summary>
/// 보강된 발신자 정보 DTO
/// </summary>
public class EnrichedSenderInfo
{
    /// <summary>
    /// 이메일 주소
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 표시 이름
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// 회사명
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// 부서
    /// </summary>
    public string? Department { get; set; }

    /// <summary>
    /// 직위
    /// </summary>
    public string? JobTitle { get; set; }

    /// <summary>
    /// 프로필 사진 (Base64)
    /// </summary>
    public string? PhotoBase64 { get; set; }

    /// <summary>
    /// 연락처에 있는지 여부
    /// </summary>
    public bool IsInContacts { get; set; }

    /// <summary>
    /// 조직 내 사용자인지 여부
    /// </summary>
    public bool IsInOrganization { get; set; }

    /// <summary>
    /// VIP 여부
    /// </summary>
    public bool IsVip { get; set; }

    /// <summary>
    /// 표시용 이름 (DisplayName이 없으면 Email)
    /// </summary>
    public string GetDisplayNameOrEmail()
    {
        return !string.IsNullOrEmpty(DisplayName) ? DisplayName : Email;
    }

    /// <summary>
    /// 직함 문자열 (부서 - 직위)
    /// </summary>
    public string? GetPositionString()
    {
        if (!string.IsNullOrEmpty(Department) && !string.IsNullOrEmpty(JobTitle))
            return $"{Department} - {JobTitle}";

        return Department ?? JobTitle;
    }
}
