// STT 환각(hallucination) 정규식 필터 - YouTube 자막 패턴 차단
using System.Text.RegularExpressions;
using NLog;

namespace mAIx.Services.AI.Helpers;

/// <summary>
/// Whisper 한국어 환각(hallucination) 패턴을 감지하는 정적 헬퍼 클래스.
/// YouTube 자막 학습 잔재로 인한 오검출 문자열을 차단.
/// OpenAiRealtimeSttService와 동일한 정규식 10개를 공유 (DRY).
/// </summary>
public static class HallucinationFilter
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    // OpenAiRealtimeSttService와 정확히 동일한 정규식 10개
    private static readonly Regex[] _patterns = new[]
    {
        new Regex(@"구독.*좋아요.*댓글", RegexOptions.Compiled),
        new Regex(@"구독.*과.*좋아요", RegexOptions.Compiled),
        new Regex(@"좋아요.*구독", RegexOptions.Compiled),
        new Regex(@"한국어.*자막.*도움", RegexOptions.Compiled),
        new Regex(@"매주.*업로드", RegexOptions.Compiled),
        new Regex(@"시청해.*감사", RegexOptions.Compiled),
        new Regex(@"알림.*설정", RegexOptions.Compiled),
        new Regex(@"채널.*구독", RegexOptions.Compiled),
        new Regex(@"댓글.*부탁", RegexOptions.Compiled),
        new Regex(@"영상.*보러", RegexOptions.Compiled),
    };

    /// <summary>
    /// 주어진 텍스트가 Whisper 환각 패턴에 해당하는지 판정.
    /// </summary>
    /// <param name="text">검사할 STT 전사 텍스트</param>
    /// <returns>환각으로 판단되면 true, 정상 발화이면 false</returns>
    public static bool IsHallucination(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var pattern in _patterns)
        {
            if (pattern.IsMatch(text))
            {
                _log.Debug("환각 패턴 감지 (패턴={0}): {1}", pattern, text);
                return true;
            }
        }
        return false;
    }
}
