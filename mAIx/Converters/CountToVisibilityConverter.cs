using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mAIx.Converters;

/// <summary>
/// 컬렉션 Count를 기반으로 특정 인덱스 항목의 Visibility를 결정하는 컨버터
/// Count가 parameter + 1 보다 크면 Visible, 아니면 Collapsed
/// 예: Count=3, parameter=0 → Visible (인덱스 0 존재)
/// 예: Count=3, parameter=3 → Collapsed (인덱스 3 없음)
/// </summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && parameter != null)
        {
            if (int.TryParse(parameter.ToString(), out int index))
            {
                // Count가 index + 1 이상이면 해당 인덱스 항목이 존재
                return count > index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
