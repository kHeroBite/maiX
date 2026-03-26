using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mAIx.Converters;

/// <summary>
/// 부울 값(읽음 여부)을 FontWeight로 변환하는 컨버터
/// 읽지 않은 메일(false)은 Bold, 읽은 메일(true)은 Normal
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    /// <summary>
    /// 부울 값을 FontWeight로 변환
    /// </summary>
    /// <param name="value">변환할 부울 값 (IsRead)</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>FontWeight 값</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isRead = false;
        if (value is bool b)
        {
            isRead = b;
        }

        // 읽지 않은 메일은 Bold, 읽은 메일은 Normal
        return isRead ? FontWeights.Normal : FontWeights.SemiBold;
    }

    /// <summary>
    /// FontWeight를 부울로 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("BoolToFontWeightConverter는 역변환을 지원하지 않습니다.");
    }
}
