// TimeSpan을 60분 이상에서도 단조 증가하도록 포맷하는 헬퍼
namespace mAIx.Helpers;

/// <summary>
/// TimeSpan 포맷 헬퍼 — C# 기본 mm 포맷(0~59 wrap-around) 문제를 해결하여
/// 60분 이상에서도 단조 증가하는 시간 문자열을 반환한다.
/// </summary>
public static class TimeSpanFormatter
{
    /// <summary>
    /// TimeSpan을 표시 문자열로 변환한다.
    /// 60분 미만: mm:ss (예: 05:23)
    /// 60분 이상: h:mm:ss (예: 01:02:03)
    /// </summary>
    public static string FormatTimeSpan(TimeSpan ts)
    {
        int totalMinutes = (int)ts.TotalMinutes;
        int seconds = ts.Seconds;
        return totalMinutes >= 60
            ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{seconds:D2}"   // 60분 이상: h:mm:ss
            : $"{totalMinutes:D2}:{seconds:D2}";              // 60분 미만: mm:ss
    }
}
