using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MaiX.Converters;

/// <summary>
/// 플래그 상태를 Visibility로 변환하는 컨버터
/// "flagged" 또는 "complete"이면 Visible, "notFlagged"나 null이면 Collapsed
/// </summary>
public class FlagStatusToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 플래그 상태를 Visibility로 변환
    /// </summary>
    /// <param name="value">플래그 상태 문자열 (flagged, complete, notFlagged)</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터 (Complete, Flagged 지정 가능)</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>Visibility 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string flagStatus)
        {
            string? targetStatus = parameter?.ToString();

            // 특정 상태만 표시하도록 파라미터 지정된 경우
            if (!string.IsNullOrEmpty(targetStatus))
            {
                if (string.Equals(flagStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return Visibility.Visible;
                }
                return Visibility.Collapsed;
            }

            // 파라미터 없으면 flagged 또는 complete인 경우 표시
            if (string.Equals(flagStatus, "flagged", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(flagStatus, "complete", StringComparison.OrdinalIgnoreCase))
            {
                return Visibility.Visible;
            }
        }

        return Visibility.Collapsed;
    }

    /// <summary>
    /// 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
