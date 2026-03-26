using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MaiX.Models;

namespace MaiX.Converters;

/// <summary>
/// RecordingSource를 Visibility로 변환 (ConverterParameter로 비교)
/// </summary>
public class RecordingSourceToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RecordingSource source && parameter is string paramStr)
        {
            if (Enum.TryParse<RecordingSource>(paramStr, out var compareSource))
            {
                return source == compareSource ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// RecordingSource를 배경색(Brush)으로 변환
/// MaiX: 파란색, External: 회색
/// </summary>
public class RecordingSourceToBackgroundConverter : IValueConverter
{
    private static readonly SolidColorBrush MaiXBrush = new(Color.FromRgb(0, 120, 212));  // #0078D4
    private static readonly SolidColorBrush ExternalBrush = new(Color.FromRgb(136, 136, 136));  // #888888

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is RecordingSource source)
        {
            return source == RecordingSource.MaiX ? MaiXBrush : ExternalBrush;
        }
        return ExternalBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
