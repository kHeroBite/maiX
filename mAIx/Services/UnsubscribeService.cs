using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Graph.Models;
using mAIx.Services.Graph;
using Serilog;

namespace mAIx.Services;

/// <summary>
/// 원클릭 구독 취소 서비스 — List-Unsubscribe 헤더 파싱 + 실행
/// </summary>
public class UnsubscribeService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;

    public UnsubscribeService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("UnsubscribeService");
        _logger = Log.ForContext<UnsubscribeService>();
    }

    /// <summary>
    /// RFC 8058 List-Unsubscribe 헤더 파싱: &lt;https://...&gt;, &lt;mailto:...&gt;
    /// </summary>
    public (string? HttpUrl, string? MailtoAddress) ParseUnsubscribeHeader(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return (null, null);

        string? httpUrl = null;
        string? mailtoAddress = null;

        // <https://...> 또는 <mailto:...> 형태 추출
        var matches = Regex.Matches(header, @"<([^>]+)>");
        foreach (Match match in matches)
        {
            var value = match.Groups[1].Value.Trim();
            if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                httpUrl ??= value;
            }
            else if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                // mailto:address@example.com?subject=unsubscribe → 주소만 추출
                var mailPart = value.Substring("mailto:".Length);
                mailtoAddress ??= mailPart.Contains('?') ? mailPart[..mailPart.IndexOf('?')] : mailPart;
            }
        }

        return (httpUrl, mailtoAddress);
    }

    /// <summary>
    /// 구독 취소 실행
    /// 1. email 헤더에서 List-Unsubscribe 추출 (Graph API로 헤더 조회)
    /// 2. HTTP URL 있으면 POST 요청 (RFC 8058 One-Click)
    /// 3. mailto 있으면 빈 메일 발송
    /// 4. 성공 시 true 반환
    /// </summary>
    public async Task<bool> UnsubscribeAsync(string emailId, GraphMailService mailService)
    {
        if (string.IsNullOrEmpty(emailId))
            return false;

        try
        {
            // Graph API로 메일 조회 (인터넷 헤더 포함)
            var message = await mailService.GetMessageAsync(emailId);
            if (message == null)
                return false;

            // List-Unsubscribe 헤더 추출
            var unsubscribeHeader = GetInternetHeader(message, "List-Unsubscribe");
            if (string.IsNullOrEmpty(unsubscribeHeader))
            {
                _logger.Warning("[UnsubscribeService] List-Unsubscribe 헤더 없음: {EmailId}", emailId);
                return false;
            }

            var (httpUrl, mailtoAddress) = ParseUnsubscribeHeader(unsubscribeHeader);

            // HTTP One-Click 구독 취소 (RFC 8058 우선)
            if (!string.IsNullOrEmpty(httpUrl))
            {
                var postBody = new StringContent("List-Unsubscribe=One-Click", Encoding.UTF8, "application/x-www-form-urlencoded");
                var response = await _httpClient.PostAsync(httpUrl, postBody);
                if (response.IsSuccessStatusCode)
                {
                    _logger.Information("[UnsubscribeService] HTTP One-Click 구독 취소 성공: {Url}", httpUrl);
                    return true;
                }
                _logger.Warning("[UnsubscribeService] HTTP One-Click 실패 {Status}, mailto 시도", response.StatusCode);
            }

            // mailto 구독 취소 (빈 메일 발송)
            if (!string.IsNullOrEmpty(mailtoAddress))
            {
                var unsubscribeMessage = new Message
                {
                    Subject = "Unsubscribe",
                    Body = new ItemBody { ContentType = BodyType.Text, Content = "" },
                    ToRecipients = new System.Collections.Generic.List<Recipient>
                    {
                        new() { EmailAddress = new EmailAddress { Address = mailtoAddress } }
                    }
                };
                await mailService.SendMessageAsync(unsubscribeMessage);
                _logger.Information("[UnsubscribeService] mailto 구독 취소 메일 발송: {Address}", mailtoAddress);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "[UnsubscribeService] 구독 취소 실패: {EmailId}", emailId);
            return false;
        }
    }

    /// <summary>
    /// 뉴스레터 이메일 여부 판단
    /// List-Unsubscribe 헤더 존재 또는 "newsletter", "unsubscribe" 키워드 감지
    /// </summary>
    public bool IsNewsletterEmail(mAIx.Models.Email email)
    {
        if (email == null) return false;

        // 제목에 키워드 포함 여부
        var subject = email.Subject ?? string.Empty;
        if (subject.Contains("newsletter", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("unsubscribe", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("구독", StringComparison.OrdinalIgnoreCase))
            return true;

        // 발신자 도메인에 newsletter 포함 여부
        var from = email.From ?? string.Empty;
        if (from.Contains("newsletter", StringComparison.OrdinalIgnoreCase) ||
            from.Contains("noreply", StringComparison.OrdinalIgnoreCase) ||
            from.Contains("no-reply", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Graph Message 객체에서 인터넷 헤더 값 추출
    /// </summary>
    private static string? GetInternetHeader(Message message, string headerName)
    {
        if (message.InternetMessageHeaders == null) return null;

        foreach (var header in message.InternetMessageHeaders)
        {
            if (string.Equals(header.Name, headerName, StringComparison.OrdinalIgnoreCase))
                return header.Value;
        }
        return null;
    }
}
