using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MaiX.Services.Storage;
using MaiX.Utils;

namespace MaiX.Services.Theme;

/// <summary>
/// 테마 관리 서비스 - 다크/라이트 모드 전환 및 기능별 AccentColor 관리
/// </summary>
public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    // 즐겨찾기 별 아이콘 색상
    private static readonly Color DarkModeStarColor = (Color)ColorConverter.ConvertFromString("#FFD700");   // 밝은 노란색
    private static readonly Color LightModeStarColor = (Color)ColorConverter.ConvertFromString("#B8860B");  // 어두운 노란색 (DarkGoldenrod)

    // 선택 배경색
    private static readonly Color DarkModeSelectionColor = (Color)ColorConverter.ConvertFromString("#444444");   // 다크모드: 어두운 회색
    private static readonly Color LightModeSelectionColor = (Color)ColorConverter.ConvertFromString("#CCCCCC");  // 라이트모드: 밝은 회색

    #region 기능별 AccentColor 정의

    /// <summary>
    /// 기능별 AccentColor (라이트모드)
    /// </summary>
    private static readonly Dictionary<string, Color> FeatureAccentColors = new()
    {
        { "mail", (Color)ColorConverter.ConvertFromString("#0078D4") },      // Outlook Blue
        { "calendar", (Color)ColorConverter.ConvertFromString("#0078D4") },  // Calendar Blue
        { "chat", (Color)ColorConverter.ConvertFromString("#6264A7") },      // Teams Purple
        { "teams", (Color)ColorConverter.ConvertFromString("#6264A7") },     // Teams Purple
        { "planner", (Color)ColorConverter.ConvertFromString("#31752F") },   // Planner Green
        { "onedrive", (Color)ColorConverter.ConvertFromString("#0078D4") },  // OneDrive Blue
        { "onenote", (Color)ColorConverter.ConvertFromString("#7719AA") },   // OneNote Purple
        { "activity", (Color)ColorConverter.ConvertFromString("#F7630C") },  // Activity Orange
        { "calls", (Color)ColorConverter.ConvertFromString("#0078D4") }      // Calling Blue
    };

    /// <summary>
    /// 기능별 AccentColor (다크모드 - 약간 밝게 조정)
    /// </summary>
    private static readonly Dictionary<string, Color> FeatureAccentColorsDark = new()
    {
        { "mail", (Color)ColorConverter.ConvertFromString("#106EBE") },      // Outlook Blue Dark
        { "calendar", (Color)ColorConverter.ConvertFromString("#106EBE") },  // Calendar Blue Dark
        { "chat", (Color)ColorConverter.ConvertFromString("#7B7DB8") },      // Teams Purple Dark
        { "teams", (Color)ColorConverter.ConvertFromString("#7B7DB8") },     // Teams Purple Dark
        { "planner", (Color)ColorConverter.ConvertFromString("#4CAF50") },   // Planner Green Dark
        { "onedrive", (Color)ColorConverter.ConvertFromString("#106EBE") },  // OneDrive Blue Dark
        { "onenote", (Color)ColorConverter.ConvertFromString("#9C27B0") },   // OneNote Purple Dark
        { "activity", (Color)ColorConverter.ConvertFromString("#FF8C00") },  // Activity Orange Dark
        { "calls", (Color)ColorConverter.ConvertFromString("#106EBE") }      // Calling Blue Dark
    };

    /// <summary>
    /// 현재 활성화된 기능
    /// </summary>
    public string CurrentFeature { get; private set; } = "mail";

    /// <summary>
    /// 기능별 테마 변경 이벤트
    /// </summary>
    public event EventHandler<string>? FeatureThemeChanged;

    #endregion

    /// <summary>
    /// 현재 테마
    /// </summary>
    public ApplicationTheme CurrentTheme { get; private set; } = ApplicationTheme.Dark;

    /// <summary>
    /// 테마 변경 이벤트
    /// </summary>
    public event EventHandler<ApplicationTheme>? ThemeChanged;

    private ThemeService() { }

    /// <summary>
    /// 설정 매니저 참조 설정
    /// </summary>
    public AppSettingsManager? SettingsManager { get; set; }

    /// <summary>
    /// 테마 적용 (Mica 백드롭 유지)
    /// </summary>
    private void ApplyThemeWithMica(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(
            theme,
            WindowBackdropType.Mica,
            updateAccent: true
        );
    }

    /// <summary>
    /// 다크 모드로 변경
    /// </summary>
    public void SetDarkMode()
    {
        Log4.Info("테마 변경: 다크 모드");
        CurrentTheme = ApplicationTheme.Dark;
        ApplyThemeWithMica(ApplicationTheme.Dark);
        UpdateThemeResources(ApplicationTheme.Dark);
        ReapplyCurrentFeatureTheme();  // 기능별 테마도 재적용
        SaveThemeSetting();
        ThemeChanged?.Invoke(this, ApplicationTheme.Dark);
    }

    /// <summary>
    /// 라이트 모드로 변경
    /// </summary>
    public void SetLightMode()
    {
        Log4.Info("테마 변경: 라이트 모드");
        CurrentTheme = ApplicationTheme.Light;
        ApplyThemeWithMica(ApplicationTheme.Light);
        UpdateThemeResources(ApplicationTheme.Light);
        ReapplyCurrentFeatureTheme();  // 기능별 테마도 재적용
        SaveThemeSetting();
        ThemeChanged?.Invoke(this, ApplicationTheme.Light);
    }

    /// <summary>
    /// 테마 설정 저장
    /// </summary>
    private void SaveThemeSetting()
    {
        if (SettingsManager != null)
        {
            SettingsManager.UserPreferences.Theme = CurrentTheme == ApplicationTheme.Dark ? "Dark" : "Light";
            SettingsManager.SaveUserPreferences();
            Log4.Debug($"테마 설정 저장: {SettingsManager.UserPreferences.Theme}");
        }
    }

    /// <summary>
    /// 저장된 테마 설정 로드 및 적용
    /// </summary>
    public void LoadSavedTheme()
    {
        if (SettingsManager != null)
        {
            var savedTheme = SettingsManager.UserPreferences.Theme;
            Log4.Info($"저장된 테마 로드: {savedTheme}");

            if (savedTheme == "Light")
            {
                CurrentTheme = ApplicationTheme.Light;
                ApplyThemeWithMica(ApplicationTheme.Light);
                UpdateThemeResources(ApplicationTheme.Light);
            }
            else
            {
                CurrentTheme = ApplicationTheme.Dark;
                ApplyThemeWithMica(ApplicationTheme.Dark);
                UpdateThemeResources(ApplicationTheme.Dark);
            }
        }
    }

    /// <summary>
    /// 테마별 커스텀 리소스 업데이트
    /// </summary>
    private void UpdateThemeResources(ApplicationTheme theme)
    {
        var starColor = theme == ApplicationTheme.Dark ? DarkModeStarColor : LightModeStarColor;
        var selectionColor = theme == ApplicationTheme.Dark ? DarkModeSelectionColor : LightModeSelectionColor;

        Application.Current.Resources["FavoriteStarBrush"] = new SolidColorBrush(starColor);
        Application.Current.Resources["SelectionBackgroundBrush"] = new SolidColorBrush(selectionColor);
    }

    /// <summary>
    /// 초기 테마 리소스 설정
    /// </summary>
    public void InitializeThemeResources()
    {
        UpdateThemeResources(CurrentTheme);
    }

    /// <summary>
    /// 테마 토글
    /// </summary>
    public void ToggleTheme()
    {
        if (CurrentTheme == ApplicationTheme.Dark)
            SetLightMode();
        else
            SetDarkMode();
    }

    /// <summary>
    /// 다크 모드 여부
    /// </summary>
    public bool IsDarkMode => CurrentTheme == ApplicationTheme.Dark;

    #region 기능별 AccentColor 메서드

    /// <summary>
    /// 기능별 AccentColor 반환
    /// </summary>
    /// <param name="feature">기능 이름 (mail, calendar, chat, teams, planner, onedrive, onenote, activity, calls)</param>
    /// <returns>해당 기능의 AccentColor</returns>
    public Color GetAccentColor(string feature)
    {
        var featureLower = feature.ToLowerInvariant();
        var colorDict = IsDarkMode ? FeatureAccentColorsDark : FeatureAccentColors;

        if (colorDict.TryGetValue(featureLower, out var color))
        {
            return color;
        }

        // 기본값: Outlook Blue
        return IsDarkMode
            ? (Color)ColorConverter.ConvertFromString("#106EBE")
            : (Color)ColorConverter.ConvertFromString("#0078D4");
    }

    /// <summary>
    /// 기능별 AccentColor Brush 반환
    /// </summary>
    /// <param name="feature">기능 이름</param>
    /// <returns>SolidColorBrush</returns>
    public SolidColorBrush GetAccentBrush(string feature)
    {
        return new SolidColorBrush(GetAccentColor(feature));
    }

    /// <summary>
    /// 기능별 테마 적용 (AccentColor 변경)
    /// </summary>
    /// <param name="feature">기능 이름</param>
    public void ApplyFeatureTheme(string feature)
    {
        var featureLower = feature.ToLowerInvariant();
        CurrentFeature = featureLower;

        var accentColor = GetAccentColor(featureLower);
        var accentBrush = new SolidColorBrush(accentColor);

        // Application 리소스에 AccentColor 업데이트
        Application.Current.Resources["FeatureAccentColor"] = accentColor;
        Application.Current.Resources["FeatureAccentBrush"] = accentBrush;

        // 투명도 변형
        Application.Current.Resources["FeatureAccentBrushLight"] = new SolidColorBrush(Color.FromArgb(80, accentColor.R, accentColor.G, accentColor.B));
        Application.Current.Resources["FeatureAccentBrushDim"] = new SolidColorBrush(Color.FromArgb(128, accentColor.R, accentColor.G, accentColor.B));

        Log4.Debug($"기능별 테마 적용: {feature} -> {accentColor}");
        FeatureThemeChanged?.Invoke(this, featureLower);
    }

    /// <summary>
    /// 현재 기능의 테마 재적용 (다크/라이트 모드 전환 시 호출)
    /// </summary>
    public void ReapplyCurrentFeatureTheme()
    {
        ApplyFeatureTheme(CurrentFeature);
    }

    /// <summary>
    /// 기능별 테마 리소스 초기화
    /// </summary>
    public void InitializeFeatureThemeResources()
    {
        // 기본값: mail
        ApplyFeatureTheme("mail");
    }

    #endregion
}
