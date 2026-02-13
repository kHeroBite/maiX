using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace MaiX.Converters;

/// <summary>
/// Base64 문자열을 ImageSource로 변환하는 컨버터
/// 프로필 사진 표시에 사용
/// </summary>
public class Base64ToImageSourceConverter : IValueConverter
{
    /// <summary>
    /// Base64 문자열을 BitmapImage로 변환
    /// </summary>
    /// <param name="value">Base64 인코딩된 이미지 문자열</param>
    /// <param name="targetType">대상 타입</param>
    /// <param name="parameter">변환 파라미터 (미사용)</param>
    /// <param name="culture">문화권 정보</param>
    /// <returns>BitmapImage 또는 null</returns>
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string base64 || string.IsNullOrEmpty(base64))
            return null;

        try
        {
            var bytes = System.Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // 메모리에 캐시하여 스트림 닫힘 후에도 사용 가능
            bitmap.DecodePixelWidth = 64; // 성능 최적화 (자동완성에서 32px 표시하므로 2배 해상도로 충분)
            bitmap.EndInit();
            bitmap.Freeze(); // 쓰레드 안전을 위해 Freeze

            return bitmap;
        }
        catch
        {
            // 이미지 변환 실패 시 null 반환 (이니셜 표시됨)
            return null;
        }
    }

    /// <summary>
    /// 역변환 (지원하지 않음)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("Base64ToImageSourceConverter는 역변환을 지원하지 않습니다.");
    }
}
