using System;
using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace MaiX.Converters;

/// <summary>
/// 정렬 기준 값에 따라 버튼 Appearance를 변환하는 컨버터
/// 현재 정렬 기준과 일치하면 Primary, 아니면 Secondary
/// </summary>
public class SortByToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string currentSort && parameter is string buttonSort)
        {
            return currentSort == buttonSort
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
