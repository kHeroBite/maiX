using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// 문자열이 파라미터와 일치하면 Visible, 아니면 Collapsed를 반환하는 컨버터
/// </summary>
public class StringEqualsToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 값을 Visibility로 변환
    /// </summary>
    /// <param name="value">변환할 문자열 값</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">비교할 문자열</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>Visibility 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return Visibility.Collapsed;

        string strValue = value.ToString() ?? string.Empty;
        string strParam = parameter.ToString() ?? string.Empty;

        bool equals = strValue.Equals(strParam, StringComparison.OrdinalIgnoreCase);
        return equals ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Visibility를 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("StringEqualsToVisibilityConverter는 역변환을 지원하지 않습니다.");
    }
}
