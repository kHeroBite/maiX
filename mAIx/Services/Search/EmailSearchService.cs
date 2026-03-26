using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using MaiX.Data;
using MaiX.Models;

namespace MaiX.Services.Search;

/// <summary>
/// 이메일 검색 서비스 - 전문 검색, 필터링, 하이라이트 지원
/// SQLite LIKE 쿼리 기반 검색 (향후 FTS5 확장 가능)
/// </summary>
public class EmailSearchService
{
    private readonly MaiXDbContext _dbContext;
    private readonly ILogger _logger;

    // 검색 결과 캐시 (간단한 메모리 캐시)
    private static readonly Dictionary<string, (DateTime CachedAt, PagedSearchResult<Email> Result)> _cache = new();
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const int MaxCacheSize = 100;

    public EmailSearchService(MaiXDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = Log.ForContext<EmailSearchService>();
    }

    /// <summary>
    /// 이메일 검색 실행
    /// </summary>
    /// <param name="query">검색 쿼리</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>페이징된 검색 결과</returns>
    public async Task<PagedSearchResult<Email>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 캐시 확인
            var cacheKey = GetCacheKey(query);
            if (TryGetFromCache(cacheKey, out var cachedResult))
            {
                _logger.Debug("검색 캐시 히트: {Key}", cacheKey);
                return cachedResult!;
            }

            // 쿼리 빌드
            var dbQuery = BuildQuery(query);

            // 전체 개수 조회
            var totalCount = await dbQuery.CountAsync(ct);

            // 정렬 적용
            dbQuery = ApplyOrdering(dbQuery, query);

            // 페이징 적용
            var items = await dbQuery
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(e => e.Attachments)
                .ToListAsync(ct);

            stopwatch.Stop();

            var result = new PagedSearchResult<Email>
            {
                Items = items,
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                SearchDurationMs = stopwatch.ElapsedMilliseconds
            };

            // 캐시 저장
            AddToCache(cacheKey, result);

            _logger.Information("이메일 검색 완료: {Count}건 / {TotalCount}건 ({Time}ms)",
                items.Count, totalCount, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "이메일 검색 실패");
            throw;
        }
    }

    /// <summary>
    /// 검색 결과에 하이라이트 적용
    /// </summary>
    /// <param name="emails">검색된 이메일 목록</param>
    /// <param name="keywords">검색 키워드</param>
    /// <returns>하이라이트 정보가 포함된 검색 결과</returns>
    public List<SearchResult> ApplyHighlights(IEnumerable<Email> emails, string? keywords)
    {
        var results = new List<SearchResult>();

        if (string.IsNullOrWhiteSpace(keywords))
        {
            // 키워드가 없으면 하이라이트 없이 반환
            foreach (var email in emails)
            {
                results.Add(new SearchResult
                {
                    EmailId = email.Id,
                    RelevanceScore = 1.0
                });
            }
            return results;
        }

        // 키워드 분리 (공백으로 구분)
        var keywordList = keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim().ToLower())
            .Where(k => k.Length >= 2)
            .Distinct()
            .ToList();

        foreach (var email in emails)
        {
            var highlight = new SearchHighlight();
            var matchedKeywords = new List<string>();
            var relevanceScore = 0.0;

            // 제목 하이라이트
            if (!string.IsNullOrEmpty(email.Subject))
            {
                var (highlightedSubject, subjectMatches) = HighlightText(email.Subject, keywordList);
                highlight.HighlightedSubject = highlightedSubject;
                matchedKeywords.AddRange(subjectMatches);
                relevanceScore += subjectMatches.Count * 2.0; // 제목 매칭은 가중치 2배
            }

            // 본문 하이라이트 (스니펫)
            if (!string.IsNullOrEmpty(email.Body))
            {
                var (highlightedBody, bodyMatches) = HighlightText(
                    GetBodySnippet(email.Body, keywordList), keywordList);
                highlight.HighlightedBody = highlightedBody;
                matchedKeywords.AddRange(bodyMatches);
                relevanceScore += bodyMatches.Count;
            }

            // 발신자 하이라이트
            if (!string.IsNullOrEmpty(email.From))
            {
                var (highlightedSender, senderMatches) = HighlightText(email.From, keywordList);
                highlight.HighlightedSender = highlightedSender;
                matchedKeywords.AddRange(senderMatches);
                relevanceScore += senderMatches.Count * 1.5; // 발신자 매칭은 가중치 1.5배
            }

            highlight.MatchedKeywords = matchedKeywords.Distinct().ToList();

            results.Add(new SearchResult
            {
                EmailId = email.Id,
                Highlight = highlight,
                RelevanceScore = relevanceScore
            });
        }

        // 관련도 순으로 정렬
        return results.OrderByDescending(r => r.RelevanceScore).ToList();
    }

    /// <summary>
    /// 최근 검색어 조회 (향후 검색 기록 기능용)
    /// </summary>
    /// <param name="accountEmail">계정 이메일</param>
    /// <param name="count">조회할 개수</param>
    /// <returns>최근 검색어 목록</returns>
    public Task<List<string>> GetRecentSearchesAsync(string accountEmail, int count = 10)
    {
        // 향후 검색 기록 테이블 추가 시 구현
        return Task.FromResult(new List<string>());
    }

    /// <summary>
    /// 검색 쿼리 빌드
    /// </summary>
    private IQueryable<Email> BuildQuery(SearchQuery query)
    {
        var dbQuery = _dbContext.Emails.AsQueryable();

        // 계정 필터
        if (!string.IsNullOrWhiteSpace(query.AccountEmail))
        {
            dbQuery = dbQuery.Where(e => e.AccountEmail == query.AccountEmail);
        }

        // 키워드 검색 (제목, 본문, 발신자, 수신자)
        if (!string.IsNullOrWhiteSpace(query.Keywords))
        {
            var keywords = query.Keywords.ToLower();
            dbQuery = dbQuery.Where(e =>
                (e.Subject != null && e.Subject.ToLower().Contains(keywords)) ||
                (e.Body != null && e.Body.ToLower().Contains(keywords)) ||
                (e.From != null && e.From.ToLower().Contains(keywords)) ||
                (e.To != null && e.To.ToLower().Contains(keywords)));
        }

        // 날짜 범위 필터
        if (query.FromDate.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.ReceivedDateTime >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            // ToDate는 해당 날짜의 끝까지 포함
            var toDateEnd = query.ToDate.Value.Date.AddDays(1).AddTicks(-1);
            dbQuery = dbQuery.Where(e => e.ReceivedDateTime <= toDateEnd);
        }

        // 폴더 필터
        if (!string.IsNullOrWhiteSpace(query.FolderId))
        {
            dbQuery = dbQuery.Where(e => e.ParentFolderId == query.FolderId);
        }

        // 첨부파일 필터
        if (query.HasAttachments.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.HasAttachments == query.HasAttachments.Value);
        }

        // 읽음 상태 필터
        if (query.IsRead.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.IsRead == query.IsRead.Value);
        }

        // 우선순위 점수 필터
        if (query.MinPriority.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.PriorityScore >= query.MinPriority.Value);
        }

        if (query.MaxPriority.HasValue)
        {
            dbQuery = dbQuery.Where(e => e.PriorityScore <= query.MaxPriority.Value);
        }

        // 우선순위 레벨 필터
        if (!string.IsNullOrWhiteSpace(query.PriorityLevel))
        {
            dbQuery = dbQuery.Where(e => e.PriorityLevel == query.PriorityLevel);
        }

        // 발신자 필터
        if (!string.IsNullOrWhiteSpace(query.Sender))
        {
            var sender = query.Sender.ToLower();
            dbQuery = dbQuery.Where(e => e.From != null && e.From.ToLower().Contains(sender));
        }

        // 수신자 필터
        if (!string.IsNullOrWhiteSpace(query.Recipient))
        {
            var recipient = query.Recipient.ToLower();
            dbQuery = dbQuery.Where(e => e.To != null && e.To.ToLower().Contains(recipient));
        }

        // 분석 완료 필터
        if (query.IsAnalyzed.HasValue)
        {
            if (query.IsAnalyzed.Value)
            {
                dbQuery = dbQuery.Where(e => e.AnalysisStatus == "completed");
            }
            else
            {
                dbQuery = dbQuery.Where(e => e.AnalysisStatus != "completed");
            }
        }

        // 마감일 필터
        if (query.HasDeadline.HasValue)
        {
            if (query.HasDeadline.Value)
            {
                dbQuery = dbQuery.Where(e => e.Deadline != null);
            }
            else
            {
                dbQuery = dbQuery.Where(e => e.Deadline == null);
            }
        }

        // 비업무 메일 제외
        if (query.ExcludeNonBusiness)
        {
            dbQuery = dbQuery.Where(e => !e.IsNonBusiness);
        }

        return dbQuery;
    }

    /// <summary>
    /// 정렬 적용
    /// </summary>
    private IQueryable<Email> ApplyOrdering(IQueryable<Email> query, SearchQuery searchQuery)
    {
        return searchQuery.OrderBy.ToLower() switch
        {
            "priorityscore" => searchQuery.OrderDescending
                ? query.OrderByDescending(e => e.PriorityScore)
                : query.OrderBy(e => e.PriorityScore),

            "deadline" => searchQuery.OrderDescending
                ? query.OrderByDescending(e => e.Deadline)
                : query.OrderBy(e => e.Deadline),

            "subject" => searchQuery.OrderDescending
                ? query.OrderByDescending(e => e.Subject)
                : query.OrderBy(e => e.Subject),

            "from" => searchQuery.OrderDescending
                ? query.OrderByDescending(e => e.From)
                : query.OrderBy(e => e.From),

            _ => searchQuery.OrderDescending
                ? query.OrderByDescending(e => e.ReceivedDateTime)
                : query.OrderBy(e => e.ReceivedDateTime)
        };
    }

    /// <summary>
    /// 텍스트에 하이라이트 적용
    /// </summary>
    private (string highlighted, List<string> matched) HighlightText(string text, List<string> keywords)
    {
        var matched = new List<string>();
        var result = text;

        foreach (var keyword in keywords)
        {
            // 대소문자 무시 검색
            var regex = new Regex(Regex.Escape(keyword), RegexOptions.IgnoreCase);

            if (regex.IsMatch(result))
            {
                matched.Add(keyword);
                result = regex.Replace(result, match => $"<mark>{match.Value}</mark>");
            }
        }

        return (result, matched);
    }

    /// <summary>
    /// 본문에서 키워드 주변 스니펫 추출
    /// </summary>
    private string GetBodySnippet(string body, List<string> keywords, int snippetLength = 200)
    {
        // HTML 태그 제거
        var plainText = Regex.Replace(body, "<[^>]*>", " ");
        plainText = Regex.Replace(plainText, @"\s+", " ").Trim();

        if (plainText.Length <= snippetLength)
            return plainText;

        // 첫 번째 키워드 위치 찾기
        foreach (var keyword in keywords)
        {
            var index = plainText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                // 키워드 주변 텍스트 추출
                var start = Math.Max(0, index - snippetLength / 2);
                var length = Math.Min(snippetLength, plainText.Length - start);

                var snippet = plainText.Substring(start, length);

                // 앞뒤 단어가 잘린 경우 ... 추가
                if (start > 0)
                    snippet = "..." + snippet;
                if (start + length < plainText.Length)
                    snippet += "...";

                return snippet;
            }
        }

        // 키워드를 찾지 못하면 앞부분 반환
        return plainText.Length > snippetLength
            ? plainText.Substring(0, snippetLength) + "..."
            : plainText;
    }

    /// <summary>
    /// 캐시 키 생성
    /// </summary>
    private string GetCacheKey(SearchQuery query)
    {
        return $"{query.AccountEmail}:{query.Keywords}:{query.FromDate}:{query.ToDate}:" +
               $"{query.FolderId}:{query.HasAttachments}:{query.IsRead}:{query.PriorityLevel}:" +
               $"{query.Sender}:{query.Recipient}:{query.OrderBy}:{query.OrderDescending}:" +
               $"{query.Page}:{query.PageSize}";
    }

    /// <summary>
    /// 캐시에서 결과 조회
    /// </summary>
    private bool TryGetFromCache(string key, out PagedSearchResult<Email>? result)
    {
        if (_cache.TryGetValue(key, out var cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < CacheExpiration)
            {
                result = cached.Result;
                return true;
            }

            // 만료된 캐시 제거
            _cache.Remove(key);
        }

        result = null;
        return false;
    }

    /// <summary>
    /// 캐시에 결과 저장
    /// </summary>
    private void AddToCache(string key, PagedSearchResult<Email> result)
    {
        // 캐시 크기 제한
        if (_cache.Count >= MaxCacheSize)
        {
            // 가장 오래된 항목 제거
            var oldestKey = _cache
                .OrderBy(kv => kv.Value.CachedAt)
                .First().Key;
            _cache.Remove(oldestKey);
        }

        _cache[key] = (DateTime.UtcNow, result);
    }

    /// <summary>
    /// 캐시 무효화 (데이터 변경 시 호출)
    /// </summary>
    public void InvalidateCache()
    {
        _cache.Clear();
        _logger.Debug("검색 캐시 무효화됨");
    }

    /// <summary>
    /// 특정 계정의 캐시만 무효화
    /// </summary>
    public void InvalidateCacheForAccount(string accountEmail)
    {
        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith($"{accountEmail}:"))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
        }

        _logger.Debug("계정 검색 캐시 무효화됨: {Account}, {Count}건", accountEmail, keysToRemove.Count);
    }
}
