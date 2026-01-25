using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// 0일 때 Visible, 그 외에는 Collapsed 반환
/// 컬렉션이 비어있을 때 빈 상태 메시지 표시용
/// </summary>
public class ZeroToVisibleConverter : IValueConverter
{
    /// <summary>
    /// 값을 Visibility로 변환
    /// </summary>
    /// <param name="value">변환할 값 (int)</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">"Invert" 지정 시 반전 (0이면 Collapsed, 그 외 Visible)</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>Visibility 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool invert = parameter?.ToString()?.Equals("Invert", StringComparison.OrdinalIgnoreCase) == true;

        bool isZero = value switch
        {
            int i => i == 0,
            long l => l == 0,
            double d => d == 0,
            _ => false
        };

        // Invert 시: 0이면 Collapsed, 그 외 Visible (컬렉션에 항목이 있을 때 표시)
        // 기본: 0이면 Visible, 그 외 Collapsed (빈 상태 메시지 표시)
        if (invert)
        {
            return isZero ? Visibility.Collapsed : Visibility.Visible;
        }

        return isZero ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Visibility를 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ZeroToVisibleConverter는 역변환을 지원하지 않습니다.");
    }
}
