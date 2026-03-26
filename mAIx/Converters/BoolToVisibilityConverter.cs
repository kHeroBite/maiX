using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mAIx.Converters;

/// <summary>
/// 부울 값을 Visibility로 변환하는 컨버터
/// true면 Visible, false면 Collapsed
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 부울 값을 Visibility로 변환
    /// </summary>
    /// <param name="value">변환할 부울 값</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터 (Invert 지정 시 반전)</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>Visibility 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true;

        bool boolValue = false;
        if (value is bool b)
        {
            boolValue = b;
        }
        else if (value != null && bool.TryParse(value.ToString(), out bool parsed))
        {
            boolValue = parsed;
        }

        if (invert)
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Visibility를 부울로 역변환
    /// </summary>
    /// <param name="value">Visibility 값</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터 (Invert 지정 시 반전)</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>부울 값</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true;

        bool result = value is Visibility visibility && visibility == Visibility.Visible;

        if (invert)
        {
            result = !result;
        }

        return result;
    }
}
