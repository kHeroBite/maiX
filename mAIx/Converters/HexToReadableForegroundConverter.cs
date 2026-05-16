// 배경색 명도(luminance)에 따라 가독성 있는 글자색을 자동 반환하는 컨버터
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mAIx.Converters;

/// <summary>
/// HEX 배경색 → 명도 기반으로 흰색/검정색 글자 자동 선택 (라이트/다크 모드 무관 가독성 확보).
/// ConverterParameter="Tertiary"이면 반투명 적용 (보조 텍스트용).
/// </summary>
public class HexToReadableForegroundConverter : IValueConverter
{
    private static readonly Brush DarkText = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
    private static readonly Brush LightText = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly Brush DarkTextTertiary = new SolidColorBrush(Color.FromArgb(0xB0, 0x1F, 0x1F, 0x1F));
    private static readonly Brush LightTextTertiary = new SolidColorBrush(Color.FromArgb(0xB0, 0xF5, 0xF5, 0xF5));

    static HexToReadableForegroundConverter()
    {
        DarkText.Freeze();
        LightText.Freeze();
        DarkTextTertiary.Freeze();
        LightTextTertiary.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTertiary = parameter is string p && p == "Tertiary";

        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return isTertiary ? DarkTextTertiary : DarkText;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            // YIQ 명도 계산 (W3C 권장): 128 미만 = 어두움 → 흰 글자, 이상 = 밝음 → 검정 글자
            var yiq = (color.R * 299 + color.G * 587 + color.B * 114) / 1000.0;
            var isDarkBackground = yiq < 128;
            if (isTertiary)
                return isDarkBackground ? LightTextTertiary : DarkTextTertiary;
            return isDarkBackground ? LightText : DarkText;
        }
        catch
        {
            return isTertiary ? DarkTextTertiary : DarkText;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
