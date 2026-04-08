using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace mAIx.Utils;

public static class TextPreprocessor
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    /// <summary>HTML 태그 제거 후 공백 정규화</summary>
    public static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var noTags = HtmlTagRegex.Replace(html, " ");
        return WhitespaceRegex.Replace(noTags, " ").Trim();
    }

    /// <summary>
    /// FTS5 MATCH 쿼리 변환.
    /// 2글자 이상 토큰 → trigram MATCH 구문
    /// 1글자 토큰 → needsLikeFallback=true 반환
    /// </summary>
    public static (string ftsQuery, bool needsLikeFallback) BuildFtsQuery(string keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
            return (string.Empty, false);

        var tokens = keywords.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ftsTokens = new List<string>();
        bool needsLike = false;

        foreach (var token in tokens)
        {
            if (token.Length >= 2)
                ftsTokens.Add($"\"{token.Replace("\"", "\"\"")}\"");
            else
                needsLike = true;
        }

        return (string.Join(" ", ftsTokens), needsLike);
    }

    /// <summary>
    /// AI 청킹: 긴 텍스트를 maxChunkSize 단위로 분할 (오버랩 포함)
    /// </summary>
    public static List<string> ChunkText(string text, int maxChunkSize = 500, int overlap = 50)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        int start = 0;
        while (start < text.Length)
        {
            int length = Math.Min(maxChunkSize, text.Length - start);
            chunks.Add(text.Substring(start, length));
            start += maxChunkSize - overlap;
            if (start >= text.Length) break;
        }
        return chunks;
    }
}
