using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace mAIx.Utils;

/// <summary>
/// 이메일 본문에서 @멘션 추출 유틸리티
/// </summary>
public static class MentionParser
{
    /// <summary>HTML 태그 제거 정규식</summary>
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>@멘션 파싱 정규식 — @ 뒤 공백/꺾쇠/특수문자 미포함 연속 문자</summary>
    private static readonly Regex MentionRegex = new(@"@[^\s<>@,;:]+", RegexOptions.Compiled);

    /// <summary>
    /// HTML 본문에서 @멘션 이름 목록 추출
    /// </summary>
    /// <param name="htmlBody">이메일 HTML 본문</param>
    /// <returns>멘션된 이름 목록 (@ 기호 제외)</returns>
    public static List<string> ParseMentions(string htmlBody)
    {
        if (string.IsNullOrEmpty(htmlBody))
            return new List<string>();

        // HTML 태그 제거
        var plainText = HtmlTagRegex.Replace(htmlBody, " ");

        // @멘션 추출
        var result = new List<string>();
        var matches = MentionRegex.Matches(plainText);
        foreach (Match match in matches)
        {
            // @ 기호 제거하여 이름만 반환
            var name = match.Value.TrimStart('@');
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(name);
        }

        return result;
    }

    /// <summary>
    /// HTML 본문에서 현재 사용자 이름이 포함된 @멘션을 강조 표시
    /// </summary>
    /// <param name="htmlBody">이메일 HTML 본문</param>
    /// <param name="myName">현재 사용자 이름</param>
    /// <returns>@멘션이 강조된 HTML 문자열</returns>
    public static string HighlightMentions(string htmlBody, string myName)
    {
        if (string.IsNullOrEmpty(htmlBody) || string.IsNullOrEmpty(myName))
            return htmlBody;

        // @{myName} 패턴을 span 태그로 감싸기 (대소문자 무관)
        var pattern = $@"@{Regex.Escape(myName)}";
        return Regex.Replace(
            htmlBody,
            pattern,
            $"<span class=\"mention-highlight\">@{myName}</span>",
            RegexOptions.IgnoreCase);
    }
}
