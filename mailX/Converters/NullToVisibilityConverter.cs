using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// null 여부를 Visibility로 변환하는 컨버터
/// null이 아니면 Visible, null이면 Collapsed
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 값을 Visibility로 변환
    /// </summary>
    /// <param name="value">변환할 값</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터 (Invert 지정 시 반전)</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>Visibility 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true;

        bool isNotNull = value != null;

        // 문자열의 경우 빈 문자열도 null로 취급
        if (value is string str)
        {
            isNotNull = !string.IsNullOrWhiteSpace(str);
        }

        if (invert)
        {
            isNotNull = !isNotNull;
        }

        return isNotNull ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Visibility를 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("NullToVisibilityConverter는 역변환을 지원하지 않습니다.");
    }
}
