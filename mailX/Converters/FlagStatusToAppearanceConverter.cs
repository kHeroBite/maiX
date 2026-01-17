using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace mailX.Converters;

/// <summary>
/// 플래그 상태(문자열)에 따라 버튼 Appearance를 변환하는 컨버터
/// flagged/complete이면 Primary, 그 외에는 Secondary
/// </summary>
public class FlagStatusToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string flagStatus)
        {
            return flagStatus == "flagged" || flagStatus == "complete"
                ? ControlAppearance.Primary
                : ControlAppearance.Secondary;
        }
        return ControlAppearance.Secondary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
