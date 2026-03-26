using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MaiX.Converters;

/// <summary>
/// 중요도 문자열을 Visibility로 변환하는 컨버터
/// "high"이면 Visible, 그 외는 Collapsed
/// </summary>
public class ImportanceToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 중요도를 Visibility로 변환
    /// </summary>
    /// <param name="value">중요도 문자열 (low, normal, high)</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>Visibility 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string importance)
        {
            // "high"인 경우 중요도 아이콘 표시
            if (string.Equals(importance, "high", StringComparison.OrdinalIgnoreCase))
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
