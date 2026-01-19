using System;
using System.Globalization;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// "이름 <주소>" 형식에서 이름만 추출하여 표시
/// 이름이 없으면 주소 그대로 표시
/// </summary>
public class EmailToDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string emailString || string.IsNullOrEmpty(emailString))
            return string.Empty;

        // "이름 <주소>" 형식인지 확인
        var bracketIndex = emailString.IndexOf(" <");
        if (bracketIndex > 0 && emailString.EndsWith(">"))
        {
            // 이름 부분만 추출
            return emailString.Substring(0, bracketIndex);
        }

        // 이름이 없으면 그대로 반환 (이메일 주소)
        return emailString;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
