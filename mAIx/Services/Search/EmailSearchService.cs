using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NLog;
using mAIx.Data;
using mAIx.Models;
using mAIx.Queries;

namespace mAIx.Services.Search;

/// <summary>
/// 이메일 검색 서비스 - 전문 검색, 필터링, 하이라이트 지원
/// SQLite LIKE 쿼리 기반 검색 (향후 FTS5 확장 가능)
/// </summary>
public class EmailSearchService
{
    private readonly mAIxDbContext _dbContext;
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    // 검색 결과 캐시 (간단한 메모리 캐시)
    private static readonly Dictionary<string, (DateTime CachedAt, PagedSearchResult<Email> Result)> _cache = new();
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const int MaxCacheSize = 100;

    public EmailSearchService(mAIxDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
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
                _log.Debug("검색 캐시 히트: {Key}", cacheKey);
                return cachedResult!;
            }

            // FTS5 우선 시도 → 실패 시 LIKE 폴백
            IQueryable<Email> dbQuery;
            List<int>? fts5OrderedIds = null;
            if (!string.IsNullOrWhiteSpace(query.Keywords))
            {
                try
                {
                    var 가중치결과 = await SearchWithFts5Async(query.Keywords, ct).ConfigureAwait(false);
                    if (가중치결과.Count > 0)
                    {
                        // score ASC 정렬된 ID 순서 보존
                        fts5OrderedIds = 가중치결과.Select(r => r.Id).ToList();
                        dbQuery = BuildQueryWithIds(query, fts5OrderedIds);
                    }
                    else
                    {
                        dbQuery = BuildQuery(query);
                    }
                    _log.Debug("FTS5 가중치 검색: {Count}건 매칭", 가중치결과.Count);
                }
                catch (Exception ex)
                {
                    _log.Warn(ex, "FTS5 검색 실패 — LIKE 폴백");
                    dbQuery = BuildQuery(query);
                }
            }
            else
            {
                dbQuery = BuildQuery(query);
            }

            // 전체 개수 조회
            var totalCount = await dbQuery.CountAsync(ct).ConfigureAwait(false);

            // 정렬 적용 (FTS5 가중치 결과가 있으면 score 순서 우선, 없으면 기본 정렬)
            List<Email> items;
            if (fts5OrderedIds != null && fts5OrderedIds.Count > 0)
            {
                // FTS5 score 순서 보존: 메모리에서 정렬 (LIMIT 500 이내)
                var 페이지시작 = (query.Page - 1) * query.PageSize;
                var 페이지아이디 = fts5OrderedIds.Skip(페이지시작).Take(query.PageSize).ToList();
                var 페이지이메일맵 = await _dbContext.Emails.AsNoTracking()
                    .Where(e => 페이지아이디.Contains(e.Id))
                    .ToListAsync(ct).ConfigureAwait(false);
                // score 순서에 맞게 재정렬
                items = 페이지아이디
                    .Select(id => 페이지이메일맵.FirstOrDefault(e => e.Id == id))
                    .Where(e => e != null)
                    .Select(e => e!)
                    .ToList();
            }
            else
            {
                dbQuery = ApplyOrdering(dbQuery, query);
                items = await dbQuery
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .ToListAsync(ct).ConfigureAwait(false);
            }

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

            _log.Info("이메일 검색 완료: {Count}건 / {TotalCount}건 ({Time}ms)",
                items.Count, totalCount, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "이메일 검색 실패");
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
    /// FTS5 가중치 검색 — (Email ID, score) 목록 반환 (EmailsFts 가상 테이블 사용)
    /// bm25 가중치: 제목(10) > 발신자이름(5) > 발신자이메일(3) > 본문(1) + 날짜 boost
    /// score ASC 정렬 = 관련도 높은 순 (bm25는 음수 반환)
    /// AttachmentsFts 매칭 결과는 score = +999.0 (후순위)으로 병합
    /// </summary>
    /// <param name="keywords">검색 키워드</param>
    /// <param name="ct">취소 토큰</param>
    /// <returns>score ASC 정렬된 (Id, Score) 목록</returns>
    public async Task<List<(int Id, double Score)>> SearchWithFts5Async(string keywords, CancellationToken ct = default)
    {
        var 결과 = new List<(int Id, double Score)>();
        if (string.IsNullOrWhiteSpace(keywords))
            return 결과;

        // 2글자 미만 단독 쿼리는 FTS5 trigram이 지원 불가 → LIKE 폴백
        var (ftsQuery, needsLikeFallback) = mAIx.Utils.TextPreprocessor.BuildFtsQuery(keywords);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return 결과;

        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct).ConfigureAwait(false);

        // EmailsFts 가중치 검색 (bm25 + 날짜 boost)
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = EmailFtsQueries.BuildWeightedMatchQuery(keywords);
            using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetInt32(0);
                var score = reader.GetDouble(1);
                결과.Add((id, score));
            }
        }

        // 이미 매칭된 ID 집합
        var 이미매칭 = new HashSet<int>(결과.Select(r => r.Id));

        // AttachmentsFts 검색 → score = +999.0 (후순위) 으로 병합
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT DISTINCT a.EmailId FROM Attachments a " +
                              $"INNER JOIN AttachmentsFts af ON af.rowid = a.Id " +
                              $"WHERE af.AttachmentsFts MATCH '{keywords.Replace("'", "''")}'";
            try
            {
                using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var emailId = reader.GetInt32(0);
                    if (!이미매칭.Contains(emailId))
                    {
                        결과.Add((emailId, 999.0));
                        이미매칭.Add(emailId);
                    }
                }
            }
            catch
            {
                // AttachmentsFts 미존재 시 무시
            }
        }

        // score ASC 정렬 (관련도 높은 순)
        결과.Sort((a, b) => a.Score.CompareTo(b.Score));
        return 결과;
    }

    /// <summary>
    /// FTS5 결과 ID 목록으로 필터된 쿼리 빌드 (키워드 LIKE 필터 제외)
    /// </summary>
    private IQueryable<Email> BuildQueryWithIds(SearchQuery query, List<int> emailIds)
    {
        var dbQuery = _dbContext.Emails.AsNoTracking().Where(e => emailIds.Contains(e.Id));

        if (!string.IsNullOrWhiteSpace(query.AccountEmail))
            dbQuery = dbQuery.Where(e => e.AccountEmail == query.AccountEmail);

        if (query.FromDate.HasValue)
            dbQuery = dbQuery.Where(e => e.ReceivedDateTime >= query.FromDate.Value);

        if (query.ToDate.HasValue)
        {
            var toDateEnd = query.ToDate.Value.Date.AddDays(1).AddTicks(-1);
            dbQuery = dbQuery.Where(e => e.ReceivedDateTime <= toDateEnd);
        }

        if (!string.IsNullOrWhiteSpace(query.FolderId))
            dbQuery = dbQuery.Where(e => e.ParentFolderId == query.FolderId);

        if (query.HasAttachments.HasValue)
            dbQuery = dbQuery.Where(e => e.HasAttachments == query.HasAttachments.Value);

        if (query.IsRead.HasValue)
            dbQuery = dbQuery.Where(e => e.IsRead == query.IsRead.Value);

        if (query.MinPriority.HasValue)
            dbQuery = dbQuery.Where(e => e.PriorityScore >= query.MinPriority.Value);

        if (query.MaxPriority.HasValue)
            dbQuery = dbQuery.Where(e => e.PriorityScore <= query.MaxPriority.Value);

        if (!string.IsNullOrWhiteSpace(query.PriorityLevel))
            dbQuery = dbQuery.Where(e => e.PriorityLevel == query.PriorityLevel);

        if (!string.IsNullOrWhiteSpace(query.Sender))
        {
            var sender = query.Sender.ToLower();
            dbQuery = dbQuery.Where(e => e.From != null && e.From.ToLower().Contains(sender));
        }

        if (!string.IsNullOrWhiteSpace(query.Recipient))
        {
            var recipient = query.Recipient.ToLower();
            dbQuery = dbQuery.Where(e => e.To != null && e.To.ToLower().Contains(recipient));
        }

        if (query.IsAnalyzed.HasValue)
        {
            if (query.IsAnalyzed.Value)
                dbQuery = dbQuery.Where(e => e.AnalysisStatus == "completed");
            else
                dbQuery = dbQuery.Where(e => e.AnalysisStatus != "completed");
        }

        if (query.HasDeadline.HasValue)
        {
            if (query.HasDeadline.Value)
                dbQuery = dbQuery.Where(e => e.Deadline != null);
            else
                dbQuery = dbQuery.Where(e => e.Deadline == null);
        }

        if (query.ExcludeNonBusiness)
            dbQuery = dbQuery.Where(e => !e.IsNonBusiness);

        return dbQuery;
    }

    /// <summary>
    /// 검색 쿼리 빌드
    /// </summary>
    private IQueryable<Email> BuildQuery(SearchQuery query)
    {
        var dbQuery = _dbContext.Emails.AsNoTracking();

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
        _log.Debug("검색 캐시 무효화됨");
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

        _log.Debug("계정 검색 캐시 무효화됨: {Account}, {Count}건", accountEmail, keysToRemove.Count);
    }
}
