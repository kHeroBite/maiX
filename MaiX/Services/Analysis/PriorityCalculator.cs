using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace MaiX.Services.Analysis;

/// <summary>
/// 우선순위 계산기 - 100점 기준 우선순위 점수 계산
/// </summary>
public class PriorityCalculator
{
    private readonly ILogger _logger;

    // VIP 발신자 목록 (설정에서 로드 가능하도록 확장 가능)
    private HashSet<string> _vipSenders = new(StringComparer.OrdinalIgnoreCase);

    // 상사/관리자 도메인 패턴
    private HashSet<string> _managerDomains = new(StringComparer.OrdinalIgnoreCase);

    // 고객사 도메인 목록
    private HashSet<string> _customerDomains = new(StringComparer.OrdinalIgnoreCase);

    // 키워드 가중치
    private static readonly Dictionary<string, int> KeywordWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        // 긴급 관련 (높은 가중치)
        ["긴급"] = 15,
        ["urgent"] = 15,
        ["asap"] = 14,
        ["immediately"] = 14,
        ["즉시"] = 14,

        // 마감 관련
        ["마감"] = 12,
        ["deadline"] = 12,
        ["기한"] = 11,
        ["due date"] = 11,
        ["납기"] = 11,

        // 계약/사업 관련
        ["계약"] = 10,
        ["contract"] = 10,
        ["입찰"] = 10,
        ["견적"] = 9,
        ["proposal"] = 9,
        ["rfp"] = 9,
        ["rfi"] = 8,

        // 중요 관련
        ["중요"] = 8,
        ["important"] = 8,
        ["critical"] = 10,
        ["필수"] = 8,

        // 요청/승인 관련
        ["승인"] = 7,
        ["approval"] = 7,
        ["검토"] = 6,
        ["review"] = 6,
        ["확인"] = 5,
        ["요청"] = 5,

        // 회의/일정 관련
        ["회의"] = 5,
        ["meeting"] = 5,
        ["미팅"] = 5,
        ["일정"] = 4,
        ["schedule"] = 4
    };

    public PriorityCalculator()
    {
        _logger = Log.ForContext<PriorityCalculator>();
    }

    /// <summary>
    /// VIP 발신자 목록 설정
    /// </summary>
    public void SetVipSenders(IEnumerable<string> senders)
    {
        _vipSenders = new HashSet<string>(senders, StringComparer.OrdinalIgnoreCase);
        _logger.Debug("VIP 발신자 {Count}명 설정", _vipSenders.Count);
    }

    /// <summary>
    /// 관리자 도메인 설정
    /// </summary>
    public void SetManagerDomains(IEnumerable<string> domains)
    {
        _managerDomains = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
        _logger.Debug("관리자 도메인 {Count}개 설정", _managerDomains.Count);
    }

    /// <summary>
    /// 고객사 도메인 설정
    /// </summary>
    public void SetCustomerDomains(IEnumerable<string> domains)
    {
        _customerDomains = new HashSet<string>(domains, StringComparer.OrdinalIgnoreCase);
        _logger.Debug("고객사 도메인 {Count}개 설정", _customerDomains.Count);
    }

    /// <summary>
    /// 우선순위 점수 계산
    /// </summary>
    /// <param name="importanceScore">중요도 점수 (0-100)</param>
    /// <param name="urgencyScore">긴급도 점수 (0-100)</param>
    /// <param name="senderEmail">발신자 이메일</param>
    /// <param name="keywords">추출된 키워드 목록</param>
    /// <param name="hasDeadline">마감일 존재 여부</param>
    /// <param name="deadlineDays">마감일까지 남은 일수 (null이면 마감일 없음)</param>
    /// <returns>우선순위 계산 결과</returns>
    public PriorityResult Calculate(
        int importanceScore,
        int urgencyScore,
        string senderEmail,
        List<string> keywords,
        bool hasDeadline,
        int? deadlineDays)
    {
        var result = new PriorityResult();
        var details = new List<string>();

        // 1. 중요도 점수 (최대 40점)
        int importancePoints = (int)Math.Round(importanceScore * 0.4);
        importancePoints = Math.Clamp(importancePoints, 0, 40);
        result.ImportanceContribution = importancePoints;
        details.Add($"중요도: {importancePoints}점 (원점수 {importanceScore}/100)");

        // 2. 긴급도 점수 (최대 30점)
        int urgencyPoints = (int)Math.Round(urgencyScore * 0.3);
        urgencyPoints = Math.Clamp(urgencyPoints, 0, 30);

        // 마감일 임박 시 추가 점수
        if (hasDeadline && deadlineDays.HasValue)
        {
            int deadlineBonus = CalculateDeadlineBonus(deadlineDays.Value);
            urgencyPoints = Math.Min(30, urgencyPoints + deadlineBonus);
            if (deadlineBonus > 0)
            {
                details.Add($"마감일 보너스: +{deadlineBonus}점 ({deadlineDays}일 남음)");
            }
        }

        result.UrgencyContribution = urgencyPoints;
        details.Add($"긴급도: {urgencyPoints}점 (원점수 {urgencyScore}/100)");

        // 3. 발신자 중요도 (최대 15점)
        int senderPoints = CalculateSenderScore(senderEmail, out string senderType);
        result.SenderContribution = senderPoints;
        if (senderPoints > 0)
        {
            details.Add($"발신자({senderType}): {senderPoints}점");
        }

        // 4. 키워드 가중치 (최대 15점)
        int keywordPoints = CalculateKeywordScore(keywords, out List<string> matchedKeywords);
        result.KeywordContribution = keywordPoints;
        if (keywordPoints > 0 && matchedKeywords.Count > 0)
        {
            details.Add($"키워드({string.Join(", ", matchedKeywords)}): {keywordPoints}점");
        }

        // 최종 점수 계산
        result.Score = importancePoints + urgencyPoints + senderPoints + keywordPoints;
        result.Score = Math.Clamp(result.Score, 0, 100);
        result.Level = DetermineLevel(result.Score);
        result.Details = details;

        _logger.Debug("우선순위 계산 완료: {Score}점 ({Level}) - {Details}",
            result.Score, result.Level, string.Join(", ", details));

        return result;
    }

    /// <summary>
    /// 마감일 임박 보너스 계산
    /// </summary>
    private int CalculateDeadlineBonus(int daysRemaining)
    {
        return daysRemaining switch
        {
            <= 0 => 10,     // 마감일 지남 또는 당일
            1 => 8,         // 1일 남음
            2 => 6,         // 2일 남음
            <= 3 => 5,      // 3일 남음
            <= 5 => 3,      // 4-5일 남음
            <= 7 => 2,      // 일주일 남음
            _ => 0          // 일주일 이상
        };
    }

    /// <summary>
    /// 발신자 중요도 점수 계산
    /// </summary>
    private int CalculateSenderScore(string senderEmail, out string senderType)
    {
        senderType = "일반";

        if (string.IsNullOrWhiteSpace(senderEmail))
            return 0;

        // VIP 발신자 체크
        if (_vipSenders.Contains(senderEmail))
        {
            senderType = "VIP";
            return 15;
        }

        // 이메일 도메인 추출
        var atIndex = senderEmail.IndexOf('@');
        if (atIndex < 0)
            return 0;

        var domain = senderEmail.Substring(atIndex + 1);

        // 관리자 도메인 체크
        if (_managerDomains.Any(d => domain.Contains(d, StringComparison.OrdinalIgnoreCase)))
        {
            senderType = "상사";
            return 12;
        }

        // 고객사 도메인 체크
        if (_customerDomains.Contains(domain))
        {
            senderType = "고객";
            return 10;
        }

        // 외부 도메인 (일반적으로 중요도 낮음)
        return 0;
    }

    /// <summary>
    /// 키워드 가중치 점수 계산
    /// </summary>
    private int CalculateKeywordScore(List<string> keywords, out List<string> matchedKeywords)
    {
        matchedKeywords = new List<string>();

        if (keywords == null || keywords.Count == 0)
            return 0;

        int totalPoints = 0;

        foreach (var keyword in keywords)
        {
            foreach (var (key, weight) in KeywordWeights)
            {
                if (keyword.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    totalPoints += weight;
                    matchedKeywords.Add(key);
                    break; // 한 키워드당 하나의 매칭만
                }
            }
        }

        // 최대 15점으로 제한
        return Math.Min(15, totalPoints);
    }

    /// <summary>
    /// 점수 기반 레벨 결정
    /// </summary>
    private string DetermineLevel(int score)
    {
        return score switch
        {
            >= 80 => "critical",
            >= 60 => "high",
            >= 40 => "medium",
            _ => "low"
        };
    }
}

/// <summary>
/// 우선순위 계산 결과
/// </summary>
public class PriorityResult
{
    /// <summary>
    /// 최종 점수 (0-100)
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 레벨 (critical, high, medium, low)
    /// </summary>
    public string Level { get; set; } = "low";

    /// <summary>
    /// 중요도 기여 점수 (최대 40점)
    /// </summary>
    public int ImportanceContribution { get; set; }

    /// <summary>
    /// 긴급도 기여 점수 (최대 30점)
    /// </summary>
    public int UrgencyContribution { get; set; }

    /// <summary>
    /// 발신자 기여 점수 (최대 15점)
    /// </summary>
    public int SenderContribution { get; set; }

    /// <summary>
    /// 키워드 기여 점수 (최대 15점)
    /// </summary>
    public int KeywordContribution { get; set; }

    /// <summary>
    /// 점수 산정 상세 내역
    /// </summary>
    public List<string> Details { get; set; } = new();
}
