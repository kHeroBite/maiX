using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MaiX.Converters;

/// <summary>
/// FlagStatus를 색상으로 변환하는 컨버터
/// flagged → 주황색, complete → 초록색, notFlagged → 회색
/// </summary>
public class FlagStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flagStatus = value as string ?? "notFlagged";

        return flagStatus.ToLower() switch
        {
            "flagged" => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x00)),  // 주황색
            "complete" => new SolidColorBrush(Color.FromRgb(0x0B, 0x8A, 0x00)), // 초록색
            _ => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))           // 회색
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
