using System;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace mailX.Converters;

/// <summary>
/// 카테고리 JSON 배열에서 첫 번째 카테고리의 색상을 반환하는 컨버터
/// Outlook 기본 카테고리 색상 매핑
/// </summary>
public class CategoryToColorConverter : IValueConverter
{
    // 아웃룩 기본 카테고리 색상 매핑
    private static readonly Dictionary<string, string> CategoryColors = new(StringComparer.OrdinalIgnoreCase)
    {
        // 한글 카테고리
        { "빨강 범주", "#E74856" },
        { "주황 범주", "#FF8C00" },
        { "노랑 범주", "#FFCD2D" },
        { "초록 범주", "#0B8A00" },
        { "파랑 범주", "#0078D7" },
        { "자주 범주", "#8764B8" },
        // 영문 카테고리
        { "Red category", "#E74856" },
        { "Red Category", "#E74856" },
        { "Orange category", "#FF8C00" },
        { "Orange Category", "#FF8C00" },
        { "Yellow category", "#FFCD2D" },
        { "Yellow Category", "#FFCD2D" },
        { "Green category", "#0B8A00" },
        { "Green Category", "#0B8A00" },
        { "Blue category", "#0078D7" },
        { "Blue Category", "#0078D7" },
        { "Purple category", "#8764B8" },
        { "Purple Category", "#8764B8" },
        // 색상 이름만 (간단 버전)
        { "빨강", "#E74856" },
        { "주황", "#FF8C00" },
        { "노랑", "#FFCD2D" },
        { "초록", "#0B8A00" },
        { "파랑", "#0078D7" },
        { "자주", "#8764B8" },
        { "보라", "#8764B8" },
        { "Red", "#E74856" },
        { "Orange", "#FF8C00" },
        { "Yellow", "#FFCD2D" },
        { "Green", "#0B8A00" },
        { "Blue", "#0078D7" },
        { "Purple", "#8764B8" }
    };

    /// <summary>
    /// 카테고리 JSON을 색상 Brush로 변환
    /// </summary>
    /// <param name="value">카테고리 JSON 배열 문자열</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>SolidColorBrush 또는 Transparent</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string categoriesJson && !string.IsNullOrWhiteSpace(categoriesJson))
        {
            try
            {
                var categories = JsonSerializer.Deserialize<string[]>(categoriesJson);
                if (categories != null && categories.Length > 0)
                {
                    string firstCategory = categories[0];

                    // 매핑에서 색상 찾기
                    if (CategoryColors.TryGetValue(firstCategory, out string? hexColor))
                    {
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
                    }

                    // 매핑에 없으면 기본 파랑색 반환
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D7"));
                }
            }
            catch
            {
                // JSON 파싱 실패 시 무시
            }
        }

        return Brushes.Transparent;
    }

    /// <summary>
    /// 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 카테고리가 있는지 확인하여 Visibility로 변환하는 컨버터
/// </summary>
public class CategoryToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// 카테고리 존재 여부를 Visibility로 변환
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string categoriesJson && !string.IsNullOrWhiteSpace(categoriesJson))
        {
            try
            {
                var categories = JsonSerializer.Deserialize<string[]>(categoriesJson);
                if (categories != null && categories.Length > 0)
                {
                    return Visibility.Visible;
                }
            }
            catch
            {
                // JSON 파싱 실패 시 무시
            }
        }

        return Visibility.Collapsed;
    }

    /// <summary>
    /// 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
