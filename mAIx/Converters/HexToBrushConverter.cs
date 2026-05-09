// 16진수 색상 코드(#RRGGBB)를 WPF SolidColorBrush로 변환하는 컨버터
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mAIx.Converters;

/// <summary>
/// Hex 색상 문자열 (#RRGGBB) → SolidColorBrush 변환 컨버터
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public class HexToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush _fallback = new(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is string hex && !string.IsNullOrWhiteSpace(hex))
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
        }
        catch
        {
            // 잘못된 hex 값은 투명으로 폴백
        }
        return _fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
