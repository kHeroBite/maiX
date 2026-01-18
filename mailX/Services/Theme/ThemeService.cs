using System;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using mailX.Services.Storage;
using mailX.Utils;

namespace mailX.Services.Theme;

/// <summary>
/// 테마 관리 서비스 - 다크/라이트 모드 전환
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
}
