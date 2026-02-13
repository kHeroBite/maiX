using System;
using System.Globalization;
using System.Windows.Data;

namespace MaiX.Converters;

/// <summary>
/// DateTime을 상대적 시간 문자열로 변환하는 컨버터
/// 예: "방금 전", "5분 전", "3시간 전", "1일 전"
/// </summary>
public class DateTimeToRelativeTimeConverter : IValueConverter
{
    /// <summary>
    /// DateTime을 상대적 시간 문자열로 변환
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return string.Empty;

        DateTime? dateTime = null;

        if (value is DateTime dt)
        {
            dateTime = dt;
        }
        else if (value is DateTimeOffset dto)
        {
            dateTime = dto.DateTime;
        }

        if (!dateTime.HasValue)
            return string.Empty;

        var diff = DateTime.Now - dateTime.Value;
        return CalculateRelativeTime(diff, dateTime.Value);
    }

    /// <summary>
    /// 시간 차이를 상대적 시간 문자열로 계산
    /// </summary>
    private static string CalculateRelativeTime(TimeSpan diff, DateTime originalDate)
    {
        if (diff.TotalMinutes < 1)
            return "방금 전";
        if (diff.TotalHours < 1)
            return $"{(int)diff.TotalMinutes}분 전";
        if (diff.TotalDays < 1)
            return $"{(int)diff.TotalHours}시간 전";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays}일 전";

        // 7일 이상인 경우 날짜 형식으로 표시
        return originalDate.Year == DateTime.Now.Year
            ? originalDate.ToString("MM-dd")
            : originalDate.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
