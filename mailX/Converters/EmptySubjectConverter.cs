using System;
using System.Globalization;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// 빈 제목을 "(제목 없음)"으로 변환하는 컨버터
/// </summary>
public class EmptySubjectConverter : IValueConverter
{
    /// <summary>
    /// 제목이 null이거나 빈 문자열이면 "(제목 없음)"으로 변환
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string subject && !string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        return "(제목 없음)";
    }

    /// <summary>
    /// 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
