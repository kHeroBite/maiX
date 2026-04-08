using System.Collections.Generic;
using System.Linq;

namespace mAIx.Utils;

public static class BigramHelper
{
    /// <summary>문자열을 bigram(2자) 집합으로 분해</summary>
    public static HashSet<string> ToBigrams(string text)
    {
        var result = new HashSet<string>();
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2) return result;
        var normalized = text.ToLowerInvariant().Replace(" ", "");
        for (int i = 0; i < normalized.Length - 1; i++)
            result.Add(normalized.Substring(i, 2));
        return result;
    }

    /// <summary>쿼리와 후보 문자열 간 bigram Jaccard 유사도 (0.0~1.0)</summary>
    public static double Score(string query, string candidate)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(candidate))
            return 0.0;
        var qGrams = ToBigrams(query);
        if (qGrams.Count == 0) return 0.0;
        var cGrams = ToBigrams(candidate);
        if (cGrams.Count == 0) return 0.0;
        var intersection = qGrams.Count(g => cGrams.Contains(g));
        var union = qGrams.Count + cGrams.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }
}
