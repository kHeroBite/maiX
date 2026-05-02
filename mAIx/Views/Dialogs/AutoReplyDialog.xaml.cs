using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mAIx.Services;
using mAIx.Utils;

namespace mAIx.Views.Dialogs;

/// <summary>
/// 부재중 자동응답 설정 다이얼로그
/// </summary>
public partial class AutoReplyDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly AutoReplyService _autoReplyService;

    public AutoReplyDialog(AutoReplyService autoReplyService)
    {
        _autoReplyService = autoReplyService;
        InitializeComponent();

        // 기본값: 종료일 = 내일
        EndDatePicker.SelectedDate = DateTime.Now.AddDays(1);
        StartDatePicker.SelectedDate = DateTime.Now;

        Loaded += async (s, e) =>
        {
            try { await LoadCurrentSettingsAsync(); }
            catch (Exception ex) { Log4.Error($"[AutoReplyDialog] Loaded 핸들러 실패: {ex}"); }
        };
    }

    /// <summary>
    /// 현재 서버 설정 로드
    /// </summary>
    private async System.Threading.Tasks.Task LoadCurrentSettingsAsync()
    {
        try
        {
            var setting = await _autoReplyService.GetAutoReplyStatusAsync();

            AutoReplyToggle.IsChecked = setting.IsEnabled;

            if (setting.Status == "scheduled")
            {
                ScheduledRadio.IsChecked = true;
            }
            else
            {
                AlwaysEnabledRadio.IsChecked = true;
            }

            InternalMessageBox.Text = setting.InternalReplyMessage;
            ExternalMessageBox.Text = setting.ExternalReplyMessage;

            if (setting.ScheduledStartDateTime.HasValue)
                StartDatePicker.SelectedDate = setting.ScheduledStartDateTime.Value.DateTime;
            if (setting.ScheduledEndDateTime.HasValue)
                EndDatePicker.SelectedDate = setting.ScheduledEndDateTime.Value.DateTime;

            // 외부 수신자 범위
            for (int i = 0; i < ExternalAudienceCombo.Items.Count; i++)
            {
                if (ExternalAudienceCombo.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == setting.ExternalAudience)
                {
                    ExternalAudienceCombo.SelectedIndex = i;
                    break;
                }
            }

            UpdatePanelVisibility();
        }
        catch (Exception ex)
        {
            Log4.Error($"자동응답 설정 로드 실패: {ex.Message}");
            MessageBox.Show($"설정을 불러올 수 없습니다.\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 토글 변경 시 패널 표시/숨김
    /// </summary>
    private void AutoReplyToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePanelVisibility();
    }

    /// <summary>
    /// 모드 라디오 변경
    /// </summary>
    private void ModeRadio_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePanelVisibility();
    }

    /// <summary>
    /// 패널 표시 상태 업데이트
    /// </summary>
    private void UpdatePanelVisibility()
    {
        if (ModePanel == null) return;

        var isEnabled = AutoReplyToggle.IsChecked == true;
        ModePanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        AudiencePanel.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        SchedulePanel.Visibility = isEnabled && ScheduledRadio.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 저장 버튼
    /// </summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveButton.IsEnabled = false;

            var setting = new AutoReplySetting();

            if (AutoReplyToggle.IsChecked == true)
            {
                setting.Status = ScheduledRadio.IsChecked == true ? "scheduled" : "alwaysEnabled";
                setting.InternalReplyMessage = InternalMessageBox.Text;
                setting.ExternalReplyMessage = ExternalMessageBox.Text;

                if (ScheduledRadio.IsChecked == true)
                {
                    setting.ScheduledStartDateTime = StartDatePicker.SelectedDate.HasValue
                        ? new DateTimeOffset(StartDatePicker.SelectedDate.Value)
                        : DateTimeOffset.Now;
                    setting.ScheduledEndDateTime = EndDatePicker.SelectedDate.HasValue
                        ? new DateTimeOffset(EndDatePicker.SelectedDate.Value)
                        : DateTimeOffset.Now.AddDays(1);
                }

                // 외부 수신자 범위
                if (ExternalAudienceCombo.SelectedItem is ComboBoxItem selectedAudience)
                {
                    setting.ExternalAudience = selectedAudience.Tag?.ToString() ?? "all";
                }
            }
            else
            {
                setting.Status = "disabled";
            }

            await _autoReplyService.SetAutoReplyAsync(setting);

            Log4.Info($"자동응답 설정 저장 완료: {setting.Status}");
            MessageBox.Show("자동응답 설정이 저장되었습니다.", "완료",
                MessageBoxButton.OK, MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Log4.Error($"자동응답 설정 저장 실패: {ex.Message}");
            MessageBox.Show($"설정 저장에 실패했습니다.\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// 취소 버튼
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 키 입력 처리
    /// </summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}
