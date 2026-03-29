using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mAIx.Converters;

/// <summary>
/// AiCategory 문자열을 배지 배경색(Brush)으로 변환하는 컨버터
/// </summary>
public class AiCategoryToBadgeConverter : IValueConverter
{
    private static readonly SolidColorBrush BrushUrgent   = new(Color.FromRgb(0xE7, 0x4C, 0x3C)); // 긴급 #E74C3C
    private static readonly SolidColorBrush BrushAction   = new(Color.FromRgb(0xF3, 0x9C, 0x12)); // 액션필요 #F39C12
    private static readonly SolidColorBrush BrushFyi      = new(Color.FromRgb(0x34, 0x98, 0xDB)); // FYI #3498DB
    private static readonly SolidColorBrush BrushNormal   = new(Color.FromRgb(0x95, 0xA5, 0xA6)); // 일반 #95A5A6

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "긴급"     => BrushUrgent,
            "액션필요"  => BrushAction,
            "FYI"      => BrushFyi,
            "일반"     => BrushNormal,
            _          => Brushes.Transparent
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
