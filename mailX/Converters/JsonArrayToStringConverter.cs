using System;
using System.Globalization;
using System.Text.Json;
using System.Windows.Data;

namespace mailX.Converters;

/// <summary>
/// JSON 배열 문자열을 표시용 문자열로 변환
/// ["김기로 <a@b.com>", "홍길동 <c@d.com>"] → "김기로 <a@b.com>; 홍길동 <c@d.com>"
/// </summary>
public class JsonArrayToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string jsonString || string.IsNullOrWhiteSpace(jsonString))
            return string.Empty;

        // JSON 배열이 아니면 그대로 반환
        if (!jsonString.StartsWith("["))
            return jsonString;

        try
        {
            var items = JsonSerializer.Deserialize<string[]>(jsonString);
            if (items == null || items.Length == 0)
                return string.Empty;

            return string.Join("; ", items);
        }
        catch
        {
            return jsonString;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
