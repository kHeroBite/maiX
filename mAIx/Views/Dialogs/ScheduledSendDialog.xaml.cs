using System;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace mAIx.Views.Dialogs;

/// <summary>
/// 예약 발송 날짜/시간 선택 다이얼로그
/// </summary>
public partial class ScheduledSendDialog : FluentWindow
{
    /// <summary>
    /// 선택된 예약 발송 시간
    /// </summary>
    public DateTime SelectedDateTime { get; private set; }

    public ScheduledSendDialog(DateTime initialDateTime)
    {
        InitializeComponent();

        DatePickerControl.SelectedDate = initialDateTime.Date;
        TimeTextBox.Text = initialDateTime.ToString("HH:mm");

        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                ConfirmSelection();
            }
        };

        Loaded += (s, e) => TimeTextBox.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        ValidationText.Visibility = Visibility.Collapsed;

        if (DatePickerControl.SelectedDate == null)
        {
            ValidationText.Text = "날짜를 선택하세요.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        if (!TimeSpan.TryParse(TimeTextBox.Text, out var time))
        {
            ValidationText.Text = "올바른 시간 형식을 입력하세요. (예: 14:30)";
            ValidationText.Visibility = Visibility.Visible;
            TimeTextBox.Focus();
            return;
        }

        var selectedDate = DatePickerControl.SelectedDate.Value.Date;
        SelectedDateTime = selectedDate.Add(time);

        if (SelectedDateTime <= DateTime.Now)
        {
            ValidationText.Text = "예약 시간은 현재 시간보다 미래여야 합니다.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
        Close();
    }
}
