using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mailX.Converters;

/// <summary>
/// 이름을 아웃룩 스타일 아바타 배경색으로 변환하는 컨버터
/// 라이트/다크 모드에 따라 밝기 조절
/// </summary>
public class NameToAvatarColorConverter : IValueConverter
{
    // 아웃룩 스타일 파스텔톤 배경색 (라이트모드용)
    private static readonly Color[] LightColors = new[]
    {
        Color.FromRgb(0xA4, 0x26, 0x2C), // 어두운 빨강
        Color.FromRgb(0xC2, 0x39, 0x34), // 빨강
        Color.FromRgb(0xE7, 0x4B, 0x56), // 밝은 빨강
        Color.FromRgb(0xB5, 0x48, 0x5D), // 핑크
        Color.FromRgb(0x88, 0x1C, 0x79), // 마젠타
        Color.FromRgb(0x73, 0x4A, 0xB8), // 보라
        Color.FromRgb(0x4F, 0x6B, 0xED), // 파랑
        Color.FromRgb(0x00, 0x78, 0xD4), // 아웃룩 파랑
        Color.FromRgb(0x00, 0x89, 0x9B), // 청록
        Color.FromRgb(0x00, 0x7B, 0x83), // 틸
        Color.FromRgb(0x49, 0x8A, 0x05), // 초록
        Color.FromRgb(0x10, 0x7C, 0x10), // 어두운 초록
        Color.FromRgb(0x84, 0x7D, 0x45), // 올리브
        Color.FromRgb(0xC6, 0x74, 0x30), // 주황
        Color.FromRgb(0xDA, 0x3B, 0x01), // 레드 오렌지
        Color.FromRgb(0x7A, 0x71, 0x74), // 회색
    };

    // 다크모드용 (약간 밝게)
    private static readonly Color[] DarkColors = new[]
    {
        Color.FromRgb(0xCC, 0x4A, 0x51), // 밝은 빨강
        Color.FromRgb(0xE5, 0x5A, 0x54), // 밝은 빨강 2
        Color.FromRgb(0xF0, 0x6A, 0x6A), // 코랄
        Color.FromRgb(0xD4, 0x6A, 0x7E), // 밝은 핑크
        Color.FromRgb(0xAA, 0x44, 0x9F), // 밝은 마젠타
        Color.FromRgb(0x95, 0x6C, 0xD6), // 밝은 보라
        Color.FromRgb(0x6B, 0x89, 0xF0), // 밝은 파랑
        Color.FromRgb(0x2B, 0x9A, 0xF3), // 밝은 아웃룩 파랑
        Color.FromRgb(0x2A, 0xAA, 0xBB), // 밝은 청록
        Color.FromRgb(0x2A, 0x9D, 0xA5), // 밝은 틸
        Color.FromRgb(0x6B, 0xA8, 0x2F), // 밝은 초록
        Color.FromRgb(0x3C, 0x9E, 0x3C), // 밝은 어두운 초록
        Color.FromRgb(0xA5, 0x9E, 0x67), // 밝은 올리브
        Color.FromRgb(0xE0, 0x95, 0x55), // 밝은 주황
        Color.FromRgb(0xEF, 0x5E, 0x2C), // 밝은 레드 오렌지
        Color.FromRgb(0x9A, 0x91, 0x94), // 밝은 회색
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string;
        if (string.IsNullOrEmpty(name))
            return new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));

        // 이름의 첫 글자 해시로 색상 선택
        var hash = Math.Abs(name.GetHashCode());
        var index = hash % LightColors.Length;

        // 다크모드 확인 (파라미터로 전달하거나 시스템 설정 확인)
        var isDarkMode = IsDarkMode();
        var colors = isDarkMode ? DarkColors : LightColors;

        return new SolidColorBrush(colors[index]);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 현재 다크모드인지 확인
    /// </summary>
    private static bool IsDarkMode()
    {
        try
        {
            // WPF UI 테마 확인
            var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
            return theme == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        }
        catch
        {
            return false;
        }
    }
}
