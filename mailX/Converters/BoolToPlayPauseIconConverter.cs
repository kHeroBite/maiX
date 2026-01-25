using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace mailX.Converters;

/// <summary>
/// 재생 중 여부를 Play/Pause 아이콘으로 변환
/// True (재생 중) → Pause24, False (정지) → Play24
/// </summary>
public class BoolToPlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isPlaying && isPlaying)
        {
            return SymbolRegular.Pause24;
        }
        return SymbolRegular.Play24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
