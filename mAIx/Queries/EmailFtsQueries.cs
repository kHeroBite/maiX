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
}
