using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// bool을 TextDecorations로 변환 (true이면 Strikethrough)
/// </summary>
public class BoolToStrikethroughConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isComplete && isComplete)
        {
            return TextDecorations.Strikethrough;
        }
        return null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
