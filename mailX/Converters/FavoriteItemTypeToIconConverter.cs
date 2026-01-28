using System;
using System.Globalization;
using System.Windows.Data;
using mailX.ViewModels;

namespace mailX.Converters;

/// <summary>
/// FavoriteItemType을 아이콘 Symbol로 변환하는 컨버터
/// </summary>
public class FavoriteItemTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FavoriteItemType itemType)
        {
            return itemType switch
            {
                FavoriteItemType.Notebook => "Notebook24",
                FavoriteItemType.Section => "FolderOpen24",
                FavoriteItemType.Page => "Document24",
                _ => "Document24"
            };
        }
        return "Document24";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// FavoriteItemType을 아이콘 색상으로 변환하는 컨버터
/// </summary>
public class FavoriteItemTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FavoriteItemType itemType)
        {
            return itemType switch
            {
                FavoriteItemType.Notebook => "#7719AA",  // 보라색 (OneNote 노트북)
                FavoriteItemType.Section => "#107C10",   // 녹색 (폴더)
                FavoriteItemType.Page => "#666666",      // 회색 (문서)
                _ => "#666666"
            };
        }
        return "#666666";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
