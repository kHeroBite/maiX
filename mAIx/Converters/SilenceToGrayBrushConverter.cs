// 묵음 항목을 회색 브러시로 변환하는 컨버터
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace mAIx.Converters;

/// <summary>
/// IsSilence == true → 회색 SolidColorBrush 반환, false → Binding.DoNothing (원래 색 유지).
/// 대화네비 카드 및 실시간요약 텍스트에 묵음 회색 강제 적용 시 사용.
/// </summary>
public class SilenceToGrayBrushConverter : IValueConverter
{
    private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0));

    static SilenceToGrayBrushConverter()
    {
        GrayBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSilence && isSilence)
            return GrayBrush;

        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
