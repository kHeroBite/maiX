using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph.Models;
using mAIx.Data;
using mAIx.Models;
using mAIx.Services.Cache;
using mAIx.Services.Graph;
using mAIx.Utils;
using NLog;

namespace mAIx.Services.Search;

/// <summary>
/// 연락처 통합 검색 서비스
/// - 로컬 DB (최근 메일 발신자)
/// - Microsoft Graph 연락처 (개인 주소록)
/// - 조직 디렉터리 (회사 동료)
/// </summary>
public class ContactSearchService
{
    private readonly mAIxDbContext _dbContext;
    private readonly GraphContactService _contactService;
    private readonly GraphAuthService _authService;
    private readonly ProfilePhotoCacheService _photoCacheService;
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    // 캐시 (10분 유효)
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private const int CacheExpirationMinutes = 10;
    private const int MaxResults = 20;
    private const int MinQueryLength = 2;

    // 디바운싱용 타이머
    private CancellationTokenSource? _debounceTokenSource;
    private const int DebounceDelayMs = 300;

    public ContactSearchService(
        mAIxDbContext dbContext,
        GraphContactService contactService,
        GraphAuthService authService,
        ProfilePhotoCacheService photoCacheService)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _contactService = contactService ?? throw new ArgumentNullException(nameof(contactService));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _photoCacheService = photoCacheService ?? throw new ArgumentNullException(nameof(photoCacheService));
    }

    /// <summary>
    /// 연락처 통합 검색 (디바운싱 적용)
    /// </summary>
    /// <param name="query">검색어 (최소 2자)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>연락처 제안 목록</returns>
    public async Task<List<ContactSuggestion>> SearchContactsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < MinQueryLength)
            return new List<ContactSuggestion>();

        var normalizedQuery = query.Trim().ToLower();

        // 캐시 확인
        if (_cache.TryGetValue(normalizedQuery, out var cached) && !cached.IsExpired)
        {
            _log.Debug("캐시 히트: {Query}", query);
            return cached.Results;
        }

        try
        {
            // 병렬로 3가지 소스에서 검색
            var localTask = SearchLocalContactsAsync(query);
            var graphContactsTask = SearchGraphContactsAsync(query);
            var organizationTask = SearchOrganizationUsersAsync(query);

            await Task.WhenAll(localTask, graphContactsTask, organizationTask).ConfigureAwait(false);

            var results = new List<ContactSuggestion>();

            // 결과 병합 (우선순위: 로컬 > 연락처 > 조직)
            results.AddRange(await localTask.ConfigureAwait(false));
            results.AddRange(await graphContactsTask.ConfigureAwait(false));
            results.AddRange(await organizationTask.ConfigureAwait(false));

            // 중복 제거 (이메일 기준, 로컬 우선)
            var deduplicated = results
                .GroupBy(c => c.Email.ToLower())
                .Select(g => g.OrderBy(c => (int)c.Source).First())
                .ToList();

            // 정렬: 빈도 내림차순 → 소스 오름차순 → 이름 오름차순
            var sorted = deduplicated
                .OrderByDescending(c => c.ContactFrequency)
                .ThenBy(c => (int)c.Source)
                .ThenBy(c => c.DisplayName)
                .Take(MaxResults)
                .ToList();

            // 캐시에 저장
            _cache[normalizedQuery] = new CacheEntry(sorted);

            _log.Debug("연락처 검색 완료: Query={Query}, Count={Count}", query, sorted.Count);
            return sorted;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "연락처 검색 실패: {Query}", query);
            return new List<ContactSuggestion>();
        }
    }

    /// <summary>
    /// 디바운싱 적용 검색 (입력 중 호출용)
    /// </summary>
    /// <param name="query">검색어</param>
    /// <param name="onResult">결과 콜백</param>
    public async Task SearchWithDebounceAsync(
        string query,
        Action<List<ContactSuggestion>> onResult)
    {
        // 이전 요청 취소
        _debounceTokenSource?.Cancel();
        _debounceTokenSource = new CancellationTokenSource();

        try
        {
            // 300ms 대기
            await Task.Delay(DebounceDelayMs, _debounceTokenSource.Token).ConfigureAwait(false);

            // 검색 실행
            var results = await SearchContactsAsync(query, _debounceTokenSource.Token).ConfigureAwait(false);
            onResult?.Invoke(results);
        }
        catch (TaskCanceledException)
        {
            // 취소됨 - 정상 흐름
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "디바운스 검색 실패: {Query}", query);
            onResult?.Invoke(new List<ContactSuggestion>());
        }
    }

    /// <summary>
    /// 로컬 DB에서 최근 메일 발신자 검색
    /// </summary>
    private async Task<List<ContactSuggestion>> SearchLocalContactsAsync(string query)
    {
        try
        {
            // 발신자 필드에서 검색 + 빈도 계산
            var emails = await _dbContext.Emails
                .GroupBy(e => e.From)
                .Select(g => new { From = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(100)
                .ToListAsync().ConfigureAwait(false);

            // 메모리에서 필터링 + BigramHelper 유사도 점수
            var filtered = emails
                .Where(x => !string.IsNullOrEmpty(x.From) &&
                           (x.From.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            ExtractDisplayName(x.From).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            BigramHelper.Score(query, x.From!) >= 0.3 ||
                            BigramHelper.Score(query, ExtractDisplayName(x.From!)) >= 0.3))
                .OrderByDescending(x => Math.Max(
                    BigramHelper.Score(query, x.From ?? ""),
                    BigramHelper.Score(query, ExtractDisplayName(x.From ?? ""))))
                .Take(MaxResults / 2) // 로컬은 최대 10개
                .ToList();

            var suggestions = new List<ContactSuggestion>();

            foreach (var item in filtered)
            {
                var displayName = ExtractDisplayName(item.From!);
                var email = ExtractEmail(item.From!);

                if (!string.IsNullOrEmpty(email))
                {
                    suggestions.Add(new ContactSuggestion
                    {
                        DisplayName = displayName,
                        Email = email,
                        Source = ContactSource.Local,
                        ContactFrequency = item.Count
                    });
                }
            }

            _log.Debug("로컬 검색 완료: {Count}건", suggestions.Count);
            return suggestions;
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "로컬 연락처 검색 실패");
            return new List<ContactSuggestion>();
        }
    }

    /// <summary>
    /// Graph API 연락처 검색
    /// </summary>
    private async Task<List<ContactSuggestion>> SearchGraphContactsAsync(string query)
    {
        try
        {
            // 인증되지 않았으면 건너뜀
            if (!_authService.IsLoggedIn)
                return new List<ContactSuggestion>();

            var contacts = await _contactService.SearchContactsAsync(query).ConfigureAwait(false);

            var suggestions = contacts
                .SelectMany(c => c.EmailAddresses ?? Enumerable.Empty<EmailAddress>(),
                    (contact, email) => new ContactSuggestion
                    {
                        DisplayName = contact.DisplayName ?? "",
                        Email = email.Address ?? "",
                        Department = contact.Department,
                        JobTitle = contact.JobTitle,
                        CompanyName = contact.CompanyName,
                        Source = ContactSource.Contact,
                        ContactFrequency = 0,
                        ContactId = contact.Id // 프로필 사진 조회용 ID
                    })
                .Where(s => !string.IsNullOrEmpty(s.Email))
                .Take(MaxResults / 3) // 연락처는 최대 6개
                .ToList();

            _log.Debug("Graph 연락처 검색 완료: {Count}건", suggestions.Count);
            return suggestions;
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "Graph 연락처 검색 실패");
            return new List<ContactSuggestion>();
        }
    }

    /// <summary>
    /// 조직 디렉터리 사용자 검색
    /// </summary>
    private async Task<List<ContactSuggestion>> SearchOrganizationUsersAsync(string query)
    {
        try
        {
            // 인증되지 않았으면 건너뜀
            if (!_authService.IsLoggedIn)
                return new List<ContactSuggestion>();

            var client = _authService.GetGraphClient();

            // OData 검색 쿼리
            var escapedQuery = EscapeODataString(query);
            var filter = $"startswith(displayName,'{escapedQuery}') " +
                        $"or startswith(givenName,'{escapedQuery}') " +
                        $"or startswith(surname,'{escapedQuery}') " +
                        $"or startswith(mail,'{escapedQuery}')";

            var response = await client.Users.GetAsync(config =>
            {
                config.QueryParameters.Filter = filter;
                config.QueryParameters.Top = MaxResults / 3;
                config.QueryParameters.Select = new[]
                {
                    "id", "displayName", "mail", "userPrincipalName",
                    "department", "jobTitle", "companyName"
                };
            }).ConfigureAwait(false);

            var suggestions = (response?.Value ?? new List<User>())
                .Where(u => !string.IsNullOrEmpty(u.Mail) || !string.IsNullOrEmpty(u.UserPrincipalName))
                .Select(u => new ContactSuggestion
                {
                    DisplayName = u.DisplayName ?? "",
                    Email = u.Mail ?? u.UserPrincipalName ?? "",
                    Department = u.Department,
                    JobTitle = u.JobTitle,
                    CompanyName = u.CompanyName,
                    Source = ContactSource.Organization,
                    ContactFrequency = 0,
                    ContactId = u.Id // 프로필 사진 조회용 ID
                })
                .ToList();

            _log.Debug("조직 사용자 검색 완료: {Count}건", suggestions.Count);
            return suggestions;
        }
        catch (Exception ex)
        {
            // 권한 없음 등 - 무시
            _log.Debug("조직 사용자 검색 실패 (권한 없음 가능): {Error}", ex.Message);
            return new List<ContactSuggestion>();
        }
    }

    /// <summary>
    /// 캐시 정리
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        _log.Debug("연락처 캐시 정리됨");
    }

    /// <summary>
    /// 만료된 캐시 항목 정리
    /// </summary>
    public void CleanupExpiredCache()
    {
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _log.Debug("만료된 캐시 {Count}건 정리됨", expiredKeys.Count);
        }
    }

    /// <summary>
    /// 연락처 목록에 프로필 사진 비동기 로딩
    /// 팝업 표시 후 호출하여 UI 차단 없이 사진 로드
    /// </summary>
    /// <param name="suggestions">연락처 목록</param>
    public async Task EnrichWithPhotosAsync(IEnumerable<ContactSuggestion> suggestions)
    {
        try
        {
            await _photoCacheService.LoadPhotosAsync(suggestions).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Warn(ex, "프로필 사진 로딩 실패");
        }
    }

    #region Helper Methods

    /// <summary>
    /// 이메일 문자열에서 표시 이름 추출
    /// </summary>
    private static string ExtractDisplayName(string emailString)
    {
        if (string.IsNullOrEmpty(emailString)) return "";

        var ltIndex = emailString.IndexOf('<');
        if (ltIndex > 0)
        {
            return emailString.Substring(0, ltIndex).Trim().Trim('"');
        }

        // @ 앞부분을 이름으로 사용
        var atIndex = emailString.IndexOf('@');
        if (atIndex > 0)
        {
            return emailString.Substring(0, atIndex);
        }

        return emailString;
    }

    /// <summary>
    /// 이메일 문자열에서 이메일 주소 추출
    /// </summary>
    private static string ExtractEmail(string emailString)
    {
        if (string.IsNullOrEmpty(emailString)) return "";

        var ltIndex = emailString.IndexOf('<');
        var gtIndex = emailString.IndexOf('>');

        if (ltIndex >= 0 && gtIndex > ltIndex)
        {
            return emailString.Substring(ltIndex + 1, gtIndex - ltIndex - 1).Trim();
        }

        if (emailString.Contains('@'))
        {
            return emailString.Trim();
        }

        return "";
    }

    /// <summary>
    /// OData 문자열 이스케이프
    /// </summary>
    private static string EscapeODataString(string value)
    {
        return value.Replace("'", "''");
    }

    #endregion

    #region Cache Entry

    /// <summary>
    /// 캐시 항목
    /// </summary>
    private class CacheEntry
    {
        public List<ContactSuggestion> Results { get; }
        public DateTime CreatedAt { get; }

        public bool IsExpired =>
            DateTime.Now > CreatedAt.AddMinutes(CacheExpirationMinutes);

        public CacheEntry(List<ContactSuggestion> results)
        {
            Results = results;
            CreatedAt = DateTime.Now;
        }
    }

    #endregion
}
