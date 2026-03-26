using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mAIx.Converters;

/// <summary>
/// 마감일 상태를 전경색(텍스트 색상)으로 변환
/// Outlook 스타일: Overdue=빨강, Urgent=주황, Soon=노랑, Normal=회색
/// </summary>
public class DueStatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status switch
            {
                "Overdue" => new SolidColorBrush(Color.FromRgb(209, 52, 56)),   // 빨강 (기한 초과)
                "Urgent" => new SolidColorBrush(Color.FromRgb(255, 140, 0)),    // 주황 (긴급)
                "Soon" => new SolidColorBrush(Color.FromRgb(200, 150, 0)),      // 어두운 노랑 (곧 마감)
                _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))          // 회색 (일반)
            };
        }
        return new SolidColorBrush(Color.FromRgb(128, 128, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
