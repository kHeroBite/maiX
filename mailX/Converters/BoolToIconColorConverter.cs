using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mailX.Converters;

/// <summary>
/// Bool 값을 아이콘 색상으로 변환하는 컨버터
/// true → 강조색 (파란색), false → 회색
/// </summary>
public class BoolToIconColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isActive = value is bool b && b;

        return isActive
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4))  // 파란색 (강조)
            : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)); // 회색
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
