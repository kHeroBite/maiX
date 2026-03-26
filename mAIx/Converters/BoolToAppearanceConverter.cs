using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace mAIx.Converters;

/// <summary>
/// bool 값에 따라 버튼 Appearance를 변환하는 컨버터
/// true이면 Primary, false이면 Secondary
/// </summary>
public class BoolToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive
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
