using System;

namespace mailX.Services.Search;

/// <summary>
/// 이메일 검색 쿼리 모델 - 다양한 검색 조건 정의
/// </summary>
public class SearchQuery
{
    /// <summary>
    /// 검색 키워드 (제목, 본문, 발신자, 수신자에서 검색)
    /// </summary>
    public string? Keywords { get; set; }

    /// <summary>
    /// 검색 시작 날짜 (수신일 기준)
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// 검색 종료 날짜 (수신일 기준)
    /// </summary>
    public DateTime? ToDate { get; set; }

    /// <summary>
    /// 폴더 ID 필터
    /// </summary>
    public string? FolderId { get; set; }

    /// <summary>
    /// 첨부파일 유무 필터 (null: 무관, true: 첨부파일 있음, false: 첨부파일 없음)
    /// </summary>
    public bool? HasAttachments { get; set; }

    /// <summary>
    /// 읽음 상태 필터 (null: 무관, true: 읽음, false: 안 읽음)
    /// </summary>
    public bool? IsRead { get; set; }

    /// <summary>
    /// 우선순위 점수 최소값 (0-100)
    /// </summary>
    public int? MinPriority { get; set; }

    /// <summary>
    /// 우선순위 점수 최대값 (0-100)
    /// </summary>
    public int? MaxPriority { get; set; }

    /// <summary>
    /// 우선순위 레벨 필터 (critical, high, medium, low)
    /// </summary>
    public string? PriorityLevel { get; set; }

    /// <summary>
    /// 발신자 이메일 또는 이름 필터
    /// </summary>
    public string? Sender { get; set; }

    /// <summary>
    /// 수신자 이메일 또는 이름 필터
    /// </summary>
    public string? Recipient { get; set; }

    /// <summary>
    /// 계정 이메일 필터 (다중 계정 지원)
    /// </summary>
    public string? AccountEmail { get; set; }

    /// <summary>
    /// 분석 완료된 메일만 검색
    /// </summary>
    public bool? IsAnalyzed { get; set; }

    /// <summary>
    /// 마감일이 있는 메일만 검색
    /// </summary>
    public bool? HasDeadline { get; set; }

    /// <summary>
    /// 비업무 메일 제외
    /// </summary>
    public bool ExcludeNonBusiness { get; set; } = true;

    /// <summary>
    /// 정렬 기준 (receivedDateTime, priorityScore, deadline)
    /// </summary>
    public string OrderBy { get; set; } = "receivedDateTime";

    /// <summary>
    /// 정렬 방향 (true: 내림차순, false: 오름차순)
    /// </summary>
    public bool OrderDescending { get; set; } = true;

    /// <summary>
    /// 페이지 번호 (1부터 시작)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// 페이지 크기
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// 검색 쿼리가 비어있는지 확인
    /// </summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Keywords) &&
        !FromDate.HasValue &&
        !ToDate.HasValue &&
        string.IsNullOrWhiteSpace(FolderId) &&
        !HasAttachments.HasValue &&
        !IsRead.HasValue &&
        !MinPriority.HasValue &&
        !MaxPriority.HasValue &&
        string.IsNullOrWhiteSpace(PriorityLevel) &&
        string.IsNullOrWhiteSpace(Sender) &&
        string.IsNullOrWhiteSpace(Recipient);
}

/// <summary>
/// 검색 결과 하이라이트 정보
/// </summary>
public class SearchHighlight
{
    /// <summary>
    /// 하이라이트된 제목 (검색어 부분이 <mark>로 감싸짐)
    /// </summary>
    public string? HighlightedSubject { get; set; }

    /// <summary>
    /// 하이라이트된 본문 스니펫
    /// </summary>
    public string? HighlightedBody { get; set; }

    /// <summary>
    /// 하이라이트된 발신자
    /// </summary>
    public string? HighlightedSender { get; set; }

    /// <summary>
    /// 매칭된 키워드 목록
    /// </summary>
    public List<string> MatchedKeywords { get; set; } = new();
}

/// <summary>
/// 검색 결과 모델
/// </summary>
public class SearchResult
{
    /// <summary>
    /// 검색된 이메일 ID
    /// </summary>
    public int EmailId { get; set; }

    /// <summary>
    /// 하이라이트 정보
    /// </summary>
    public SearchHighlight Highlight { get; set; } = new();

    /// <summary>
    /// 검색 관련도 점수 (높을수록 관련도 높음)
    /// </summary>
    public double RelevanceScore { get; set; }
}

/// <summary>
/// 페이징된 검색 결과
/// </summary>
/// <typeparam name="T">결과 타입</typeparam>
public class PagedSearchResult<T>
{
    /// <summary>
    /// 검색 결과 목록
    /// </summary>
    public List<T> Items { get; set; } = new();

    /// <summary>
    /// 전체 결과 수
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 현재 페이지
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// 페이지 크기
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// 전체 페이지 수
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>
    /// 다음 페이지 존재 여부
    /// </summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>
    /// 이전 페이지 존재 여부
    /// </summary>
    public bool HasPreviousPage => Page > 1;

    /// <summary>
    /// 검색 소요 시간 (밀리초)
    /// </summary>
    public long SearchDurationMs { get; set; }
}
