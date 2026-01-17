using System;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;
using mailX.Models.Settings;
using mailX.Utils;

namespace mailX.Views.Dialogs;

/// <summary>
/// 동기화 설정 다이얼로그
/// </summary>
public partial class SyncSettingsDialog : FluentWindow
{
    /// <summary>
    /// 다이얼로그 결과 - 메일 동기화 설정
    /// </summary>
    public SyncPeriodSettings? MailSyncSettings { get; private set; }

    /// <summary>
    /// 다이얼로그 결과 - AI 분석 설정
    /// </summary>
    public SyncPeriodSettings? AiAnalysisSettings { get; private set; }

    /// <summary>
    /// 저장 여부
    /// </summary>
    public bool IsSaved { get; private set; }

    public SyncSettingsDialog()
    {
        InitializeComponent();
        Log4.Debug("SyncSettingsDialog 생성");

        // ESC 키로 창 닫기
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                IsSaved = false;
                DialogResult = false;
                Close();
            }
        };
    }

    /// <summary>
    /// 현재 설정으로 다이얼로그 초기화
    /// </summary>
    public void LoadSettings(SyncPeriodSettings mailSettings, SyncPeriodSettings aiSettings)
    {
        // 메일 설정 로드
        LoadMailSettings(mailSettings);

        // AI 설정 로드
        LoadAiSettings(aiSettings);

        Log4.Debug($"설정 로드 완료 - 메일: {mailSettings.ToDisplayString()}, AI: {aiSettings.ToDisplayString()}");
    }

    private void LoadMailSettings(SyncPeriodSettings settings)
    {
        switch (settings.PeriodType)
        {
            case SyncPeriodType.Count:
                MailCount.IsChecked = true;
                MailCountValue.Value = settings.Value;
                break;
            case SyncPeriodType.Days:
                MailDays.IsChecked = true;
                MailDaysValue.Value = settings.Value;
                break;
            case SyncPeriodType.Weeks:
                MailWeeks.IsChecked = true;
                MailWeeksValue.Value = settings.Value;
                break;
            case SyncPeriodType.Months:
                MailMonths.IsChecked = true;
                MailMonthsValue.Value = settings.Value;
                break;
            case SyncPeriodType.Years:
                MailYears.IsChecked = true;
                MailYearsValue.Value = settings.Value;
                break;
            case SyncPeriodType.DateRange:
                MailDateRange.IsChecked = true;
                MailStartDate.SelectedDate = settings.StartDate ?? DateTime.Today;
                MailEndDate.SelectedDate = settings.EndDate ?? DateTime.Today;
                break;
            case SyncPeriodType.All:
                MailAll.IsChecked = true;
                break;
        }
    }

    private void LoadAiSettings(SyncPeriodSettings settings)
    {
        switch (settings.PeriodType)
        {
            case SyncPeriodType.Count:
                AiCount.IsChecked = true;
                AiCountValue.Value = settings.Value;
                break;
            case SyncPeriodType.Days:
                AiDays.IsChecked = true;
                AiDaysValue.Value = settings.Value;
                break;
            case SyncPeriodType.Weeks:
                AiWeeks.IsChecked = true;
                AiWeeksValue.Value = settings.Value;
                break;
            case SyncPeriodType.Months:
                AiMonths.IsChecked = true;
                AiMonthsValue.Value = settings.Value;
                break;
            case SyncPeriodType.Years:
                AiYears.IsChecked = true;
                AiYearsValue.Value = settings.Value;
                break;
            case SyncPeriodType.DateRange:
                AiDateRange.IsChecked = true;
                AiStartDate.SelectedDate = settings.StartDate ?? DateTime.Today;
                AiEndDate.SelectedDate = settings.EndDate ?? DateTime.Today;
                break;
            case SyncPeriodType.All:
                AiAll.IsChecked = true;
                break;
        }
    }

    private SyncPeriodSettings GetMailSettings()
    {
        var settings = new SyncPeriodSettings();

        if (MailCount.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Count;
            settings.Value = (int)(MailCountValue.Value ?? 100);
        }
        else if (MailDays.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Days;
            settings.Value = (int)(MailDaysValue.Value ?? 7);
        }
        else if (MailWeeks.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Weeks;
            settings.Value = (int)(MailWeeksValue.Value ?? 1);
        }
        else if (MailMonths.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Months;
            settings.Value = (int)(MailMonthsValue.Value ?? 1);
        }
        else if (MailYears.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Years;
            settings.Value = (int)(MailYearsValue.Value ?? 1);
        }
        else if (MailDateRange.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.DateRange;
            settings.StartDate = MailStartDate.SelectedDate;
            settings.EndDate = MailEndDate.SelectedDate;
        }
        else if (MailAll.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.All;
        }

        return settings;
    }

    private SyncPeriodSettings GetAiSettings()
    {
        var settings = new SyncPeriodSettings();

        if (AiCount.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Count;
            settings.Value = (int)(AiCountValue.Value ?? 100);
        }
        else if (AiDays.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Days;
            settings.Value = (int)(AiDaysValue.Value ?? 7);
        }
        else if (AiWeeks.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Weeks;
            settings.Value = (int)(AiWeeksValue.Value ?? 1);
        }
        else if (AiMonths.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Months;
            settings.Value = (int)(AiMonthsValue.Value ?? 1);
        }
        else if (AiYears.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.Years;
            settings.Value = (int)(AiYearsValue.Value ?? 1);
        }
        else if (AiDateRange.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.DateRange;
            settings.StartDate = AiStartDate.SelectedDate;
            settings.EndDate = AiEndDate.SelectedDate;
        }
        else if (AiAll.IsChecked == true)
        {
            settings.PeriodType = SyncPeriodType.All;
        }

        return settings;
    }

    private void MailPeriodType_Changed(object sender, RoutedEventArgs e)
    {
        // UI 상태 업데이트 (필요 시)
    }

    private void AiPeriodType_Changed(object sender, RoutedEventArgs e)
    {
        // UI 상태 업데이트 (필요 시)
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        MailSyncSettings = GetMailSettings();
        AiAnalysisSettings = GetAiSettings();
        IsSaved = true;

        Log4.Info($"동기화 설정 저장 - 메일: {MailSyncSettings.ToDisplayString()}, AI: {AiAnalysisSettings.ToDisplayString()}");

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsSaved = false;
        DialogResult = false;
        Close();
    }
}
