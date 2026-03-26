using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mAIx.Converters;

/// <summary>
/// 마감일 상태를 배경색으로 변환
/// </summary>
public class DueStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status switch
            {
                "Overdue" => new SolidColorBrush(Color.FromArgb(255, 209, 52, 56)),  // 빨강
                "Urgent" => new SolidColorBrush(Color.FromArgb(255, 255, 140, 0)),   // 주황
                "Soon" => new SolidColorBrush(Color.FromArgb(255, 255, 200, 50)),    // 노랑
                _ => new SolidColorBrush(Color.FromArgb(50, 128, 128, 128))          // 회색
            };
        }
        return new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
