using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Graph.Models;
using Wpf.Ui.Controls;
using mailX.Services.Graph;
using mailX.Utils;

using Application = System.Windows.Application;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace mailX.Views.Dialogs;

/// <summary>
/// 일정 편집 다이얼로그
/// </summary>
public partial class EventEditDialog : FluentWindow
{
    private readonly GraphCalendarService? _calendarService;
    private Event? _existingEvent;
    private bool _isEditMode;

    /// <summary>
    /// 생성된/수정된 일정
    /// </summary>
    public Event? ResultEvent { get; private set; }

    /// <summary>
    /// 일정이 삭제되었는지 여부
    /// </summary>
    public bool IsDeleted { get; private set; }

    /// <summary>
    /// 새 일정 생성 모드
    /// </summary>
    public EventEditDialog()
    {
        InitializeComponent();
        _calendarService = ((App)Application.Current).GetService<GraphCalendarService>();
        _isEditMode = false;

        // 기본값 설정
        StartDatePicker.SelectedDate = DateTime.Today;
        EndDatePicker.SelectedDate = DateTime.Today;
        StartTimeTextBox.Text = DateTime.Now.AddHours(1).ToString("HH:00");
        EndTimeTextBox.Text = DateTime.Now.AddHours(2).ToString("HH:00");
    }

    /// <summary>
    /// 기존 일정 수정 모드
    /// </summary>
    public EventEditDialog(Event existingEvent) : this()
    {
        _existingEvent = existingEvent;
        _isEditMode = true;
        DeleteButton.Visibility = Visibility.Visible;

        LoadEventData();
    }

    /// <summary>
    /// 특정 날짜에 새 일정 생성
    /// </summary>
    public EventEditDialog(DateTime targetDate) : this()
    {
        StartDatePicker.SelectedDate = targetDate;
        EndDatePicker.SelectedDate = targetDate;
    }

    /// <summary>
    /// 기존 일정 데이터 로드
    /// </summary>
    private void LoadEventData()
    {
        if (_existingEvent == null) return;

        SubjectTextBox.Text = _existingEvent.Subject ?? string.Empty;
        LocationTextBox.Text = _existingEvent.Location?.DisplayName ?? string.Empty;
        BodyTextBox.Text = _existingEvent.Body?.Content ?? string.Empty;

        // 종일 일정 여부
        IsAllDayCheckBox.IsChecked = _existingEvent.IsAllDay ?? false;

        // 시작/종료 시간
        if (_existingEvent.Start?.DateTime != null)
        {
            var startDt = DateTime.Parse(_existingEvent.Start.DateTime);
            StartDatePicker.SelectedDate = startDt.Date;
            StartTimeTextBox.Text = startDt.ToString("HH:mm");
        }

        if (_existingEvent.End?.DateTime != null)
        {
            var endDt = DateTime.Parse(_existingEvent.End.DateTime);
            EndDatePicker.SelectedDate = endDt.Date;
            EndTimeTextBox.Text = endDt.ToString("HH:mm");
        }

        // 알림 설정
        if (_existingEvent.ReminderMinutesBeforeStart.HasValue)
        {
            var minutes = _existingEvent.ReminderMinutesBeforeStart.Value;
            for (int i = 0; i < ReminderComboBox.Items.Count; i++)
            {
                if (ReminderComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == minutes.ToString())
                {
                    ReminderComboBox.SelectedIndex = i;
                    break;
                }
            }
        }

        // 온라인 회의
        IsOnlineMeetingCheckBox.IsChecked = _existingEvent.IsOnlineMeeting ?? false;

        // 참석자
        if (_existingEvent.Attendees != null && _existingEvent.Attendees.Any())
        {
            var emails = _existingEvent.Attendees
                .Where(a => a.EmailAddress?.Address != null)
                .Select(a => a.EmailAddress!.Address);
            AttendeesTextBox.Text = string.Join(", ", emails);
        }

        // 시간 입력 필드 활성화/비활성화
        UpdateTimeFieldsVisibility();
    }

    /// <summary>
    /// 종일 일정 체크박스 변경
    /// </summary>
    private void IsAllDayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateTimeFieldsVisibility();
    }

    /// <summary>
    /// 시간 입력 필드 표시/숨김
    /// </summary>
    private void UpdateTimeFieldsVisibility()
    {
        var isAllDay = IsAllDayCheckBox.IsChecked ?? false;
        StartTimeTextBox.IsEnabled = !isAllDay;
        EndTimeTextBox.IsEnabled = !isAllDay;

        if (isAllDay)
        {
            StartTimeTextBox.Text = "00:00";
            EndTimeTextBox.Text = "23:59";
        }
    }

    /// <summary>
    /// 저장 버튼 클릭
    /// </summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // 유효성 검사
        if (string.IsNullOrWhiteSpace(SubjectTextBox.Text))
        {
            System.Windows.MessageBox.Show("제목을 입력해주세요.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            SubjectTextBox.Focus();
            return;
        }

        if (!StartDatePicker.SelectedDate.HasValue || !EndDatePicker.SelectedDate.HasValue)
        {
            System.Windows.MessageBox.Show("날짜를 선택해주세요.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 시간 파싱
        if (!TimeSpan.TryParse(StartTimeTextBox.Text, out var startTime))
        {
            startTime = new TimeSpan(9, 0, 0);
        }
        if (!TimeSpan.TryParse(EndTimeTextBox.Text, out var endTime))
        {
            endTime = startTime.Add(TimeSpan.FromHours(1));
        }

        var startDateTime = StartDatePicker.SelectedDate.Value.Date + startTime;
        var endDateTime = EndDatePicker.SelectedDate.Value.Date + endTime;

        // 종료 시간이 시작 시간보다 빠른 경우
        if (endDateTime <= startDateTime)
        {
            System.Windows.MessageBox.Show("종료 시간이 시작 시간보다 늦어야 합니다.", "입력 오류",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 알림 시간
        int reminderMinutes = 15;
        if (ReminderComboBox.SelectedItem is ComboBoxItem selectedReminder &&
            int.TryParse(selectedReminder.Tag?.ToString(), out var mins))
        {
            reminderMinutes = mins;
        }

        // 참석자 파싱
        var attendees = new List<string>();
        if (!string.IsNullOrWhiteSpace(AttendeesTextBox.Text))
        {
            attendees = AttendeesTextBox.Text
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => a.Contains('@'))
                .ToList();
        }

        // 요청 객체 생성
        var request = new EventCreateRequest
        {
            Subject = SubjectTextBox.Text.Trim(),
            Location = LocationTextBox.Text.Trim(),
            Body = BodyTextBox.Text.Trim(),
            StartDateTime = startDateTime,
            EndDateTime = endDateTime,
            IsAllDay = IsAllDayCheckBox.IsChecked ?? false,
            IsOnlineMeeting = IsOnlineMeetingCheckBox.IsChecked ?? false,
            ReminderMinutesBefore = reminderMinutes,
            Attendees = attendees.Any() ? attendees : null
        };

        SaveButton.IsEnabled = false;
        SaveButton.Content = "저장 중...";

        try
        {
            if (_calendarService == null)
            {
                System.Windows.MessageBox.Show("캘린더 서비스에 연결할 수 없습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Event? result;
            if (_isEditMode && _existingEvent?.Id != null)
            {
                // 수정
                Log4.Debug($"일정 수정: {_existingEvent.Id}");
                result = await _calendarService.UpdateEventAsync(_existingEvent.Id, request);
            }
            else
            {
                // 생성
                Log4.Debug($"새 일정 생성: {request.Subject}");
                result = await _calendarService.CreateEventAsync(request);
            }

            if (result != null)
            {
                ResultEvent = result;
                Log4.Info($"일정 저장 완료: {result.Subject}");
                DialogResult = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show("일정 저장에 실패했습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"일정 저장 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"일정 저장 중 오류가 발생했습니다.\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Content = "저장";
        }
    }

    /// <summary>
    /// 삭제 버튼 클릭
    /// </summary>
    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_existingEvent?.Id == null) return;

        var result = System.Windows.MessageBox.Show(
            $"'{_existingEvent.Subject}' 일정을 삭제하시겠습니까?",
            "일정 삭제",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        DeleteButton.IsEnabled = false;
        DeleteButton.Content = "삭제 중...";

        try
        {
            if (_calendarService == null)
            {
                System.Windows.MessageBox.Show("캘린더 서비스에 연결할 수 없습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Log4.Debug($"일정 삭제: {_existingEvent.Id}");
            var success = await _calendarService.DeleteEventAsync(_existingEvent.Id);

            if (success)
            {
                IsDeleted = true;
                Log4.Info($"일정 삭제 완료: {_existingEvent.Subject}");
                DialogResult = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show("일정 삭제에 실패했습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Log4.Error($"일정 삭제 실패: {ex.Message}");
            System.Windows.MessageBox.Show($"일정 삭제 중 오류가 발생했습니다.\n{ex.Message}", "오류",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DeleteButton.IsEnabled = true;
            DeleteButton.Content = "삭제";
        }
    }

    /// <summary>
    /// 취소 버튼 클릭
    /// </summary>
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
