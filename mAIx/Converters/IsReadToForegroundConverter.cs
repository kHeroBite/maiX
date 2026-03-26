using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MaiX.Converters;

/// <summary>
/// IsRead 부울 값을 Foreground 색상으로 변환하는 컨버터
/// - IsRead=false (읽지 않음): 파란색 강조
/// - IsRead=true (읽음): 테마 기본 색상
/// </summary>
public class IsReadToForegroundConverter : IValueConverter
{
    /// <summary>
    /// IsRead 값을 Brush로 변환
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isRead)
        {
            if (!isRead) // 읽지 않음
            {
                return new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); // 파란색
            }
            // 읽음 - 기본색
            return GetDefaultBrush();
        }

        return GetDefaultBrush();
    }

    private static SolidColorBrush GetDefaultBrush()
    {
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        return theme == Wpf.Ui.Appearance.ApplicationTheme.Light
            ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) // 라이트모드: 검정 계열
            : new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)); // 다크모드: 흰색 계열
    }

    /// <summary>
    /// Brush를 IsRead로 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("IsReadToForegroundConverter는 역변환을 지원하지 않습니다.");
    }
}
