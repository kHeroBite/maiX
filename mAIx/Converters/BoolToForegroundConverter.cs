using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace mAIx.Converters;

/// <summary>
/// 부울 값 또는 플래그 상태(문자열)를 Foreground 색상으로 변환하는 컨버터
/// - bool true (IsPinned): 파란색 강조
/// - string flagged/complete: 빨간색/녹색 강조
/// - 기본: 테마 기본 색상
/// </summary>
public class BoolToForegroundConverter : IValueConverter
{
    /// <summary>
    /// 부울 값 또는 문자열을 Brush로 변환
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 문자열 플래그 상태 처리
        if (value is string flagStatus)
        {
            if (flagStatus == "flagged")
            {
                return new SolidColorBrush(Color.FromRgb(0xFF, 0x44, 0x44)); // 빨간색
            }
            else if (flagStatus == "complete")
            {
                return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // 녹색
            }
            // 기본색
            return GetDefaultBrush();
        }

        // 부울 값 처리 (IsPinned 등)
        if (value is bool isActive)
        {
            if (isActive)
            {
                return new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); // 파란색
            }
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
    /// Brush를 부울로 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("BoolToForegroundConverter는 역변환을 지원하지 않습니다.");
    }
}
