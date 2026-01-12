using System;
using System.Globalization;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// 문자열(이름/이메일)을 이니셜로 변환하는 컨버터
/// 아바타 표시용
/// </summary>
public class StringToInitialConverter : IValueConverter
{
    /// <summary>
    /// 문자열을 이니셜로 변환
    /// </summary>
    /// <param name="value">변환할 문자열 (이름 또는 이메일)</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>이니셜 문자열 (1-2자)</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string str || string.IsNullOrWhiteSpace(str))
        {
            return "?";
        }

        // 이메일 주소인 경우 @ 앞 부분 사용
        if (str.Contains('@'))
        {
            str = str.Split('@')[0];
        }

        // 이름에서 공백으로 구분된 경우 각 단어의 첫 글자 조합
        var parts = str.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            // 두 단어의 첫 글자 조합
            return $"{GetFirstChar(parts[0])}{GetFirstChar(parts[1])}".ToUpperInvariant();
        }

        // 단일 단어인 경우 첫 글자만
        return GetFirstChar(str).ToString().ToUpperInvariant();
    }

    /// <summary>
    /// 문자열의 첫 글자 반환 (한글, 영문 등 처리)
    /// </summary>
    private static char GetFirstChar(string str)
    {
        if (string.IsNullOrEmpty(str))
            return '?';

        return str[0];
    }

    /// <summary>
    /// 이니셜을 문자열로 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("StringToInitialConverter는 역변환을 지원하지 않습니다.");
    }
}
