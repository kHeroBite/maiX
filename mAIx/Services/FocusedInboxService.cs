using System;
using System.Collections.Generic;
using System.Linq;
using mAIx.Models;

namespace mAIx.Services;

/// <summary>
/// Focused Inbox — 중요 메일 분류 서비스
/// 규칙 기반 점수화로 "Focused" vs "Other" 탭 분류
/// </summary>
public class FocusedInboxService
{
    private readonly List<string> _vipSenders = new();
    private readonly HashSet<string> _alwaysFocused = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alwaysOther = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] _marketingKeywords =
    [
        "sale", "offer", "deal", "promo", "discount", "coupon", "% off",
        "limited time", "exclusive", "unsubscribe", "newsletter", "특가",
        "할인", "이벤트", "프로모션"
    ];

    /// <summary>
    /// 이메일 점수 계산
    /// </summary>
    public int ScoreEmail(Email email, string myEmail)
    {
        if (email == null) throw new ArgumentNullException(nameof(email));

        var score = 0;
        var senderEmail = ExtractEmail(email.From);

        // alwaysFocused/alwaysOther 오버라이드
        if (_alwaysFocused.Contains(senderEmail)) return 100;
        if (_alwaysOther.Contains(senderEmail)) return -100;

        // +30: 직접 수신자(To)에 내 이메일 포함
        if (!string.IsNullOrEmpty(email.To) && ContainsEmail(email.To, myEmail))
            score += 30;

        // +20: 이전에 답장한 발신자 (alwaysFocused 기준으로 대체 — 실제 sent 추적 없음)
        // (답장 이력은 DB 조회 필요 — 이 서비스 범위 밖이므로 외부에서 alwaysFocused로 등록)

        // +15: VIP 발신자
        if (_vipSenders.Any(v => string.Equals(v, senderEmail, StringComparison.OrdinalIgnoreCase)))
            score += 15;

        // +10: 캘린더 초대 포함 (카테고리 또는 Subject 기준)
        if (!string.IsNullOrEmpty(email.Subject) &&
            (email.Subject.Contains("invite", StringComparison.OrdinalIgnoreCase) ||
             email.Subject.Contains("초대", StringComparison.OrdinalIgnoreCase) ||
             email.Subject.Contains("meeting", StringComparison.OrdinalIgnoreCase)))
            score += 10;

        // -10: 대량 발신 (List-Unsubscribe 헤더 존재 — Body에서 감지)
        if (!string.IsNullOrEmpty(email.Body) &&
            email.Body.Contains("List-Unsubscribe", StringComparison.OrdinalIgnoreCase))
            score -= 10;

        // -15: 마케팅 키워드 감지
        var subjectLower = email.Subject?.ToLowerInvariant() ?? "";
        if (_marketingKeywords.Any(k => subjectLower.Contains(k, StringComparison.OrdinalIgnoreCase)))
            score -= 15;

        // -20: CC/BCC로만 수신 (To에 내 이메일 없고, Cc/Bcc에 있는 경우)
        if (!string.IsNullOrEmpty(myEmail))
        {
            var inTo = !string.IsNullOrEmpty(email.To) && ContainsEmail(email.To, myEmail);
            var inCc = !string.IsNullOrEmpty(email.Cc) && ContainsEmail(email.Cc, myEmail);
            var inBcc = !string.IsNullOrEmpty(email.Bcc) && ContainsEmail(email.Bcc, myEmail);
            if (!inTo && (inCc || inBcc))
                score -= 20;
        }

        return score;
    }

    /// <summary>
    /// Focused 여부 판정 (점수 >= 20)
    /// </summary>
    public bool IsFocused(Email email, string myEmail)
        => ScoreEmail(email, myEmail) >= 20;

    /// <summary>
    /// VIP 발신자 목록 등록
    /// </summary>
    public void SetVipSenders(IEnumerable<string> emails)
    {
        _vipSenders.Clear();
        _vipSenders.AddRange(emails ?? Enumerable.Empty<string>());
    }

    /// <summary>
    /// VIP 발신자 목록 조회
    /// </summary>
    public IReadOnlyList<string> GetVipSenders() => _vipSenders.AsReadOnly();

    /// <summary>
    /// 발신자를 항상 Focused로 등록
    /// </summary>
    public void MarkSenderAsFocused(string senderEmail)
    {
        if (string.IsNullOrEmpty(senderEmail)) return;
        _alwaysFocused.Add(senderEmail);
        _alwaysOther.Remove(senderEmail);
    }

    /// <summary>
    /// 발신자를 항상 Other로 등록
    /// </summary>
    public void MarkSenderAsOther(string senderEmail)
    {
        if (string.IsNullOrEmpty(senderEmail)) return;
        _alwaysOther.Add(senderEmail);
        _alwaysFocused.Remove(senderEmail);
    }

    // ===== 내부 유틸 =====

    private static string ExtractEmail(string? from)
    {
        if (string.IsNullOrEmpty(from)) return string.Empty;
        // "이름 <email>" 형식 처리
        var start = from.IndexOf('<');
        var end = from.IndexOf('>');
        if (start >= 0 && end > start)
            return from.Substring(start + 1, end - start - 1).Trim();
        return from.Trim();
    }

    private static bool ContainsEmail(string? json, string email)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(email)) return false;
        return json.Contains(email, StringComparison.OrdinalIgnoreCase);
    }
}
