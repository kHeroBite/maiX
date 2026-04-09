namespace mAIx.Queries;

/// <summary>
/// 이메일 FTS5 전문 검색 쿼리 정의
/// EF Core LINQ로 표현 불가능한 FTS5 MATCH 전용 Raw SQL
/// </summary>
public static class EmailFtsQueries
{
    /// <summary>
    /// FTS5 MATCH 검색 — 키워드로 Email rowid 목록 반환
    /// {0} 위치에 이스케이프된 검색어를 삽입 (큰따옴표로 감싼 형태)
    /// 예: SELECT rowid FROM EmailsFts WHERE EmailsFts MATCH '"검색어"'
    /// </summary>
    public const string MatchByKeyword = "SELECT rowid FROM EmailsFts WHERE EmailsFts MATCH '\"";

    /// <summary>
    /// FTS5 MATCH 쿼리 생성 — 키워드 이스케이프 포함
    /// </summary>
    /// <param name="keywords">검색 키워드 (이스케이프 전 원본)</param>
    /// <returns>완성된 FTS5 MATCH SQL 문자열</returns>
    public static string BuildMatchQuery(string keywords)
    {
        // FTS5 구문 내 큰따옴표 이스케이프 (doubled quote 방식)
        var escaped = keywords.Replace("\"", "\"\"");
        return $"SELECT rowid FROM EmailsFts WHERE EmailsFts MATCH '\"{escaped}\"'";
    }

    /// <summary>
    /// AttachmentsFts MATCH 쿼리 생성 — 키워드 이스케이프 포함
    /// </summary>
    /// <param name="keywords">검색 키워드 (이스케이프 전 원본)</param>
    /// <returns>완성된 FTS5 MATCH SQL 문자열</returns>
    public static string BuildAttachmentMatchQuery(string keywords)
    {
        var escaped = keywords.Replace("\"", "\"\"");
        return $"SELECT rowid FROM AttachmentsFts WHERE AttachmentsFts MATCH '\"{escaped}\"'";
    }

    /// <summary>
    /// 가중치 기반 FTS5 MATCH 검색 쿼리 생성
    /// bm25 가중치: 제목(10) > 발신자이름(5) > 발신자이메일(3) > 본문(1)
    /// 날짜 boost: 2020-01-01 기준 경과 일수 비례로 최신 메일 상위 노출
    /// bm25 반환값은 음수이므로 ORDER BY ASC = 관련도 높은 순
    /// </summary>
    /// <param name="keywords">검색 키워드 (이스케이프 전 원본)</param>
    /// <returns>완성된 가중치 정렬 SQL 문자열</returns>
    public static string BuildWeightedMatchQuery(string keywords)
    {
        var escaped = keywords.Replace("\"", "\"\"");
        var ftsQuery = $"\"{escaped}\"";
        return $@"SELECT f.rowid,
    (bm25(EmailsFts, 10.0, 1.0, 5.0, 3.0)
     - (JULIANDAY(e.ReceivedDateTime) - JULIANDAY('2020-01-01')) / 2000.0 * 2.0) AS score
FROM EmailsFts f
INNER JOIN Emails e ON e.Id = f.rowid
WHERE EmailsFts MATCH '{ftsQuery}'
ORDER BY score ASC
LIMIT 500";
    }
}
