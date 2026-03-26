using System;
using System.Globalization;
using System.Windows.Data;
using mAIx.ViewModels;

namespace mAIx.Converters;

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

/// <summary>
/// FavoriteItemType이 Notebook인 경우 Visible 반환
/// </summary>
public class FavoriteItemTypeToNotebookVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FavoriteItemType itemType && itemType == FavoriteItemType.Notebook)
        {
            return System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// FavoriteItemType이 Section인 경우 Visible 반환
/// </summary>
public class FavoriteItemTypeToSectionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FavoriteItemType itemType && itemType == FavoriteItemType.Section)
        {
            return System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// FavoriteItemType이 Notebook 또는 Section인 경우 Visible 반환 (Separator용)
/// </summary>
public class FavoriteItemTypeToNotSectionVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FavoriteItemType itemType && (itemType == FavoriteItemType.Notebook || itemType == FavoriteItemType.Section))
        {
            return System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// FavoriteItemType이 Page인 경우 Visible 반환
/// </summary>
public class FavoriteItemTypeToPageVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FavoriteItemType itemType && itemType == FavoriteItemType.Page)
        {
            return System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// IsDirectFavorite가 true인 경우 Visible 반환 (직접 즐겨찾기 항목만 표시)
/// </summary>
public class DirectFavoriteToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isDirectFavorite && isDirectFavorite)
        {
            return System.Windows.Visibility.Visible;
        }
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// IsSelected를 배경색으로 변환하는 컨버터 (페이지 선택 하이라이트용)
/// </summary>
public class IsSelectedToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isSelected && isSelected)
        {
            // 선택된 경우 파란색 배경
            return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x00, 0x78, 0xD7));
        }
        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
