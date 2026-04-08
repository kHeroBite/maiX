using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Serilog;

namespace mAIx.Services;

/// <summary>
/// 추적 픽셀 차단 서비스 — 이메일 본문에서 추적 URL 감지 + 제거
/// </summary>
public class TrackingBlockerService
{
    private readonly ILogger _logger;

    private static readonly HashSet<string> _trackerDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "list-manage.com", "sendgrid.net", "clicks.aweber.com",
        "track.hubspot.com", "em.exacttarget.com", "mandrillapp.com",
        "mailgun.org", "trk.klclick.com", "go.pardot.com",
        "click.mailchimp.com", "tracking.campaignmonitor.com",
        "links.mailchimp.com", "email.mailchimp.com",
        "open.mailchimp.com", "r.sendgrid.net",
    };

    // 추적 픽셀 패턴: 1x1 이미지, width=1/height=1, tracking URL 포함 img 태그
    private static readonly Regex _trackingImgRegex = new(
        @"<img[^>]+(?:width=[""']?1[""']?|height=[""']?1[""']?)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // URL 추출 패턴
    private static readonly Regex _urlRegex = new(
        @"https?://([^/""'\s>]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public TrackingBlockerService()
    {
        _logger = Log.ForContext<TrackingBlockerService>();
    }

    /// <summary>
    /// HTML 본문에서 추적 픽셀 제거 후 반환
    /// </summary>
    public string RemoveTrackingPixels(string htmlBody)
    {
        if (string.IsNullOrEmpty(htmlBody))
            return htmlBody;

        var result = htmlBody;
        var removedCount = 0;

        // 1x1 이미지 태그 제거
        result = _trackingImgRegex.Replace(result, match =>
        {
            if (IsTrackerUrl(match.Value))
            {
                removedCount++;
                return string.Empty;
            }
            return match.Value;
        });

        // 알려진 트래커 도메인 img 태그 제거
        result = Regex.Replace(result, @"<img[^>]+src=[""']?https?://([^""'\s>]+)[""']?[^>]*>",
            match =>
            {
                var urlMatch = _urlRegex.Match(match.Value);
                if (urlMatch.Success && IsDomainTracker(urlMatch.Groups[1].Value))
                {
                    removedCount++;
                    return string.Empty;
                }
                return match.Value;
            },
            RegexOptions.IgnoreCase);

        if (removedCount > 0)
            _logger.Debug("[TrackingBlocker] {Count}개 추적 픽셀 제거", removedCount);

        return result;
    }

    /// <summary>
    /// 추적 픽셀 포함 여부 확인
    /// </summary>
    public bool HasTrackingPixel(string htmlBody)
    {
        if (string.IsNullOrEmpty(htmlBody)) return false;
        return GetTrackerCount(htmlBody) > 0;
    }

    /// <summary>
    /// 추적 픽셀 개수 반환
    /// </summary>
    public int GetTrackerCount(string htmlBody)
    {
        if (string.IsNullOrEmpty(htmlBody)) return 0;

        var count = 0;

        // 1x1 이미지 중 트래커 URL 포함
        foreach (Match match in _trackingImgRegex.Matches(htmlBody))
        {
            if (IsTrackerUrl(match.Value)) count++;
        }

        // 알려진 트래커 도메인 img 태그
        foreach (Match match in Regex.Matches(htmlBody,
            @"<img[^>]+src=[""']?https?://([^""'\s>]+)[""']?[^>]*>",
            RegexOptions.IgnoreCase))
        {
            var urlMatch = _urlRegex.Match(match.Value);
            if (urlMatch.Success && IsDomainTracker(urlMatch.Groups[1].Value))
                count++;
        }

        return count;
    }

    private bool IsTrackerUrl(string imgTag)
    {
        var urlMatch = _urlRegex.Match(imgTag);
        return urlMatch.Success && IsDomainTracker(urlMatch.Groups[1].Value);
    }

    private static bool IsDomainTracker(string hostAndPath)
    {
        foreach (var trackerDomain in _trackerDomains)
        {
            if (hostAndPath.Contains(trackerDomain, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
