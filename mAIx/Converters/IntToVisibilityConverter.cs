using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mAIx.Converters;

/// <summary>
/// 정수를 Visibility로 변환하는 컨버터
/// 0이면 Collapsed, 그 외 Visible
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 정수 값을 Visibility로 변환
    /// </summary>
    /// <param name="value">변환할 정수 값</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터 (Invert 지정 시 반전)</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>Visibility 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true;

        int intValue = 0;
        if (value is int i)
        {
            intValue = i;
        }
        else if (value is long l)
        {
            intValue = (int)l;
        }
        else if (value != null && int.TryParse(value.ToString(), out int parsed))
        {
            intValue = parsed;
        }

        bool isVisible = intValue != 0;

        if (invert)
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Visibility를 정수로 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("IntToVisibilityConverter는 역변환을 지원하지 않습니다.");
    }
}
